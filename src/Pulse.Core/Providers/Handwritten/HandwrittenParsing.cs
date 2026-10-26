// Translated from Pulse b035a7acf1aad9c1b6f9f895197ad1a5f58ea64e.
// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace Pulse.Core;

public static partial class HandwrittenProviders
{
    static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase) { "claudeCode","codex","kiro","antigravity","cursor","openCodeGo","kimiCode","ollamaCloud","zai","glmCoding","minimax","copilot","grok","grokBot","volcengine","commandCode","deepSeek","devin","xiaomiMiMo","sub2api","newAPI","v2ex","qoder","stepFun" };
    public static IUsageProvider? TryCreate(string providerId) => Ids.Contains(providerId) ? new HandwrittenProvider(providerId) : null;
    public static UsageReading Parse(string providerId,string body,params string[] supplements) {
        var account=new AccountConfig { Id=providerId,ProviderId=providerId };
        return Parse(account,body,supplements);
    }
    internal static UsageReading Parse(AccountConfig account,string body,params string[] supplements) {
        var result=new UsageReading { AccountId=account.Id,ProviderId=account.ProviderId,ObservedAt=DateTimeOffset.UtcNow,Source="endpoint" };
        try {
            if(account.ProviderId.Equals("ollamaCloud",StringComparison.OrdinalIgnoreCase)) Ollama(result,body);
            else {
                using var document=JsonDocument.Parse(body); var node=document.RootElement;
                var extra=supplements.Select(ParseJson).ToArray();
                switch(account.ProviderId.ToLowerInvariant()) {
                    case "codex": Codex(result,node);break;
                    case "claudecode": Claude(result,node);break;
                    case "deepseek": DeepSeek(result,node,account);break;
                    case "cursor": Cursor(result,node,extra);break;
                    case "copilot": Copilot(result,node);break;
                    case "kimicode": Kimi(result,node);break;
                    case "minimax": MiniMax(result,node);break;
                    case "opencodego": OpenCode(result,node);break;
                    case "zai": case "glmcoding": Zai(result,node);break;
                    case "grok": Grok(result,node,extra);break;
                    case "grokbot": GrokBot(result,node);break;
                    case "antigravity": Antigravity(result,node);break;
                    case "kiro": Kiro(result,node);break;
                    case "sub2api": Sub2Api(result,node);break;
                    case "newapi": NewApi(result,node,extra);break;
                    case "v2ex": V2ex(result,node);break;
                    case "volcengine": Volcengine(result,node,extra);break;
                    case "devin": Devin(result,node,account.Route);break;
                    case "xiaomimimo": Xiaomi(result,node,extra);break;
                    case "qoder": Qoder(result,node,account.Detailed);break;
                    case "stepfun": StepFun(result,node,extra);break;
                    case "commandcode": CommandCode(result,node,extra);break;
                }
            }
        } catch(JsonException) { Failure(result,"unreadableReply","服务返回的内容无法解析。"); }
          catch(System.Xml.XmlException) { Failure(result,"unreadableReply","用量页面结构无法解析。"); }
        if(!result.Success && result.ErrorCode is null) Failure(result,"noLimitsReported","服务没有返回可显示的额度或余额。");
        return result;
    }
    static JsonElement ParseJson(string body) {try {using var d=JsonDocument.Parse(body);return d.RootElement.Clone();}catch(JsonException){return default;}}
    static JsonElement A(JsonElement node,string path)=>JsonTools.At(node,path);
    static string? S(JsonElement node,string path)=>JsonTools.String(node,path);
    static double? N(JsonElement node,string path)=>JsonTools.Number(node,path);
    static decimal? M(JsonElement node,string path)=>JsonTools.Decimal(node,path);
    static bool? B(JsonElement node,string path)=>JsonTools.Bool(node,path);
    static DateTimeOffset? D(JsonElement node,string path)=>JsonTools.Date(node,path);
    static IEnumerable<JsonElement> Items(JsonElement node,string path)=>JsonTools.Array(node,path);
    static bool Present(JsonElement node)=>node.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
    static string? First(JsonElement node,params string[] paths)=>paths.Select(p=>S(node,p)).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    static void Failure(UsageReading result,string code,string message){result.ErrorCode=code;result.ErrorMessage=message;}
    static string Kind(double? seconds)=>seconds switch {18000=>"fiveHour",86400=>"daily",604800=>"weekly",2592000=>"monthly",_=>"other"};
    static UsageWindow Add(UsageReading r,string id,double percent,DateTimeOffset? reset=null,double? seconds=null,string? scope=null,bool exhausted=false,string? kind=null) {
        var w=new UsageWindow {Id=id,Kind=kind??Kind(seconds),UsedPercent=Math.Max(percent,0),ResetsAt=reset,WindowSeconds=seconds,ReportsLength=seconds>0,Scope=scope,Exhausted=exhausted};r.Windows.Add(w);return w;
    }
    static double? Ratio(double? used,double? limit)=>used is {} u && limit is >0 and {} l && double.IsFinite(u/l)?u/l*100:null;
    static double? Duration(double? count,double multiplier) => count is >0 and {} n && n<=long.MaxValue/multiplier?n*multiplier:null;
    static double? NamedDuration(string? name)=>name?.ToLowerInvariant() switch {"5h" or "fivehour" or "five_hour"=>18000,"daily" or "1d"=>86400,"weekly" or "7d"=>604800,"monthly"=>2592000,_=>null};
    static string Tidy(string? name)=>CultureInfo.InvariantCulture.TextInfo.ToTitleCase((name??"").Replace('_',' ').Replace('-',' ').ToLowerInvariant());
    static void Balance(UsageReading r,decimal value,string currency) {r.Balance=new(value,currency);r.CreditBalance=$"{value:0.##} {currency}";}

    static void Codex(UsageReading r,JsonElement root) {
        var rpc=Present(A(root,"rateLimitsByLimitId"))||Present(A(root,"rateLimits"));
        void Group(JsonElement group,string id,string? scope) {
            var current=new List<UsageWindow>();
            foreach(var slot in rpc?new[]{"primary","secondary"}:new[]{"primary_window","secondary_window"}) {
                var node=A(group,slot);var percent=N(node,rpc?"usedPercent":"used_percent");if(percent is null)continue;
                var seconds=rpc?Duration(N(node,"windowDurationMins"),60):N(node,"limit_window_seconds");
                current.Add(Add(r,id+"."+slot,percent.Value,D(node,rpc?"resetsAt":"reset_at"),seconds,scope));
            }
            var refused=rpc?(B(root,"ordinaryUsageAllowed")==false||B(group,"limitReached")==true):(B(group,"limit_reached")==true||B(group,"allowed")==false);
            if(!rpc && id=="codex")refused|=B(root,"spend_control.reached")==true||Present(A(root,"rate_limit_reached_type"));
            if(refused && current.Count>0) {var max=current.Max(w=>w.UsedPercent);foreach(var w in current.Where(w=>w.UsedPercent==max))w.Exhausted=true;}
            r.Plan??=S(group,"planType");var credits=A(group,"credits");if(B(credits,"unlimited")!=true)r.CreditBalance??=N(credits,"balance")?.ToString("0.##",CultureInfo.InvariantCulture)??S(credits,"balance");
        }
        if(rpc) {
            var groups=A(root,"rateLimitsByLimitId");if(groups.ValueKind==JsonValueKind.Object)foreach(var group in groups.EnumerateObject().OrderBy(p=>S(p.Value,"limitName")!=null).ThenBy(p=>p.Name))Group(group.Value,group.Name,S(group.Value,"limitName"));
            else Group(A(root,"rateLimits"),"codex",null);r.Source="app-server";
        }else {
            Group(A(root,"rate_limit"),"codex",null);
            foreach(var group in Items(root,"additional_rate_limits"))Group(A(group,"rate_limit"),First(group,"metered_feature","limit_name")??"extra",S(group,"limit_name"));
            r.Plan=S(root,"plan_type");if(B(root,"credits.unlimited")!=true)r.CreditBalance=N(root,"credits.balance")?.ToString("0.##",CultureInfo.InvariantCulture)??S(root,"credits.balance");
        }
        if(r.Plan is {} name)r.Plan=name.ToLowerInvariant()=="prolite"?"Pro 5x":Tidy(name);
    }
    static bool ClaudeSpent(JsonElement n) => Present(A(n,"locked_reason")) || S(n,"severity") is {} s && !new[]{"normal","ok","none","healthy","warning","warn"}.Contains(s.ToLowerInvariant());
    static void Claude(UsageReading r,JsonElement root) {
        foreach(var node in Items(root,"limits")) {
            var kind=S(node,"kind");var seconds=kind switch {"session"=>18000,"weekly_all" or "weekly_scoped"=>604800,_=>0};
            if(seconds==0||N(node,"percent") is not {} percent)continue;var scope=S(node,"scope.model.display_name");
            Add(r,"claudeCode."+kind+"."+(scope??"all"),percent,D(node,"resets_at"),seconds,scope,ClaudeSpent(node));
        }
        var captured=Present(A(root,"rate_limits"));
        if(r.Windows.Count==0)foreach(var (key,seconds) in new[]{("five_hour",18000d),("seven_day",604800d),("spend_limit",0d)}) {
            var node=A(root,(captured?"rate_limits.":"")+key);var p=N(node,captured?"used_percentage":"utilization");if(p is null)continue;
            var reset=D(node,"resets_at");if(captured&&reset<=DateTimeOffset.UtcNow)continue;
            Add(r,"claudeCode."+key,p.Value,reset,seconds>0?seconds:null,exhausted:!captured&&ClaudeSpent(node),kind:key=="spend_limit"?"spend":null);
        }
        if(captured){r.Source="status-line";r.ObservedAt=D(root,"capturedAt");r.IsStale=r.ObservedAt is null||DateTimeOffset.UtcNow-r.ObservedAt>TimeSpan.FromMinutes(10);}
    }
    static void DeepSeek(UsageReading r,JsonElement root,AccountConfig a) {
        var purses=Items(root,"balance_infos").Where(n=>M(n,"total_balance")!=null&&S(n,"currency")!=null).ToArray();
        foreach(var p in purses)r.Metrics.Add(new("balance."+S(p,"currency"),"余额",(double)M(p,"total_balance")!,S(p,"currency")));
        var chosen=purses.FirstOrDefault(n=>S(n,"currency")==a.Extra.GetValueOrDefault("currency"));
        if(!Present(chosen))chosen=purses.FirstOrDefault(n=>M(n,"total_balance")>0);if(!Present(chosen))chosen=purses.FirstOrDefault();
        if(!Present(chosen))return;Balance(r,M(chosen,"total_balance")!.Value,S(chosen,"currency")!);
        if(a.BalanceMode=="budget"&&a.Budget is >0){var w=Add(r,"balance",(double)((1-r.Balance!.Amount/a.Budget.Value)*100),exhausted:B(root,"is_available")==false,kind:"spend");w.Estimated=true;w.EstimatedFrom="userBudget";}
    }
    static void Cursor(UsageReading r,JsonElement root,JsonElement[] extra) {
        r.Plan=Tidy(S(root,"membershipType")).Replace(" Plus","+");
        var plan=A(root,"individualUsage.plan");if(!Present(plan))plan=A(root,"teamUsage.pooled");var reset=D(root,"billingCycleEnd");
        foreach(var (key,id,label) in new[]{("autoPercentUsed","cursorModels","Cursor Models"),("apiPercentUsed","otherModels","Other Models")})if(N(plan,key) is {} p)Add(r,id,p,reset,scope:label,exhausted:p>=100,kind:"monthly");
        if(r.Windows.Count==0 && B(plan,"enabled")!=false && Ratio(N(plan,"used"),N(plan,"limit")) is {} percent)Add(r,"plan",percent,reset,exhausted:N(plan,"used")>=N(plan,"limit"),kind:"monthly");
        if(M(plan,"remaining") is {} remaining)Balance(r,remaining/100,"USD");
        var demand=A(root,"individualUsage.onDemand");if(!Present(demand))demand=A(root,"teamUsage.onDemand");
        if(B(demand,"enabled")!=false&&Ratio(N(demand,"used"),N(demand,"limit")) is {} dp)Add(r,"onDemand",dp,reset,exhausted:N(demand,"used")>=N(demand,"limit"),kind:"spend");
        var info=extra.FirstOrDefault(n=>Present(A(n,"planInfo")));var period=S(info,"planInfo.includedUsagePeriod");
        if(period is "2" or "WEEKLY" or "INCLUDED_USAGE_PERIOD_WEEKLY") {
            r.Windows.RemoveAll(w=>w.Kind!="spend");var usage=extra.FirstOrDefault(n=>Present(A(n,"planUsage")));
            if(N(usage,"planUsage.totalPercentUsed") is {} wp)r.Windows.Insert(0,new(){Id="weekly",Kind="weekly",UsedPercent=Math.Clamp(wp,0,100),WindowSeconds=604800,ReportsLength=true,ResetsAt=D(info,"planInfo.includedUsageResetsAt"),Exhausted=wp>=100});
            else Failure(r,"noLimitsReported","每周套餐没有返回用量。");
        }
    }
    static void Copilot(UsageReading r,JsonElement root) {
        r.Plan=Tidy(S(root,"copilot_plan"));var reset=D(root,"quota_reset_date_utc")??D(root,"quota_reset_date");
        foreach(var (key,label) in new[]{("premium_interactions","Premium requests"),("chat","Chat"),("completions","Completions")}) {
            var n=A(root,"quota_snapshots."+key);if(B(n,"has_quota")==false||B(n,"unlimited")==true)continue;
            if(B(n,"has_quota")==null && (N(n,"entitlement")??0)<=0&&(N(n,"remaining")??0)<=0&&(N(n,"percent_remaining")??0)>=100)continue;
            if(N(n,"percent_remaining") is {} p)Add(r,"copilot."+key,100-p,reset,scope:label,exhausted:p<=0&&B(n,"overage_permitted")!=true,kind:"monthly");
        }
    }
    static void Kimi(UsageReading r,JsonElement root) {
        r.Plan=Tidy(S(root,"user.membership.level")?.Replace("LEVEL_",""));var index=0;
        void Detail(JsonElement d,string id,double? seconds,bool stated) {var limit=N(d,"limit");var used=N(d,"used")??(N(d,"remaining") is {} rem&&limit is {} lim?lim-rem:null);if(Ratio(used,limit) is {} p){var w=Add(r,id,p,D(d,"resetTime"),seconds,exhausted:used>=limit);w.ReportsLength=stated;if(id=="weekly")w.Kind="weekly";}}
        foreach(var n in Items(root,"limits")) {var unit=S(n,"window.timeUnit") switch {"TIME_UNIT_SECOND"=>1,"TIME_UNIT_MINUTE"=>60,"TIME_UNIT_HOUR"=>3600,"TIME_UNIT_DAY"=>86400,_=>0};var seconds=unit>0?Duration(N(n,"window.duration"),unit):null;if(seconds!=null)Detail(A(n,"detail"),"limit."+index+"."+seconds,seconds,true);index++;}
        Detail(A(root,"usage"),"weekly",604800,false);r.Windows=r.Windows.OrderBy(w=>w.WindowSeconds).ToList();
    }
    static void MiniMax(UsageReading r,JsonElement root) {
        if(N(root,"base_resp.status_code") is {} code&&code!=0){Failure(r,code==1004?"apiKeyRefused":"serverError","服务拒绝了读取请求。");return;}
        var data=Present(A(root,"data"))?A(root,"data"):root;r.Plan=First(data,"current_subscribe_title","plan_name","combo_title","current_plan_title");r.CreditBalance=First(data,"points_balance","point_balance","credits_balance","credit_balance","balance");var i=0;
        foreach(var n in Items(data,"model_remains")) {
            var name=S(n,"model_name")??"lane"+i++;var scope=name=="general"?null:name;
            foreach(var weekly in new[]{false,true}) {
                var prefix=weekly?"current_weekly_":"current_interval_";var remaining=N(n,prefix+"remaining_percent");var status=N(n,prefix+"status");
                if(status==3&&(N(n,prefix+"total_count")??0)==0&&remaining>=100)continue;
                var spent=remaining is {} rem?100-rem:N(n,prefix+"total_count") is >0 and {} total&&N(n,prefix+"usage_count") is {} left?(total-left)/total*100:(double?)null;
                if(spent==null)continue;
                var end=D(n,weekly?"weekly_end_time":"end_time");var start=D(n,"start_time");var seconds=weekly?604800d:end is {} e&&start is {} s?(e-s).TotalSeconds:0;
                if(seconds<=0)continue;Add(r,name+(weekly?".weekly":".interval"),spent.Value,end,seconds,scope);
            }
        }r.Windows=r.Windows.OrderBy(w=>w.WindowSeconds).ToList();
    }
    static void OpenCode(UsageReading r,JsonElement root) {
        foreach(var (key,seconds) in new[]{("rolling",18000d),("weekly",604800d),("monthly",2592000d)}) {
            var n=A(root,"usage."+key);if(N(n,"percent") is {} p){var w=Add(r,key,p,D(n,"resetsAt"),seconds,exhausted:!string.Equals(S(n,"status")??"ok","ok",StringComparison.OrdinalIgnoreCase));if(key=="monthly")w.ReportsLength=false;}
        }
        if(r.Windows.Count==0)foreach(var (key,id,kind) in new[]{("fiveHour","rolling","fiveHour"),("week","weekly","weekly"),("month","monthly","monthly")}) {
            var n=A(root,"access.meters."+key);var p=Ratio(N(n,"usedMicroCents"),N(n,"limitMicroCents"));if(p==null)continue;var end=D(n,"resetsAt");var start=D(n,"startsAt");if(key=="month"&&S(root,"access.endsAt")==S(n,"resetsAt"))start=D(root,"access.startsAt");var length=start!=null&&end>start?(end.Value-start.Value).TotalSeconds:(double?)null;Add(r,id,p.Value,end,length,exhausted:N(n,"usedMicroCents")>=N(n,"limitMicroCents"),kind:kind);r.Source="web-session";r.Plan=S(root,"product")=="go"?"Go":null;
        }
    }
    static void Zai(UsageReading r,JsonElement root) {
        if(B(root,"success")==false || N(root,"code") is {} code&&code!=200) {
            var message=S(root,"msg")??"";var auth=new[]{"token","auth","key","身份验证","鉴权","认证","令牌","密钥"}.Any(message.ToLowerInvariant().Contains);
            Failure(r,message.Contains("coding plan",StringComparison.OrdinalIgnoreCase)?"noPlan":auth||N(root,"code") is >=1000 and <=1099 or 401 or 403?"apiKeyRefused":N(root,"code")==429?"rateLimited":"serverError","服务拒绝了读取请求。");return;
        }
        var data=A(root,"data");r.Plan=First(data,"planName","plan","plan_type","packageName","level");var i=0;
        foreach(var n in Items(data,"limits")) {
            var type=S(n,"type");var unit=N(n,"unit");if(type is not ("TOKENS_LIMIT" or "CREDIT_LIMIT" or "TIME_LIMIT"))continue;
            var multiplier=unit switch {1=>86400,3=>3600,5=>60,6=>604800,_=>0};var seconds=Duration(N(n,"number"),multiplier);if(multiplier==0||seconds==null)continue;
            if(type=="TIME_LIMIT"&&unit==5&&N(n,"number")==1)seconds=2592000;
            var limit=N(n,"usage");var used=N(n,"currentValue");if(N(n,"remaining") is {} left&&limit is {} total)used=Math.Max(total-left,used??total-left);
            var p=Ratio(used,limit)??N(n,"percentage");if(p==null)continue;Add(r,r.ProviderId+"."+type+"."+i++,p.Value,D(n,"nextResetTime"),seconds,type=="TIME_LIMIT"?"MCP":null);
        }r.Windows=r.Windows.OrderBy(w=>w.WindowSeconds).ToList();
    }
    static void Grok(UsageReading r,JsonElement root,JsonElement[] extra) {
        var n=A(root,"config");var start=D(n,"currentPeriod.start")??D(n,"billingPeriodStart");var end=D(n,"currentPeriod.end")??D(n,"billingPeriodEnd");
        if(start is null||end is null||end<=start)return;var percent=N(n,"creditUsagePercent")??(start<=DateTimeOffset.UtcNow&&end>=DateTimeOffset.UtcNow?0:null);
        if(percent is {} p){var seconds=(end.Value-start.Value).TotalSeconds;var w=Add(r,"grok-pool",p,end,seconds,exhausted:p>=100);w.Kind=seconds is >=518400 and <=691200?"weekly":seconds is >=2332800 and <=2764800?"monthly":Kind(seconds);}
        r.Plan=extra.Select(n=>S(n,"subscription_tier_display")).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    }
    static void GrokBot(UsageReading r,JsonElement root) {
        if(B(root,"usesPooledEnterpriseAllowance")==true||B(root,"includedLimitZero")==true||B(root,"hasNonZeroIncludedLimit")!=true){if(Present(A(root,"hasNonZeroIncludedLimit")))Failure(r,"noPlan","当前账号没有包含 Grok Bot 额度。");return;}
        if(N(root,"usagePercent") is {} p){var w=Add(r,"grokBot",p,D(root,"nextResetTimestampUtc"),scope:null,exhausted:p>=100,kind:"weekly");w.ReportsLength=false;}r.Plan=S(root,"grokPlanLabel");
    }
    static void Antigravity(UsageReading r,JsonElement root) {
        var cli=Present(A(root,"command.data"));var path=cli?"command.data.groups":"response.groups";
        foreach(var group in Items(root,path))foreach(var n in Items(group,"buckets")) {
            var seconds=NamedDuration(S(n,"window"));var left=N(n,cli?"remaining_fraction":"remainingFraction");if(seconds==null||left==null)continue;
            Add(r,First(n,"bucketId","id")??"bucket."+r.Windows.Count,(1-left.Value)*100,D(n,cli?"reset_time":"resetTime"),seconds,First(group,"displayName","name"),left<=0);
        }if(cli)r.Source="cli";
    }
    static void Kiro(UsageReading r,JsonElement root) {
        var data=Present(A(root,"data"))?A(root,"data"):root;r.Plan=S(data,"planName");var i=0;
        foreach(var n in Items(data,"usageBreakdowns")) {
            if(B(n,"hasLimit")==false||N(n,"limit") is not >0)continue;var p=Ratio(N(n,"used"),N(n,"limit"))??N(n,"percentage");if(p is {} value)Add(r,"kiro."+(S(n,"resourceType")??i++.ToString()),value,D(data,"billingCycleReset"),scope:S(n,"displayName"),kind:"monthly");
        }
    }
}
