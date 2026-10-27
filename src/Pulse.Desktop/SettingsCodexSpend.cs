namespace Pulse.Desktop;

public sealed partial class SettingsWindow
{
    private void CodexSpendDirectory(AppConfig config)
    {
        page.Children.Add(Ui.Label("Codex 数据目录（可选）",13,Ui.Muted));
        var input=Ui.Field(config.CodexSpendHome??"");input.Tag="codex-spend-home";
        var info=Ui.Label("",11,Ui.Muted);info.Tag="codex-spend-roots";
        var status=Ui.Label("",12,Ui.Muted);status.Tag="codex-spend-home-status";
        void ShowRoots()
        {
            var environment=Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(item=>(string)item.Key,item=>(string)item.Value!,StringComparer.OrdinalIgnoreCase);
            var source=SpendSources.All.Single(source=>source.Id=="codex");
            var roots=SpendSources.ResolvePaths(source,LocalPaths.Home,environment,config.CodexSpendHome);
            info.Text=Localization.Format("Codex 日志目录：{0}",string.Join(Environment.NewLine,roots));
        }
        void Commit()
        {
            string? directory=string.IsNullOrWhiteSpace(input.Text)?null:input.Text.Trim();
            try
            {
                if(directory is not null)
                {
                    if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))throw new ArgumentException();
                    directory=Path.GetFullPath(directory);
                    if(!Directory.Exists(Path.Combine(directory,"sessions"))&&!Directory.Exists(Path.Combine(directory,"archived_sessions")))
                    {status.Text=Localization.Translate("选择包含 sessions 或 archived_sessions 的 Codex 数据目录");return;}
                }
            }
            catch(Exception exception)when(exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {status.Text=Localization.Translate("请输入已存在的完整数据目录，或留空使用自动检测。" );return;}
            config.CodexSpendHome=directory;app.Save();input.Text=directory??"";ShowRoots();
            status.Text=Localization.Translate("目录已保存，请点击“重新扫描”更新统计。" );
        }
        var apply=Ui.Button("应用目录",Commit);apply.Tag="codex-spend-home-apply";
        var automatic=Ui.Button("自动检测",()=>{input.Text="";Commit();});automatic.Tag="codex-spend-home-auto";
        var browse=Ui.Button("选择目录",()=>
        {
            using var picker=new System.Windows.Forms.FolderBrowserDialog{Description=Localization.Translate("选择包含 sessions 或 archived_sessions 的 Codex 数据目录"),UseDescriptionForTitle=true};
            if(Directory.Exists(input.Text))picker.SelectedPath=input.Text;
            if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK){input.Text=picker.SelectedPath;Commit();}
        });
        input.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){Commit();e.Handled=true;}};
        page.Children.Add(input);page.Children.Add(Ui.Row(apply,browse,automatic));page.Children.Add(info);page.Children.Add(status);
        page.Children.Add(Ui.Label("本机 Token 统计读取会话日志，不依赖 Codex 可执行文件版本。留空时优先使用 CODEX_HOME，再使用默认目录。",11,Ui.Muted));
        ShowRoots();
    }
}
