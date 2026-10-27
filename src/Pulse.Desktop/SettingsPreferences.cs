namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private void Appearance()
    {
        var config=app.Engine.Config;Header("外观与圆环","调整浮动栏的读数、颜色和尺寸。");AddCheck("显示剩余额度",config.ShowRemaining,value=>config.ShowRemaining=value);AddCheck("显示第二条额度内环",config.ShowSecondaryRing,value=>config.ShowSecondaryRing=value);AddCheck("显示额度窗口时间弧",config.ShowClock,value=>config.ShowClock=value);AddCheck("分段进度条",config.SegmentedBars,value=>config.SegmentedBars=value);AddCheck("使用动画机器人标记",config.AnimatedMarks,value=>config.AnimatedMarks=value);
        page.Children.Add(Ui.Label("主题",13,Ui.Muted));page.Children.Add(Ui.Select(["dark","light","glass"],config.Theme,value=>{config.Theme=value;app.SaveAppearance();Navigate("appearance");}));
        AppearanceColors();
        AddSelect("圆环大小",["36","48","60","72"],config.RingSize.ToString(),value=>config.RingSize=double.Parse(value));AddSelect("圆环间距",["4","8","12","18"],config.RingGap.ToString(),value=>config.RingGap=double.Parse(value));AddField("面板不透明度（0.2 到 1）",config.PanelOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture),text=>{if(double.TryParse(text,out var opacity))config.PanelOpacity=Math.Clamp(opacity,.2,1);});
        AddSelect("托盘额度样式",["number","ring","dual"],config.TrayUsageMode,value=>config.TrayUsageMode=value);AddSelect("托盘显示账号",new[]{"automatic"}.Concat(config.Accounts.Where(a=>a.Enabled).Select(a=>a.Id)),config.TrayAccountId??"automatic",value=>config.TrayAccountId=value=="automatic"?null:value);AddCheck("点击托盘打开用量总览",config.TrayOpensDashboard,value=>config.TrayOpensDashboard=value);
    }
    private void Position()
    {
        var config=app.Engine.Config;Header("位置与行为","浮动栏可停靠在屏幕四边，也可拖动到任意位置。");AddSelect("停靠位置",["left","right","top","bottom","free"],config.DockEdge,value=>config.DockEdge=value);AddCheck("显示浮动栏",config.ShowPanel,value=>config.ShowPanel=value);AddCheck("横向排列",config.Horizontal,value=>config.Horizontal=value);AddCheck("拖动时吸附屏幕边缘",config.SnapToEdges,value=>config.SnapToEdges=value);AddCheck("停靠时自动收起",config.AutoCollapse,value=>config.AutoCollapse=value);AddCheck("跟随鼠标所在显示器",config.FollowDisplay,value=>config.FollowDisplay=value);AddCheck("全屏应用前隐藏",config.HideFullscreen,value=>config.HideFullscreen=value);AddCheck("从支持的截图和录屏中隐藏",config.HideCapture,value=>config.HideCapture=value);
    }
    private void General()
    {
        var config=app.Engine.Config;Header("通用与快捷键","快捷键默认未设置。关闭设置窗口后，Pulse 继续在托盘运行。");AddCheck("登录 Windows 时启动",config.StartWithWindows,value=>{config.StartWithWindows=value;WindowsServices.Startup(value);});AddSelect("界面语言",["zh-CN","zh-Hant","en","ja","ko","ru"],config.Language,value=>config.Language=value);AddSelect("估算金额显示币种",["USD","CNY","HKD","TWD","JPY","KRW","RUB"],config.Currency,value=>{if(config.Currency!=value)config.ManualExchangeRate=null;config.Currency=value;Dispatcher.BeginInvoke(()=>Navigate("general"));});AddField("手动汇率（1 美元等于所选货币的数量，留空使用公开汇率）",config.ManualExchangeRate?.ToString()??"",value=>config.ManualExchangeRate=decimal.TryParse(value,out var rate)&&rate>0?rate:null);AddField("打开设置快捷键（例如 Ctrl+Alt+P）",config.SettingsShortcut??"",value=>config.SettingsShortcut=value);AddField("显示/隐藏快捷键（例如 Ctrl+Alt+O）",config.PanelShortcut??"",value=>config.PanelShortcut=value);AddCheck("启用消耗速度预测",config.ForecastEnabled,value=>config.ForecastEnabled=value);
        page.Children.Add(Ui.Button("注册 pulse:// 账号链接",()=>{WindowsServices.RegisterProtocol();MessageBox.Show("已注册当前 Windows 用户的 Pulse 账号链接。","Pulse");}));
        shortcutStatus=Ui.Label("",12,Ui.Brush("#FFBA83"));page.Children.Add(shortcutStatus);UpdateShortcutStatus();
    }
    private void Notifications()
    {
        var config=app.Engine.Config;Header("通知提醒","通知默认关闭。服务商的原始耗尽状态、读取失败和余额分别处理。");AddCheck("启用额度通知",config.AlertsEnabled,value=>config.AlertsEnabled=value);
        foreach(var threshold in new[]{75,80,90,95})AddCheck($"已用额度达到 {threshold}%",config.AlertThresholds.Contains(threshold),value=>{config.AlertThresholds.Remove(threshold);if(value)config.AlertThresholds.Add(threshold);});
        AddCheck("已提醒的额度重置时通知",config.ResetAlerts,value=>config.ResetAlerts=value);AddCheck("连续读取失败时通知",config.FailureAlerts,value=>config.FailureAlerts=value);AddCheck("官方状态页报告故障时通知",config.StatusAlerts,value=>config.StatusAlerts=value);AddField("低余额提醒阈值（余额原币种，留空关闭）",config.LowBalanceThreshold?.ToString()??"",text=>config.LowBalanceThreshold=decimal.TryParse(text,out var threshold)?threshold:null);
    }
    private void Network()
    {
        var config=app.Engine.Config;Header("网络与刷新","各服务独立读取。失败时保留上次有效数据及读取时间。使用电脑、服务工作中或设置窗口打开时按所选间隔刷新；电脑空闲达到 2、5、10 分钟时，分别放慢至至少 5、10、30 分钟。");AddSelect("代理模式",["system","custom","none"],config.ProxyMode,value=>config.ProxyMode=value);AddField("自定义代理地址（例如 http://127.0.0.1:7890）",config.ProxyAddress??"",value=>config.ProxyAddress=value);
        var intervals=new[]{10,30,60,120,300,600,900,1800}.ToDictionary(seconds=>seconds<60?$"{seconds} 秒":$"{seconds/60} 分钟",seconds=>seconds/60d);
        var activeMinutes=RefreshSchedule.Minutes(config.RefreshMinutes,true,false,TimeSpan.Zero);
        var selected=intervals.FirstOrDefault(interval=>Math.Abs(interval.Value-activeMinutes)<.000001).Key;
        if(selected is null){var seconds=activeMinutes*60;selected=seconds<60?seconds.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" 秒":activeMinutes.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" 分钟";intervals[selected]=activeMinutes;}
        AddSelect("活跃刷新间隔",intervals.Keys,selected,label=>config.RefreshMinutes=intervals[label]);page.Children.Add(Ui.Button("现在刷新",()=>_=app.Engine.RefreshAsync(),true));
        AddSelect("窗口启动器允许开始时间",Enumerable.Range(0,24).Select(h=>h.ToString("00")),config.WindowStarterStartHour.ToString("00"),value=>config.WindowStarterStartHour=int.Parse(value));AddSelect("窗口启动器允许结束时间",Enumerable.Range(0,24).Select(h=>h.ToString("00")),config.WindowStarterEndHour.ToString("00"),value=>config.WindowStarterEndHour=int.Parse(value));
        page.Children.Add(Ui.Label("开始时间与结束时间相同表示全天执行。",12,Ui.Muted));
    }
    private void Diagnostics()
    {
        Header("连接诊断","响应仅在内存中保留；复制报告时隐藏凭据和账号信息。");var output=new System.Text.StringBuilder();
        foreach(var account in app.Engine.Config.Accounts.Where(a=>a.Enabled)){var reading=app.Engine.Reading(account.Id);output.AppendLine($"{ProviderCatalog.Find(account.ProviderId)?.Name??account.ProviderId} | {reading?.Source??"none"} | {reading?.ObservedAt:o} | {reading?.ErrorCode??"ok"}");var block=new StackPanel();block.Children.Add(Ui.Label(app.AccountName(account.Id),16));block.Children.Add(Ui.Label($"来源：{reading?.Source??"未读取"}   时间：{reading?.ObservedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"—"}"));if(reading?.ErrorMessage is {} error)block.Children.Add(Ui.Label(error,13,Ui.Brush("#FFBA83")));foreach(var diagnostic in reading?.Diagnostics??[]){var text=$"{diagnostic.Status} {diagnostic.Url}\n{diagnostic.Body}";output.AppendLine(text);block.Children.Add(new TextBox {Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Ui.Bg,Foreground=Ui.Muted});}page.Children.Add(Ui.Box(block));}
        page.Children.Add(Ui.Button("复制脱敏诊断",()=>Clipboard.SetText(Redaction.Clean(output.ToString()))));
    }
    private void Integrations()
    {
        Header("开发者集成","命令行读取本机缓存，不会额外查询服务商。");page.Children.Add(Ui.Label("Pulse-cli.exe --json\nPulse-cli.exe --recap --period month\nPulse-cli.exe --statusline\nPulse-cli.exe --install-statusline\nPulse-cli.exe --uninstall-statusline",14));page.Children.Add(Ui.Button("复制 PowerShell 示例",()=>Clipboard.SetText("& '"+Path.Combine(AppContext.BaseDirectory,"Pulse-cli.exe")+"' --json | ConvertFrom-Json")));page.Children.Add(Ui.Button("打开数据目录",()=>{Directory.CreateDirectory(PulseApp.DataRoot);WindowsServices.Open(PulseApp.DataRoot);}));
    }
    private void About()
    {
        Header("关于 Pulse Windows","Windows 移植版本 0.1.5 · 基于 Pulse 1.12.2");page.Children.Add(Ui.Label("原作者：qunqin24\n上游许可证：Apache-2.0\nWindows 版使用独立实现的桌面界面和本机存储。"));page.Children.Add(Ui.Button("上游项目",()=>WindowsServices.Open("https://github.com/qunqin24/Pulse")));AddField("Windows 更新清单地址（HTTPS）",app.Engine.Config.UpdateManifestUrl??"",text=>app.Engine.Config.UpdateManifestUrl=text);page.Children.Add(Ui.Button("检查更新",()=>_=app.CheckUpdates()));page.Children.Add(Ui.Button("打开许可证",()=>WindowsServices.Open(Path.Combine(AppContext.BaseDirectory,"LICENSE"))));
    }
}
