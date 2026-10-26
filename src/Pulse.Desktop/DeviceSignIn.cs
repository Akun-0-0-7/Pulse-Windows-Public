namespace Pulse.Desktop;
public sealed class DeviceSignIn : Window
{
    private readonly CancellationTokenSource cancellation=new();
    public DeviceSignIn(PulseApp app,AccountConfig account)
    {
        Tag=account.Id;var credentialVersion=app.CredentialVersion(account.Id);Title=Localization.Translate("连接 ")+app.AccountName(account.Id);Width=500;Height=330;Background=Ui.Bg;WindowStartupLocation=WindowStartupLocation.CenterOwner;var panel=new StackPanel{Margin=new Thickness(28)};panel.Children.Add(Ui.Title("连接账号"));var note=Ui.Label("正在获取设备登录代码…",14,Ui.Muted);panel.Children.Add(note);Content=panel;Closed+=(_,_)=>cancellation.Cancel();Loaded+=async(_,_)=> {
            try {
                using var http=UsageEngine.CreateHttp(app.Engine.Config);var prompt=await HandwrittenLogin.BeginDeviceAsync(account.ProviderId,http,cancellation.Token);note.Text="在服务商页面输入下方代码，完成后回到这里。";panel.Children.Add(Ui.Label(prompt.UserCode,32,Ui.Accent));panel.Children.Add(Ui.Row(Ui.Button("复制代码",()=>Clipboard.SetText(prompt.UserCode)),Ui.Button("打开登录页面",()=>WindowsServices.Open(prompt.VerificationUrl),true)));
                while(!cancellation.IsCancellationRequested) {await Task.Delay(TimeSpan.FromSeconds(prompt.IntervalSeconds),cancellation.Token);var result=await HandwrittenLogin.PollDeviceAsync(prompt,http,cancellation.Token);if(result.Credential is {} credential){if(!app.CanSaveCredential(account.Id,credentialVersion)){Close();return;}app.Secrets.Set(account.Id,credential);app.Save();_=app.Engine.RefreshAsync();Close();return;}if(!result.Pending){note.Text=result.ErrorMessage??"登录没有完成，请重试。";return;}if(result.RetryAfterSeconds>prompt.IntervalSeconds)await Task.Delay(TimeSpan.FromSeconds(result.RetryAfterSeconds-prompt.IntervalSeconds),cancellation.Token);}
            }catch(OperationCanceledException){}catch(Exception exception) when(exception is System.Net.Http.HttpRequestException or InvalidOperationException or System.Text.Json.JsonException){note.Text="暂时无法连接登录服务，请检查网络和代理后重试。";}
        };
    }
}
