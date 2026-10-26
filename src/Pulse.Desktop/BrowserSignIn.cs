using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
namespace Pulse.Desktop;
public sealed record BrowserSessionSource(string Origin,string[] Cookies,string[]? StorageKeys=null,string? Slot=null);
public sealed class BrowserSignIn : Window
{
    private readonly WebView2 web=new();
    public static string BrowserArguments(AppConfig config)=>config.ProxyMode=="none"?"--no-proxy-server":config.ProxyMode=="custom"&&Uri.TryCreate(config.ProxyAddress,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "socks5"?"--proxy-server=\""+uri.GetComponents(UriComponents.SchemeAndServer,UriFormat.UriEscaped)+"\"":"";
    public async Task ClearProfileAsync(){if(web.CoreWebView2 is {} core)try{await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);}catch(System.Runtime.InteropServices.COMException){} }
    public static IReadOnlyDictionary<string,BrowserSessionSource> Sources {get;}=new Dictionary<string,BrowserSessionSource> {
        ["deepSeek"]=new("https://platform.deepseek.com",[],["userToken"],"console:"),
        ["claudeCode"]=new("https://claude.ai",["sessionKey","sessionKeyV3","lastActiveOrg"],Slot:"desktop:"),
        ["cursor"]=new("https://cursor.com",["WorkosCursorSessionToken"]),["grokBot"]=new("https://cursor.com",["WorkosCursorSessionToken"]),
        ["ollamaCloud"]=new("https://ollama.com",["wos-session*","__Secure-session*","__Secure-next-auth.session-token*","next-auth.session-token*"]),
        ["qoder"]=new("https://qoder.com",["*"]),["stepFun"]=new("https://platform.stepfun.com",["Oasis-Token","Oasis-Webid","INGRESSCOOKIE"]),
        ["xiaomiMiMo"]=new("https://platform.xiaomimimo.com",["api-platform_serviceToken","userId","api-platform_ph","api-platform_slh"]),
        ["augment"]=new("https://app.augmentcode.com",["_session","web_rpc_proxy_session"]),["abacus"]=new("https://abacus.ai",["sessionid","session_id","session_token","auth_token","access_token"]),
        ["longCat"]=new("https://longcat.chat",["passport_token","uid"]),["mistral"]=new("https://admin.mistral.ai",["ory_session_*","csrftoken"]),
        ["manus"]=new("https://manus.im",["session_id"]),["notionAI"]=new("https://www.notion.so",["token_v2"]),
        ["qwenCloud"]=new("https://home.qwencloud.com",["login_qwencloud_ticket","login_aliyunid_ticket","qwen_sso_ticket","login_current_pk","login_aliyunid_pk","login_aliyunid_csrf","csrf","cna","sec_token"]),
        ["perplexity"]=new("https://www.perplexity.ai",["__Secure-next-auth.session-token"]),["raycastAI"]=new("https://www.raycast.com",["__raycast_session","csrf_token"]),
        ["replicate"]=new("https://replicate.com",["sessionid","csrftoken"]),["sakana"]=new("https://console.sakana.ai",["__Secure-authjs.session-token"]),
        ["t3Chat"]=new("https://t3.chat",["wos-session","_vcrcs"]),["zoomMate"]=new("https://ai.zoom.us",["_zm_ssid","cf_clearance"]),["zed"]=new("https://zed.dev",["zed.session"]),
        ["windsurf"]=new("https://windsurf.com",[],["devin_session_token","devin_auth1_token","devin_account_id","devin_primary_org_id"]),
        ["devin"]=new("https://app.devin.ai",[],["auth1_session","devin_primary_org_id","devin_session_token","devin_auth1_token","devin_account_id"])
    };
    public BrowserSignIn(PulseApp app,AccountConfig account)
    {
        Tag=account.Id;var credentialVersion=app.CredentialVersion(account.Id);var source=Sources[account.ProviderId];Title=Localization.Translate("登录 ")+app.AccountName(account.Id);Width=1040;Height=800;Background=Ui.Bg;WindowStartupLocation=WindowStartupLocation.CenterOwner;var layout=new DockPanel();var top=new StackPanel {Margin=new Thickness(18)};top.Children.Add(Ui.Label("在服务商页面登录后，点击“保存本账号登录”。Pulse 只读取此服务所需的会话字段。",13));var note=Ui.Label("",12,Ui.Muted);top.Children.Add(note);top.Children.Add(Ui.Row(Ui.Button("保存本账号登录",()=>_=Capture(),true),Ui.Button("关闭",Close)));DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);layout.Children.Add(web);Content=layout;
        Loaded+=async(_,_)=>{try {var safeId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.Id)))[..20];var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(PulseApp.DataRoot,"browser-sessions",safeId),new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments=BrowserArguments(app.Engine.Config)});await web.EnsureCoreWebView2Async(environment);web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.IsPasswordAutosaveEnabled=false;web.CoreWebView2.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;web.Source=new Uri(source.Origin);}catch(Exception exception) when(exception is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException){note.Text="需要 Microsoft Edge WebView2 运行环境。可从微软官网下载后重新打开。";top.Children.Add(Ui.Button("打开微软下载页",()=>WindowsServices.Open("https://developer.microsoft.com/microsoft-edge/webview2/")));}};
        Closed+=(_,_)=>web.Dispose();
        async Task Capture()
        {
            if(web.CoreWebView2 is null)return;var actual=new Uri(web.CoreWebView2.Source);var origin=new Uri(source.Origin);if(actual.Scheme!="https"||actual.Host!=origin.Host){note.Text="请先回到该服务页面完成登录，再保存。";return;}
            string? credential=null;
            if(source.StorageKeys is {} keys){var script="JSON.stringify(Object.fromEntries("+JsonSerializer.Serialize(keys)+".map(k => [k,localStorage.getItem(k)])))";var encoded=await web.ExecuteScriptAsync(script);var text=JsonSerializer.Deserialize<string>(encoded);if(text is not null){using var doc=JsonDocument.Parse(text);if(account.ProviderId=="deepSeek"){var wrapper=JsonTools.String(doc.RootElement,"userToken");if(wrapper is not null){using var token=JsonDocument.Parse(wrapper);credential=JsonTools.String(token.RootElement,"value");}}else if(account.ProviderId=="devin"){var raw=JsonTools.String(doc.RootElement,"auth1_session");var token=raw is null?null:JsonTools.String(JsonDocument.Parse(raw).RootElement,"token");credential=token is null?null:token+" "+JsonTools.String(doc.RootElement,"devin_primary_org_id");}else if(keys.All(k=>!string.IsNullOrWhiteSpace(JsonTools.String(doc.RootElement,k))))credential=text;}}
            else {var cookies=await web.CoreWebView2.CookieManager.GetCookiesAsync(source.Origin);var selected=cookies.Where(c=>source.Cookies.Any(pattern=>pattern=="*"||pattern.EndsWith('*')?c.Name.StartsWith(pattern.TrimEnd('*'),StringComparison.Ordinal):c.Name==pattern)).Where(c=>!string.IsNullOrWhiteSpace(c.Value));credential=string.Join("; ",selected.Select(c=>c.Name+"="+c.Value));}
            if(string.IsNullOrWhiteSpace(credential)||account.ProviderId=="claudeCode"&&HandwrittenLocalSessions.ClaudeCookie(credential).Session is null){note.Text="还没有找到所需登录，请完成服务商登录后再保存。";return;}if(!app.CanSaveCredential(account.Id,credentialVersion)){Close();return;}app.Secrets.Set((source.Slot??"")+account.Id,credential);if(source.Slot=="desktop:")account.Route="desktop";app.Save();_=app.Engine.RefreshAsync();Close();
        }
    }
}
