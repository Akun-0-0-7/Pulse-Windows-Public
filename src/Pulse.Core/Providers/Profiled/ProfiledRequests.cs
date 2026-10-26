// Translated from qunqin24/Pulse b035a7a. Copyright (c) 2026 qunqin24. Apache-2.0.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;

public static partial class ProfiledProviders
{
    static string? CookieValue(string cookie,string name) => cookie.Split(';').Select(x=>x.Trim()).FirstOrDefault(x=>x.StartsWith(name+"=",StringComparison.Ordinal))?[(name.Length+1)..] is {Length:>0} value?value:null;
    static async Task<HttpReply> Ask(FetchContext c,string credential,string url,string method="GET",object? body=null,bool cookie=false,Dictionary<string,string>? headers=null) {
        var all=Auth(credential,cookie);if(headers is not null)foreach(var (key,value) in headers)all[key]=value;
        return await ProviderRequest.SendAsync(c,url,method,body,all);
    }
    static UsageReading ReadReply(FetchContext c,HttpReply reply,IReadOnlyDictionary<string,string>? extra=null) {
        if(reply.Status is >=300 and <400)return Missing(c,"sessionExpired","网页登录已过期，请重新连接。");
        if(reply.Status is <200 or >=300)return ProviderParsing.Failed(c,reply);
        var result=Parse(c,reply.Body,extra);result.Diagnostics.Add(reply.Diagnostic);return result;
    }
    static async Task<UsageReading?> FetchSpecial(string id,FetchContext c) => id switch {
        "gemini"=>await FetchGemini(c), "nousPortal"=>await FetchNous(c), "jetBrainsAI"=>FetchJetBrains(c),
        "qwenCloud"=>await FetchQwen(c), "alibabaCodingPlan"=>await FetchAlibaba(c), "notionAI"=>await FetchNotion(c),
        "windsurf"=>await FetchWindsurf(c), "factory"=>await FetchFactory(c), "longCat"=>await FetchLongCat(c),
        "manus"=>await FetchManus(c), "typeSafe"=>await FetchTypeSafe(c), _=>null
    };
    static string? LocalProfileCredential(string id,FetchContext c) {
        string? Read(params string[] path)=>LocalPaths.ReadText(Path.Combine(new[]{c.HomeDirectory}.Concat(path).ToArray()));
        return id switch {
            "huggingFace"=>Read(".cache","huggingface","token")?.Split('\n').FirstOrDefault()?.Trim().Trim('\"','\''),
            "codebuff"=>J.Read(Read(".config","manicode","credentials.json")??"").S("default.authToken")??J.Read(Read(".config","manicode","credentials.json")??"").S("authToken"),
            "kiloCode"=>J.Read(Read(".local","share","kilo","auth.json")??Read("AppData","Local","kilo","auth.json")??"").S("kilo.access"),
            _=>null
        };
    }
    static Dictionary<string,string> BlEnvironment(FetchContext context) {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var key in new[]{"SystemRoot","WINDIR","COMSPEC","PATH","PATHEXT","TEMP","TMP","USERPROFILE","HOMEDRIVE","HOMEPATH","APPDATA","LOCALAPPDATA","USERNAME","USERDOMAIN","LANG","LC_ALL"})
            if(Environment.GetEnvironmentVariable(key) is {} value)result[key]=value;
        foreach(var key in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY","NO_PROXY"})if((context.ProcessEnvironment?.GetValueOrDefault(key)??(context.ProcessEnvironment is null?Environment.GetEnvironmentVariable(key):null)) is {} proxy)result[key]=proxy;
        return result;
    }
    static Dictionary<string,string> RequestHeaders(string id,string credential,bool cookie) {
        var h=new Dictionary<string,string>();
        void Site(string origin,string page){h["Origin"]=origin;h["Referer"]=origin+page;}
        switch(id) {
            case "bifrost":h["x-bf-vk"]=credential;h["Authorization"]="";break;
            case "gitKraken":h["Client-Name"]="Pulse";h["Client-Version"]="0.1.0";break;
            case "raycastAI":Site("https://www.raycast.com","/settings");break;
            case "perplexity":Site("https://www.perplexity.ai","/account/usage");h["User-Agent"]="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/135.0.0.0 Safari/537.36";break;
            case "sakana":h["Accept"]="text/html,application/xhtml+xml";h["Accept-Language"]="en-US,en;q=0.9";break;
            case "t3Chat":Site("https://t3.chat","/settings/customization");h["Accept"]="*/*";h["trpc-accept"]="application/jsonl";h["x-trpc-source"]="web-client";h["x-trpc-batch"]="true";break;
            case "mistral":Site("https://admin.mistral.ai","/organization/billing");if(CookieValue(credential,"csrftoken") is {} csrf)h["X-CSRFToken"]=csrf;break;
            case "zoomMate":Site("https://zoommate.zoom.us","");break;
            case "warp":h["x-warp-client-id"]="warp-app";h["x-warp-os-category"]="Windows";h["x-warp-os-name"]="Windows";h["x-warp-os-version"]=Environment.OSVersion.Version.ToString();h["User-Agent"]="Warp/1.0";break;
        }
        if(cookie&&credential.StartsWith("Cookie:",StringComparison.OrdinalIgnoreCase))h["Cookie"]=credential[7..].Trim();
        return h;
    }
    static async Task<UsageReading> FetchQwen(FetchContext c) {
        var cookie=Credential(c);if(cookie is null)return Missing(c,"sessionMissing","请先连接 Qwen Cloud 浏览器会话。");
        const string page="https://home.qwencloud.com/billing/subscription/token-plan-individual";
        var html=await Ask(c,cookie,page,cookie:true,headers:new(){{"Accept","text/html,application/xhtml+xml"}});
        var match=Regex.Match(html.Body,"(?:secToken|sec_token|csrfToken)[\"']?\\s*[:=]\\s*[\"']([^\"']+)[\"']");
        var signIn=Regex.IsMatch(html.Body,"passport.alibabacloud.com|signin.aliyun.com|account.alibabacloud.com/login|login.qwencloud.com",RegexOptions.IgnoreCase);
        string? token=html.Status==200&&!signIn&&match.Success?match.Groups[1].Value:CookieValue(cookie,"sec_token");
        if(token is null){var info=await Ask(c,cookie,"https://home.qwencloud.com/tool/user/info.json",cookie:true);if(info.Status==200)token=FindText(J.Read(info.Body),"secToken","sec_token","csrfToken","token");}
        if(token is null)return Missing(c,html.Status>=500?"serverError":"sessionExpired","Qwen Cloud 会话中未找到控制台安全令牌。");
        async Task<HttpReply> GatewayCall(string api,Dictionary<string,object> data) {
            var cornerstone=new Dictionary<string,string>{{"feTraceId",Guid.NewGuid().ToString()},{"feURL",page},{"protocol","V2"},{"console","ONE_CONSOLE"},{"productCode","p_efm"},{"domain","home.qwencloud.com"},{"consoleSite","QWENCLOUD"},{"userNickName",""},{"userPrincipalName",""},{"xsp_lang","en-US"}};
            if(CookieValue(cookie,"cna") is {} browser)cornerstone["X-Anonymous-Id"]=browser;
            data["cornerstoneParam"]=cornerstone;
            var fields=new Dictionary<string,string>{{"product","sfm_bailian"},{"action","IntlBroadScopeAspnGateway"},{"sec_token",token},{"region","ap-southeast-1"},{"language","en-US"},{"params",JsonSerializer.Serialize(new {Api=api,V="1.0",Data=data})}};
            var body=string.Join("&",fields.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
            var headers=new Dictionary<string,string>{{"Content-Type","application/x-www-form-urlencoded"},{"Origin","https://home.qwencloud.com"},{"Referer",page},{"X-Requested-With","XMLHttpRequest"}};
            if((CookieValue(cookie,"login_aliyunid_csrf")??CookieValue(cookie,"csrf")) is {} csrf){headers["x-xsrf-token"]=csrf;headers["x-csrf-token"]=csrf;}
            return await Ask(c,cookie,"https://cs-data.qwencloud.com/data/api.json?action=IntlBroadScopeAspnGateway&product=sfm_bailian&api="+Uri.EscapeDataString(api)+"&_v=undefined","POST",body,true,headers);
        }
        var usage=await GatewayCall("zeldaHttp.apikeyMgr./tokenplan/personal/api/v2/usage",new());
        var reading=ReadReply(c,usage);if(!reading.Success)return reading;
        var subscription=await GatewayCall("zeldaHttp.apikeyMgr./tokenplan/personal/api/v2/subscription",new(){{"commodityCode","sfm_tokenplansolo_public_intl"}});
        return subscription.Status==200?ReadReply(c,usage,new Dictionary<string,string>{{"subscription",subscription.Body}}):reading;
    }
    static async Task<UsageReading> FetchAlibaba(FetchContext c) {
        var key=Credential(c);if(key is null)return Missing(c,"apiKeyMissing","请先保存 Alibaba Coding Plan API 密钥。");
        UsageReading? first=null;
        foreach(var (host,region,commodity,page) in new[]{("modelstudio.console.alibabacloud.com","ap-southeast-1","sfm_codingplan_public_intl","/ap-southeast-1/?tab=coding-plan#/efm/coding_plan"),("bailian.console.aliyun.com","cn-beijing","sfm_codingplan_public_cn","/cn-beijing/?tab=model#/efm/coding_plan")}) {
            var origin="https://"+host;
            var reply=await Ask(c,key,origin+"/data/api.json?action=zeldaEasy.broadscope-bailian.codingPlan.queryCodingPlanInstanceInfoV2&product=broadscope-bailian&api=queryCodingPlanInstanceInfoV2&currentRegionId="+region,"POST",new {queryCodingPlanInstanceInfoRequest=new {commodityCode=commodity}},headers:new(){{"x-api-key",key},{"X-DashScope-API-Key",key},{"Origin",origin},{"Referer",origin+page}});
            var root=J.Read(reply.Body);
            var message=(FindText(root,"statusMessage","status_msg","message","msg")??"").ToLowerInvariant();
            var code=(FindText(root,"code","status","statusCode")??"").ToLowerInvariant();
            if(code.Contains("login")||message.Contains("login")||message.Contains("log in"))return Missing(c,"apiKeyRefused","控制台要求重新登录，当前 API 密钥未被接受。");
            var reading=ReadReply(c,reply);if(reading.Success)return reading;
            var status=Find(root,"statusCode","status_code","code").N("");
            if(status is not null&&status!=0&&status!=200)reading=Missing(c,status is 401 or 403||message.Contains("api key")||message.Contains("unauthorized")?"apiKeyRefused":"serverError","Alibaba Coding Plan 控制台拒绝了请求。");
            else if(reply.Status is 401 or 403)reading=Missing(c,"apiKeyRefused","此区域未接受当前 API 密钥。");
            if(reply.Status>=500||reading.ErrorCode=="serverError"&&reply.Status!=404)return reading;
            first??=reading;if(host.StartsWith("bailian"))return reading.ErrorCode=="apiKeyRefused"?first:reading;
        }
        return first!;
    }
    static string? AlibabaRefusal(J root) {
        var message=(FindText(root,"statusMessage","status_msg","message","msg")??"").ToLowerInvariant();
        var code=(FindText(root,"code","status","statusCode")??"").ToLowerInvariant();
        if(code.Contains("login")||message.Contains("login")||message.Contains("log in"))return "apiKeyRefused";
        var status=Find(root,"statusCode","status_code","code").N("");
        if(status is not null&&status!=0&&status!=200)return status is 401 or 403||message.Contains("api key")||message.Contains("unauthorized")?"apiKeyRefused":"serverError";
        return null;
    }
    static async Task<UsageReading> FetchGemini(FetchContext c) {
        var root=Path.Combine(c.HomeDirectory,".gemini");
        var settings=J.Read(LocalPaths.ReadText(Path.Combine(root,"settings.json"))??"{}");
        if((settings.S("security.auth.selectedType")??settings.S("selectedAuthType")) is "gemini-api-key" or "api-key" or "vertex-ai")return Missing(c,"localLoginMissing","Gemini CLI 当前使用 API Key 或 Vertex，请先在 CLI 登录 Google 账号。");
        var saved=J.Read(LocalPaths.ReadText(Path.Combine(root,"oauth_creds.json"))??"{}");
        var token=saved.S("access_token")??Credential(c);if(token is null)return Missing(c,"localLoginMissing","未找到 Gemini CLI Google 登录文件。");
        if(saved.D("expiry_date")<=DateTimeOffset.UtcNow)return Missing(c,"localLoginExpired","Gemini CLI 登录已过期，请运行 Gemini CLI 更新登录。");
        var assist=await Ask(c,token,"https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist","POST",new {metadata=new {ideType="GEMINI_CLI",pluginType="GEMINI"}});
        if(assist.Status==401)return Missing(c,"localLoginExpired","Gemini CLI 登录已过期。");
        var unsupported=Regex.IsMatch(assist.Body,"unsupported_client|ineligibletiererror",RegexOptions.IgnoreCase);
        if(assist.Status is <200 or >=300&&unsupported)return Missing(c,"noPlan","当前账号的套餐不提供 Gemini CLI 额度。");
        var body=J.Read(assist.Body);var project=body.S("cloudaicompanionProject")??body.S("cloudaicompanionProject.id")??body.S("cloudaicompanionProject.projectId");
        var quota=await Ask(c,token,"https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota","POST",project is null?new Dictionary<string,string>():new Dictionary<string,string>{{"project",project}});
        if(quota.Status==403&&(unsupported||Regex.IsMatch(quota.Body,"unsupported_client|ineligibletiererror",RegexOptions.IgnoreCase)))return Missing(c,"noPlan","当前账号的套餐不提供 Gemini CLI 额度。");
        if(quota.Status is 401 or 403)return Missing(c,"localLoginExpired","Gemini CLI 登录已过期。");
        return ReadReply(c,quota,new Dictionary<string,string>{{"assist",assist.Body}});
    }
    static async Task<UsageReading> FetchNotion(FetchContext c) {
        var cookie=Credential(c);if(cookie is null)return Missing(c,"sessionMissing","请先连接 Notion 浏览器会话。");
        var headers=new Dictionary<string,string>{{"Origin","https://app.notion.com"},{"Referer","https://app.notion.com/"}};
        var reply=await Ask(c,cookie,"https://app.notion.com/api/v3/getSpaces","POST",new{},true,headers);
        if(reply.Status!=200)return ReadReply(c,reply);
        var root=J.Read(reply.Body);var users=root.Properties().ToArray();
        J Record(J r)=>r["value.value"].IsObject?r["value.value"]:r["value"].IsObject?r["value"]:r;
        var identified=users.Where(x=>Record(x.Value["notion_user."+x.Name]).S("id")==x.Name).ToArray();
        if(identified.Length!=1&&users.Length!=1)return Missing(c,"unreadableReply","Notion 工作区响应包含多个账号，无法选择当前账号。");
        var user=identified.Length==1?identified[0].Value:users[0].Value;
        var spaces=user["space"].Properties().OrderBy(x=>x.Name).Select(x=>(Key:x.Name,Value:Record(x.Value))).ToArray();
        if(spaces.Length==0)return Missing(c,"noPlan","当前 Notion 账号没有工作区。");
        var selected=spaces.FirstOrDefault(x=>x.Value.S("subscription_tier")?.ToLowerInvariant() is "business" or "enterprise");if(selected.Key is null)selected=spaces[0];
        reply=await Ask(c,cookie,"https://app.notion.com/api/v3/getCreditRateLimitStatus","POST",new {spaceId=selected.Value.S("id")??selected.Key},true,headers);
        var result=ReadReply(c,reply);result.Plan=Title(selected.Value.S("subscription_tier"));return result;
    }
    static async Task<UsageReading> FetchWindsurf(FetchContext c) {
        var saved=J.Read(Credential(c)??"");var keys=new[]{"devin_session_token","devin_auth1_token","devin_account_id","devin_primary_org_id"};
        if(keys.Any(x=>saved.S(x) is null))return Missing(c,"sessionMissing","Windsurf 登录需要浏览器储存中的四项会话值。");
        var token=saved.S(keys[0])!;using var body=new MemoryStream();
        void Varint(ulong n){while(n>=128){body.WriteByte((byte)(n|128));n>>=7;}body.WriteByte((byte)n);}
        var bytes=Encoding.UTF8.GetBytes(token);body.WriteByte(10);Varint((ulong)bytes.Length);body.Write(bytes);body.WriteByte(16);body.WriteByte(1);
        const string url="https://windsurf.com/_backend/exa.seat_management_pb.SeatManagementService/GetPlanStatus";
        using var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new ByteArrayContent(body.ToArray())};request.Content.Headers.ContentType=new("application/proto");
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version","1");request.Headers.TryAddWithoutValidation("Origin","https://windsurf.com");request.Headers.TryAddWithoutValidation("Referer","https://windsurf.com/profile");
        foreach(var (header,index) in new[]{("x-auth-token",0),("x-devin-session-token",0),("x-devin-auth1-token",1),("x-devin-account-id",2),("x-devin-primary-org-id",3)})request.Headers.TryAddWithoutValidation(header,saved.S(keys[index]));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(c.CancellationToken);deadline.CancelAfter(c.RequestTimeout??TimeSpan.FromSeconds(25));
        using var response=await c.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
        while((count=await input.ReadAsync(chunk,deadline.Token))>0){if(buffer.Length+count>2*1024*1024)throw new IOException("服务响应超过大小限制。");buffer.Write(chunk,0,count);}
        var reply=new HttpReply((int)response.StatusCode,response.IsSuccessStatusCode?Convert.ToBase64String(buffer.ToArray()):Encoding.UTF8.GetString(buffer.ToArray())){Url=url};return ReadReply(c,reply);
    }
    static UsageReading FetchJetBrains(FetchContext c) {
        var candidates=c.Account.SourcePaths.Select(x=>LocalPaths.Expand(x,c.HomeDirectory)).ToList();
        if(candidates.Count==0)foreach(var directory in new[]{Path.Combine(c.HomeDirectory,"AppData","Roaming","JetBrains"),Path.Combine(c.HomeDirectory,".config","JetBrains"),Path.Combine(c.HomeDirectory,"AppData","Roaming","Google")}) {
            if(!Directory.Exists(directory))continue;
            try{foreach(var ide in Directory.EnumerateDirectories(directory).Take(200))candidates.Add(Path.Combine(ide,"options","AIAssistantQuotaManager2.xml"));}catch(UnauthorizedAccessException){}
        }
        var path=candidates.Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();var text=path is null?null:LocalPaths.ReadText(path);
        return text is null?Missing(c,"localAppMissing","未找到 JetBrains AI 配额文件，可在账号设置中指定文件。"):Parse(c,text);
    }
    static DateTimeOffset? JwtExpiry(string token) {
        var parts=token.Split('.');if(parts.Length!=3)return null;var raw=parts[1].Replace('-','+').Replace('_','/');raw=raw.PadRight((raw.Length+3)/4*4,'=');
        try{return J.Read(Encoding.UTF8.GetString(Convert.FromBase64String(raw))).D("exp");}catch(FormatException){return null;}
    }
    static async Task<UsageReading> FetchNous(FetchContext c) {
        var entries=new List<J>();bool expired=false;
        if(Credential(c) is {} pasted){var json=J.Read(pasted);entries.Add(json.IsObject?json:J.Read(JsonSerializer.Serialize(new {access_token=pasted})));}
        foreach(var path in new[]{Path.Combine(c.HomeDirectory,".hermes","auth.json"),Path.Combine(c.HomeDirectory,".hermes","shared","nous_auth.json")}) {
            var root=J.Read(LocalPaths.ReadText(path)??"");if(root["providers.nous"].IsObject)entries.Add(root["providers.nous"]);
            entries.AddRange(root.A("credential_pool.nous").OrderByDescending(x=>x.D("expires_at")??JwtExpiry(x.S("access_token")??"")));entries.Add(root);
        }
        foreach(var entry in entries) {
            if(entry.S("access_token") is not {} token)continue;
            if((entry.D("expires_at")??JwtExpiry(token))<=DateTimeOffset.UtcNow.AddSeconds(60)){expired=true;continue;}
            var portal=entry.S("portal_base_url")??"https://portal.nousresearch.com";
            if(!Uri.TryCreate(portal,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!(uri.Host=="nousresearch.com"||uri.Host.EndsWith(".nousresearch.com",StringComparison.Ordinal))||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.AbsolutePath!="/")uri=new("https://portal.nousresearch.com");
            return ReadReply(c,await Ask(c,token,uri.GetLeftPart(UriPartial.Authority)+"/api/oauth/account"));
        }
        return Missing(c,expired?"localLoginExpired":"localLoginMissing",expired?"Hermes 的 Nous 登录已过期，请先在 Hermes 重新登录。":"未找到 Hermes 保存的 Nous 登录。");
    }
    static async Task<UsageReading> FetchFactory(FetchContext c) {
        var key=Credential(c);if(key is null)return Missing(c,"apiKeyMissing","请先保存 Factory API 密钥。");
        var h=new Dictionary<string,string>{{"x-factory-client","web-app"},{"Origin","https://app.factory.ai"},{"Referer","https://app.factory.ai/"}};
        var me=await Ask(c,key,"https://api.factory.ai/api/app/auth/me",headers:h);if(me.Status is <200 or >=300)return ReadReply(c,me);
        var extras=new Dictionary<string,string>{{"me",me.Body}};
        var limits=await Ask(c,key,"https://api.factory.ai/api/billing/limits",headers:h);
        if(limits.Status is >=200 and <300&&J.Read(limits.Body).B("usesTokenRateLimitsBilling")==true){var reading=ReadReply(c,limits,extras);if(reading.Success)return reading;}
        var identity=J.Read(me.Body);var user=identity.S("userProfile.id");
        var route="https://api.factory.ai/api/organization/subscription/usage?useCache=true"+(user is null?"":"&userId="+Uri.EscapeDataString(user));
        return ReadReply(c,await Ask(c,key,route,headers:h),extras);
    }
    static async Task<UsageReading> FetchLongCat(FetchContext c) {
        var cookie=Credential(c);if(cookie is null)return Missing(c,"sessionMissing","请先连接 LongCat 浏览器会话。");
        var h=new Dictionary<string,string>{{"Origin","https://longcat.chat"},{"Referer","https://longcat.chat/platform/usage"}};
        var user=await Ask(c,cookie,"https://longcat.chat/api/v1/user-current",cookie:true,headers:h);if(user.Status is <200 or >=300)return ReadReply(c,user);
        var packs=await Ask(c,cookie,"https://longcat.chat/api/pay/quota/metering/token-packs/summary","POST",new{},true,h);
        var extras=new Dictionary<string,string>();var lot=Unwrap(J.Read(packs.Body))["currentLot"];
        if(packs.Status!=200||lot.S("status")?.ToUpperInvariant()!="ACTIVE"||lot.N("totalToken") is not >0){var usage=await Ask(c,cookie,"https://longcat.chat/api/lc-platform/v1/tokenUsage",cookie:true,headers:h);if(usage.Status!=200)return ReadReply(c,usage);extras["usage"]=usage.Body;}
        var fuel=await Ask(c,cookie,"https://longcat.chat/api/lc-platform/v1/pending-fuel-packages",cookie:true,headers:h);if(fuel.Status==200)extras["fuel"]=fuel.Body;
        return ReadReply(c,packs.Status==200?packs:new HttpReply(200,"{}"),extras);
    }
    static async Task<UsageReading> FetchManus(FetchContext c) {
        var credential=Credential(c);var token=credential is null?null:CookieValue(credential,"session_id");
        if(token is null)return Missing(c,"sessionMissing","请先连接含 session_id 的 Manus 浏览器会话。");
        return ReadReply(c,await Ask(c,token,"https://api.manus.im/user.v1.UserService/GetAvailableCredits","POST",new{},headers:new(){{"Connect-Protocol-Version","1"},{"Origin","https://manus.im"},{"Referer","https://manus.im/"}}));
    }
    static async Task<UsageReading> FetchTypeSafe(FetchContext c) {
        var cookie=Credential(c);if(cookie is null)return Missing(c,"sessionMissing","请先保存 TypeSafe 的 Cookie 请求头。");
        if(cookie.StartsWith("Cookie:",StringComparison.OrdinalIgnoreCase))cookie=cookie[7..].Trim();
        const string origin="https://console.typesafe.ai",page=origin+"/settings/billing";
        var html=await Ask(c,cookie,page,cookie:true,headers:new(){{"Accept","text/html"}});
        if(html.Status!=200)return ReadReply(c,html);
        if(Regex.IsMatch(html.Body,@"\(auth\).*children.*login",RegexOptions.IgnoreCase))return Missing(c,"sessionExpired","TypeSafe 网页会话已过期。");
        var chunks=Regex.Matches(html.Body,"<script\\b[^>]*\\bsrc=[\"']([^\"']+)[\"'][^>]*>",RegexOptions.IgnoreCase).Select(x=>x.Groups[1].Value).Select(x=>x.StartsWith('/')&&!x.StartsWith("//")?origin+x:x).Where(x=>x.StartsWith(origin+"/",StringComparison.Ordinal)&&Regex.IsMatch(x,@"\.js($|\?)",RegexOptions.IgnoreCase)).Distinct().Take(60);
        string? action=null;
        foreach(var chunk in chunks) {
            var script=await ProviderRequest.SendAsync(c,chunk);if(script.Status==429||script.Status>=500)return ReadReply(c,script);if(script.Status is <200 or >=300)continue;
            var match=Regex.Match(script.Body,"\"([0-9a-f]{40,})\"[^)]{0,150}\"getBillingOverviewResult\"",RegexOptions.IgnoreCase);
            if(match.Success){action=match.Groups[1].Value;break;}
        }
        if(action is null)return Missing(c,"unreadableReply","TypeSafe 页面脚本未报告账单读取动作。");
        var reply=await Ask(c,cookie,page,"POST","[]",true,new(){{"Origin",origin},{"Next-Action",action},{"Accept","text/x-component"}});
        return ReadReply(c,reply);
    }
}
