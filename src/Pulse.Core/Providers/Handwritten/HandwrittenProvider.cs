// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Windows request orchestration translated from the 24 Pulse handwritten services.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
namespace Pulse.Core;

sealed class HandwrittenProvider(string id) : IUsageProvider
{
    static JsonElement Json(string? text) {try {using var d=JsonDocument.Parse(text??"{}");return d.RootElement.Clone();}catch(JsonException){return default;}}
    static string? S(JsonElement n,string p)=>JsonTools.String(n,p);
    static string? Read(FetchContext c,string path)=>LocalPaths.ReadText(Path.Combine(c.HomeDirectory,path));
    static bool Added(FetchContext c)=>c.Account.Extra.GetValueOrDefault("isAdditionalAccount")=="true";
    static string? Token(string? credential) {if(string.IsNullOrWhiteSpace(credential))return null;if(!credential.TrimStart().StartsWith('{'))return credential.Trim();var n=Json(credential);return S(n,"accessToken")??S(n,"access_token")??S(n,"token")??S(n,"tokens.access_token");}
    static Dictionary<string,string> Bearer(string token)=>new(){{"Authorization","Bearer "+token},{"Accept","application/json"}};
    static Dictionary<string,string> Cookie(string cookie,string? origin=null)=>new(){{"Cookie",cookie},{"Accept","application/json"},{"Origin",origin??"https://cursor.com"}};
    UsageReading Missing(FetchContext c,string code="credentialMissing",string message="请先为当前账号保存登录凭据。")=>UsageReading.Unavailable(c.Account,code,message);
    static UsageReading Finish(FetchContext c,HttpReply reply,params string[] extra) {if(reply.Status is <200 or >=300)return ProviderParsing.Failed(c,reply);var r=HandwrittenProviders.Parse(c.Account,reply.Body,extra);r.Diagnostics.Add(reply.Diagnostic);return r;}
    public async Task<UsageReading> FetchAsync(FetchContext c) {
        if(!c.Account.Enabled)return Missing(c,"disabled","当前账号未启用。");
        try {
            if(!(id.Equals("claudeCode",StringComparison.OrdinalIgnoreCase)&&c.Account.Route is "desktopApp" or "desktop"))c=await HandwrittenLogin.RenewIfExpiredAsync(c);
            return id.ToLowerInvariant() switch {
                "codex"=>await Codex(c),"claudecode"=>await Claude(c),"deepseek"=>await DeepSeek(c),"cursor"=>await Cursor(c,false),"grokbot"=>await Cursor(c,true),"grok"=>await Grok(c),
                "copilot"=>await Copilot(c),"kiro"=>await Kiro(c),"antigravity"=>await Antigravity(c),"volcengine"=>await Volcengine(c),"commandcode"=>await Command(c),
                "newapi"=>await Gateway(c,true),"sub2api"=>await Gateway(c,false),"xiaomimimo"=>await Xiaomi(c),"stepfun"=>await StepFun(c),"devin"=>await Devin(c),"opencodego"=>await OpenCode(c),
                _=>await Single(c)
            };
        } catch(OperationCanceledException)when(!c.CancellationToken.IsCancellationRequested){return Missing(c,"unreachable","读取额度超时。");}
          catch(HttpRequestException){return Missing(c,"unreachable","无法连接服务。");}
          catch(IOException){return Missing(c,"localReadFailed","无法读取本机数据。");}
          catch(System.ComponentModel.Win32Exception){return Missing(c,"localToolMissing","找不到所需的客户端工具。");}
          catch(JsonException){return Missing(c,"unreadableReply","服务返回的内容无法解析。");}
          catch(InvalidOperationException){return Missing(c,"toolFailed","本机工具没有完成额度读取。");}
    }
    async Task<UsageReading> Codex(FetchContext c) {
        const string usageUrl="https://chatgpt.com/backend-api/wham/usage";
        var supplied=!string.IsNullOrWhiteSpace(c.Credential);var additional=Added(c);
        if(c.Account.Route is "tooling" or "app-server" && !supplied&&!additional)return await Rpc(c,"codex",["app-server"],false);
        var auth=supplied?Json(c.Credential):Json(additional?null:Read(c,".codex/auth.json"));
        var token=supplied?Token(c.Credential):S(auth,"tokens.access_token");
        var account=S(auth,"accountId")??S(auth,"account_id")??S(auth,"tokens.account_id")??c.Account.Extra.GetValueOrDefault("accountId");
        UsageReading? failed=null;
        if(token!=null) {var headers=Bearer(token);if(!string.IsNullOrEmpty(account))headers["ChatGPT-Account-Id"]=account;
            try {
                var reply=await ProviderRequest.SendAsync(c,usageUrl,headers:headers);
                failed=Finish(c,reply);if(failed.Success||reply.Status is not (401 or 403))return failed;
            } catch(HttpRequestException) {c.CancellationToken.ThrowIfCancellationRequested();failed=ConnectionFailure(false);}
              catch(OperationCanceledException)when(!c.CancellationToken.IsCancellationRequested){failed=ConnectionFailure(true);}
        }
        if(c.Account.Route=="endpoint"||supplied||additional)return failed??Missing(c,"signInRequired","请登录当前 Codex 账号。");
        var tooling=await Rpc(c,"codex",["app-server"],false);if(failed!=null)tooling.Diagnostics.InsertRange(0,failed.Diagnostics);return tooling;
        UsageReading ConnectionFailure(bool timeout) {
            var reading=Missing(c,"unreachable",timeout?"读取额度超时。":"无法连接服务。");
            reading.Diagnostics.Add(new(DateTimeOffset.UtcNow,usageUrl,0,JsonSerializer.Serialize(new{error=timeout?"requestTimeout":"connectionFailed"})));
            return reading;
        }
    }
    async Task<UsageReading> Claude(FetchContext c) {
        var additional=Added(c)||!string.IsNullOrWhiteSpace(c.Credential);var route=c.Account.Route;
        if(route is "desktopApp" or "desktop") {
            var session=c.Secrets?.Get("desktop:"+c.Account.Id)??c.Credential;
            if(HandwrittenLocalSessions.ClaudeCookie(session).Session==null&&!Added(c)&&string.IsNullOrWhiteSpace(session)&&string.IsNullOrWhiteSpace(c.Credential))session=ClaudeWindowsCookieStore.ReadSession(c.HomeDirectory==LocalPaths.Home?null:Path.Combine(c.HomeDirectory,"AppData","Roaming","Claude"));
            return HandwrittenLocalSessions.ClaudeCookie(session).Session is null?Missing(c,"signedOut","请连接当前账号的 Claude 桌面会话。"):
                await HandwrittenLocalSessions.QueryClaudeDesktopAsync(c,session!);
        }
        if(!additional&&route is "tooling" or "status-line")return Capture(c);
        var auth=Json(additional?c.Credential:Read(c,".claude/.credentials.json"));var token=additional?Token(c.Credential):S(auth,"claudeAiOauth.accessToken");
        if(!additional&&JsonTools.Date(auth,"claudeAiOauth.expiresAt")<=DateTimeOffset.UtcNow)token=null;
        UsageReading? failed=null;
        if(token!=null) {var h=Bearer(token);h["anthropic-beta"]="oauth-2025-04-20";h["User-Agent"]="claude-cli (external, cli)";
            var reply=await ProviderRequest.SendAsync(c,"https://api.anthropic.com/api/oauth/usage",headers:h);failed=Finish(c,reply);if(failed.Success)return failed;
        }
        if(additional||route=="endpoint")return failed??Missing(c,"signInRequired","请登录当前 Claude Code 账号。");
        var capture=Capture(c);if(capture.Success){if(failed!=null)capture.Diagnostics.AddRange(failed.Diagnostics);return capture;}return failed??capture;
    }
    UsageReading Capture(FetchContext c) {
        var file=c.Account.Extra.GetValueOrDefault("statusLineFile")??Path.Combine(c.DataDirectory,"statusline","claudeCode.json");var text=LocalPaths.ReadText(file);
        if(text==null)return Missing(c,"awaitingResponse","尚未收到 Claude Code 状态栏用量。");var r=HandwrittenProviders.Parse(c.Account,text);r.Source="status-line";return r;
    }
    async Task<UsageReading> DeepSeek(FetchContext c) {
        UsageReading? primary=null;var key=Token(c.Credential);
        if(key!=null) {primary=Finish(c,await ProviderRequest.SendAsync(c,"https://api.deepseek.com/user/balance",headers:Bearer(key)));if(primary.Success)return primary;}
        var console=c.Secrets?.Get("console:"+c.Account.Id);if(string.IsNullOrWhiteSpace(console))return primary??Missing(c,"apiKeyMissing","请保存 API 密钥或 DeepSeek 控制台登录。");
        var reply=await ProviderRequest.SendAsync(c,"https://platform.deepseek.com/api/v0/users/get_user_summary",headers:Bearer(console));if(reply.Status!=200)return primary??ProviderParsing.Failed(c,reply);
        var envelope=reply.Json;var code=JsonTools.Number(envelope,"data.biz_code");if(code is not null and not 0)return primary??Missing(c,code is >=40000 and <41000?"sessionExpired":"serverError","控制台拒绝了余额读取。");
        var n=JsonTools.At(envelope,"data.biz_data");var wallets=new Dictionary<string,decimal>();
        foreach(var field in new[]{"normal_wallets","bonus_wallets"})foreach(var w in JsonTools.Array(n,field))if(S(w,"currency") is {} currency&&JsonTools.Decimal(w,"balance") is {} balance)wallets[currency]=wallets.GetValueOrDefault(currency)+balance;
        var body=JsonSerializer.Serialize(new {balance_infos=wallets.Select(w=>new{currency=w.Key,total_balance=w.Value.ToString(CultureInfo.InvariantCulture)})});var r=HandwrittenProviders.Parse(c.Account,body);r.Source="web-session";r.Diagnostics.Add(reply.Diagnostic);return r.Success?r:primary??r;
    }
    async Task<UsageReading> Copilot(FetchContext c) {
        var token=Token(c.Credential);if(token==null)return Missing(c,"signedOut","请保存 GitHub Copilot 登录令牌。");var h=Bearer(token);h["Authorization"]="token "+token;h["X-Github-Api-Version"]="2025-04-01";h["Editor-Version"]="vscode/1.96.2";h["Editor-Plugin-Version"]="copilot-chat/0.26.7";
        return Finish(c,await ProviderRequest.SendAsync(c,"https://api.github.com/copilot_internal/user",headers:h));
    }
    async Task<UsageReading> OpenCode(FetchContext c) {
        var token=Token(c.Credential)??S(Json(Read(c,".local/share/opencode/auth.json")),"opencode-go.key");UsageReading? primary=null;
        if(token!=null) {
            try {var reply=await ProviderRequest.SendAsync(c,"https://opencode.ai/zen/go/v1/usage",headers:Bearer(token));primary=Finish(c,reply);if(reply.Status==403&&S(reply.Json,"error.type")=="EntitlementError")primary=Missing(c,"noPlan","当前工作区没有 OpenCode Go 套餐。");if(primary.Success||reply.Status==200&&primary.ErrorCode=="noLimitsReported"||reply.Status==429)return primary;}
            catch(HttpRequestException){primary=Missing(c,"unreachable","无法连接服务。");}
        }
        var cookie=HandwrittenCookies.Normalize("openCodeGo",c.Secrets?.Get("console:"+c.Account.Id)??"");if(cookie==null)return primary??Missing(c,"apiKeyMissing","请保存 API 密钥或 OpenCode 控制台登录。");var h=Cookie(cookie,"https://opencode.ai");
        var orgs=await ProviderRequest.SendAsync(c,"https://opencode.ai/console/api/orgs",headers:h);if(orgs.Status!=200)return primary??ProviderParsing.Failed(c,orgs);
        var list=orgs.Json;if(list.ValueKind!=JsonValueKind.Array)return primary??Missing(c,"sessionExpired","OpenCode 控制台会话已失效。");UsageReading? first=null;
        foreach(var org in list.EnumerateArray()) {
            var orgId=S(org,"id");if(string.IsNullOrWhiteSpace(orgId))continue;h["x-org-id"]=orgId;var reply=await ProviderRequest.SendAsync(c,"https://opencode.ai/console/api/go/status",headers:h);var read=Finish(c,reply);first??=read;
            if(read.Success&&JsonTools.At(reply.Json,"access").ValueKind==JsonValueKind.Object){read.Source="web-session";return read;}
        }return primary??first??Missing(c,"noPlan","当前控制台没有可读取的 Go 套餐。");
    }
    internal static string? CursorCookie(string token) {
        if(token.Contains('='))return HandwrittenCookies.Normalize("cursor",token);
        try {var pieces=token.Split('.');if(pieces.Length!=3)return null;var middle=pieces[1].Replace('-','+').Replace('_','/');middle=middle.PadRight((middle.Length+3)/4*4,'=');var claims=Json(Encoding.UTF8.GetString(Convert.FromBase64String(middle)));var sub=S(claims,"sub");var expiry=JsonTools.Date(claims,"exp");if(sub==null||expiry==null||expiry<=DateTimeOffset.UtcNow.AddMinutes(1))return null;return "WorkosCursorSessionToken="+Uri.EscapeDataString(sub.Split('|').Last()+"::"+token);}catch(FormatException){return null;}
    }
    string? LocalCursor(FetchContext c) {
        var db=c.Account.SourcePaths.FirstOrDefault()??Path.Combine(c.HomeDirectory,"AppData","Roaming","Cursor","User","globalStorage","state.vscdb");if(!File.Exists(db))return null;
        var row=SqliteReader.Query(db,"SELECT value FROM ItemTable WHERE key='cursorAuth/accessToken' LIMIT 1").FirstOrDefault();return row?.GetValueOrDefault("value");
    }
    async Task<UsageReading> Cursor(FetchContext c,bool bot) {
        var supplied=Token(c.Credential);var token=supplied??(Added(c)?null:LocalCursor(c));var cookie=token==null?null:CursorCookie(token);if(cookie==null)return Missing(c,"signedOut","请登录当前 Cursor 账号。");var headers=Cookie(cookie);
        if(bot)return Finish(c,await ProviderRequest.SendAsync(c,"https://cursor.com/api/dashboard/get-sand-usage-status","POST",new{},headers));
        var summary=await ProviderRequest.SendAsync(c,"https://cursor.com/api/usage-summary",headers:headers);if(summary.Status!=200)return ProviderParsing.Failed(c,summary);
        var info=await ProviderRequest.SendAsync(c,"https://cursor.com/api/dashboard/get-plan-info","POST",new{},headers);var period=S(info.Json,"planInfo.includedUsagePeriod");
        if(info.Status==200&&period is "2" or "WEEKLY" or "INCLUDED_USAGE_PERIOD_WEEKLY") {
            var usage=await ProviderRequest.SendAsync(c,"https://cursor.com/api/dashboard/get-current-period-usage","POST",new{},headers);if(usage.Status!=200)return ProviderParsing.Failed(c,usage);return Finish(c,summary,info.Body,usage.Body);
        }return Finish(c,summary);
    }
    async Task<UsageReading> Grok(FetchContext c) {
        var token=Token(c.Credential);
        if(token==null&&!Added(c)) {
            var root=Json(Read(c,".grok/auth.json"));if(root.ValueKind==JsonValueKind.Object)token=root.EnumerateObject().Select(p=>p.Value).Where(p=>S(p,"key")!=null&&(JsonTools.Date(p,"expires_at")==null||JsonTools.Date(p,"expires_at")>DateTimeOffset.UtcNow)).OrderByDescending(p=>JsonTools.Date(p,"expires_at")??DateTimeOffset.MinValue).Select(p=>S(p,"key")).FirstOrDefault();
        }
        if(token==null)return Missing(c,"signedOut","请登录当前 Grok 账号。");var billing=await ProviderRequest.SendAsync(c,"https://cli-chat-proxy.grok.com/v1/billing?format=credits",headers:Bearer(token));if(billing.Status!=200)return ProviderParsing.Failed(c,billing);
        var settings=await ProviderRequest.SendAsync(c,"https://cli-chat-proxy.grok.com/v1/settings",headers:Bearer(token));return Finish(c,billing,settings.Body);
    }
    async Task<UsageReading> Kiro(FetchContext c)=>await Rpc(c,"kiro-cli",["acp","--agent-engine","v3","--auth-method","cli"],true);
    async Task<UsageReading> Rpc(FetchContext c,string name,string[] args,bool kiro) {
        var executable=c.Account.Extra.GetValueOrDefault("executable")??LocalPaths.FindExecutable(name);if(executable==null)return Missing(c,"localToolMissing","未找到 "+name+" 工具。");
        var reply=await HandwrittenRpc.ReadAsync(executable,args,kiro,c.CancellationToken,environment:c.ProcessEnvironment);var result=HandwrittenProviders.Parse(c.Account,reply);result.Source=kiro?"kiro-acp":"app-server";return result;
    }
    async Task<UsageReading> Antigravity(FetchContext c) {
        if(c.Account.Route is not("tooling" or "cli")) {
            var servers=await HandwrittenLocalSessions.DiscoverAntigravityAsync(c.CancellationToken);
            using var loopback=HandwrittenLocalSessions.CreateLoopbackClient();
            var local=await HandwrittenLocalSessions.QueryAntigravityAsync(c with{Http=loopback},servers);
            if(local.Success||c.Account.Route is "desktopApp" or "desktop")return local;
        }
        var executable=c.Account.Extra.GetValueOrDefault("executable")??LocalPaths.FindExecutable("agy");if(executable==null)return Missing(c,"localToolMissing","未找到 agy 工具。");
        var version=await ProcessRunner.RunAsync(executable,["--version"],TimeSpan.FromSeconds(10),c.CancellationToken,environment:c.ProcessEnvironment,maxOutputBytes:4096);
        if(version.ExitCode!=0||!Version.TryParse(version.Output.Trim().Split('-')[0],out var v)||v<new Version(1,1,11))return Missing(c,"cliOutdated","agy 需要 1.1.11 或更新版本才能只读查询用量。");
        var output=await ProcessRunner.RunAsync(executable,["--log-file","NUL","-p","/usage","--output-format","json"],TimeSpan.FromSeconds(30),c.CancellationToken,Path.GetTempPath(),environment:c.ProcessEnvironment,maxOutputBytes:524288);
        if(output.ExitCode!=0)return Missing(c,output.TimedOut?"unreachable":"toolFailed","agy 没有返回额度。");
        var result=HandwrittenProviders.Parse(c.Account,output.Output);result.Source="cli";return result;
    }
    async Task<UsageReading> Volcengine(FetchContext c) {
        var entered=Token(c.Credential);var pair=entered?.Split(':',2);var pinned=c.Account.Route=="endpoint";
        if(c.Account.Route is "tooling" or "arkcli"||pair?.Length!=2&&!pinned)return await Arkcli(c);
        if(pair?.Length!=2)return Missing(c,"apiKeyMissing","请保存访问密钥 ID 与秘密密钥。");
        var replies=new List<HttpReply>();foreach(var action in new[]{"GetCodingPlanUsage","GetAFPUsage"}){var url="https://open.volcengineapi.com/?Action="+action+"&Version=2024-01-01";replies.Add(await ProviderRequest.SendAsync(c,url,headers:HandwrittenSigning.Headers(url,pair[0],pair[1],DateTimeOffset.UtcNow)));}
        var good=replies.Where(x=>x.Status==200).ToArray();if(good.Length>0){var r=Finish(c,good[0],good.Skip(1).Select(x=>x.Body).ToArray());if(r.Success)return r;}
        if(!pinned&&replies.Any(r=>r.Status==0))return await Arkcli(c);return ProviderParsing.Failed(c,replies[0]);
    }
    async Task<UsageReading> Arkcli(FetchContext c) {
        var executable=c.Account.Extra.GetValueOrDefault("executable")??LocalPaths.FindExecutable("arkcli");if(executable==null)return Missing(c,"localToolMissing","未找到 arkcli 工具。");
        var output=await ProcessRunner.RunAsync(executable,["usage","plan","--format","json"],TimeSpan.FromSeconds(15),c.CancellationToken,environment:c.ProcessEnvironment,maxOutputBytes:524288);return output.ExitCode==0?HandwrittenProviders.Parse(c.Account,output.Output):Missing(c,output.TimedOut?"unreachable":"toolFailed","arkcli 没有返回额度。");
    }
    async Task<UsageReading> Single(FetchContext c) {
        var token=Token(c.Credential);string? url=null;Dictionary<string,string>? headers=null;string method="GET";object? body=null;
        switch(id.ToLowerInvariant()) {
            case "kimicode":url="https://api.kimi.com/coding/v1/usages";break;
            case "v2ex":url="https://edge.v2ex.com/api/v2/chat/quota";break;
            case "opencodego":token??=S(Json(Read(c,".local/share/opencode/auth.json")),"opencode-go.key");url="https://opencode.ai/zen/go/v1/usage";break;
            case "zai":url="https://api.z.ai/api/monitor/usage/quota/limit";break;
            case "glmcoding":foreach(var file in new[]{".coding-relay/glm-api-key",".config/bigmodel/api_key",".config/zhipu/api_key"}){token??=Read(c,file)?.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();}url="https://open.bigmodel.cn/api/monitor/usage/quota/limit";break;
            case "ollamacloud":url="https://ollama.com/settings";if(token!=null){token=HandwrittenCookies.Normalize(id,token);if(token==null)return Missing(c,"invalidCookie","请保存 Ollama 登录会话 Cookie。");headers=Cookie(token,"https://ollama.com");headers["Accept"]="text/html";headers["Accept-Language"]="en-US,en;q=0.9";}break;
            case "qoder":var host=c.Account.Extra.GetValueOrDefault("site")=="china"?"qoder.com.cn":"qoder.com";url="https://"+host+"/api/v2/me/usages/big_model_credits";if(token!=null){token=HandwrittenCookies.Normalize(id,token);if(token==null)return Missing(c,"invalidCookie","会话 Cookie 无效。");headers=Cookie(token,"https://"+host);}break;
            case "minimax":
                var api=c.Account.Extra.GetValueOrDefault("site")=="china"?"https://api.minimaxi.com":"https://api.minimax.io";
                if(token==null)return Missing(c,"apiKeyMissing","请先保存 API 密钥。");UsageReading? first=null;foreach(var path in new[]{"/v1/token_plan/remains","/v1/api/openplatform/coding_plan/remains"}){var reading=Finish(c,await ProviderRequest.SendAsync(c,api+path,headers:Bearer(token)));if(reading.Success)return reading;first??=reading;}return first!;
        }
        if(token==null)return Missing(c,"apiKeyMissing","请先保存当前账号的凭据。");if(url==null)return Missing(c,"unsupportedRoute","此读取通道尚未接入。");
        return Finish(c,await ProviderRequest.SendAsync(c,url,method,body,headers??Bearer(token)));
    }
    async Task<UsageReading> Gateway(FetchContext c,bool newApi) {
        var token=Token(c.Credential);if(token==null)return Missing(c,"apiKeyMissing","请先保存 API 密钥。");var root=c.Account.ServerAddress?.Trim().TrimEnd('/');
        if(!newApi&&root?.EndsWith("/v1/usage",StringComparison.OrdinalIgnoreCase)==true)root=root[..^9];
        if(root?.EndsWith("/v1",StringComparison.OrdinalIgnoreCase)==true)root=root[..^3];
        if(!Uri.TryCreate(root,UriKind.Absolute,out var uri)||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||uri.Scheme!="https"&&!(uri.Scheme=="http"&&(uri.IsLoopback||IsPrivate(uri.Host))))return Missing(c,"serverAddressRefused","请输入 HTTPS 服务地址；本机或私有网络可使用 HTTP。");
        if(!newApi)return Finish(c,await ProviderRequest.SendAsync(c,root+"/v1/usage",headers:Bearer(token)));
        var sub=await ProviderRequest.SendAsync(c,root+"/v1/dashboard/billing/subscription",headers:Bearer(token));if(sub.Status!=200)return ProviderParsing.Failed(c,sub);
        var usage=await ProviderRequest.SendAsync(c,root+"/v1/dashboard/billing/usage",headers:Bearer(token));if(usage.Status!=200)return ProviderParsing.Failed(c,usage);
        var status=await ProviderRequest.SendAsync(c,root+"/api/status");return Finish(c,sub,usage.Body,status.Body);
    }
    static bool IsPrivate(string host) {if(!IPAddress.TryParse(host,out var ip))return false;var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1] is >=16 and <=31)||ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal;}
    async Task<UsageReading> Command(FetchContext c) {
        var token=Token(c.Credential)??S(Json(Read(c,".commandcode/auth.json")),"apiKey");if(token==null)return Missing(c,"apiKeyMissing","请先保存 API 密钥或登录 Command Code。");var h=Bearer(token);const string root="https://api.commandcode.ai";
        var who=await ProviderRequest.SendAsync(c,root+"/alpha/whoami?limits=1",headers:h);if(who.Status!=200)return ProviderParsing.Failed(c,who);
        if(JsonTools.Bool(who.Json,"success")==false)return Missing(c,"apiKeyRefused","服务拒绝了当前凭据。");var org=S(who.Json,"org.id");var query=org==null?"":"?orgId="+Uri.EscapeDataString(org);
        var credits=await ProviderRequest.SendAsync(c,root+"/alpha/billing/credits"+query,headers:h);if(credits.Status!=200)return ProviderParsing.Failed(c,credits);
        var sub=await ProviderRequest.SendAsync(c,root+"/alpha/billing/subscriptions"+query,headers:h);var since=S(sub.Json,"data.currentPeriodStart");var summary=await ProviderRequest.SendAsync(c,root+"/alpha/usage/summary"+query+(since==null?"":(query==""?"?":"&")+"since="+Uri.EscapeDataString(since)),headers:h);
        return Finish(c,credits,who.Body,sub.Status==200?sub.Body:"{}",summary.Status==200?summary.Body:"{}");
    }
    async Task<UsageReading> Xiaomi(FetchContext c) {
        var token=HandwrittenCookies.Normalize("xiaomiMiMo",c.Credential??"");if(token==null)return Missing(c,"sessionMissing","请先保存小米平台登录会话。");var h=Cookie(token,"https://platform.xiaomimimo.com");var replies=new List<HttpReply>();foreach(var path in new[]{"tokenPlan/detail","tokenPlan/usage","balance"})replies.Add(await ProviderRequest.SendAsync(c,"https://platform.xiaomimimo.com/api/v1/"+path,headers:h));
        var good=replies.Where(r=>r.Status==200).ToArray();if(good.Length==0)return ProviderParsing.Failed(c,replies[0]);return Finish(c,good[0],good.Skip(1).Select(r=>r.Body).ToArray());
    }
    async Task<UsageReading> StepFun(FetchContext c) {
        var token=HandwrittenCookies.Normalize("stepFun",c.Credential??"");if(token==null)return Missing(c,"sessionMissing","请先保存 StepFun 登录会话。");var host=c.Account.Extra.GetValueOrDefault("site")=="international"?"https://platform.stepfun.ai":"https://platform.stepfun.com";var h=Cookie(token,host);h["Referer"]=host+"/plan-usage";h["oasis-appid"]="10300";h["oasis-platform"]="web";if(HandwrittenCookies.StepFunWebId(token) is {} webid)h["oasis-webid"]=webid;
        var usage=await ProviderRequest.SendAsync(c,host+"/api/step.openapi.devcenter.Dashboard/QueryStepPlanRateLimit","POST",new{},h);if(usage.Status!=200)return ProviderParsing.Failed(c,usage);var plan=await ProviderRequest.SendAsync(c,host+"/api/step.openapi.devcenter.Dashboard/GetStepPlanStatus","POST",new{},h);return Finish(c,usage,plan.Body);
    }
    async Task<UsageReading> Devin(FetchContext c) {
        if(c.Account.Route is "tooling" or "desktop-cache")return DevinCache(c);
        var credential=Token(c.Credential);if(credential==null)return c.Account.Route=="endpoint"?Missing(c):DevinCache(c);var pieces=credential.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);var token=pieces[0];var org=pieces.Length>1?pieces[1]:c.Account.Extra.GetValueOrDefault("organization");if(string.IsNullOrWhiteSpace(org))return Missing(c,"organizationMissing","请同时指定 Devin 组织名称。");
        var normalized=org.Trim('/');if(Uri.TryCreate(org,UriKind.Absolute,out var orgUrl)&&orgUrl.Host.EndsWith("devin.ai",StringComparison.OrdinalIgnoreCase))normalized=orgUrl.AbsolutePath.Trim('/');
        if(!normalized.StartsWith("org/")&&!normalized.StartsWith("organizations/"))normalized=(normalized.StartsWith("org_")||normalized.StartsWith("org-")?"organizations/":"org/")+normalized;
        var paths=new List<string>{normalized};if(normalized.StartsWith("organizations/"))paths.Insert(0,normalized[14..]);if(normalized.StartsWith("org/"))paths.Add(normalized[4..]);
        var h=Bearer(token);if(normalized.StartsWith("organizations/"))h["x-cog-org-id"]=normalized[14..];HttpReply? last=null;foreach(var path in paths.Distinct()) {var escaped=string.Join('/',path.Split('/').Select(Uri.EscapeDataString));last=await ProviderRequest.SendAsync(c,"https://app.devin.ai/api/"+escaped+"/billing/quota/usage",headers:h);if(last.Status==200)return Finish(c,last);if(last.Status!=404)break;}return ProviderParsing.Failed(c,last!);
    }
    UsageReading DevinCache(FetchContext c) {
        var file=c.Account.SourcePaths.FirstOrDefault();if(file==null)return Missing(c,"localLoginMissing","请指定 Devin 客户端状态数据库。");
        foreach(var row in SqliteReader.Query(file,"SELECT key,value FROM ItemTable WHERE key LIKE '%plan%' OR key LIKE '%Plan%'")) {
            var text=row.GetValueOrDefault("value");if(text!=null){var r=HandwrittenProviders.Parse(c.Account,text);if(r.Success){r.Source="desktop-cache";r.IsStale=true;r.ObservedAt=File.GetLastWriteTimeUtc(file);return r;}}
        }return Missing(c,"noLimitsReported","Devin 客户端尚未保存套餐用量。");
    }
}
