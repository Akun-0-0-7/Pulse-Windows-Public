namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private void StartupConsistency()
    {
        var service=StartupConsistencyService.Create();
        page.Children.Add(Ui.Label(Localization.Format("当前运行位置：{0}",Environment.ProcessPath??"—"),12,Ui.Muted));
        var url=AppReleaseInfo.ManifestUrl(app.Engine.Config.UpdateManifestUrl);
        var channel=url==AppReleaseInfo.StableManifestUrl?"稳定版":url==AppReleaseInfo.PreviewManifestUrl?"预览版":"自定义";
        page.Children.Add(Ui.Label(Localization.Format("更新渠道：{0}",Localization.Translate(channel)),12,Ui.Muted));
        var status=Ui.Label("",12,Ui.Muted);status.Tag="startup-consistency-status";page.Children.Add(status);
        var snapshot=service.Inspect(app.Engine.Config.StartWithWindows);
        string State(LaunchEntryState value)=>Localization.Translate(value switch{LaunchEntryState.Current=>"当前版本",LaunchEntryState.Disabled=>"未开启",LaunchEntryState.Missing=>"入口缺失",LaunchEntryState.DifferentVersion=>"指向其他版本",LaunchEntryState.DisabledButPresent=>"已关闭但仍有旧入口",LaunchEntryState.Unrecognized=>"入口包含其他命令，请手动检查",_=>"无法读取入口"});
        void Show()=>status.Text=Localization.Format("开机启动：{0}\n开始菜单：{1}",State(snapshot.Startup.State),State(snapshot.Shortcut.State));
        Show();
        Button? repair=null;
        repair=Ui.Button("修复 Pulse 启动入口",()=>
        {
            try{var success=app.Engine.Config.StartWithWindows==snapshot.StartWithWindows&&service.Repair(snapshot);snapshot=service.Inspect(app.Engine.Config.StartWithWindows);Show();if(!success)status.Text+="\n"+Localization.Translate("入口已变化或修复未完成，请重新检查。");}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Security.SecurityException){status.Text=Localization.Translate("无法修复启动入口，请检查目录权限。");}
            repair!.IsEnabled=snapshot.CanRepair;app.RefreshStartupEntryStatus();UpdateStatus();
        });
        repair.Tag="startup-consistency-repair";repair.IsEnabled=snapshot.CanRepair;page.Children.Add(repair);
        page.Children.Add(Ui.Label("仅修复 Pulse 的开机启动和开始菜单入口，保持开机启动开关不变。",11,Ui.Muted));
    }
}
