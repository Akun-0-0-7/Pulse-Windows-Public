namespace Pulse.Desktop;
public sealed partial class SettingsWindow : Window
{
    private readonly PulseApp app; private readonly StackPanel sidebar=new(); private readonly StackPanel page=new(); private readonly TextBlock status=Ui.Label("",12,Ui.Muted); private string current="accounts";
    private readonly Stack<string> back=new(); private readonly Stack<string> forward=new(); private bool navigating;
    public string CurrentPage=>current;
    private Border? accountReading;
    private TextBlock? windowStarterStatus;
    private Button? windowStarterRetryButton;
    private TextBlock? shortcutStatus;
    private readonly DispatcherTimer statusRefresh=new(){Interval=TimeSpan.FromMinutes(5)};
    public SettingsWindow(PulseApp app)
    {
        Ui.StyleSettingsScrollBars(this);
        SettingsChrome.Attach(this);
        this.app=app;Title=Localization.Translate("Pulse · 设置");Width=1100;Height=790;MinWidth=850;MinHeight=580;Background=Ui.Bg;Foreground=Ui.Text;FontFamily=new FontFamily("Microsoft YaHei UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var layout=new Grid{Background=Ui.Bg};TextOptions.SetTextFormattingMode(layout,TextFormattingMode.Display);layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});layout.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel {Background=Ui.Card,Margin=new Thickness(0,0,1,0)};
        var brand=new StackPanel {Margin=new Thickness(22,25,22,14)};brand.Children.Add(Ui.Label("PULSE",25,Ui.Accent));brand.Children.Add(Ui.Label("AI 额度，随时看见。",12,Ui.Muted));brand.Children.Add(Ui.Field("",text=>BuildSidebar(text)));DockPanel.SetDock(brand,Dock.Top);left.Children.Add(brand);
        var footer=new StackPanel{Margin=new Thickness(16)};footer.Children.Add(Ui.Button("刷新额度",()=>_=app.Engine.RefreshAsync()));footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);left.Children.Add(footer);left.Children.Add(new ScrollViewer{Content=sidebar,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});layout.Children.Add(left);
        var right=new DockPanel {Margin=new Thickness(30,24,30,24)};Grid.SetColumn(right,1);var navigation=Ui.Row(Ui.Button("←",GoBack),Ui.Button("→",GoForward));DockPanel.SetDock(navigation,Dock.Top);right.Children.Add(navigation);right.Children.Add(new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});layout.Children.Add(right);Content=layout;BuildSidebar();statusRefresh.Tick+=(_,_)=>{if(current=="status"&&IsVisible)Navigate("status");};statusRefresh.Start();Closed+=(_,_)=>{statusRefresh.Stop();scanning?.Cancel();};
    }
    private void BuildSidebar(string filter="")
    {
        sidebar.Children.Clear();
        foreach(var entry in new (string id,string title)[]{("dashboard","用量总览"),("accounts","服务与账号"),("appearance","外观与圆环"),("position","位置与行为"),("general","通用与快捷键"),("notifications","通知提醒"),("network","网络与刷新"),("spend","Token 消耗"),("reports","用量报告"),("status","服务状态"),("diagnostics","连接诊断"),("extensions","扩展"),("integrations","开发者集成"),("about","关于与更新")}) {
            if(filter.Length>0&&!entry.title.Contains(filter,StringComparison.OrdinalIgnoreCase))continue;var id=entry.id;var button=Ui.Button(entry.title,()=>Navigate(id));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(12,0,12,5);button.BorderThickness=new Thickness(0);if(current==id)button.Background=Ui.Selection;sidebar.Children.Add(button);
        }
        if(app.Engine.Config.Accounts.Count>0)sidebar.Children.Add(Ui.Label("账号",11,Ui.Muted));
        foreach(var account in app.Engine.Config.Accounts.Where(a=>a.Enabled)) {var label=app.AccountName(account.Id);if(filter.Length>0&&!label.Contains(filter,StringComparison.OrdinalIgnoreCase))continue;var id=account.Id;var button=Ui.Button(label,()=>Navigate("account:"+id));button.Margin=new Thickness(12,0,12,5);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.BorderThickness=new Thickness(0);sidebar.Children.Add(button);}
    }
    public void Navigate(string id)
    {
        if(!navigating&&id!=current){back.Push(current);forward.Clear();}current=id;accountReading=null;windowStarterStatus=null;windowStarterRetryButton=null;page.Children.Clear();BuildSidebar();
        if(id.StartsWith("account:")){AccountPage(id[8..]);return;}
        switch(id){case "onboarding":Onboarding();break;case "dashboard":Dashboard();break;case "accounts":Accounts();break;case "appearance":Appearance();break;case "position":Position();break;case "general":General();break;case "notifications":Notifications();break;case "network":Network();break;case "spend":Spend();break;case "reports":Reports();break;case "status":Status();break;case "diagnostics":Diagnostics();break;case "extensions":Extensions();break;case "integrations":Integrations();break;default:About();break;}UpdateStatus();
    }
    private void GoBack(){if(back.Count==0)return;forward.Push(current);navigating=true;Navigate(back.Pop());navigating=false;}
    private void GoForward(){if(forward.Count==0)return;back.Push(current);navigating=true;Navigate(forward.Pop());navigating=false;}
    public void UpdateStatus(){status.Text=Localization.Translate(app.Engine.IsRefreshing?"正在读取所选服务…":$"{app.Engine.Config.Accounts.Count(a=>a.Enabled)} 个账号已启用");}
    public void UpdateReadings()
    {
        UpdateStatus();
        if(current=="dashboard"&&!app.Engine.IsRefreshing){page.Children.Clear();Dashboard();}
        else if(accountReading is not null&&current.StartsWith("account:")&&app.Engine.Config.Accounts.FirstOrDefault(a=>a.Id==current[8..]) is {} account){accountReading.Child=RailWindow.DetailContent(app,account,true);if(windowStarterStatus is not null)windowStarterStatus.Text=app.WindowStarterStatus(account.Id);if(windowStarterRetryButton is not null)windowStarterRetryButton.IsEnabled=app.CanRetryWindowStarter(account.Id);}
    }
    public void RefreshPage()=>Navigate(current);
    public void UpdateShortcutStatus(){if(current=="general"&&shortcutStatus is not null)shortcutStatus.Text=string.Join(Environment.NewLine,app.Rail?.ShortcutErrors.Select(Localization.Translate)??[]);}
    private void Header(string title,string note){page.Children.Add(Ui.Title(title));page.Children.Add(Ui.Label(note,13,Ui.Muted));}
    private void AddCheck(string text,bool value,Action<bool> change)=>page.Children.Add(Ui.Check(text,value,enabled=>{change(enabled);app.Save();}));
    private void AddSelect(string title,IEnumerable<string> values,string value,Action<string> change){page.Children.Add(Ui.Label(title,13,Ui.Muted));page.Children.Add(Ui.Select(values,value,text=>{change(text);app.Save();}));}
    private void AddField(string title,string value,Action<string> change,bool multiline=false){page.Children.Add(Ui.Label(title,13,Ui.Muted));var field=Ui.Field(value,text=>{change(text);app.Save();});if(multiline){field.AcceptsReturn=true;field.TextWrapping=TextWrapping.Wrap;field.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;field.MinHeight=70;field.MaxHeight=150;}page.Children.Add(field);}
    private void Onboarding()
    {
        Header("欢迎使用 Pulse","先选择要监控的服务。点击“开始监控”后，Pulse 才会读取所选服务的本机登录并查询额度。手动密钥可稍后在账号设置中填写。");var selected=new HashSet<string>();
        page.Children.Add(Ui.Label("常用服务",16));
        var ordered=ProviderCatalog.All.OrderBy(p=>p.Id=="codex"?0:p.Id=="deepSeek"?1:p.Id=="claudeCode"?2:3).ThenBy(p=>p.Name).ToList();
        var actions=Ui.Row(Ui.Button("开始监控",()=>{foreach(var id in selected)if(!app.Engine.Config.Accounts.Any(a=>a.ProviderId==id))app.Engine.Config.Accounts.Add(new(){Id=id,ProviderId=id,Enabled=true});app.Engine.Config.OnboardingCompleted=true;app.Save();Navigate("dashboard");_=app.Engine.RefreshAsync();},true),Ui.Button("稍后设置",()=>{app.Engine.Config.OnboardingCompleted=true;app.Save();Navigate("accounts");}));page.Children.Add(actions);
        foreach(var provider in ordered)page.Children.Add(Ui.Check(provider.Name+"  ·  "+AuthLabel(provider.AuthKind),false,check=>{if(check)selected.Add(provider.Id);else selected.Remove(provider.Id);}));
    }
    private static string AuthLabel(string kind)=>kind switch{"localLogin"=>"使用本机工具登录","apiKey"=>"输入 API 密钥","keyAndAddress"=>"输入地址和密钥","sessionCookie"=>"连接网页会话","browserStorage"=>"连接网页登录","oauth"=>"通过服务商登录",_=>"连接服务账号"};
    private void Dashboard()
    {
        Header("用量总览","所有读数来自对应服务；详情显示数据来源和读取时间。");var accounts=app.Engine.Config.Accounts.Where(a=>a.Enabled).ToList();if(accounts.Count==0){page.Children.Add(Ui.Button("选择服务",()=>Navigate("onboarding"),true));return;}
        foreach(var account in accounts){var content=RailWindow.DetailContent(app,account,true);content.Width=double.NaN;page.Children.Add(Ui.Box(content));}
        page.Children.Add(Ui.Button("刷新全部",()=>_=app.Engine.RefreshAsync(),true));
    }
    private void Accounts()
    {
        Header("服务与账号","打开服务后读取额度；关闭后停止读取。每个账号的凭据分别加密保存在这台电脑。");
        foreach(var provider in ProviderCatalog.All.OrderBy(p=>p.Id=="codex"?0:p.Id=="deepSeek"?1:p.Id=="claudeCode"?2:3).ThenBy(p=>p.Name)) {
            var matching=app.Engine.Config.Accounts.Where(a=>a.ProviderId==provider.Id).ToList();var row=new StackPanel();
            row.Children.Add(Ui.Check(provider.Name+"  ·  "+AuthLabel(provider.AuthKind),matching.Any(a=>a.Enabled),enabled=>{var first=app.Engine.Config.Accounts.FirstOrDefault(a=>a.ProviderId==provider.Id);if(first is null&&enabled){first=new(){Id=provider.Id,ProviderId=provider.Id};app.Engine.Config.Accounts.Add(first);}if(first is not null)foreach(var a in app.Engine.Config.Accounts.Where(a=>a.ProviderId==provider.Id))a.Enabled=enabled;app.Save();if(enabled)_=app.Engine.RefreshAsync(waitForCurrent:true);}));
            var buttons=new WrapPanel();foreach(var account in matching){var id=account.Id;buttons.Children.Add(Ui.Button(app.AccountName(id)+" · 设置",()=>Navigate("account:"+id)));}
            if(provider.MultipleAccounts)buttons.Children.Add(Ui.Button("添加账号",()=>{var account=new AccountConfig{ProviderId=provider.Id,Enabled=true,Label=provider.Name+" 额外账号",Extra=new(){{"isAdditionalAccount","true"}}};app.Engine.Config.Accounts.Add(account);app.Save();Navigate("account:"+account.Id);}));row.Children.Add(buttons);page.Children.Add(Ui.Box(row));
        }
    }
    private void AccountPage(string id)
    {
        var account=app.Engine.Config.Accounts.FirstOrDefault(a=>a.Id==id);if(account is null){Header("账号已移除","可在服务与账号中重新添加。");return;}
        var definition=ProviderCatalog.Find(account.ProviderId);Header(app.AccountName(id),AuthLabel(definition?.AuthKind??""));page.Children.Add(Ui.Check("启用这个账号",account.Enabled,value=>{account.Enabled=value;app.Save();if(value)_=app.Engine.RefreshAsync(waitForCurrent:true);}));AddField("账号名称",account.Label,value=>account.Label=value);
        var credential=new PasswordBox {Background=Ui.Card,Foreground=Ui.Text,BorderBrush=Ui.BorderBrush,Padding=new Thickness(10),Margin=new Thickness(0,0,0,12)};
        page.Children.Add(Ui.Label("密钥或会话凭据（留空保留现有内容）",13,Ui.Muted));page.Children.Add(credential);
        page.Children.Add(Ui.Row(Ui.Button("保存凭据并读取",()=>{if(!string.IsNullOrWhiteSpace(credential.Password)){app.Secrets.Set(id,credential.Password);credential.Clear();}app.Save();_=app.Engine.RefreshAsync();},true),Ui.Button("清除已保存凭据",async()=>{await app.ClearCredentialsAsync(account);MessageBox.Show("此账号在 Pulse 保存的凭据已清除。","Pulse");})));
        if(account.ProviderId is "codex" or "copilot")page.Children.Add(Ui.Button("通过服务商登录",()=>new DeviceSignIn(app,account){Owner=this}.Show(),true));
        if(account.ProviderId is "claudeCode" or "grok")page.Children.Add(Ui.Button("通过浏览器授权登录",()=>new OAuthSignIn(app,account){Owner=this}.Show(),true));
        if(BrowserSignIn.Sources.ContainsKey(account.ProviderId))page.Children.Add(Ui.Button(account.ProviderId=="deepSeek"?"连接 DeepSeek 控制台":account.ProviderId=="claudeCode"?"连接 Claude 桌面会话":"通过网页连接账号",()=>new BrowserSignIn(app,account){Owner=this}.Show()));
        if(account.ProviderId=="codex")page.Children.Add(Ui.Label("主账号默认使用 Codex 已保存的登录；额外账号请导入自己的登录 JSON。可包含 access_token、refresh_token 和 account_id，不能借用主账号登录。",12,Ui.Muted));
        if(definition?.AuthKind.Contains("session",StringComparison.OrdinalIgnoreCase)==true||definition?.AuthKind.Contains("Cookie",StringComparison.OrdinalIgnoreCase)==true)page.Children.Add(Ui.Label("会话账号在凭据栏填写本账号的 Cookie 请求头；具体字段见“设置帮助”。",12,Ui.Muted));
        AddField("服务地址（自建网关）",account.ServerAddress??"",text=>account.ServerAddress=text);
        AddField("本机工具完整路径（可选）",account.Extra.GetValueOrDefault("executable")??"",text=>{if(string.IsNullOrWhiteSpace(text))account.Extra.Remove("executable");else account.Extra["executable"]=text;});
        AddField("本机数据目录或数据库（每行一个）",string.Join(Environment.NewLine,account.SourcePaths),text=>account.SourcePaths=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).ToList(),true);
        if(account.ProviderId is "codex" or "claudeCode" or "antigravity" or "volcengine" or "devin"){
            var routes=account.ProviderId switch{"claudeCode"=>new[]{"automatic","endpoint","status-line","desktop"},"antigravity"=>["automatic","desktopApp","tooling"],"devin"=>["automatic","endpoint","desktop-cache"],_=>["automatic","endpoint","tooling"]};
            AddSelect("读取通道",routes,account.ProviderId=="claudeCode"&&account.Route=="tooling"?"status-line":account.Route,text=>account.Route=text);
        }
        AddSelect("余额显示方式",["sinceTopUp","balanceOnly","budget"],account.BalanceMode,text=>account.BalanceMode=text);AddField("预算（与服务商余额相同币种）",account.Budget?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",text=>{account.Budget=decimal.TryParse(text,out var value)&&value>0?value:null;});
        AddField("自定义颜色（例如 #B5ADFF）",account.AccentColor??"",text=>account.AccentColor=string.IsNullOrWhiteSpace(text)?null:text);AddCheck("显示详细卡片",account.Detailed,value=>account.Detailed=value);
        AddSelect("机器人身体",BotRenderer.Bodies,BotRenderer.Bodies[int.TryParse(account.Extra.GetValueOrDefault("botBody"),out var body)?Math.Clamp(body,0,17):0],value=>account.Extra["botBody"]=Array.IndexOf(BotRenderer.Bodies,value).ToString());AddSelect("机器人性格",BotRenderer.Personalities,BotRenderer.Personalities[int.TryParse(account.Extra.GetValueOrDefault("botPersonality"),out var personality)?Math.Clamp(personality,0,7):0],value=>account.Extra["botPersonality"]=Array.IndexOf(BotRenderer.Personalities,value).ToString());
        if(account.ProviderId is "codex" or "claudeCode"&&app.UsesWindowStarterLogin(account)){
            page.Children.Add(Ui.Check("5 小时重置后发送一次 hi 并刷新额度",account.Extra.GetValueOrDefault("windowStarter")=="true",value=>{if(value&&MessageBox.Show("开启后，Pulse 会在 5 小时额度重置时通过本机工具发送一次 hi，并立即刷新额度。该请求会使用账号额度。是否启用？","窗口启动器",MessageBoxButton.YesNo)!=MessageBoxResult.Yes){Navigate(current);return;}account.Extra["windowStarter"]=value?"true":"false";app.Engine.Config.WindowStarterEnabled=app.Engine.Config.Accounts.Any(a=>a.Extra.GetValueOrDefault("windowStarter")=="true");app.Save();}));
            page.Children.Add(Ui.Label("仅处理 5 小时额度；每周重置不另发消息。",12,Ui.Muted));
            page.Children.Add(Ui.Label("每分钟检查额度；明确的网络或额度错误会自动补发。剩余恢复 100% 时仅发送一次。",12,Ui.Muted));
            if(account.ProviderId=="codex")WindowStarterModel(account);
            windowStarterStatus=Ui.Label(app.WindowStarterStatus(id),12,Ui.Muted);page.Children.Add(windowStarterStatus);
            windowStarterRetryButton=Ui.Button("再次发送 hi 并刷新额度",()=>_=app.RetryWindowStarterAsync(id));
            windowStarterRetryButton.IsEnabled=app.CanRetryWindowStarter(id);page.Children.Add(windowStarterRetryButton);
            page.Children.Add(Ui.Label("超时或中断时，请先查看额度；手动重试可能重复已被服务商接收的请求。自动发送遵守允许时段，手动重试可随时进行。",12,Ui.Muted));
        }
        var reading=app.Engine.Reading(id);if(reading?.Windows.Count>0)AddSelect("圆环主额度",new[]{"automatic"}.Concat(reading.Windows.Select(w=>w.Id)),account.PrimaryWindowId??"automatic",text=>account.PrimaryWindowId=text=="automatic"?null:text);
        accountReading=Ui.Box(RailWindow.DetailContent(app,account,true));page.Children.Add(accountReading);
        var actions=new WrapPanel();if(!string.IsNullOrWhiteSpace(definition?.HelpUrl))actions.Children.Add(Ui.Button("设置帮助",()=>WindowsServices.Open(definition.HelpUrl)));if(!string.IsNullOrWhiteSpace(definition?.UsageUrl))actions.Children.Add(Ui.Button("服务商用量页面",()=>WindowsServices.Open(definition.UsageUrl)));
        actions.Children.Add(Ui.Button("向上移动",()=>{var index=app.Engine.Config.Accounts.IndexOf(account);if(index>0){app.Engine.Config.Accounts.RemoveAt(index);app.Engine.Config.Accounts.Insert(index-1,account);app.Save();}}));actions.Children.Add(Ui.Button("移除账号",async()=>{account.Enabled=false;await app.ClearCredentialsAsync(account);app.Engine.Config.Accounts.Remove(account);app.Save();Navigate("accounts");}));page.Children.Add(actions);
    }
}
