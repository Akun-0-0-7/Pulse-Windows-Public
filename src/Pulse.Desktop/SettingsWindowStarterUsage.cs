namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private TextBlock? starterUsageSummary;
    private void WindowStarterUsageSettings(AccountConfig account)
    {
        page.Children.Add(Ui.Label("每日 hi 尝试上限（0 为不限，建议 6）",13,Ui.Muted));
        var input=Ui.Field(account.Extra.GetValueOrDefault("windowStarterDailyLimit")??"0");input.Tag="starter-daily-limit";
        var status=Ui.Label("",12,Ui.Muted);status.Tag="starter-daily-limit-status";
        void Commit()
        {
            if(!int.TryParse(input.Text.Trim(),out var value)||value is <0 or >1000){status.Text=Localization.Translate("请输入 0 到 1000 之间的整数。");return;}
            var text=value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(account.Extra.GetValueOrDefault("windowStarterDailyLimit")!=text){account.Extra["windowStarterDailyLimit"]=text;app.Save();}
            input.Text=text;status.Text="";UpdateReadings();
        }
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};
        input.LostKeyboardFocus+=(_,_)=>Commit();
        page.Children.Add(input);var apply=Ui.Button("应用",Commit);apply.Tag="starter-daily-limit-apply";page.Children.Add(apply);page.Children.Add(status);
        page.Children.Add(Ui.Label("按本机日期计数；自动和手动共用上限。发送前计一次，失败或结果未知仍占次数；配置检查不计数。",11,Ui.Muted));
        starterUsageSummary=Ui.Label(app.WindowStarterUsageSummary(account.Id),12,Ui.Muted);starterUsageSummary.Tag="starter-usage-summary";page.Children.Add(starterUsageSummary);
    }
}
