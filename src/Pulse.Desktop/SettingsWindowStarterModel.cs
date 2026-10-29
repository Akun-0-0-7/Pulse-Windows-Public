namespace Pulse.Desktop;

public sealed partial class SettingsWindow
{
    private void WindowStarterModel(AccountConfig account)
    {
        page.Children.Add(Ui.Label("自动 hi 使用的模型（留空跟随 Codex）",13,Ui.Muted));
        var input=Ui.Field(account.Extra.GetValueOrDefault("windowStarterModel")??"");
        input.Tag="window-starter-model";input.ToolTip="gpt-6.1-terra";
        var status=Ui.Label("",12,Ui.Muted);status.Tag="window-starter-model-status";
        void Commit()
        {
            var model=input.Text.Trim();
            if(model.Length>0&&!CodexStarterModel.IsValidName(model))
            {
                status.Text=Localization.Translate("请输入有效模型名称（例如 gpt-6.1-terra），或留空跟随 Codex。");
                return;
            }
            var hadModel=account.Extra.TryGetValue("windowStarterModel",out var before);
            if(model.Length==0)account.Extra.Remove("windowStarterModel");
            else account.Extra["windowStarterModel"]=model;
            if(model.Length==0?hadModel:before!=model)app.Save();
            input.Text=model;status.Text="";
        }
        var apply=Ui.Button("应用",Commit);apply.Tag="window-starter-model-apply";
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};
        input.LostKeyboardFocus+=(_,_)=>Commit();
        page.Children.Add(input);page.Children.Add(apply);page.Children.Add(status);
        page.Children.Add(Ui.Label("只用于自动发送和手动补发 hi，不改变 Codex 聊天模型。",11,Ui.Muted));
    }
}
