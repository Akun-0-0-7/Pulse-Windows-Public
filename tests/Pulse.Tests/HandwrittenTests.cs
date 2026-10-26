// Fixtures and translated expectations: Pulse b035a7a, Copyright (c) 2026 qunqin24, Apache-2.0.
using System.Net;
using System.Text;
using System.Text.Json;
using Pulse.Core;
namespace Pulse.Tests;

public static class HandwrittenTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","Handwritten",name));
    static UsageReading Read(string id,string fixture,params string[] extra) => HandwrittenProviders.Parse(id,Fixture(fixture),extra.Select(Fixture).ToArray());
    [Check] public static void DeepSeekMoneyAndCurrencies() {
        var one=Read("deepSeek","deepseek-balance.json"); Assert.Equal(42.30m,one.Balance!.Amount); Assert.Equal("CNY",one.Balance.Currency); Assert.Equal(0,one.Windows.Count);
        var two=Read("deepSeek","deepseek-two-currencies.json"); Assert.True(two.Metrics.Count>=2); Assert.True(two.Success);
    }
    [Check] public static void CursorSmallPercentIsAlreadyPercent() { var r=Read("cursor","cursor-normal.json"); Assert.Equal(2,r.Windows.Count); Assert.Near(.0267,r.Windows[0].UsedPercent); Assert.Equal(8m,r.Balance!.Amount); Assert.True(!r.Windows[0].ReportsLength); }
    [Check] public static void CopilotInvertsRemainingAndPreservesOverage() { var r=Read("copilot","copilot-normal.json"); Assert.Equal(3,r.Windows.Count); Assert.Near(30,r.Windows[0].UsedPercent); Assert.True(Read("copilot","copilot-overage.json").Windows.All(x=>!x.Exhausted)); Assert.Equal(0,Read("copilot","copilot-empty.json").Windows.Count); }
    [Check] public static void KimiRealDurationsAndUnknownUnit() { var r=Read("kimiCode","kimi-code-normal.json"); Assert.Equal(3,r.Windows.Count); Assert.Near(40,r.Windows[0].UsedPercent); Assert.True(!r.Windows.Single(w=>w.Id=="weekly").ReportsLength); Assert.Equal(0,Read("kimiCode","kimi-code-missing-fields.json").Windows.Count); }
    [Check] public static void MiniMaxInvertsAndOmitsUnavailableLanes() {var r=Read("minimax","minimax-normal.json");Assert.Equal(3,r.Windows.Count);Assert.Near(4,r.Windows[0].UsedPercent);var old=Read("minimax","minimax-old-endpoint-unwrapped.json");Assert.Equal(1,old.Windows.Count);Assert.Near(20,old.Windows[0].UsedPercent);}
    [Check] public static void GrokAndBotHaveActualPeriod() {Assert.Near(42.5,Read("grok","grok-billing-weekly.json").Windows[0].UsedPercent);Assert.Near(30,Read("grokBot","grok-bot-normal.json").Windows[0].UsedPercent);Assert.True(!Read("grokBot","grok-bot-not-included.json").Success);Assert.Near(0,Read("grok","grok-billing-percent-absent-active.json").Windows[0].UsedPercent);Assert.True(!Read("grok","grok-billing-percent-absent-ended.json").Success);}
    [Check] public static void OpenCodeExplicitBlockedStatus() { Assert.Equal(3,Read("openCodeGo","opencode-go-normal.json").Windows.Count); Assert.True(Read("openCodeGo","opencode-go-blocked.json").Windows.Any(w=>w.Exhausted)); }
    [Check] public static void ZhipuQuota() {var r=Read("glmCoding","glm-coding-plan-quota.json");Assert.Equal(2,r.Windows.Count);Assert.Near(0,r.Windows[0].UsedPercent);Assert.Equal("lite",r.Plan);}
    [Check] public static void AntigravityBothResponseShapes() {var r=Read("antigravity","antigravity-quota.json");Assert.Equal(4,r.Windows.Count);Assert.Near(.50932,r.Windows.Single(w=>w.Id=="gemini-weekly").UsedPercent);var cli=Read("antigravity","antigravity-cli-usage.json");Assert.Equal(4,cli.Windows.Count);Assert.Near(25,cli.Windows.Single(w=>w.Id=="gemini-weekly").UsedPercent);}
    [Check] public static void KiroUsesPreciseCounts() {var r=Read("kiro","kiro-pro-plus-usage.json");Assert.Equal(2,r.Windows.Count);Assert.Near(6.1725,r.Windows[0].UsedPercent);}
    [Check] public static void GatewayMoneyAndNoFabricatedAllowance() {
        var n=Read("newAPI","newapi-subscription.json","newapi-usage.json","newapi-status-cny.json");Assert.Equal(13.155m,n.Balance!.Amount);Assert.Equal("CNY",n.Balance.Currency);Assert.Equal(0,n.Windows.Count);
        Assert.True(!Read("newAPI","newapi-subscription.json","newapi-usage.json","newapi-status-tokens.json").Success);
        Assert.True(!Read("newAPI","newapi-unlimited.json","newapi-usage.json","newapi-status-usd.json").Success);
        var s=Read("sub2api","sub2api-quota.json");Assert.Equal(3,s.Windows.Count);Assert.Near(91,s.Windows[0].UsedPercent);Assert.Near(25,s.Windows[2].UsedPercent);
        Assert.Equal(2,Read("sub2api","sub2api-subscription.json").Windows.Count);Assert.True(!Read("sub2api","sub2api-refused.json").Success);
    }
    [Check] public static void OtherHandwrittenCapturedResponses() {
        var v=Read("v2ex","v2ex-quota.json");Assert.Equal(2,v.Windows.Count);Assert.Near(25,v.Windows[0].UsedPercent);
        var d=Read("devin","devin-quota-usage.json");Assert.Equal(2,d.Windows.Count);Assert.Near(2,d.Windows[0].UsedPercent);Assert.Equal(10m,d.Balance!.Amount);Assert.Equal(1,Read("devin","devin-hidden-daily.json").Windows.Count);
        var x=Read("xiaomiMiMo","xiaomi-plan-detail.json","xiaomi-plan-usage.json","xiaomi-balance.json");Assert.Near(37.5,x.Windows[0].UsedPercent);Assert.Equal(42.75m,x.Balance!.Amount);
        Assert.Near(25,Read("qoder","qoder-credits.json").Windows[0].UsedPercent);Assert.Equal(2,Read("qoder","qoder-credits-team.json").Windows.Count);
        Assert.Near(25,Read("stepFun","stepfun-coding-plan.json").Windows[0].UsedPercent);Assert.True(Read("stepFun","stepfun-token-plan.json").Windows[0].UsedPercent<.01);
        var volc=Read("volcengine","volcengine-coding-plan.json","volcengine-agent-plan.json");Assert.Equal(5,volc.Windows.Count);Assert.Equal(4,Read("volcengine","volcengine-arkcli-usage.json").Windows.Count);
        var command=Read("commandCode","command-code-credits.json","command-code-whoami.json","command-code-subscriptions.json","command-code-summary.json");Assert.Equal(6,command.Windows.Count);Assert.Equal(17m,command.Balance!.Amount);Assert.True(command.Windows.Single(w=>w.Id=="monthly").Estimated);
    }
    [Check] public static void OllamaLabelledDomAndInvalidPages() {
        var r=Read("ollamaCloud","ollama-page-normal.html");Assert.Equal(2,r.Windows.Count);Assert.Near(12,r.Windows[0].UsedPercent);Assert.Near(34.5,r.Windows[1].UsedPercent);
        foreach(var name in new[]{"changed","ambiguous-percent","duplicate-label","malformed-time","multiple-times","no-percent","percent-out-of-range","entity-injection"})Assert.True(!Read("ollamaCloud","ollama-page-"+name+".html").Success,name);
        Assert.True(!Read("ollamaCloud","ollama-page-only-session.html").Success);
    }
    [Check] public static void ClaudeSeverityAndCodexVariableGroups() {
        // Mirrors upstream ClaudeCodeSeverityTests and CodexUsageService's two documented response shapes.
        var claude=HandwrittenProviders.Parse("claudeCode","""{"limits":[{"kind":"weekly_scoped","percent":76,"severity":"warning"},{"kind":"session","percent":100,"locked_reason":"limit"}]}""");Assert.Equal(2,claude.Windows.Count);Assert.True(!claude.Windows[0].Exhausted);Assert.True(claude.Windows[1].Exhausted);
        var codex=HandwrittenProviders.Parse("codex","""{"plan_type":"prolite","rate_limit":{"primary_window":{"used_percent":23,"limit_window_seconds":604800,"reset_at":1793000000}},"additional_rate_limits":[{"limit_name":"model","metered_feature":"m","rate_limit":{"allowed":false,"primary_window":{"used_percent":90,"limit_window_seconds":18000},"secondary_window":{"used_percent":10,"limit_window_seconds":604800}}}]}""");Assert.Equal(3,codex.Windows.Count);Assert.Equal("weekly",codex.Windows[0].Kind);Assert.True(!codex.Windows[0].Exhausted);Assert.True(codex.Windows[1].Exhausted);Assert.True(!codex.Windows[2].Exhausted);Assert.Equal("Pro 5x",codex.Plan);
        var rpc=HandwrittenProviders.Parse("codex","""{"ordinaryUsageAllowed":false,"rateLimitsByLimitId":{"a":{"primary":{"usedPercent":20,"windowDurationMins":60},"secondary":{"usedPercent":80,"windowDurationMins":10080}},"b":{"limitName":"model","primary":{"usedPercent":10,"windowDurationMins":300}}}}""");Assert.Equal(3,rpc.Windows.Count);Assert.True(!rpc.Windows[0].Exhausted);Assert.True(rpc.Windows[1].Exhausted);Assert.True(rpc.Windows[2].Exhausted);
    }
    static FetchContext Context(string id,HttpMessageHandler handler,string? credential="fixture-token")=>new(new AccountConfig {Id="test-account",ProviderId=id,Enabled=true},credential,new HttpClient(handler),Path.Combine(Path.GetTempPath(),"pulse-no-user-login"),Path.GetTempPath(),CancellationToken.None);
    sealed class Replies(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler {public List<string> Urls {get;}=[];protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Urls.Add(request.RequestUri!.ToString());return Task.FromResult(reply(request));}}
    static HttpResponseMessage Response(string body,HttpStatusCode code=HttpStatusCode.OK)=>new(code){Content=new StringContent(body)};
    [Check] public static async Task RequestHeadersAndAddedAccountIsolation() {
        var h=new Replies(req=>{Assert.Equal("Bearer",req.Headers.Authorization!.Scheme);Assert.Equal("fixture-token",req.Headers.Authorization.Parameter);Assert.Equal("account-extra",req.Headers.GetValues("ChatGPT-Account-Id").Single());return Response("""{"rate_limit":{"primary_window":{"used_percent":0,"limit_window_seconds":604800}}}""");});
        var c=Context("codex",h,"""{"accessToken":"fixture-token","accountId":"account-extra"}""");c.Account.Route="tooling";c.Account.Extra["isAdditionalAccount"]="true";var r=await HandwrittenProviders.TryCreate("codex")!.FetchAsync(c);Assert.True(r.Success);Assert.Equal("test-account",r.AccountId);Assert.Equal(1,h.Urls.Count);
        c=c with {Credential=null};Assert.True(!(await HandwrittenProviders.TryCreate("codex")!.FetchAsync(c)).Success);Assert.Equal(1,h.Urls.Count);
    }
    [Check] public static async Task NewApiStatusNeverReceivesKeyAndSuffixesAreTrimmed() {
        var h=new Replies(req=>{var path=req.RequestUri!.AbsolutePath;if(path=="/api/status"){Assert.True(req.Headers.Authorization==null);return Response(Fixture("newapi-status-cny.json"));}Assert.Equal("Bearer",req.Headers.Authorization!.Scheme);return Response(Fixture(path.EndsWith("subscription")?"newapi-subscription.json":"newapi-usage.json"));});var c=Context("newAPI",h);c.Account.ServerAddress="https://example.test/v1";var r=await HandwrittenProviders.TryCreate("newAPI")!.FetchAsync(c);Assert.Equal(13.155m,r.Balance!.Amount);Assert.Equal(3,h.Urls.Count);Assert.True(h.Urls.All(u=>!u.Contains("/v1/v1/")));
    }
    [Check] public static async Task DisabledAccountMakesNoRequests() {var h=new Replies(_=>throw new Exception("Disabled account was read"));var c=Context("deepSeek",h);c.Account.Enabled=false;var r=await HandwrittenProviders.TryCreate("deepSeek")!.FetchAsync(c);Assert.Equal("disabled",r.ErrorCode);Assert.Equal(0,h.Urls.Count);}
    [Check] public static void CursorWeeklyReplacesMonthly() {var r=Read("cursor","cursor-normal.json","cursor-plan-info-weekly.json","cursor-period-usage-weekly.json");Assert.Equal(1,r.Windows.Count);Assert.Equal("weekly",r.Windows[0].Kind);Assert.Near(35.51462621885157,r.Windows[0].UsedPercent);Assert.True(r.Windows[0].ReportsLength);}
    [Check] public static void SessionCookiesOnlyKeepRelevantFields() {
        Assert.Equal("wos-session=abc",HandwrittenCookies.Normalize("ollamaCloud","Cookie: tracking=not-kept; wos-session=abc; wos-session=second"));
        Assert.True(HandwrittenCookies.Normalize("ollamaCloud","api-key-only")==null);Assert.True(HandwrittenCookies.Normalize("stepFun","Oasis-Token=abc\r\ninjected=x")==null);
        Assert.Equal("api-platform_serviceToken=token; userId=fixture",HandwrittenCookies.Normalize("xiaomiMiMo","tracking=x; api-platform_serviceToken=token; userId=fixture"));
        Assert.True(HandwrittenCookies.Normalize("xiaomiMiMo","api-platform_serviceToken=token")==null);
        Assert.Equal("Oasis-Token=token; Oasis-Webid=device",HandwrittenCookies.Normalize("stepFun","Oasis-Token=token; Oasis-Webid=device; tracking=x"));
    }
    [Check] public static async Task DeviceLoginPendingThenExchangeAndGithubSlowDown() {
        var codex=new Replies(req=>Response(req.RequestUri!.AbsolutePath.EndsWith("usercode")?"""{"user_code":"TEST-CODE","device_auth_id":"fixture-device","interval":"5"}""":req.RequestUri.AbsolutePath.EndsWith("deviceauth/token")?"""{"authorization_code":"fixture-code","code_verifier":"fixture-verifier"}""":"""{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":3600}"""));
        using var http=new HttpClient(codex);var prompt=await HandwrittenLogin.BeginDeviceAsync("codex",http);Assert.Equal("TEST-CODE",prompt.UserCode);Assert.Equal("https://auth.openai.com/codex/device",prompt.VerificationUrl);var granted=await HandwrittenLogin.PollDeviceAsync(prompt,http);Assert.True(!granted.Pending);Assert.True(granted.Credential!.Contains("fixture-access"));Assert.Equal(3,codex.Urls.Count);
        var github=new Replies(req=>Response(req.RequestUri!.AbsolutePath.EndsWith("/code")?"""{"user_code":"1234","device_code":"fixture-device","verification_uri":"https://github.com/login/device","interval":5}""":"""{"error":"slow_down","interval":10}"""));using var gitHttp=new HttpClient(github);var gitPrompt=await HandwrittenLogin.BeginDeviceAsync("copilot",gitHttp);var pending=await HandwrittenLogin.PollDeviceAsync(gitPrompt,gitHttp);Assert.True(pending.Pending);Assert.Equal(10,pending.RetryAfterSeconds);
    }
    [Check] public static async Task BrowserPkceClaudeAndGrokValidateStateBeforeExchange() {
        foreach(var provider in new[]{"claudeCode","grok"}) {
            var prompt=HandwrittenLogin.BeginBrowser(provider,"http://127.0.0.1:49123/callback");
            var query=new Uri(prompt.AuthorizationUrl).Query;
            Assert.True(query.Contains("code_challenge_method=S256"));Assert.True(query.Contains("state="));Assert.True(!prompt.AuthorizationUrl.Contains(prompt.CodeVerifier));
            var handler=new Replies(req=>{Assert.Equal(provider=="claudeCode"?"platform.claude.com":"auth.x.ai",req.RequestUri!.Host);return Response("""{"access_token":"fixture-access","refresh_token":"fixture-refresh","expires_in":3600}""");});
            using var http=new HttpClient(handler);
            var rejected=false;try{await HandwrittenLogin.CompleteBrowserAsync(prompt,prompt.RedirectUri+"?code=fixture-code&state=wrong",http);}catch(InvalidOperationException){rejected=true;}Assert.True(rejected);Assert.Equal(0,handler.Urls.Count);
            var saved=await HandwrittenLogin.CompleteBrowserAsync(prompt,prompt.RedirectUri+"?code=fixture-code&state="+prompt.State,http);Assert.True(saved.Contains("fixture-access"));Assert.Equal(1,handler.Urls.Count);
        }
    }
    [Check] public static async Task ClaudeDesktopUsesOnlySessionCookiesAndActiveOrganization() {
        var cookie="tracking=ignored; sessionKey=fixture-session; sessionKey=duplicate; lastActiveOrg=22222222-2222-4222-8222-222222222222";
        var selected=HandwrittenLocalSessions.ClaudeCookie(cookie);Assert.Equal("sessionKey=fixture-session",selected.Session);Assert.Equal("22222222-2222-4222-8222-222222222222",selected.ActiveOrganization);
        var handler=new Replies(req=>{Assert.Equal("claude.ai",req.RequestUri!.Host);Assert.Equal("sessionKey=fixture-session",req.Headers.GetValues("Cookie").Single());if(req.RequestUri.AbsolutePath=="/api/bootstrap")return Response("""{"account":{"memberships":[{"organization":{"uuid":"11111111-1111-4111-8111-111111111111","subscription_type":"pro"}},{"organization":{"uuid":"22222222-2222-4222-8222-222222222222","rate_limit_tier":"default_max_5x"}}]}}""");Assert.True(req.RequestUri.AbsolutePath.Contains("22222222-2222-4222-8222-222222222222"));return Response("""{"five_hour":{"utilization":27,"resets_at":"2026-10-21T00:00:00Z"}}""");});
        var c=Context("claudeCode",handler,"""{"accessToken":"old","refreshToken":"fixture-refresh","expiresAt":"2020-01-01T00:00:00Z"}""");c.Account.Route="desktopApp";var secrets=new Secrets();secrets.Set("desktop:"+c.Account.Id,cookie);c=c with{Secrets=secrets};var result=await HandwrittenProviders.TryCreate("claudeCode")!.FetchAsync(c);Assert.True(result.Success);Assert.Equal("desktop-session",result.Source);Assert.Near(27,result.Windows[0].UsedPercent);Assert.Equal(2,handler.Urls.Count);
    }
    [Check] public static void ClaudeDesktopChromiumCipherUsesSyntheticFixture() {
        var key=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);var nonce=System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var plain=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(".claude.ai")).Concat(Encoding.UTF8.GetBytes("fixture-session")).ToArray();var cipher=new byte[plain.Length];var tag=new byte[16];
        using(var aes=new System.Security.Cryptography.AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag);
        var encrypted=Encoding.ASCII.GetBytes("v10").Concat(nonce).Concat(cipher).Concat(tag).ToArray();Assert.Equal("fixture-session",ClaudeWindowsCookieStore.DecryptChromiumCookie(encrypted,key,".claude.ai"));
        Assert.True(ClaudeWindowsCookieStore.DecryptChromiumCookie(encrypted,System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),".claude.ai")==null);
    }
    [Check] public static async Task ExtraClaudeDesktopNeverBorrowsPrimaryLocalCookies() {
        if(!OperatingSystem.IsWindows())return;var home=Path.Combine(Path.GetTempPath(),"pulse-isolation-"+Guid.NewGuid().ToString("N"));var profile=Path.Combine(home,"AppData","Roaming","Claude","Network");Directory.CreateDirectory(profile);
        try{var file=Path.Combine(profile,"Cookies");Assert.Equal(0,FixtureSqlite.sqlite3_open_v2(file,out var db,6,IntPtr.Zero));try{Assert.Equal(0,FixtureSqlite.sqlite3_exec(db,"CREATE TABLE cookies(host_key TEXT,name TEXT,value TEXT,encrypted_value BLOB); INSERT INTO cookies VALUES('claude.ai','sessionKeyV3','fixture-only',X'');",IntPtr.Zero,IntPtr.Zero,out _));}finally{FixtureSqlite.sqlite3_close(db);}
            var handler=new Replies(req=>Response(req.RequestUri!.AbsolutePath=="/api/bootstrap"?"""{"account":{"memberships":[{"organization":{"uuid":"11111111-1111-4111-8111-111111111111"}}]}}""":"""{"five_hour":{"utilization":12}}"""));var context=Context("claudeCode",handler,null) with{HomeDirectory=home};context.Account.Route="desktop";context.Account.Extra["isAdditionalAccount"]="true";
            var extra=await HandwrittenProviders.TryCreate("claudeCode")!.FetchAsync(context);Assert.Equal("signedOut",extra.ErrorCode);Assert.Equal(0,handler.Urls.Count);
            context.Account.Extra.Clear();Assert.True((await HandwrittenProviders.TryCreate("claudeCode")!.FetchAsync(context)).Success);Assert.Equal(2,handler.Urls.Count);
            var explicitBad=await HandwrittenProviders.TryCreate("claudeCode")!.FetchAsync(context with{Credential="lastActiveOrg=11111111-1111-4111-8111-111111111111"});Assert.Equal("signedOut",explicitBad.ErrorCode);Assert.Equal(2,handler.Urls.Count);
        }finally{Directory.Delete(home,true);}
    }
    static class FixtureSqlite {
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)] public static extern int sqlite3_open_v2([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string file,out IntPtr db,int flags,IntPtr vfs);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db,[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string sql,IntPtr callback,IntPtr data,out IntPtr error);
        [System.Runtime.InteropServices.DllImport("winsqlite3.dll",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
    }
    [Check] public static void ClaudeDesktopReadsOnlyNamedCookiesFromTemporaryDatabase() {
        if(!OperatingSystem.IsWindows())return;
        var folder=Path.Combine(Path.GetTempPath(),"pulse-claude-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(folder,"Network"));
        try {var file=Path.Combine(folder,"Network","Cookies");Assert.Equal(0,FixtureSqlite.sqlite3_open_v2(file,out var db,6,IntPtr.Zero));
            try {var sql="CREATE TABLE cookies(host_key TEXT,name TEXT,value TEXT,encrypted_value BLOB); INSERT INTO cookies VALUES('claude.ai','sessionKey','fixture-session',X''); INSERT INTO cookies VALUES('other.example','sessionKey','wrong',X''); INSERT INTO cookies VALUES('claude.ai','analytics','ignored',X''); INSERT INTO cookies VALUES('.claude.ai','lastActiveOrg','22222222-2222-4222-8222-222222222222',X'');";Assert.Equal(0,FixtureSqlite.sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out _));}finally {FixtureSqlite.sqlite3_close(db);}
            var cookie=ClaudeWindowsCookieStore.ReadSession(folder);Assert.True(cookie!.Contains("sessionKey=fixture-session"));Assert.True(cookie.Contains("lastActiveOrg="));Assert.True(!cookie.Contains("wrong")&&!cookie.Contains("analytics"));
        }finally {Directory.Delete(folder,true);}
    }
    [Check] public static async Task AntigravityWindowsDiscoveryAndCandidateFallback() {
        var listed="""[{"ProcessId":4,"ExecutablePath":"C:\\Other\\language_server.exe","CommandLine":"--csrf_token no","Ports":[1]},{"ProcessId":8,"ExecutablePath":"C:\\Antigravity IDE\\language_server.exe","CommandLine":"language_server --csrf_token fixture-ide-token","Ports":[34001,34002]},{"ProcessId":7,"ExecutablePath":"C:\\Antigravity\\language_server.exe","CommandLine":"language_server --csrf_token fixture-app-token","Ports":[34000]}]""";
        var servers=HandwrittenLocalSessions.ParseAntigravityProcesses(listed);Assert.Equal(2,servers.Count);Assert.Equal("app",servers[0].Origin);
        var handler=new Replies(req=>{Assert.Equal("fixture-app-token",req.Headers.GetValues("x-codeium-csrf-token").Single());if(req.RequestUri!.Port==34000)return Response("{}",HttpStatusCode.Unauthorized);if(req.RequestUri.AbsolutePath.EndsWith("GetUserStatus"))return Response("""{"userStatus":{"planStatus":{"planInfo":{"planName":"Pro"}}}}""");return Response(Fixture("antigravity-quota.json"));});
        // A refused first process must not stop discovery of the second one.
        var second=servers[1] with{CsrfToken="fixture-app-token"};using var http=new HttpClient(handler);var result=await HandwrittenLocalSessions.QueryAntigravityAsync(Context("antigravity",handler,null) with{Http=http},[servers[0],second]);Assert.True(result.Success);Assert.Equal("language-server",result.Source);Assert.Equal("Pro",result.Plan);Assert.Equal(4,result.Windows.Count);
    }
    sealed class Secrets: ISecretStore {readonly Dictionary<string,string> values=[];public string? Get(string key)=>values.GetValueOrDefault(key);public void Set(string key,string? value){if(value==null)values.Remove(key);else values[key]=value;}}
    [Check] public static async Task ExpiredAdditionalCredentialsRenewOnlyTheirOwnSavedSlot() {
        var saved=new Secrets();var h=new Replies(req=>Response(req.RequestUri!.Host=="auth.openai.com"?"""{"access_token":"renewed-token","refresh_token":"rotated-token","expires_in":3600}""":"""{"rate_limit":{"primary_window":{"used_percent":12,"limit_window_seconds":604800}}}"""));
        var c=Context("codex",h,"""{"accessToken":"expired","refreshToken":"fixture-refresh","expiresAt":"2020-01-01T00:00:00Z","accountId":"fixture-account"}""") with{Secrets=saved};saved.Set(c.Account.Id,c.Credential);c.Account.Extra["isAdditionalAccount"]="true";
        var r=await HandwrittenProviders.TryCreate("codex")!.FetchAsync(c);Assert.True(r.Success);Assert.True(saved.Get(c.Account.Id)!.Contains("renewed-token"));Assert.True(saved.Get(c.Account.Id)!.Contains("fixture-account"));Assert.Equal(2,h.Urls.Count);
    }
    [Check] public static async Task OpenCodeConsoleWorkspaceFallback() {
        var h=new Replies(req=>Response(req.RequestUri!.AbsolutePath.EndsWith("orgs")?"""[{"id":"org-fixture"}]""":"""{"product":"go","access":{"meters":{"fiveHour":{"usedMicroCents":"25","limitMicroCents":"100","startsAt":"2026-10-20T00:00:00Z","resetsAt":"2026-10-20T05:00:00Z"}}}}"""));var store=new Secrets();store.Set("console:test-account","__Host-console_session=fixture");var c=Context("openCodeGo",h,null) with{Secrets=store};var r=await HandwrittenProviders.TryCreate("openCodeGo")!.FetchAsync(c);Assert.True(r.Success);Assert.Near(25,r.Windows[0].UsedPercent);Assert.Equal("web-session",r.Source);
    }
    [Check] public static async Task JsonRpcHandshakeWithFakeChildAndEarlyEof() {
        if(!OperatingSystem.IsWindows())return;
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
        var script="$first=[Console]::ReadLine()|ConvertFrom-Json;if($first.method -ne 'initialize'){exit 2};[Console]::WriteLine('{\"id\":1,\"result\":{}}');$notify=[Console]::ReadLine()|ConvertFrom-Json;if($notify.method -ne 'initialized'){exit 3};$second=[Console]::ReadLine()|ConvertFrom-Json;if($second.method -ne 'account/rateLimits/read'){exit 4};[Console]::WriteLine('{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":17,\"windowDurationMins\":300}}}}')";
        var text=await HandwrittenRpc.ReadAsync(executable,["-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],false,timeout:TimeSpan.FromSeconds(8));Assert.Near(17,HandwrittenProviders.Parse("codex",text).Windows[0].UsedPercent);
        var failed=false;try {await HandwrittenRpc.ReadAsync(executable,["-NoProfile","-NonInteractive","-Command","exit 0"],false,timeout:TimeSpan.FromSeconds(3));}catch(InvalidOperationException){failed=true;}Assert.True(failed);
    }
    [Check] public static void AllFactoriesAndEmptyData() {foreach(var id in new[]{"codex","claudeCode","kiro","antigravity","cursor","openCodeGo","kimiCode","ollamaCloud","zai","glmCoding","minimax","copilot","grok","grokBot","volcengine","commandCode","deepSeek","devin","xiaomiMiMo","sub2api","newAPI","v2ex","qoder","stepFun"}) {Assert.True(HandwrittenProviders.TryCreate(id)!=null,id);Assert.True(!HandwrittenProviders.Parse(id,"{}").Success,id);}Assert.True(HandwrittenProviders.TryCreate("unknown")==null);}
}
