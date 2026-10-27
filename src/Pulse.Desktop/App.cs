using System.Drawing;
using System.Text.Json;
namespace Pulse.Desktop;
public sealed partial class PulseApp : Application
{
    public static string DataRoot=>Environment.GetEnvironmentVariable("PULSE_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PulseWindows");
    public AppStorage Storage {get;}=new(DataRoot);
    public WindowsSecrets Secrets {get;}=new(DataRoot);
    public UsageEngine Engine {get;}
    public AlertRules Alerts {get;}=new();
    public UsageHistoryStore History {get;}
    private readonly StatusAlertRules statusAlerts=new();
    private DateTimeOffset lastBackgroundCheck=DateTimeOffset.MinValue;
    private bool checkingBackground,recapBalloon;
    private System.Windows.Forms.NotifyIcon? tray;
    public RailWindow? Rail {get;private set;}
    private SettingsWindow? settings;
    private readonly DispatcherTimer refresh=new();
    private readonly DispatcherTimer activityTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    public Dictionary<string,bool> Activities {get;private set;}=[];
    public Dictionary<string,List<PromptCacheSession>> PromptCaches {get;private set;}=[];
    private DateTimeOffset lastPromptRead=DateTimeOffset.MinValue;
    private SpendReport? localSpend;private DateTime spendFileTime;
    public SpendReport? LocalSpend {get{if(!Engine.Config.SpendEnabled)return null;var path=Path.Combine(Storage.Root,"spend-cache.json");var changed=File.Exists(path)?File.GetLastWriteTimeUtc(path):DateTime.MinValue;if(changed!=spendFileTime){spendFileTime=changed;localSpend=Storage.Load<SpendReport>("spend-cache.json");}return localSpend;}}
    private bool readingActivity;
    private bool refreshingStaleReadings;
    private bool startingWindow;
    private bool exiting;
    private readonly Dictionary<string,long> credentialVersions=[];
    public long CredentialVersion(string id)=>credentialVersions.GetValueOrDefault(id);
    public bool UsesLocalLogin(AccountConfig account)=>account.Enabled&&account.Extra.GetValueOrDefault("isAdditionalAccount")!="true"&&string.IsNullOrWhiteSpace(Secrets.Get(account.Id))&&string.IsNullOrWhiteSpace(Secrets.Get("desktop:"+account.Id));
    public bool CanSaveCredential(string id,long version)=>!exiting&&CredentialVersion(id)==version&&Engine.Config.Accounts.Any(a=>a.Id==id);
    public async Task ClearCredentialsAsync(AccountConfig account)
    {
        credentialVersions[account.Id]=CredentialVersion(account.Id)+1;Engine.Save();CredentialSlots.Clear(Secrets,account.Id);foreach(var window in Windows.OfType<Window>().Where(w=>w.Tag is string id&&id==account.Id).ToArray()){if(window is BrowserSignIn browser)await browser.ClearProfileAsync();window.Close();}
        var root=Path.GetFullPath(Path.Combine(Storage.Root,"browser-sessions"));var safeId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.Id)))[..20];var profile=Path.GetFullPath(Path.Combine(root,safeId));
        if(profile.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(profile)&&new DirectoryInfo(profile).LinkTarget is null)for(var attempt=0;attempt<5&&Directory.Exists(profile);attempt++){try{Directory.Delete(profile,true);}catch(IOException){await Task.Delay(300);}catch(UnauthorizedAccessException){break;}}
    }
    private System.Threading.Mutex? single;
    private readonly string[] startupArguments;
    public PulseApp(string[]? startupArguments=null) {this.startupArguments=startupArguments??[];Engine=new(Storage,Secrets,UsageProviders.Create);History=new(Storage);InitializeWindowStarter(); ShutdownMode=ShutdownMode.OnExplicitShutdown;}
    [STAThread] public static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--render-checks")return VisualChecks.Run(args[1]);
        if(args.SequenceEqual(new[]{"--startup-check"})){if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PULSE_DATA_DIR")))return 2;return new PulseApp(args).Run();}
        if(args.Any(a=>a.StartsWith("--",StringComparison.Ordinal))) {WindowsServices.AttachConsole(-1); return CommandModes.RunAsync(args,DataRoot).GetAwaiter().GetResult();}
        var app=new PulseApp(args); app.Run(); return 0;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); single=new Mutex(true,"Local\\PulseWindows-"+InstanceChannel.InstanceKey,out var created);
        if(!created) {InstanceChannel.SendAsync(startupArguments.FirstOrDefault(a=>a.StartsWith("pulse://",StringComparison.OrdinalIgnoreCase))??"settings").GetAwaiter().GetResult();Shutdown();return;}
        _=InstanceChannel.ListenAsync(message=>Dispatcher.BeginInvoke(()=>{if(exiting)return;if(message=="settings")ShowSettings();else OpenAccountUri(message);}),Engine.LifetimeToken);
        DispatcherUnhandledException+=(_,eventArgs)=> {Storage.Save("last-error.json",new {at=DateTimeOffset.Now,error=Redaction.Clean(eventArgs.Exception.ToString())}); MessageBox.Show("Pulse 遇到错误，诊断已保存在数据目录。","Pulse"); eventArgs.Handled=true;};
        Localization.Language=Engine.Config.Language;Ui.ApplyTheme(Engine.Config);Alerts.Seen=Storage.Load<HashSet<string>>("alert-memory.json")??[];statusAlerts.Seen=Storage.Load<HashSet<string>>("status-alert-memory.json")??[];
        tray=new System.Windows.Forms.NotifyIcon {Visible=true,Text="Pulse · AI 额度",Icon=MakeIcon(null)};
        tray.MouseClick+=(_,args)=>{if(args.Button==System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(()=>{if(Engine.Config.TrayOpensDashboard)ShowDashboard();else ShowSettings();});};
        tray.BalloonTipClicked+=(_,_)=>Dispatcher.Invoke(()=>{ShowSettings(recapBalloon?"reports":"dashboard");recapBalloon=false;});
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("用量面板",null,(_,_)=>Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("刷新",null,async(_,_)=>await Engine.RefreshAsync());
        menu.Items.Add("设置",null,(_,_)=>Dispatcher.Invoke(()=>ShowSettings()));
        menu.Items.Add("显示 / 隐藏浮动栏",null,(_,_)=>Dispatcher.Invoke(TogglePanel));
        menu.Items.Add("检查更新",null,async(_,_)=>await CheckUpdates());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(Quit));foreach(System.Windows.Forms.ToolStripItem item in menu.Items){item.Tag=item.Text;item.Text=Localization.Translate(item.Text??"");}tray.ContextMenuStrip=menu;
        Rail=new RailWindow(this); Rail.Show();
        Engine.Changed+=()=>Dispatcher.BeginInvoke(()=>{if(exiting)return;Rail?.Render(); UpdateTray(); settings?.UpdateReadings();});
        DetailCharts.CodexUpdated+=_=>Dispatcher.BeginInvoke(()=>{if(!exiting)settings?.UpdateReadings();});
        Engine.ReadingUpdated+=reading=> Dispatcher.BeginInvoke(()=> {if(exiting)return;History.Observe(reading);foreach(var message in Alerts.Evaluate(Engine.Config,reading)){recapBalloon=false;tray?.ShowBalloonTip(8000,"Pulse · "+AccountName(reading.AccountId),Localization.Translate(message),System.Windows.Forms.ToolTipIcon.Info);} Storage.Save("alert-memory.json",Alerts.Seen);});
        ApplyPollingInterval(); refresh.Tick+=async(_,_)=>await Engine.RefreshAsync(); refresh.Start();
        activityTimer.Tick+=async(_,_)=>{if(!readingActivity){readingActivity=true;try{var config=System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(Engine.Config,StorageJson.Options),StorageJson.Options)!;foreach(var a in config.Accounts)if(!UsesLocalLogin(a))a.Enabled=false;Activities=await Task.Run(()=>AgentActivity.Read(config,LocalPaths.Home));if(config.SpendEnabled&&DateTimeOffset.UtcNow-lastPromptRead>TimeSpan.FromSeconds(30)){lastPromptRead=DateTimeOffset.UtcNow;var providers=config.Accounts.Where(a=>a.Enabled&&a.Extra.GetValueOrDefault("isAdditionalAccount")!="true"&&a.ProviderId is "codex" or "claudeCode").Select(a=>a.ProviderId).Distinct().ToArray();var caches=await Task.Run(()=>providers.ToDictionary(p=>p,p=>PromptCacheService.Read(p,LocalPaths.Home,DateTimeOffset.Now)));if(Engine.Config.SpendEnabled)PromptCaches=caches;}else if(!config.SpendEnabled)PromptCaches.Clear();Rail?.Render();ApplyPollingInterval();}finally{readingActivity=false;}}await CheckBackground();};activityTimer.Start();windowStarterTimer.Start();
        if(startupArguments.Contains("--startup-check")){
            Rail.Hide();Storage.Save("startup-ready.json",new{ready=true});var check=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};check.Tick+=(_,_)=>{check.Stop();Storage.Save("startup-check.json",new{railLoaded=Rail.IsLoaded,dispatcherRunning=Dispatcher.CheckAccess(),navigation=settings?.CurrentPage,at=DateTimeOffset.UtcNow});Quit();};check.Start();
        }else if(!Engine.Config.OnboardingCompleted) ShowSettings("onboarding"); else _=RefreshStartupAsync();
        if(startupArguments.FirstOrDefault(a=>a.StartsWith("pulse://",StringComparison.OrdinalIgnoreCase)) is {} uri) OpenAccountUri(uri);
    }
    public string AccountName(string id) {var account=Engine.Config.Accounts.FirstOrDefault(a=>a.Id==id); return account is null?id:string.IsNullOrWhiteSpace(account.Label)?ProviderCatalog.Find(account.ProviderId)?.Name??account.ProviderId:account.Label;}
    public void ShowSettings(string page="accounts") {if(settings is null) {settings=new SettingsWindow(this); settings.Closed+=(_,_)=>settings=null;} settings.Navigate(page); settings.Show(); if(settings.WindowState==WindowState.Minimized) settings.WindowState=WindowState.Normal; settings.Activate();ApplyPollingInterval();_=RefreshStaleReadingsAsync();}
    private async Task RefreshStartupAsync(){await Engine.RefreshAsync();await RefreshStaleReadingsAsync();}
    private async Task RefreshStaleReadingsAsync()
    {
        if(exiting||refreshingStaleReadings)return;
        var now=DateTimeOffset.UtcNow;
        if(!Engine.Config.Accounts.Any(a=>a.Enabled&&(Engine.Reading(a.Id) is not {} reading||reading.ErrorCode is not null||reading.IsStale||reading.ObservedAt is not {} at||now-at>TimeSpan.FromMinutes(5))))return;
        refreshingStaleReadings=true;
        try{await Engine.RefreshAsync(Engine.LifetimeToken,waitForCurrent:true);}
        catch(OperationCanceledException)when(exiting){}
        finally{refreshingStaleReadings=false;}
    }
    public void ShowDashboard() {ShowSettings("dashboard");}
    public void OpenAccountUri(string uri) {if(Uri.TryCreate(uri,UriKind.Absolute,out var parsed)&&parsed.Scheme=="pulse"&&parsed.Host=="account"&&parsed.AbsolutePath.Length<1024) ShowSettings("account:"+Uri.UnescapeDataString(parsed.AbsolutePath.Trim('/')));}
    public void TogglePanel() {Engine.Config.ShowPanel=!Engine.Config.ShowPanel; Save();}
    public void Save() {var languageChanged=Localization.Language!=Engine.Config.Language;Localization.Language=Engine.Config.Language;Ui.ApplyTheme(Engine.Config);Engine.Save();ApplyPollingInterval(); Rail?.ApplySettings();settings?.UpdateShortcutStatus();if(languageChanged){settings?.RefreshPage();if(tray?.ContextMenuStrip is {} menu)foreach(System.Windows.Forms.ToolStripItem item in menu.Items)if(item.Tag is string text)item.Text=Localization.Translate(text);}}
    public void SaveAppearance(){Ui.ApplyTheme(Engine.Config);Engine.Save();Rail?.Render();UpdateTray();}
    private void ApplyPollingInterval(){var interval=TimeSpan.FromMinutes(RefreshSchedule.Minutes(Engine.Config.RefreshMinutes,Activities.Values.Any(a=>a),settings?.IsVisible==true,WindowsServices.IdleTime()));if(refresh.Interval!=interval)refresh.Interval=interval;}
    private async Task CheckBackground()
    {
        if(exiting||checkingBackground||DateTimeOffset.UtcNow-lastBackgroundCheck<TimeSpan.FromMinutes(5))return;checkingBackground=true;lastBackgroundCheck=DateTimeOffset.UtcNow;
        try {
            var config=Engine.Config;
            if(config.SpendEnabled)await ScanSpendAsync(Engine.LifetimeToken);
            if(config.RecapAlerts&&config.SpendEnabled&&DateTime.Today.Day==1){var period=DateTime.Today.AddMonths(-1).ToString("yyyy-MM");var seen=Storage.Load<HashSet<string>>("recap-alert-memory.json")??[];var report=Storage.Load<SpendReport>("spend-cache.json");if(!seen.Contains(period)&&report?.Records.Any(r=>r.At.ToLocalTime().ToString("yyyy-MM")==period)==true){seen.Add(period);Storage.Save("recap-alert-memory.json",seen.TakeLast(120).ToHashSet());recapBalloon=true;tray?.ShowBalloonTip(8000,"Pulse",Localization.Translate("上个月的用量回顾已就绪，点击打开报告。"),System.Windows.Forms.ToolTipIcon.Info);}}
            if(!config.AlertsEnabled||!config.StatusAlerts)return;
            var providers=config.Accounts.Where(a=>a.Enabled).Select(a=>a.ProviderId).Distinct().Where(p=>StatusService.Pages.Any(s=>s.ProviderId==p)).ToArray();
            using var http=UsageEngine.CreateHttp(config);using var gate=new SemaphoreSlim(4);
            var reports=await Task.WhenAll(providers.Select(async provider=>{await gate.WaitAsync(Engine.LifetimeToken);try{return await new StatusService().FetchAsync(provider,http,Engine.LifetimeToken);}finally{gate.Release();}}));
            if(exiting)return;
            foreach(var report in reports)foreach(var message in statusAlerts.Evaluate(Engine.Config,report)){recapBalloon=false;tray?.ShowBalloonTip(8000,"Pulse · "+(ProviderCatalog.Find(report.ProviderId)?.Name??report.ProviderId),Localization.Translate(message),System.Windows.Forms.ToolTipIcon.Warning);}
            Storage.Save("status-alert-memory.json",statusAlerts.Seen);
        }catch(OperationCanceledException)when(exiting){}catch(Exception exception)when(exception is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException){Storage.Save("background-last-error.json",new{at=DateTimeOffset.UtcNow,error=Redaction.Clean(exception.Message)});}finally{checkingBackground=false;}
    }
    private bool checkingUpdate;
    public async Task CheckUpdates()
    {
        if(checkingUpdate||exiting)return;
        if(!Uri.TryCreate(AppReleaseInfo.ManifestUrl(Engine.Config.UpdateManifestUrl),UriKind.Absolute,out var uri)||uri.Scheme!="https"){MessageBox.Show("尚未配置 Windows 发布源。可在设置的“关于与更新”页填写更新清单地址。","Pulse");return;}
        checkingUpdate=true;
        try {
            using var client=UpdateHttpClient.Create(Engine.Config);var manifest=await UpdatePackage.ReadManifestAsync(client,uri,Engine.LifetimeToken);
            if(!AppReleaseInfo.IsNewer(manifest.VersionText,AppReleaseInfo.CurrentVersion)){MessageBox.Show("当前已是此发布源提供的最新版本。","Pulse");return;}
            tray?.ShowBalloonTip(4000,"Pulse",Localization.Translate("正在下载并校验 Windows 更新包…"),System.Windows.Forms.ToolTipIcon.Info);
            var prepared=await UpdatePackage.PrepareAsync(client,manifest,Storage.Root,Engine.LifetimeToken);if(exiting)return;
            if(MessageBox.Show($"Windows 更新包 {prepared.Version} 已下载并通过 SHA-256 校验。\n文件数：{prepared.Files.Count}\n安装目录：{AppContext.BaseDirectory}\n准备目录：{prepared.PreparationDirectory}\n现在退出 Pulse 并安装？","Pulse",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            var result=UpdateInstaller.Schedule(prepared,AppContext.BaseDirectory,Environment.ProcessId,true);
            if(result.Scheduled){Quit();return;}
            MessageBox.Show(result.Message+Environment.NewLine+result.PreparedDirectory,"Pulse");WindowsServices.Open(result.PreparedDirectory);
        }catch(OperationCanceledException)when(exiting){}
        catch(Exception exception)when(exception is System.Net.Http.HttpRequestException or JsonException or OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException){MessageBox.Show("更新检查或下载失败："+Redaction.Clean(exception.Message),"Pulse");}
        finally{checkingUpdate=false;}
    }
    private Icon MakeIcon(double? percent,string mode="number",double? second=null,string? accountColor=null,bool exhausted=false)
    {
        var chosen=System.Drawing.ColorTranslator.FromHtml(AppearancePalette.Quota(Engine.Config,percent,exhausted,accountColor));
        using var bitmap=new Bitmap(32,32); using var g=Graphics.FromImage(bitmap); g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var pen=new System.Drawing.Pen(chosen,3);using var track=new System.Drawing.Pen(System.Drawing.Color.FromArgb(75,80,95),3);
        if(mode!="dual"){g.DrawEllipse(track,3,3,26,26);if(percent is {} fraction)g.DrawArc(pen,3,3,26,26,-90,(float)(360*Math.Clamp(fraction/100,.001,1)));else g.DrawEllipse(pen,3,3,26,26);}
        if(percent is {} value&&mode!="ring") {using var font=new Font("Segoe UI",mode=="dual"?7:9, System.Drawing.FontStyle.Bold); using var brush=new SolidBrush(chosen); var text=mode=="dual"?$"{UsageFigures.Percent(value)}%\n{(second is {} next?UsageFigures.Percent(next).ToString():"—")}%":$"{UsageFigures.Percent(value)}"; var size=g.MeasureString(text,font); g.DrawString(text,font,brush,(32-size.Width)/2,(32-size.Height)/2);}
        var handle=bitmap.GetHicon(); try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    private void UpdateTray()
    {
        if(tray is null)return; var enabled=Engine.Config.Accounts.Where(a=>a.Enabled).ToList();var candidates=enabled.Select(a=>(account:a,window:UsageEngine.Headline(a,Engine.Reading(a.Id)))).Where(x=>x.window is not null).ToList();var best=candidates.FirstOrDefault(x=>x.account.Id==Engine.Config.TrayAccountId);if(best.window is null)best=candidates.OrderByDescending(x=>x.window!.UsedPercent).FirstOrDefault();
        var reading=best.account is null?null:Engine.Reading(best.account.Id);var first=Engine.Config.TrayUsageMode=="dual"?reading?.Windows.FirstOrDefault(w=>w.Kind=="fiveHour")??best.window:best.window;var second=reading?.Windows.FirstOrDefault(w=>w.Kind=="weekly");var icon=MakeIcon(first?.UsedPercent,Engine.Config.TrayUsageMode,second?.UsedPercent,best.account?.AccentColor,first?.Exhausted==true); var old=tray.Icon; tray.Icon=icon; old?.Dispose(); var text=best.window is null||best.account is null?"Pulse · AI 额度":$"Pulse · {AccountName(best.account.Id)} 已用 {best.window.UsedPercent:0.#}%";text=Localization.Translate(text);tray.Text=text[..Math.Min(63,text.Length)];
    }
    public async void Quit() {if(exiting)return; exiting=true; refresh.Stop();activityTimer.Stop();windowStarterTimer.Stop();Engine.Dispose();ChildProcesses.KillAll();Engine.Save();foreach(var window in Windows.OfType<Window>().Where(w=>w.Tag is string).ToArray())window.Close();for(var attempt=0;attempt<50&&(Engine.IsRefreshing||startingWindow);attempt++)await Task.Delay(100);tray?.Dispose();Rail?.Close();settings?.Close();Shutdown();}
    protected override void OnExit(ExitEventArgs e) {windowStarterTimer.Stop();Engine.Dispose();ChildProcesses.KillAll();tray?.Dispose(); single?.Dispose(); base.OnExit(e);}
}
