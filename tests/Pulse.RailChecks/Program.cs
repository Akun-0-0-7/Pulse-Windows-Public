using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var area=new Rect(100,50,1000,700);
        var expanded=RailPlacement.Calculate(area,new Size(100,400),"right",null);
        var collapsed=RailPlacement.Calculate(area,new Size(8,120),"right",null);
        Equal(1094,expanded.X+100,"Expanded rail right edge");
        Equal(1094,collapsed.X+8,"Collapsed rail right edge");
        var bottom=RailPlacement.Calculate(area,new Size(120,8),"bottom",null);
        Equal(744,bottom.Y+8,"Collapsed rail bottom edge");
        var free=RailPlacement.Calculate(area,new Size(100,100),"free",new Point(900,200));
        Equal(900,free.X,"Free rail position");Equal(200,free.Y,"Free rail position");
        var mixed=RailPlacement.Physical(new Rect(-1920,-100,1920,1080),new Size(100,400),1.5,"right",null);
        Equal(-9,mixed.X+150,"Negative display origin with 150 percent scaling");
        var local=RailPlacement.Physical(new Rect(-1920,-100,1920,1080),new Size(100,100),1.5,"free",new Point(20,40));
        Equal(-1890,local.X,"Monitor-relative free position");Equal(-40,local.Y,"Monitor-relative free position");
        var fills=UsageBar.FillFractions(52.5,20);
        Equal(10.5,fills.Sum(),"Segmented bar fractional usage");
        if(fills.Any(f=>f<0||f>1))throw new Exception("Segment outside range");
        Equal(0,UsageBar.FillFractions(-2,20).Sum(),"Negative usage");
        Equal(20,UsageBar.FillFractions(140,20).Sum(),"Over-limit usage");
        Equal(0,UsageBar.FillFractions(double.NaN,20).Sum(),"Missing usage");
        var output=args.FirstOrDefault(a=>!a.StartsWith("--",StringComparison.Ordinal)) is {} folder?Path.GetFullPath(folder):Path.Combine(Path.GetTempPath(),"pulse-rail-checks");Directory.CreateDirectory(output);
        var data=Path.Combine(output,"synthetic-data");Environment.SetEnvironmentVariable("PULSE_DATA_DIR",data);var storage=new AppStorage(data);var account=new AccountConfig{Id="primary-demo",ProviderId="codex",Enabled=true,Detailed=true};storage.SaveConfig(new AppConfig{Accounts=[account]});
        storage.SaveCache([new(){AccountId=account.Id,ProviderId="codex",Plan="cached-plan",ObservedAt=DateTimeOffset.UtcNow,ErrorCode="unreachable",ErrorMessage="cached-connection-error"}]);
        var app=new PulseApp();account=app.Engine.Config.Accounts[0];
        if(args.Contains("--starter-only")){WindowStarterRecoveryChecks(app);app.Shutdown();return 0;}
        if(args.Contains("--visual-only")){PulseShapeChecks.Run();VisualParityChecks(app,output);return 0;}
        if(args.Contains("--motion-only")){RailMotionMathChecks.Run();MotionChecks(app,output,args.Contains("--render"));return 0;}
        var windowStarterTimer=Field<DispatcherTimer>(app,"windowStarterTimer");
        Equal(1,windowStarterTimer.Interval.TotalSeconds,"Independent window starter interval");
        if(windowStarterTimer.IsEnabled)throw new Exception("Window starter timer started during synthetic app construction.");
        if(!DetailCharts.IsPrimaryCodex(app,account))throw new Exception("Primary Codex omitted history action");
        app.Secrets.Set(account.Id,"fixture-explicit-login");if(DetailCharts.IsPrimaryCodex(app,account))throw new Exception("Explicit Codex login received another local account's history action");app.Secrets.Set(account.Id,null);
        var extra=new AccountConfig{Id="additional-demo",ProviderId="codex",Enabled=true,Extra=new(){{"isAdditionalAccount","true"}}};app.Engine.Config.Accounts.Add(extra);
        if(DetailCharts.IsPrimaryCodex(app,extra))throw new Exception("Additional Codex received local account history action");
        account.Enabled=false;
        var disabled=RailWindow.DetailContent(app,account);var disabledText=string.Join("\n",Descendants(disabled).OfType<TextBlock>().Select(t=>t.Text));
        if(!disabledText.Contains("账号未启用",StringComparison.Ordinal)||disabledText.Contains("cached-connection-error",StringComparison.Ordinal)||disabledText.Contains("cached-plan",StringComparison.Ordinal)||disabledText.Contains("读取于",StringComparison.Ordinal))throw new Exception("Disabled account displayed cached connection state instead of the disabled state.");
        if(app.Engine.Reading(account.Id)?.ErrorCode!="unreachable")throw new Exception("Disabling an account discarded its saved reading.");
        account.ProviderId="unknown-offline-fixture";extra.Enabled=false;var sawImmediateRefresh=false;app.Engine.Changed+=()=>sawImmediateRefresh|=app.Engine.IsRefreshing;
        var settings=new SettingsWindow(app);settings.Navigate("account:"+account.Id);
        var enable=Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Single(c=>Equals(c.Content,"启用这个账号"));enable.IsChecked=true;
        if(!account.Enabled||!sawImmediateRefresh)throw new Exception("Enabling an account on its settings page did not immediately request a refresh.");
        var authReader=Field<Func<string,string?>>(app,"windowStarterAuthReader");
        var starterEnvironment=new[]{"OPENAI_API_KEY","CODEX_API_KEY","CODEX_HOME"}.ToDictionary(key=>key,Environment.GetEnvironmentVariable);
        try
        {
            foreach(var key in starterEnvironment.Keys)Environment.SetEnvironmentVariable(key,null);
            SetField(app,"windowStarterAuthReader",(Func<string,string?>)(_=>"{\"accountId\":\"rail-fixture-account\",\"accessToken\":\"fixture-token\"}"));
            FiveHourWindowStarterLabels(app,settings,account);
            ShortRefreshChecks(app,settings);
            settings.Close();
            WindowStarterClaimChecks(app,account);
            CorruptWindowStarterHistoryChecks(app,account);
            WindowStarterReceiptMetadataChecks(app,account);
            WindowStarterRecoveryChecks(app);
            ShowSettingsRefreshChecks(app,account,extra);
        }
        finally
        {
            SetField(app,"windowStarterAuthReader",authReader);
            foreach(var entry in starterEnvironment)Environment.SetEnvironmentVariable(entry.Key,entry.Value);
        }
        AutoCollapseChecks(app);
        RailMotionMathChecks.Run();
        MotionChecks(app,output,args.Contains("--render"));
        PulseShapeChecks.Run();
        VisualParityChecks(app,output,!args.Contains("--skip-raster"));
        var now=DateTimeOffset.UtcNow;var history=Enumerable.Range(0,6).Select(i=>new MetricPoint(now.AddHours(i-6),i%3)).ToList();
        var reading=new UsageReading{AccountId=account.Id,ObservedAt=now,Metrics=[new("one","扩展趋势一",2,"次",history),new("two","扩展趋势二",2,"次",history),new("three","扩展趋势三",2,"次",history)]};
        var content=new StackPanel{Width=310,Background=Ui.Bg};content.Children.Add(UsageBar.Create(52.5,true));DetailCharts.AddHistory(content,app,account,reading);
        Equal(2,content.Children.OfType<MiniTrendChart>().Count(),"Extension history chart limit");
        content.Children.Add(new MiniTrendChart([new(now,42)]){Height=64});content.Children.Add(new MiniTrendChart([new(now,double.NaN),new(now,42),new(now,42)]){Height=64});
        if(args.Contains("--render"))
        {
            content.Height=550;content.Measure(new Size(310,550));content.Arrange(new Rect(0,0,310,550));content.UpdateLayout();var bitmap=new RenderTargetBitmap(310,550,96,96,PixelFormats.Pbgra32);bitmap.Render(content);bitmap.Freeze();var pixels=new byte[310*550*4];bitmap.CopyPixels(pixels,310*4,0);if(!pixels.Where((_,i)=>i%4==3).Any(p=>p>0))throw new Exception("Rendered chart image is fully transparent");var file=Path.Combine(output,"rail-detail-charts.png");ReportWindow.Write(bitmap,file);
            using(var stream=File.OpenRead(file)){var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);Equal(310,decoder.Frames[0].PixelWidth,"Chart PNG width");Equal(550,decoder.Frames[0].PixelHeight,"Chart PNG height");}
            Console.WriteLine("PASS: chart PNG rendering. Output: "+output);
        }
        app.Shutdown();
        Console.WriteLine("PASS: docking during resize, mixed-DPI geometry, free placement, fractional segmented bars, primary Codex action, disabled cached state, immediate enable refresh, five-hour window starter settings, timer, primary-account ownership, claim rollback, corrupt-history pause and safe receipt metadata, stale-cache settings refresh, short refresh selection and persistence, automatic collapse lifecycle and extension chart limit.");
        return 0;
    }
    private static void Equal(double expected,double actual,string name){if(Math.Abs(expected-actual)>0.000001)throw new Exception($"{name}: expected {expected}, actual {actual}");}
    private static void FiveHourWindowStarterLabels(PulseApp app,SettingsWindow settings,AccountConfig account)
    {
        var providerId=account.ProviderId;
        try
        {
            account.ProviderId="codex";
            settings.Navigate("account:"+account.Id);
            var texts=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            var checks=Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Select(c=>c.Content?.ToString()).ToArray();
            if(!checks.Contains("5 小时重置后发送一次 hi 并刷新额度"))throw new Exception("Window starter checkbox does not say that it handles the five-hour reset and refreshes usage.");
            if(!texts.Contains("仅处理 5 小时额度；每周重置不另发消息。"))throw new Exception("Window starter page lacks five-hour-only help text.");
            foreach(var key in new[]{"OPENAI_API_KEY","CODEX_API_KEY"})
            {
                Environment.SetEnvironmentVariable(key,"fixture");settings.Navigate("account:"+account.Id);
                if(HasWindowStarterCheck(settings))throw new Exception(key+" unexpectedly allowed the local window starter.");
                Environment.SetEnvironmentVariable(key,null);
            }
            var customHome=Path.Combine(app.Storage.Root,"fixture-codex-home");
            var customAuth=Path.Combine(customHome,"auth.json");
            Environment.SetEnvironmentVariable("CODEX_HOME",customHome);
            SetField(app,"windowStarterAuthReader",(Func<string,string?>)(path=>path==customAuth?"{\"accountId\":\"different-account\",\"accessToken\":\"fixture-token\"}":"{\"accountId\":\"rail-fixture-account\",\"accessToken\":\"fixture-token\"}"));
            settings.Navigate("account:"+account.Id);
            if(HasWindowStarterCheck(settings))throw new Exception("Different CODEX_HOME account offered the local window starter.");
            SetField(app,"windowStarterAuthReader",(Func<string,string?>)(_=>"{\"accountId\":\"rail-fixture-account\",\"accessToken\":\"fixture-token\"}"));
            settings.Navigate("account:"+account.Id);
            if(!HasWindowStarterCheck(settings))throw new Exception("Matching CODEX_HOME account lost the local window starter.");
            Environment.SetEnvironmentVariable("CODEX_HOME",null);
            var status=Field<TextBlock>(settings,"windowStarterStatus");
            if(status.Text!=app.WindowStarterStatus(account.Id))throw new Exception("Window starter status does not match the current account.");
            status.Text="stale synthetic status";
            settings.UpdateReadings();
            if(status.Text!=app.WindowStarterStatus(account.Id))throw new Exception("Window starter status was not updated with the account reading.");
            settings.Navigate("network");
            if(Field<TextBlock?>(settings,"windowStarterStatus") is not null)throw new Exception("Window starter status was retained after leaving its account page.");
            account.Extra["isAdditionalAccount"]="true";
            settings.Navigate("account:"+account.Id);
            if(Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Any(c=>Equals(c.Content,"5 小时重置后发送一次 hi 并刷新额度")))throw new Exception("Additional account offered the local window starter.");
            if(account.Extra.ContainsKey("windowStarter"))throw new Exception("Inspecting window starter settings changed the account's choice.");
            account.Extra.Remove("isAdditionalAccount");
            app.Secrets.Set(account.Id,"fixture-explicit-login");
            settings.Navigate("account:"+account.Id);
            if(Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Any(c=>Equals(c.Content,"5 小时重置后发送一次 hi 并刷新额度")))throw new Exception("Explicit credential account offered the local window starter.");
            if(account.Extra.ContainsKey("windowStarter"))throw new Exception("Inspecting explicit credentials changed the account's choice.");
            app.Secrets.Set(account.Id,"{\"accountId\":\"rail-fixture-account\",\"accessToken\":\"fixture-token\"}");
            settings.Navigate("account:"+account.Id);
            if(!HasWindowStarterCheck(settings))throw new Exception("Matching saved Codex identity did not offer the local window starter.");
            app.Secrets.Set(account.Id,"{\"accountId\":\"different-account\",\"accessToken\":\"fixture-token\"}");
            settings.Navigate("account:"+account.Id);
            if(HasWindowStarterCheck(settings))throw new Exception("Different saved Codex identity offered the local window starter.");
            app.Secrets.Set(account.Id,null);app.Secrets.Set("desktop:"+account.Id,"fixture-desktop-login");
            settings.Navigate("account:"+account.Id);
            if(HasWindowStarterCheck(settings))throw new Exception("Desktop credential account offered the local window starter.");
        }
        finally{app.Secrets.Set(account.Id,null);app.Secrets.Set("desktop:"+account.Id,null);account.Extra.Remove("isAdditionalAccount");account.ProviderId=providerId;settings.Navigate("account:"+account.Id);}
    }
    private static bool HasWindowStarterCheck(SettingsWindow settings)=>Descendants((DependencyObject)settings.Content).OfType<CheckBox>().Any(c=>Equals(c.Content,"5 小时重置后发送一次 hi 并刷新额度"));
    private static void ShortRefreshChecks(PulseApp app,SettingsWindow settings)
    {
        var originalCulture=System.Globalization.CultureInfo.CurrentCulture;
        app.Activities["short-refresh-fixture"]=true;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            settings.Navigate("network");
            if(!Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Any(t=>t.Text=="开始时间与结束时间相同表示全天执行。"))throw new Exception("Network page does not explain all-day window starter hours.");
            var selector=RefreshSelector(settings);
            var labels=selector.Items.OfType<ComboBoxItem>().Select(item=>item.Tag as string).ToArray();
            if(!labels.SequenceEqual(new[]{"10 秒","30 秒","1 分钟","2 分钟","5 分钟","10 分钟","15 分钟","30 分钟"}))throw new Exception("Active refresh options did not retain all second and minute intervals with explicit units.");
            foreach(var (label,seconds,minutes) in new[]{("30 秒",30,.5),("10 秒",10,1d/6)})
            {
                selector.SelectedValue=label;
                Equal(minutes,app.Engine.Config.RefreshMinutes,"Selected refresh minutes");
                Equal(seconds,Field<DispatcherTimer>(app,"refresh").Interval.TotalSeconds,"Immediately applied refresh seconds");
                Equal(minutes,app.Storage.LoadConfig().RefreshMinutes,"Saved refresh minutes");
                settings.Navigate("appearance");settings.Navigate("network");selector=RefreshSelector(settings);
                if(!Equals(selector.SelectedValue,label))throw new Exception("Reopening the network page lost the selected short refresh interval.");
            }
            foreach(var (minutes,label,seconds) in new[]{(90d,"30 分钟",1800d),(.01,"10 秒",10d),(3d,"3 分钟",180d),(.75,"45 秒",45d)})
            {
                app.Engine.Config.RefreshMinutes=minutes;settings.Navigate("network");selector=RefreshSelector(settings);
                if(!Equals(selector.SelectedValue,label))throw new Exception("Saved refresh interval was displayed differently from its effective active interval.");
                app.Save();Equal(seconds,Field<DispatcherTimer>(app,"refresh").Interval.TotalSeconds,"Displayed effective refresh interval");
            }
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=originalCulture;app.Activities.Remove("short-refresh-fixture");}
    }
    private static ComboBox RefreshSelector(SettingsWindow settings)=>Descendants((DependencyObject)settings.Content).OfType<ComboBox>().SingleOrDefault(box=>box.Items.OfType<ComboBoxItem>().Any(item=>Equals(item.Tag,"10 秒")))??throw new Exception("Active refresh options omit the requested 10-second and 30-second intervals.");
    private static void WindowStarterClaimChecks(PulseApp app,AccountConfig account)
    {
        var config=app.Engine.Config;
        var providerId=account.ProviderId;
        var enabled=account.Enabled;
        var hadChoice=account.Extra.TryGetValue("windowStarter",out var choice);
        var starterEnabled=config.WindowStarterEnabled;
        var startHour=config.WindowStarterStartHour;
        var endHour=config.WindowStarterEndHour;
        var coordinator=Field<WindowResetCoordinator>(app,"windowResets");
        var savedState=Field<string>(app,"savedWindowResetState");
        var statePath=Path.Combine(app.Storage.Root,"window-starter-state.json");
        if(Directory.Exists(statePath))throw new Exception("Synthetic window starter state path is already a directory.");
        var originalState=File.Exists(statePath)?File.ReadAllBytes(statePath):null;
        var backup=statePath+"."+Guid.NewGuid().ToString("N")+".rail-backup";
        var second=new AccountConfig{Id="second-local-codex-fixture",ProviderId="codex",Enabled=true,Extra=new(){{"windowStarter","true"}}};
        config.Accounts.Add(second);
        try
        {
            account.ProviderId="codex";account.Enabled=true;account.Extra["windowStarter"]="true";
            config.WindowStarterEnabled=true;config.WindowStarterStartHour=0;config.WindowStarterEndHour=0;
            SetField(app,"windowResets",new WindowResetCoordinator());
            SetField(app,"savedWindowResetState","");
            if(!CanStartWindow(app,account)||CanStartWindow(app,second))throw new Exception("Only the first local account for a provider may start its window.");

            var now=DateTimeOffset.UtcNow;
            var reset=now.AddMinutes(1);
            var reading=new UsageReading{AccountId=account.Id,ProviderId="codex",ObservedAt=now,Windows=[new UsageWindow{Id="five-hour-fixture",Kind="fiveHour",UsedPercent=20,ResetsAt=reset}]};
            if(ClaimWindowReset(app,account,reading,now) is not null)throw new Exception("Future five-hour reset was claimed before it was due.");
            if(!File.Exists(statePath))throw new Exception("Future five-hour reset plan was not saved.");
            File.Move(statePath,backup);
            Directory.CreateDirectory(statePath);
            try
            {
                try{ClaimWindowReset(app,account,reading,reset);throw new Exception("Claim unexpectedly succeeded when its state file could not be saved.");}
                catch(TargetInvocationException exception) when(exception.InnerException is IOException or UnauthorizedAccessException){}
            }
            finally{Directory.Delete(statePath);File.Move(backup,statePath);}
            if(ClaimWindowReset(app,account,reading,reset) is not { } job||job.ResetAt!=reset)throw new Exception("Failed persistence consumed the five-hour reset claim.");
            if(ClaimWindowReset(app,account,reading,reset) is not null)throw new Exception("A saved five-hour reset claim was repeated.");
        }
        finally
        {
            if(Directory.Exists(statePath))Directory.Delete(statePath);
            if(File.Exists(backup))File.Move(backup,statePath,true);
            if(originalState is null){if(File.Exists(statePath))File.Delete(statePath);}
            else File.WriteAllBytes(statePath,originalState);
            SetField(app,"windowResets",coordinator);SetField(app,"savedWindowResetState",savedState);
            config.Accounts.Remove(second);account.ProviderId=providerId;account.Enabled=enabled;
            if(hadChoice)account.Extra["windowStarter"]=choice!;else account.Extra.Remove("windowStarter");
            config.WindowStarterEnabled=starterEnabled;config.WindowStarterStartHour=startHour;config.WindowStarterEndHour=endHour;
        }
    }
    private static bool CanStartWindow(PulseApp app,AccountConfig account)=>(bool)typeof(PulseApp).GetMethod("CanStartWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,[account])!;
    private static WindowResetJob? ClaimWindowReset(PulseApp app,AccountConfig account,UsageReading reading,DateTimeOffset now)=>(WindowResetJob?)typeof(PulseApp).GetMethod("ClaimWindowReset",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,[account,reading,now]);
    private static void CorruptWindowStarterHistoryChecks(PulseApp app,AccountConfig account)
    {
        var config=app.Engine.Config;
        var providerId=account.ProviderId;
        var enabled=account.Enabled;
        var hadChoice=account.Extra.TryGetValue("windowStarter",out var choice);
        var starterEnabled=config.WindowStarterEnabled;
        var coordinator=Field<WindowResetCoordinator>(app,"windowResets");
        var savedState=Field<string>(app,"savedWindowResetState");
        var unavailable=Field<bool>(app,"windowResetStateUnavailable");
        var statePath=Path.Combine(app.Storage.Root,"window-starter-state.json");
        if(Directory.Exists(statePath))throw new Exception("Synthetic window starter state path is already a directory.");
        var originalState=File.Exists(statePath)?File.ReadAllBytes(statePath):null;
        try
        {
            account.ProviderId="codex";account.Enabled=true;account.Extra["windowStarter"]="true";config.WindowStarterEnabled=true;
            foreach(var badJson in new[]{"not JSON", "{\"accounts\":null}", "{\"accounts\":{\"fixture\":{\"plannedResets\":null}}}"})
            {
                var badBytes=System.Text.Encoding.UTF8.GetBytes(badJson);
                File.WriteAllBytes(statePath,badBytes);
                Invoke(app,"LoadWindowResetState");
                if(!Field<bool>(app,"windowResetStateUnavailable"))throw new Exception("Corrupt window starter history did not pause automatic sending.");
                if(!app.WindowStarterStatus(account.Id).Contains("自动发送已暂停",StringComparison.Ordinal))throw new Exception("Corrupt window starter history did not display its paused status.");
                if(!File.ReadAllBytes(statePath).SequenceEqual(badBytes))throw new Exception("Loading corrupt window starter history overwrote its original bytes.");
            }
        }
        finally
        {
            if(originalState is null){if(File.Exists(statePath))File.Delete(statePath);}
            else File.WriteAllBytes(statePath,originalState);
            SetField(app,"windowResets",coordinator);SetField(app,"savedWindowResetState",savedState);SetField(app,"windowResetStateUnavailable",unavailable);
            account.ProviderId=providerId;account.Enabled=enabled;
            if(hadChoice)account.Extra["windowStarter"]=choice!;else account.Extra.Remove("windowStarter");
            config.WindowStarterEnabled=starterEnabled;
        }
    }
    private static void WindowStarterReceiptMetadataChecks(PulseApp app,AccountConfig account)
    {
        var config=app.Engine.Config;
        var providerId=account.ProviderId;
        var enabled=account.Enabled;
        var hadChoice=account.Extra.TryGetValue("windowStarter",out var choice);
        var starterEnabled=config.WindowStarterEnabled;
        var reports=Field<Dictionary<string,PulseApp.WindowStarterReport>>(app,"windowStarterReports");
        var path=Path.Combine(app.Storage.Root,"window-starter-last.json");
        if(Directory.Exists(path))throw new Exception("Synthetic window starter report path is already a directory.");
        var original=File.Exists(path)?File.ReadAllBytes(path):null;
        try
        {
            account.ProviderId="codex";account.Enabled=true;account.Extra["windowStarter"]="true";config.WindowStarterEnabled=true;
            SetField(app,"windowStarterReports",new Dictionary<string,PulseApp.WindowStarterReport>());
            var job=new WindowResetJob(account.Id,"codex",DateTimeOffset.UtcNow);
            var process=new ProcessResult(0,"SECRET-fixture-stdout","SECRET-fixture-stderr",false);
            Invoke(app,"SetWindowStarterReport",job,"success",new WindowStarterReceipt("success",true,123,5,null),process);
            var successText=File.ReadAllText(path);
            if(successText.Contains("SECRET-fixture",StringComparison.Ordinal))throw new Exception("Raw process output entered window starter report.");
            using(var document=System.Text.Json.JsonDocument.Parse(successText))
            {
                var report=document.RootElement.GetProperty(account.Id);
                if(!report.GetProperty("completed").GetBoolean()||report.GetProperty("inputTokens").GetInt32()!=123||
                   report.GetProperty("outputTokens").GetInt32()!=5||report.GetProperty("exitCode").GetInt32()!=0||
                   report.GetProperty("timedOut").GetBoolean())throw new Exception("Completed receipt metadata was not saved correctly.");
                var observed=app.Engine.Reading(account.Id)?.ObservedAt;
                if(observed is not null&&report.GetProperty("quotaObservedAt").GetDateTimeOffset()!=observed)
                    throw new Exception("Saved quota observation time differs from the synthetic reading.");
            }
            Invoke(app,"SetWindowStarterReport",job,"turnIncomplete",new WindowStarterReceipt("failed",false,null,null,"turnIncomplete"),process);
            var incompleteText=File.ReadAllText(path);
            if(incompleteText.Contains("SECRET-fixture",StringComparison.Ordinal))throw new Exception("Raw failed process output entered window starter report.");
            using(var document=System.Text.Json.JsonDocument.Parse(incompleteText))
            {
                var report=document.RootElement.GetProperty(account.Id);
                if(report.GetProperty("completed").GetBoolean()||report.GetProperty("failureKind").GetString()!="turnIncomplete"||
                   report.GetProperty("status").GetString()!="turnIncomplete")
                    throw new Exception("Incomplete receipt metadata was not saved correctly.");
            }
            if(!app.WindowStarterStatus(account.Id).Contains("未确认请求完成",StringComparison.Ordinal))
                throw new Exception("Incomplete Codex turn was displayed as a completed send.");
        }
        finally
        {
            if(original is null){if(File.Exists(path))File.Delete(path);}
            else File.WriteAllBytes(path,original);
            SetField(app,"windowStarterReports",reports);
            account.ProviderId=providerId;account.Enabled=enabled;
            if(hadChoice)account.Extra["windowStarter"]=choice!;else account.Extra.Remove("windowStarter");
            config.WindowStarterEnabled=starterEnabled;
        }
    }
    private static void ShowSettingsRefreshChecks(PulseApp app,AccountConfig account,AccountConfig extra)
    {
        var config=app.Engine.Config;
        var providerId=account.ProviderId;
        var enabled=account.Enabled;
        var extraEnabled=extra.Enabled;
        var refreshMinutes=config.RefreshMinutes;
        var refresh=Field<DispatcherTimer>(app,"refresh");
        var priorInterval=refresh.Interval;
        var reading=app.Engine.Reading(account.Id)??throw new Exception("Synthetic cached reading is missing.");
        var priorError=reading.ErrorCode;
        var priorStale=reading.IsStale;
        var priorObserved=reading.ObservedAt;
        WaitUntil(()=>!app.Engine.IsRefreshing,"Synthetic provider was still refreshing before settings check.");
        var sawRefreshRequest=false;
        void ObserveRefresh()=>sawRefreshRequest|=app.Engine.IsRefreshing;
        app.Engine.Changed+=ObserveRefresh;
        try
        {
            account.ProviderId="unknown-offline-fixture";account.Enabled=true;extra.Enabled=false;
            config.RefreshMinutes=.5;
            reading.ErrorCode="unreachable";reading.IsStale=true;reading.ObservedAt=DateTimeOffset.UtcNow.AddMinutes(-10);
            refresh.Interval=TimeSpan.FromMinutes(30);
            app.ShowSettings("accounts");
            var opened=Field<SettingsWindow?>(app,"settings");
            if(opened is null||!opened.IsVisible)throw new Exception("Settings did not open for the stale-cache check.");
            Equal(30,refresh.Interval.TotalSeconds,"Opening settings restores the configured active refresh interval");
            if(!sawRefreshRequest)throw new Exception("Opening settings did not immediately queue a refresh for stale cached usage.");
        }
        finally
        {
            Field<SettingsWindow?>(app,"settings")?.Close();
            app.Engine.Changed-=ObserveRefresh;
            account.ProviderId=providerId;account.Enabled=enabled;extra.Enabled=extraEnabled;
            config.RefreshMinutes=refreshMinutes;refresh.Interval=priorInterval;
            reading.ErrorCode=priorError;reading.IsStale=priorStale;reading.ObservedAt=priorObserved;
        }
    }
    private static void AutoCollapseChecks(PulseApp app)
    {
        var config=app.Engine.Config;
        var accountState=config.Accounts.Select(account=>(Account:account,account.Enabled,account.Detailed,account.ProviderId)).ToArray();
        foreach(var account in config.Accounts)account.Enabled=false;
        config.ShowPanel=true;config.AutoCollapse=true;config.DockEdge="top";config.HideFullscreen=false;config.FollowDisplay=false;config.PanelOpacity=0;config.SettingsShortcut=null;config.PanelShortcut=null;
        var rail=new RailWindow(app);
        try
        {
            // The synthetic rail stays transparent; these checks send no native pointer input.
            rail.Show();RunFor(80);Field<DispatcherTimer>(rail,"screenTick").Stop();SetMouseOver(rail,false);
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Docked rail did not collapse after startup without a mouse enter/leave.");
            rail.ApplySettings();
            if(Field<bool>(rail,"collapsed"))throw new Exception("Saving settings did not first expand the rail.");
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Saving settings left the docked rail expanded indefinitely.");
            rail.ApplySettings();
            for(var refresh=0;refresh<7;refresh++){rail.Render();RunFor(80);}
            if(!Field<bool>(rail,"collapsed"))throw new Exception("Repeated reading renders postponed automatic collapse indefinitely.");
            config.AutoCollapse=false;rail.ApplySettings();RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Disabling automatic collapse did not restore the expanded rail.");
            config.AutoCollapse=true;config.DockEdge="free";rail.ApplySettings();RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("A freely positioned rail collapsed.");
            config.DockEdge="top";rail.ApplySettings();
            SetMouseOver(rail,true);RaiseMouse(rail,"surface",Mouse.MouseEnterEvent);RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Rail collapsed while the pointer hovered over it.");
            SetMouseOver(rail,false);RaiseMouse(rail,"surface",Mouse.MouseLeaveEvent);
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Rail did not collapse after its hover ended.");
            rail.ApplySettings();
            var popup=OpenDetail(rail,app);SetMouseOver(popup,true);RaiseMouse(rail,"surface",Mouse.MouseLeaveEvent);RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Rail collapsed while the pointer hovered over its detail window.");
            SetMouseOver(popup,false);Invoke(rail,"UpdateAutoCollapse");
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Rail did not collapse after detail hover ended.");
            rail.ApplySettings();
            var menu=Field<Border>(rail,"surface").ContextMenu!;menu.Opacity=0;menu.IsOpen=true;RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Rail collapsed while its context menu was open.");
            menu.IsOpen=false;
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Closing the context menu did not restart automatic collapse.");
            rail.ApplySettings();SetField(rail,"dragging",true);RaiseMouse(rail,"surface",Mouse.MouseLeaveEvent);RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Rail collapsed during dragging.");
            SetField(rail,"dragging",false);Invoke(rail,"UpdateAutoCollapse");
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Rail did not collapse after dragging ended.");
            rail.ApplySettings();popup=OpenDetail(rail,app);Invoke(rail,"SetFullscreenHidden",true);
            if(Field<Window?>(rail,"detail") is not null||popup.IsVisible)throw new Exception("Fullscreen hiding left the detail window above the fullscreen application.");
            RunFor(450);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Fullscreen hiding consumed the rail's idle collapse delay.");
            Invoke(rail,"SetFullscreenHidden",false);
            if(Field<bool>(rail,"collapsed"))throw new Exception("Rail collapsed immediately when fullscreen hiding ended.");
            WaitUntil(()=>Field<bool>(rail,"collapsed"),"Rail did not resume automatic collapse after fullscreen hiding ended.");
            rail.ApplySettings();popup=OpenDetail(rail,app);SetMouseOver(popup,true);rail.Close();
            if(Field<DispatcherTimer>(rail,"idle").IsEnabled)throw new Exception("Closing the rail restarted its idle timer when the detail window closed.");
        }
        finally{rail.Close();foreach(var state in accountState){state.Account.Enabled=state.Enabled;state.Account.Detailed=state.Detailed;state.Account.ProviderId=state.ProviderId;}}
    }
    private static Window OpenDetail(RailWindow rail,PulseApp app)
    {
        var account=app.Engine.Config.Accounts[0];account.Enabled=true;account.ProviderId="unknown-offline-fixture";account.Detailed=false;
        rail.Render();var tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().First();
        Invoke(rail,"ShowDetail",account,tile);var popup=Field<Window>(rail,"detail");popup.Opacity=0;return popup;
    }
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void SetField(object instance,string name,object value)=>instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(instance,value);
    private static void Invoke(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,args);
    private static void SetMouseOver(UIElement element,bool value)
    {
        var key=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        element.SetValue(key,value);
        var writeFlag=typeof(UIElement).GetMethod("WriteFlag",BindingFlags.Instance|BindingFlags.NonPublic)!;
        writeFlag.Invoke(element,[Enum.Parse(writeFlag.GetParameters()[0].ParameterType,"IsMouseOverCache"),value]);
        if(element.IsMouseOver!=value)throw new Exception("Synthetic WPF pointer hover was not applied.");
    }
    private static void RaiseMouse(RailWindow rail,string field,RoutedEvent routedEvent)=>Field<UIElement>(rail,field).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=routedEvent});
    private static void WaitUntil(Func<bool> condition,string message)
    {
        var timeout=DateTime.UtcNow.AddSeconds(2);
        while(!condition()&&DateTime.UtcNow<timeout)RunFor(20);
        if(!condition())throw new Exception(message);
    }
    private static void RunFor(int milliseconds)
    {
        var frame=new DispatcherFrame();var stop=new DispatcherTimer(DispatcherPriority.Send){Interval=TimeSpan.FromMilliseconds(milliseconds)};
        stop.Tick+=(_,_)=>{stop.Stop();frame.Continue=false;};stop.Start();Dispatcher.PushFrame(frame);
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var descendant in Descendants(child))yield return descendant;
    }
}
