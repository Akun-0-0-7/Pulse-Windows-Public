// Translated from qunqin24/Pulse b035a7a. Copyright (c) 2026 qunqin24. Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;

public static partial class ProfiledProviders
{
    static readonly IReadOnlyDictionary<string,string> BearerRoutes = new Dictionary<string,string> {
        ["clinePass"]="https://api.cline.bot/api/v1/users/me/plan/usage-limits",
        ["synthetic"]="https://api.synthetic.new/v2/quotas",
        ["atlasCloud"]="https://api.atlascloud.ai/public/v1/balance",
        ["aixy"]="https://api.aixy-gateway.com/v1/usage",
        ["bifrost"]="/api/governance/virtual-keys/quota",
        ["chutes"]="https://api.chutes.ai/users/me/subscription_usage",
        ["clawRouter"]="https://clawrouter.openclaw.ai/v1/usage",
        ["deepInfra"]="https://api.deepinfra.com/payment/checklist?compute_owed=true",
        ["devPass"]="https://api.llmgateway.io/v1/key",
        ["hyper"]="https://hyper.charm.land/v1/credits",
        ["gitKraken"]="https://api.gitkraken.dev/v1/ai-tasks/usage",
        ["kiloCode"]="https://app.kilo.ai/api/trpc/user.getCreditBlocks,kiloPass.getState?batch=1&input=%7B%220%22%3A%7B%22json%22%3A%7B%7D%7D%2C%221%22%3A%7B%22json%22%3A%7B%7D%7D%7D",
        ["v0"]="https://api.v0.dev/v1/user/billing",
        ["zenMux"]="https://zenmux.ai/api/v1/management/subscription/detail",
        ["moonshot"]="https://api.moonshot.ai/v1/users/me/balance",
        ["neuralwatt"]="https://api.neuralwatt.com/v1/quota",
        ["openAIPlatform"]="https://api.openai.com/v1/dashboard/billing/credit_grants",
        ["poe"]="https://api.poe.com/usage/current_balance",
        ["venice"]="https://api.venice.ai/api/v1/billing/balance",
        ["vercelAIGateway"]="https://ai-gateway.vercel.sh/v1/credits",
        ["xKiro"]="https://api.xkiro.com/v1/usage"
    };
    static readonly IReadOnlyDictionary<string,string> CookieRoutes = new Dictionary<string,string> {
        ["augment"]="https://app.augmentcode.com/api/credits",
        ["zed"]="https://cloud.zed.dev/frontend/billing/usage",
        ["raycastAI"]="https://www.raycast.com/frontend_api/current_user/ai_credits",
        ["perplexity"]="https://www.perplexity.ai/rest/billing/credits?version=2.18&source=default",
        ["mistral"]="https://admin.mistral.ai/api/billing/credits",
        ["sakana"]="https://console.sakana.ai/billing",
        ["t3Chat"]="https://t3.chat/api/trpc/getCustomerData?batch=1&input="+Uri.EscapeDataString("{\"0\":{\"json\":{\"sessionId\":null},\"meta\":{\"values\":{\"sessionId\":[\"undefined\"]}}}}"),
        ["notionAI"]="https://app.notion.com/api/v3/getAIUsageLimit",
        ["qwenCloud"]="https://cs-data.qwencloud.com/data/api.json",
        ["longCat"]="https://longcat.chat/api/pay/quota/metering/token-packs/summary",
        ["typeSafe"]="https://console.typesafe.ai/settings/billing"
    };
    static Dictionary<string,string> Auth(string value,bool cookie=false) => new() {{cookie?"Cookie":"Authorization",cookie?value:"Bearer "+value},{"Accept","application/json"}};
    static UsageReading Missing(FetchContext c,string code,string message) => UsageReading.Unavailable(c.Account,code,message);
    static string? Credential(FetchContext c) => string.IsNullOrWhiteSpace(c.Credential)?null:c.Credential.Trim();
    static string? Gateway(FetchContext c,string path) {
        var source=c.Account.ServerAddress?.Trim().TrimEnd('/');
        if(!Uri.TryCreate(source,UriKind.Absolute,out var uri) || (uri.Scheme!="https" && !(uri.Scheme=="http"&&uri.IsLoopback)) || uri.UserInfo.Length>0 || uri.Fragment.Length>0) return null;
        var baseUrl=uri.GetLeftPart(UriPartial.Authority)+uri.AbsolutePath.TrimEnd('/');
        if(baseUrl.EndsWith("/v1",StringComparison.OrdinalIgnoreCase))baseUrl=baseUrl[..^3];
        return baseUrl+path;
    }
    static async Task<UsageReading> Fetch(string id,FetchContext c) {
        if(!c.Account.Enabled)return Missing(c,"disabled","当前账号未启用。");
        try {return await FetchCore(id,c);}
        catch(OperationCanceledException) when(!c.CancellationToken.IsCancellationRequested) {return Missing(c,"unreachable","读取额度超时。");}
        catch(HttpRequestException) {return Missing(c,"unreachable","无法连接服务。");}
        catch(IOException) {return Missing(c,"localReadFailed","无法读取本机数据。");}
        catch(JsonException) {return Missing(c,"unreadableReply","服务返回格式无法解析。");}
        catch(FormatException) {return Missing(c,"unreadableReply","服务返回格式无法解析。");}
        catch(InvalidOperationException) {return Missing(c,"unreadableReply","服务返回格式无法解析。");}
        catch(System.ComponentModel.Win32Exception) {return Missing(c,"localToolMissing","无法启动本机命令行工具，请检查程序路径。");}
    }
    static async Task<UsageReading> FetchCore(string id,FetchContext c) {
        if(await FetchSpecial(id,c) is {} special)return special;
        var credential=Credential(c)??LocalProfileCredential(id,c);
        if(id=="gitKraken"&&credential?.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)==true)credential=credential[7..].Trim();
        if(id=="jetBrainsAI") {
            var path=c.Account.SourcePaths.FirstOrDefault();
            if(path is null)return Missing(c,"localAppMissing","请指定 JetBrains AI 配额文件。");
            var text=LocalPaths.ReadText(LocalPaths.Expand(path,c.HomeDirectory));
            return text is null?Missing(c,"localAppMissing","未找到 JetBrains AI 配额文件。") : Parse(c,text);
        }
        if(id=="alibabaTokenPlan") {
            var binary=LocalPaths.FindExecutable("bl");
            if(binary is null)return Missing(c,"localLoginMissing","未找到阿里云 bl 命令行工具。");
            UsageReading? first=null;
            foreach(var (site,region) in new[]{("international","ap-southeast-1"),("domestic","cn-beijing")}) {
                var run=await ProcessRunner.RunAsync(binary,["usage","token-plan","--console-region",region,"--console-site",site,"--output","json"],TimeSpan.FromSeconds(15),c.CancellationToken,environment:BlEnvironment(c),maxOutputBytes:262144);
                var parsed=run.ExitCode==0&&!run.TimedOut?Parse(c,run.Output):Missing(c,run.TimedOut?"localToolTimeout":"localLoginExpired","阿里云 bl 命令未返回有效用量。");
                if(parsed.Success)return parsed;
                first??=parsed;
            }
            return first!;
        }
        if(credential is null)return Missing(c, CookieRoutes.ContainsKey(id)?"sessionMissing":"apiKeyMissing","请先为当前账号保存登录凭据。");
        var extras=new Dictionary<string,string>();
        HttpReply? main=null;
        async Task<HttpReply> Send(string url,string method="GET",object? body=null,bool cookie=false,Dictionary<string,string>? additional=null) {
            var h=Auth(credential,cookie);foreach(var item in RequestHeaders(id,credential,cookie))h[item.Key]=item.Value;if(additional is not null)foreach(var item in additional)h[item.Key]=item.Value;
            return await ProviderRequest.SendAsync(c,url,method,body,h);
        }
        async Task<bool> Extra(string key,string url,string method="GET",object? body=null,bool cookie=false,Dictionary<string,string>? headers=null) {
            var reply=await Send(url,method,body,cookie,headers);
            if(reply.Status is >=200 and <300){extras[key]=reply.Body;return true;}return false;
        }
        if(BearerRoutes.TryGetValue(id,out var url)) {
            if(id=="bifrost") {url=Gateway(c,url)!;if(url is null)return Missing(c,"serverAddressRefused","请填写有效的网关地址。");}
            if(id=="moonshot"&&c.Account.Extra.GetValueOrDefault("region")=="china")url="https://api.moonshot.cn/v1/users/me/balance";
            main=await Send(url);
            if(main.Status is >=200 and <300) {
                if(id=="v0")await Extra("rateLimit","https://api.v0.dev/v1/rate-limits");
                if(id=="zenMux")await Extra("balance","https://zenmux.ai/api/v1/management/payg/balance");
            }
        } else if(CookieRoutes.TryGetValue(id,out url)) {
            if(id=="longCat") {var identity=await Send("https://longcat.chat/api/v1/user-current",cookie:true);if(identity.Status is <200 or >=300)return ProviderParsing.Failed(c,identity);await Extra("usage","https://longcat.chat/api/lc-platform/v1/tokenUsage",cookie:true);await Extra("fuel","https://longcat.chat/api/lc-platform/v1/pending-fuel-packages",cookie:true);main=await Send(url,"POST",new{},true);}
            else if(id=="qwenCloud")main=await Send(url,"POST",new{},true);
            else if(id=="notionAI")main=await Send(url,"POST",new{},true);
            else main=await Send(url,cookie:true);
            if(main.Status is >=200 and <300) {
                switch(id) {
                    case "augment":await Extra("subscription","https://app.augmentcode.com/api/subscription",cookie:true);break;
                    case "mistral":await Extra("subscriptionPage","https://admin.mistral.ai/subscription",cookie:true);if(CookieValue(credential,"csrftoken") is not null)await Extra("vibe","https://console.mistral.ai/api-ui/trpc/billing.vibeUsage?batch=1&input="+Uri.EscapeDataString("{\"0\":{\"json\":null,\"meta\":{\"values\":[\"undefined\"],\"v\":1}}}"),cookie:true);break;
                    case "sakana":await Extra("payAsYouGo","https://console.sakana.ai/billing?tab=payAsYouGo",cookie:true);break;
                }
            }
        } else switch(id) {
            case "elevenLabs":main=await Send("https://api.elevenlabs.io/v1/user/subscription",additional:new(){{"xi-api-key",credential},{"Authorization",""}});break;
            case "huggingFace":main=await Send("https://huggingface.co/api/spaces/zero-gpu/quota");break;
            case "xaiAPI":{var parts=credential.Split(':',2);if(parts.Length!=2)return Missing(c,"apiKeyMissing","请输入团队 ID 和 API 密钥。");credential=parts[1];main=await Send("https://management-api.x.ai/v1/billing/teams/"+Uri.EscapeDataString(parts[0])+"/prepaid/balance");break;}
            case "amp":main=await Send("https://ampcode.com/api/internal?userDisplayBalanceInfo","POST",new{method="userDisplayBalanceInfo", @params=new{} });break;
            case "manus":main=await Send("https://api.manus.im/user.v1.UserService/GetAvailableCredits","POST",new{});break;
            case "codebuff":main=await Send("https://www.codebuff.com/api/v1/usage","POST",new{fingerprintId="pulse-usage"});if(Credential(c) is null&&main.Status is >=200 and <300)await Extra("subscription","https://www.codebuff.com/api/user/subscription");break;
            case "abacus":main=await Send("https://apps.abacus.ai/api/_getOrganizationComputePoints",cookie:true);if(main.Status is >=200 and <300)await Extra("billing","https://apps.abacus.ai/api/_getBillingInfo","POST",new{},true);break;
            case "factory":await Extra("me","https://api.factory.ai/api/app/auth/me");main=await Send("https://api.factory.ai/api/billing/limits");if(main.Status is <200 or >=300)main=await Send("https://api.factory.ai/api/organization/subscription/usage?useCache=true");break;
            case "ibmBob":{
                var authorization=credential.Split('.').Length==3?"Bearer "+credential:"Apikey "+credential;
                var headers=new Dictionary<string,string>{{"Authorization",authorization}};
                main=await Send("https://api.us-east.bob.ibm.com/admin/v1/profile",additional:headers);
                if(main.Status is >=200 and <300){var profile=J.Read(main.Body);int index=0;var teamReplies=new Dictionary<string,JsonElement>();
                    foreach(var instance in profile.A("instances")) {
                        var user=instance.S("user_id");var host=instance.S("region_domain")?.ToLowerInvariant()??"us-east.bob.ibm.com";
                        if(!host.StartsWith("api."))host="api."+host;
                        if(!System.Text.RegularExpressions.Regex.IsMatch(host,@"^(?:[a-z0-9-]+\.)*bob\.ibm\.com$")||user is null)return Missing(c,"unreadableReply","IBM Bob 区域或用户信息无效。");
                        foreach(var team in instance.A("teams")) {
                            var teamId=team.S("id");if(teamId is null)continue;
                            var h=new Dictionary<string,string>(headers){{"x-instance-id",instance.S("instance_id")??""},{"x-team-id",teamId}};
                            var reply=await Send("https://"+host+"/admin/v1/teams/"+Uri.EscapeDataString(teamId)+"/users/"+Uri.EscapeDataString(user),additional:h);
                            if(reply.Status is <200 or >=300)return ProviderParsing.Failed(c,reply);
                            teamReplies[(index++).ToString(System.Globalization.CultureInfo.InvariantCulture)]=reply.Json;
                        }
                    }
                    extras["teams"]=JsonSerializer.Serialize(teamReplies);
                }break;
            }
            case "zoomMate":{
                foreach(var host in new[]{"ai.zoom.us","zoommate.zoom.us"}) {
                    var login=await Send("https://"+host+"/ai-computer/api/v1/login/?continue=https%3A%2F%2Fzoommate.zoom.us%2F",cookie:true);
                    if(login.Status is <200 or >=300){main=login;if(login.Status is not (500 or 502 or 503 or 504))break;continue;}
                    var token=J.Read(login.Body).S("data.nak");
                    if(token is null)return Missing(c,"sessionExpired","ZoomMate 登录会话已失效。");
                    main=await Send("https://"+host+"/ai-computer/api/v1/credits/status",cookie:true,additional:new(){{"Authorization","Bearer "+token}});
                    if(main.Status is >=200 and <300)break;
                }
                break;
            }
            case "replicate":{
                var page=await Send("https://replicate.com/account/billing",cookie:true,additional:new(){{"Accept","text/html"}});
                if(page.Status is <200 or >=300)return ProviderParsing.Failed(c,page);
                var match=System.Text.RegularExpressions.Regex.Match(page.Body,"<script\\b[^>]*id=[\\\"']react-component-props[^\\\"']*[\\\"'][^>]*>([\\s\\S]*?)</script>",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(!match.Success)return Missing(c,"unreadableReply","Replicate 账单页没有账号信息。");
                var account=FindObject(J.Read(match.Groups[1].Value),"account.kind")["account"];
                var kind=account.S("kind");var name=account.S("username");
                if(kind is not("user" or "organization")||name is null)return Missing(c,"unreadableReply","Replicate 账单页账号信息无效。");
                main=await Send("https://replicate.com/api/"+(kind=="user"?"users":"organizations")+"/"+Uri.EscapeDataString(name)+"/unused-credit",cookie:true);
                break;
            }
            case "gemini":await Extra("assist","https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist","POST",new{});main=await Send("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota","POST",new{});break;
            case "warp":main=await Send("https://app.warp.dev/graphql/v2?op=GetRequestLimitInfo","POST",new{operationName="GetRequestLimitInfo",query="query GetRequestLimitInfo($requestContext: RequestContext!) { user(requestContext: $requestContext) { __typename ... on UserOutput { user { requestLimitInfo { isUnlimited requestsUsedSinceLastRefresh requestLimit nextRefreshTime } bonusGrants { requestCreditsGranted requestCreditsRemaining expiration } workspaces { bonusGrantsInfo { grants { requestCreditsGranted requestCreditsRemaining expiration } } } } } } }",variables=new {requestContext=new {clientContext=new{},osContext=new {category="Windows",name="Windows",version=Environment.OSVersion.Version.ToString()}}}});break;
            case "windsurf":main=await Send("https://windsurf.com/_backend/exa.seat_management_pb.SeatManagementService/GetPlanStatus","POST","");break;
            case "llmProxy":case "liteLLM":{var route=Gateway(c,id=="llmProxy"?"/v1/quota-stats":"/key/info");if(route is null)return Missing(c,"serverAddressRefused","请填写有效的网关地址。");main=await Send(route);if(id=="liteLLM"&&main.Status is >=200 and <300){var key=J.Read(main.Body);var user=key.S("info.user_id")??key.S("user_id");var team=key.S("info.team_id")??key.S("team_id");if(user is not null)await Extra("user",Gateway(c,"/user/info?user_id="+Uri.EscapeDataString(user))!);else if(team is not null)await Extra("team",Gateway(c,"/team/info?team_id="+Uri.EscapeDataString(team))!);}break;}
            case "nousPortal":{var auth=J.Read(credential);var token=auth.S("access_token")??credential;credential=token;var portal=auth.S("portal_base_url")??"https://portal.nousresearch.com";if(!Uri.TryCreate(portal,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!(uri.Host=="nousresearch.com"||uri.Host.EndsWith(".nousresearch.com",StringComparison.Ordinal)))return Missing(c,"serverAddressRefused","Nous 门户地址无效。");main=await Send(uri.GetLeftPart(UriPartial.Authority)+"/api/oauth/account");break;}
            case "alibabaCodingPlan":main=await Send("https://bailian.console.aliyun.com/data/api.json","POST",new{});break;
            case "openAIPlatform":break;
            default:return Missing(c,"unsupportedProvider","尚未接入此服务读取流程。");
        }
        if(main is null)return Missing(c,"unsupportedProvider","尚未接入此服务读取流程。");
        if(main.Status is <200 or >=300)return ProviderParsing.Failed(c,main);
        var result=Parse(c,main.Body,extras);
        result.Diagnostics.Add(main.Diagnostic);
        return result;
    }
}
