namespace Pulse.Desktop;
public sealed partial class SettingsWindow
{
    private CancellationTokenSource? starterCheck;
    private Action? updateStarterDiagnostics;
    private bool starterCloseHook;
    private Func<string,string?,string?,Dictionary<string,string>?,CancellationToken,Task<WindowStarterPreflightResult>> starterPreflight=WindowStarterPreflight.InspectAsync;
    private void ResetStarterDiagnostics(){starterCheck?.Cancel();starterCheck=null;updateStarterDiagnostics=null;}
    private void WindowStarterDiagnostics(AccountConfig account)
    {
        if(!starterCloseHook){Closed+=(_,_)=>ResetStarterDiagnostics();starterCloseHook=true;}
        var choice=Ui.Label("",12,Ui.Muted);choice.Tag="starter-next-model";page.Children.Add(choice);
        var status=Ui.Label("",12,Ui.Muted);status.Tag="starter-preflight-status";
        string Fingerprint()=>string.Join('\n',account.Extra.GetValueOrDefault("windowStarterModel"),account.Extra.GetValueOrDefault("executable"),app.Engine.Config.ProxyMode,app.Engine.Config.ProxyAddress,app.CredentialVersion(account.Id),account.Enabled);
        var checkedSettings=Fingerprint();
        updateStarterDiagnostics=()=>
        {
            choice.Text=Localization.Format("下次 hi 使用：{0}",string.IsNullOrWhiteSpace(account.Extra.GetValueOrDefault("windowStarterModel"))?Localization.Translate("跟随 Codex 当前配置"):account.Extra["windowStarterModel"]);
            if(checkedSettings!=Fingerprint())
            {checkedSettings=Fingerprint();starterCheck?.Cancel();status.Text=Localization.Translate("配置已变化，请重新检查；下方保留的是历史结果。");}
        };
        updateStarterDiagnostics();
        Button? check=null;
        async Task Inspect()
        {
            if(starterCheck is not null)return;
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(app.Engine.LifetimeToken);starterCheck=cancellation;
            var expected=Fingerprint();check!.IsEnabled=false;status.Text=Localization.Translate("正在只读检查，不会发送 hi…");
            try
            {
                if(!app.UsesWindowStarterLogin(account)){status.Text=Localization.Translate("当前账号与本机登录不匹配，不能发送 hi。");return;}
                if(app.Engine.Config.ProxyMode is not "system" and not "none"&&(!Uri.TryCreate(app.Engine.Config.ProxyAddress,UriKind.Absolute,out var proxyUri)||proxyUri.Scheme is not "http" and not "https" and not "socks5"||string.IsNullOrWhiteSpace(proxyUri.Host)))throw new InvalidOperationException("Invalid proxy configuration");
                var result=await starterPreflight(app.Storage.Root,account.Extra.GetValueOrDefault("executable"),account.Extra.GetValueOrDefault("windowStarterModel"),ProxyEnvironment.ForTool(app.Engine.Config),cancellation.Token);
                if(cancellation.IsCancellationRequested||current!="account:"+account.Id||Fingerprint()!=expected)return;
                var proxy=app.Engine.Config.ProxyMode switch{"system"=>"系统代理","none"=>"直连",_=>"自定义代理"};
                status.Text=Localization.Format("工具：{0}\n版本：{1}\n模型：{2}\n连接设置：{3}",result.ToolPath??"—",result.Version??"—",result.Model??"—",Localization.Translate(proxy))+"\n"+
                    Localization.Translate(result.ErrorCode is null?"配置检查通过，尚未验证服务端是否接受；本次没有发送消息。":"配置检查未通过，请检查工具及模型设置；本次没有发送消息。");
            }
            catch(OperationCanceledException){if(current=="account:"+account.Id&&updateStarterDiagnostics is not null)status.Text=Localization.Translate("检查已取消，请重新检查。");}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException){if(current=="account:"+account.Id)status.Text=Localization.Translate("无法完成配置检查，请检查工具及代理设置。");}
            finally{if(ReferenceEquals(starterCheck,cancellation))starterCheck=null;check!.IsEnabled=true;}
        }
        check=Ui.Button("检查 hi 配置（不发送）",()=>_=Inspect());check.Tag="starter-preflight-check";page.Children.Add(check);page.Children.Add(status);
        page.Children.Add(Ui.Label("历史记录不会因修改模型而变成成功；下一次实际发送后才更新结果。",11,Ui.Muted));
    }
}
