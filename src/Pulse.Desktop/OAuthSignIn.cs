using System.Net;
using System.Net.Sockets;
namespace Pulse.Desktop;
public sealed class OAuthSignIn : Window
{
    private readonly CancellationTokenSource cancellation=new(TimeSpan.FromMinutes(10));
    public OAuthSignIn(PulseApp app,AccountConfig account)
    {
        Tag=account.Id;var credentialVersion=app.CredentialVersion(account.Id);Title=Localization.Translate("授权登录 ")+app.AccountName(account.Id);Width=530;Height=340;Background=Ui.Bg;WindowStartupLocation=WindowStartupLocation.CenterOwner;var panel=new StackPanel{Margin=new Thickness(25)};panel.Children.Add(Ui.Title("在浏览器中连接账号"));var note=Ui.Label("正在准备登录…",14,Ui.Muted);panel.Children.Add(note);Content=panel;Closed+=(_,_)=>cancellation.Cancel();
        Loaded+=async(_,_)=>{
            try{
                var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();var redirect=$"http://127.0.0.1:{port}/callback";var prompt=HandwrittenLogin.BeginBrowser(account.ProviderId,redirect);
                using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var registration=cancellation.Token.Register(()=>listener.Stop());note.Text="已打开服务商授权页面。完成授权后，此窗口会保存当前账号登录。";panel.Children.Add(Ui.Button("打开授权页面",()=>WindowsServices.Open(prompt.AuthorizationUrl),true));WindowsServices.Open(prompt.AuthorizationUrl);
                using var client=UsageEngine.CreateHttp(app.Engine.Config);
                while(!cancellation.IsCancellationRequested){var request=await listener.GetContextAsync().WaitAsync(cancellation.Token);if(request.Request.Url is not {} uri){request.Response.StatusCode=400;request.Response.Close();continue;}
                    try{var credential=await HandwrittenLogin.CompleteBrowserAsync(prompt,uri.ToString(),client,cancellation.Token);if(!app.CanSaveCredential(account.Id,credentialVersion)){Close();return;}app.Secrets.Set(account.Id,credential);request.Response.ContentType="text/html; charset=utf-8";var body=System.Text.Encoding.UTF8.GetBytes("<html><body><h2>Pulse 登录已完成</h2><p>可以关闭此页面并返回 Pulse。</p></body></html>");await request.Response.OutputStream.WriteAsync(body,cancellation.Token);request.Response.Close();app.Save();_=app.Engine.RefreshAsync();Close();return;}
                    catch(InvalidOperationException){request.Response.StatusCode=400;request.Response.Close();note.Text="授权回调未通过检查，请重新打开授权页面。";}
                }
            }catch(OperationCanceledException){}catch(Exception exception)when(exception is HttpListenerException or System.Net.Http.HttpRequestException or InvalidOperationException or SocketException){if(!cancellation.IsCancellationRequested)note.Text="登录未完成。你可以重试，或使用本机已登录工具。";}
        };
    }
}
