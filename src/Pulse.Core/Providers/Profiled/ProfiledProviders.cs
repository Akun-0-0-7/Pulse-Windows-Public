// Translated from qunqin24/Pulse b035a7a. Copyright (c) 2026 qunqin24. Apache-2.0.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;

public static partial class ProfiledProviders
{
    public static readonly IReadOnlyList<string> Ids = Array.AsReadOnly(new[]{"clinePass","alibabaCodingPlan","alibabaTokenPlan","qwenCloud","factory","gemini","kiloCode","augment","jetBrainsAI","t3Chat","synthetic","elevenLabs","warp","windsurf","bifrost","chutes","longCat","zoomMate","notionAI","ibmBob","nousPortal","raycastAI","gitKraken","xKiro","abacus","moonshot","hyper","atlasCloud","poe","venice","openAIPlatform","amp","zed","sakana","mistral","codebuff","llmProxy","liteLLM","aixy","neuralwatt","clawRouter","zenMux","v0","devPass","perplexity","manus","huggingFace","deepInfra","xaiAPI","replicate","typeSafe","vercelAIGateway"});
    public static IUsageProvider? TryCreate(string id) => Ids.Contains(id) ? new Profile(id) : null;
    sealed class Profile(string id) : IUsageProvider { public Task<UsageReading> FetchAsync(FetchContext c) => Fetch(id,c); }
    public static UsageReading Parse(FetchContext context,string body,IReadOnlyDictionary<string,string>? supplements=null,DateTimeOffset? observedAt=null)
    {
        var p=new Parsed(context,observedAt??DateTimeOffset.UtcNow); var r=J.Read(body);
        J Extra(string key)=> supplements is not null && supplements.TryGetValue(key,out var text)?J.Read(text):default;
        try {
            switch(context.Account.ProviderId) {
                case "elevenLabs": p.Ratio("elevenlabs.characters",r.N("character_count"),r.N("character_limit"),Month,"credits",r.D("next_character_count_reset_unix"),false);p.Value.Plan=Title(r.S("tier"));break;
                case "atlasCloud": if(r.S("object")=="balance"&&r.S("scope")=="account"&&r.S("available.currency") is {} currency&&Regex.IsMatch(currency,"^[A-Za-z]{3}$"))p.Money(r.N("available.value"),currency.ToUpperInvariant());break;
                case "xaiAPI": if(r.S("total.val") is {} raw&&Regex.IsMatch(raw,@"^-?\d+(\.\d+)?$"))p.Money(-r.N("total.val")/100,"USD");break;
                case "vercelAIGateway": if(r["balance"].IsString)p.Money(r.N("balance"),"USD");break;
                case "replicate": if(r["unused_credit"].IsString)p.Money(r.N("unused_credit"),"USD");break;
                case "hyper": if(r.N("balance") is >=0)p.Value.CreditBalance=r.N("balance")!.Value.ToString("N2",CultureInfo.InvariantCulture)+" HC";break;
                case "poe": if(r.N("current_point_balance") is {} points)p.Value.CreditBalance=points.ToString("N0",CultureInfo.InvariantCulture)+" points";break;
                case "moonshot": if(r.N("code")==0&&r.B("status")==true)p.Money(r.N("data.available_balance"),context.Account.Extra.GetValueOrDefault("region")=="china"?"CNY":"USD");else p.Fail("serverError");break;
                case "openAIPlatform": if(r.N("total_available") is >=0)p.Money(r.N("total_available"),"USD");break;
                case "deepInfra": if(r.N("stripe_balance") is {} ledger&&r.N("recent") is {} recent){var unbilled=Math.Max(0,recent);p.Money(-(ledger+unbilled),"USD");p.Ratio("deepinfra.spend",unbilled,r.N("limit"),Month,"spend",null,false);}break;
                case "venice": Venice(p,r);break;
                case "clawRouter": if(r.B("budget.configured")==true)p.Ratio("clawrouter.monthly",r.N("budget.spentMicros"),r.N("budget.limitMicros"),Month,"monthly",null,false);break;
                case "clinePass": if(r.B("success")==true)foreach(var x in r.A("data.limits")){var type=x.S("type");var sec=type switch{"five_hour"=>FiveHour,"weekly"=>Week,"monthly"=>Month,_=>0};if(sec>0)p.Percent("clinepass."+type,x.N("percentUsed"),sec,Kind(sec),x.D("resetsAt"),sec!=Month);}break;
                case "synthetic": Synthetic(p,r);break;
                case "huggingFace": p.Remaining("huggingface.zeroGPU",r.N("current"),r.N("base"),Day,"daily",r.D("resetsAt"),false,"ZeroGPU");break;
                case "raycastAI": p.Remaining("raycast.ai_credits",r.N("remaining_balance_credits"),r.N("total_balance_credits"),Month,"credits",r.D("next_credits_at"),false);p.Value.Plan=r.S("funding_subscription.tier") switch{"pro"=>"Pro","pro_plus"=>"Pro+","max"=>"Max",var x=>x};break;
                case "gitKraken": p.Ratio("gitkraken.personal",r.N("data.used"),r.N("data.limit"),Week,"credits",r.D("data.resetsOn"),false);p.Ratio("gitkraken.organization",r.N("data.organization.used"),r.N("data.organization.limit"),Week,"sharedCredits",r.D("data.resetsOn"),false);break;
                case "augment": p.Ratio("augment.credits",r.N("usageUnitsConsumedThisBillingCycle"),r.N("usageUnitsAvailable"),Month,"credits",Extra("subscription").D("billingPeriodEnd"),false);p.Value.Plan=Extra("subscription").S("planName");break;
                case "abacus": if(r.B("success")==true){p.Remaining("abacus.credits",r.N("result.computePointsLeft"),r.N("result.totalComputePoints"),Month,"credits",Extra("billing").D("result.nextBillingDate"),false,allowNegative:true);p.Value.Plan=Extra("billing").S("result.currentTier");}else if(r.B("success")==false)p.Fail(Regex.IsMatch(r.S("error")??"","expired|session|login|authenticate|unauthorized|forbidden",RegexOptions.IgnoreCase)?"sessionExpired":"serverError");break;
                case "codebuff": Codebuff(p,r,Extra("subscription"));break;
                case "kiloCode": Kilo(p,r);break;
                case "zed": p.Value.Plan=Title(r.S("plan"));p.Ratio("zed.editPredictions",r.N("current_usage.edit_predictions.used"),r.N("current_usage.edit_predictions.limit")??r.N("current_usage.edit_predictions.limit.limited"),Month,"monthly",null,false,"Edit Predictions");p.Ratio("zed.tokenSpend",r.N("current_usage.token_spend.spend_in_cents"),r.N("current_usage.token_spend.limit_in_cents"),Month,"spend",null,false);break;
                case "v0": V0(p,r,Extra("rateLimit"));break;
                case "zenMux": ZenMux(p,r,Extra("balance"));break;
                case "devPass": DevPass(p,r);break;
                case "manus": Manus(p,r);break;
                case "perplexity": Perplexity(p,r);break;
                case "neuralwatt": Neuralwatt(p,r);break;
                case "xKiro": XKiro(p,r);break;
                case "aixy": Aixy(p,r);break;
                case "bifrost": Bifrost(p,r);break;
                case "chutes": Chutes(p,r);break;
                case "llmProxy": LlmProxy(p,r);break;
                case "liteLLM": LiteLlm(p,r,Extra("user"),Extra("team"));break;
                case "factory": Factory(p,r,Extra("me"),Extra("allowance"));break;
                case "gemini": Gemini(p,r,Extra("assist"));break;
                case "ibmBob": IbmBob(p,r,Extra("teams"));break;
                case "nousPortal": Nous(p,r);break;
                case "alibabaCodingPlan": if(AlibabaRefusal(r) is {} reason)p.Fail(reason);else AlibabaCoding(p,r);break;
                case "alibabaTokenPlan": AlibabaToken(p,r);break;
                case "qwenCloud": Qwen(p,r,Extra("subscription"));break;
                case "longCat": LongCat(p,r,Extra("usage"),Extra("fuel"));break;
                case "notionAI": Notion(p,r);break;
                case "zoomMate": ZoomMate(p,r);break;
                case "amp": Amp(p,r);break;
                case "t3Chat": T3(p,body);break;
                case "jetBrainsAI": JetBrains(p,body);break;
                case "mistral": Mistral(p,r,Extra("vibe"),supplements?.GetValueOrDefault("subscriptionPage"));break;
                case "sakana": Sakana(p,body,supplements?.GetValueOrDefault("payAsYouGo"));break;
                case "typeSafe": TypeSafe(p,body);break;
                case "warp": Warp(p,r);break;
                case "windsurf": Windsurf(p,body);break;
                default:p.Fail("unsupportedProvider");break;
            }
        }catch(Exception e) when(e is JsonException or FormatException or OverflowException or ArgumentException or InvalidOperationException or System.Xml.XmlException){p.Fail("unreadableReply");}
        return p.Finish(r.IsValid||context.Account.ProviderId is "sakana" or "t3Chat" or "windsurf" or "jetBrainsAI");
    }
    const int Day=86400,Week=604800,Month=2592000,FiveHour=18000;
    static string Kind(int seconds)=>seconds switch{FiveHour=>"fiveHour",Day=>"daily",Week=>"weekly",Month=>"monthly",_=>"other"};
    static string? Title(string? value)=>string.IsNullOrWhiteSpace(value)?null:CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().Replace('_',' '));
    static double? Amount(string? value)=>double.TryParse(value?.Replace("$","").Replace(",",""),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)?number:null;
    readonly struct J(JsonElement value)
    {
        public bool IsValid=>value.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        public bool IsObject=>value.ValueKind==JsonValueKind.Object;
        public bool IsArray=>value.ValueKind==JsonValueKind.Array;
        public bool IsString=>value.ValueKind==JsonValueKind.String;
        public string Raw=>value.ValueKind==JsonValueKind.Undefined?"":value.GetRawText();
        public J this[string path]=>new(JsonTools.At(value,path));
        public double? N(string path)=>JsonTools.Number(value,path);
        public string? S(string path)=>JsonTools.String(value,path)?.Trim() is {Length:>0} text?text:null;
        public bool? B(string path)=>JsonTools.Bool(value,path);
        public DateTimeOffset? D(string path)=>JsonTools.Date(value,path);
        public IEnumerable<J> A(string path)=>JsonTools.Array(value,path).Select(x=>new J(x));
        public IEnumerable<(string Name,J Value)> Properties()=>IsObject?value.EnumerateObject().Select(x=>(x.Name,new J(x.Value))):[];
        public static J Read(string text){try{using var d=JsonDocument.Parse(text);return new(d.RootElement.Clone());}catch(JsonException){return default;}}
    }
    sealed class Parsed(FetchContext c,DateTimeOffset now)
    {
        public readonly DateTimeOffset Now=now;
        public readonly UsageReading Value=new(){AccountId=c.Account.Id,ProviderId=c.Account.ProviderId,ObservedAt=now,Source="endpoint"};
        public void Fail(string code){Value.ErrorCode=code;Value.ErrorMessage=code switch{"noLimitsReported"=>"服务商未报告可显示的额度。","unreadableReply"=>"服务商响应格式无法识别。","sessionExpired"=>"网页登录已过期，请重新导入。",_=>code};}
        public void Money(double? amount,string currency){if(amount is {} a&&double.IsFinite(a)){Value.Balance=new((decimal)a,currency);Value.CreditBalance=a.ToString("0.00",CultureInfo.InvariantCulture)+" "+currency;}}
        public void Ratio(string id,double? used,double? limit,int seconds,string kind,DateTimeOffset? reset=null,bool reported=true,string? scope=null){if(used is >=0&&limit is >0)Percent(id,used/limit*100,seconds,kind,reset,reported,scope);}
        public void Remaining(string id,double? remaining,double? limit,int seconds,string kind,DateTimeOffset? reset=null,bool reported=true,string? scope=null,bool allowNegative=false){if(remaining is {} left&&(left>=0||allowNegative)&&limit is >0)Percent(id,Math.Max(0,limit.Value-left)/limit*100,seconds,kind,reset,reported,scope);}
        public void Percent(string id,double? percent,int seconds,string kind,DateTimeOffset? reset=null,bool reported=true,string? scope=null,bool? exhausted=null){if(percent is {} n&&double.IsFinite(n)&&n>=0)Value.Windows.Add(new(){Id=id,Kind=kind,Scope=scope,UsedPercent=n,Exhausted=exhausted??n>=100,ResetsAt=reset,WindowSeconds=seconds,ReportsLength=reported});}
        public UsageReading Finish(bool valid=true){Value.Windows=Value.Windows.OrderBy(x=>x.WindowSeconds).ToList();if(!Value.Success&&Value.ErrorCode is null)Fail(valid?"noLimitsReported":"unreadableReply");return Value;}
    }
}

