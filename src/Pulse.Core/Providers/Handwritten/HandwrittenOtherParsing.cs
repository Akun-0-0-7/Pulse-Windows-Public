// Copyright (c) 2026 qunqin24. Licensed under the Apache License, Version 2.0.
// Windows translation of Pulse b035a7a handwritten provider response mappings.
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace Pulse.Core;
public static partial class HandwrittenProviders
{
    static void Sub2Api(UsageReading r,JsonElement root) {
        if(B(root,"isValid")==false){Failure(r,"apiKeyRefused","服务拒绝了当前凭据。");return;}r.Plan=S(root,"planName");
        foreach(var n in Items(root,"rate_limits")) {
            var name=S(n,"window");var match=Regex.Match(name??"","^([0-9]+)([hd])$");var seconds=match.Success?Duration(double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture),match.Groups[2].Value=="h"?3600:86400):null;
            if(seconds is {} len&&Ratio(N(n,"used"),N(n,"limit")) is {} p)Add(r,"rate."+name,p,D(n,"reset_at"),len,exhausted:N(n,"remaining")<=0);
        }
        var quota=A(root,"quota");if(Ratio(N(quota,"used"),N(quota,"limit")) is {} qp)Add(r,"quota",qp,exhausted:N(quota,"remaining")<=0,kind:"spend");
        foreach(var key in new[]{"daily","weekly","monthly"})if(Ratio(N(root,"subscription."+key+"_usage_usd"),N(root,"subscription."+key+"_limit_usd")) is {} p)Add(r,"subscription."+key,p,kind:key);
        var unit=(First(root,"unit","quota.unit")??"USD").ToUpperInvariant();var amount=M(root,"balance")??(!Present(quota)&&!Present(A(root,"subscription"))?M(root,"remaining"):null);
        if(amount!=null&&Regex.IsMatch(unit,"^[A-Z]{3}$"))Balance(r,amount.Value,unit);
    }
    static void NewApi(UsageReading r,JsonElement subscription,JsonElement[] extra) {
        var limit=M(subscription,"hard_limit_usd");if(limit is null||limit>=100000000)return;
        var usage=extra.FirstOrDefault(n=>M(n,"total_usage")!=null);var status=extra.FirstOrDefault(n=>Present(A(n,"data")));var unit=S(status,"data.quota_display_type")?.ToUpperInvariant();
        if(unit==null && B(status,"data.display_in_currency")==true)unit="USD";
        if(unit is not ("USD" or "CNY"))return;if(M(usage,"total_usage") is {} used)Balance(r,limit.Value-used/100,unit);
        r.Plan=S(status,"data.system_name");
    }
    static void V2ex(UsageReading r,JsonElement root) {
        if(B(root,"success")==false)return;var n=A(root,"result");
        if(Ratio(N(n,"used_tokens"),N(n,"total_tokens")) is {} p){var w=Add(r,"window",p,B(n,"active")==true?D(n,"period_end"):null,18000,exhausted:N(n,"remaining_tokens")<=0);w.ReportsLength=B(n,"active")==true;}
        var extra=A(n,"extra_usage");if(N(extra,"pack_count")>0&&Ratio(N(extra,"used_tokens"),N(extra,"total_tokens")) is {} ep)Add(r,"extra",ep,exhausted:N(extra,"remaining_tokens")<=0,kind:"topUp");
    }
    static void Volcengine(UsageReading r,JsonElement root,JsonElement[] extra) {
        foreach(var item in Items(root,"items")) {
            var product=S(item,"product");if(B(item,"subscribed")!=true||product is not ("coding-plan" or "coding-plan-team" or "agent-plan" or "agent-plan-team"))continue;
            r.Plan??=Tidy(product);foreach(var n in Items(item,"periods")){var name=S(n,"label");var seconds=NamedDuration(name);if(seconds==null||N(n,"percent") is not {} p)continue;Add(r,product+"."+name,p,D(n,"reset_at"),seconds,product,p>=100);}r.Source="arkcli";
        }
        foreach(var envelope in new[]{root}.Concat(extra)) {
            var data=A(envelope,"Result");foreach(var n in Items(data,"QuotaUsage")) {var name=S(n,"Level");var seconds=NamedDuration(name);if(seconds!=null&&N(n,"Percent") is {} p)Add(r,"coding."+name,p,D(n,"ResetTimestamp"),seconds,"Coding Plan",p>=100);}
            foreach(var (key,seconds) in new[]{("AFPFiveHour",18000d),("AFPWeekly",604800d),("AFPMonthly",2592000d),("AFPDaily",86400d)}) {var n=A(data,key);if(Ratio(N(n,"Used"),N(n,"Quota")) is {} p)Add(r,"agent."+key,p,N(n,"ResetTime")>0?D(n,"ResetTime"):null,seconds,"Agent Plan",p>=100);}
        }
        if(r.Windows.Count>0){r.Plan??="Volcengine";r.Windows=r.Windows.OrderBy(w=>w.WindowSeconds).ToList();}
    }
    static void Devin(UsageReading r,JsonElement root,string route) {
        var desktop=Present(A(root,"dailyRemainingPercent"))||Present(A(root,"weeklyRemainingPercent"));r.Plan=First(root,"planName","plan_name","plan","tier");
        if(B(root,"has_quota_allocation")!=false)foreach(var (key,seconds) in new[]{("daily",86400d),("weekly",604800d)}) {
            var p=desktop?N(root,key+"RemainingPercent") is {} left?100-left:null:N(root,key+"_percentage");
            if(B(root,desktop?"hide"+Tidy(key)+"Quota":"hide_"+key+"_quota")==true||p is null)continue;
            Add(r,"devin-"+key,p.Value,D(root,desktop?key+"ResetAtUnix":key+"_reset_at"),seconds);
        }
        var balance=desktop?M(root,"overageBalanceMicros")/1000000:M(root,"overage_balance")??M(root,"overage_balance_cents")/100;
        if(balance is {} value)Balance(r,value,"USD");if(desktop){r.Source="desktop-cache";r.IsStale=true;}
    }
    static void Xiaomi(UsageReading r,JsonElement root,JsonElement[] extra) {
        foreach(var envelope in new[]{root}.Concat(extra)) {
            if(N(envelope,"code") is {} code&&code!=0)continue;var n=A(envelope,"data");
            if(M(n,"balance") is {} amount&&S(n,"currency") is {} currency)Balance(r,amount,currency);
        }
        var detail=new[]{root}.Concat(extra).FirstOrDefault(n=>Present(A(n,"data.planCode")));var usage=new[]{root}.Concat(extra).FirstOrDefault(n=>N(n,"data.monthUsage.percent")!=null);
        if(B(detail,"data.expired")!=true&&N(usage,"data.monthUsage.percent") is {} p){var resetText=S(detail,"data.currentPeriodEnd");DateTimeOffset? reset=null;if(resetText!=null&&DateTime.TryParse(resetText,CultureInfo.InvariantCulture,out var dt))reset=new DateTimeOffset(DateTime.SpecifyKind(dt,DateTimeKind.Unspecified),TimeSpan.FromHours(8));Add(r,"xiaomi.monthly",p,reset,exhausted:p>=100,kind:"monthly");r.Plan=S(detail,"data.planCode");}
    }
    static JsonElement Either(JsonElement n,string camel,string snake)=>Present(A(n,camel))?A(n,camel):A(n,snake);
    static double? EitherNumber(JsonElement n,string camel,string snake)=>N(n,camel)??N(n,snake);
    static void Qoder(UsageReading r,JsonElement root,bool parts) {
        var reset=D(root,"nextResetAt")??D(root,"next_reset_at");if(reset<=DateTimeOffset.UtcNow)reset=null;
        void Pool(JsonElement container,string id,string kind,DateTimeOffset? resets) {
            var n=Either(container,"quotaSummary","quota_summary");var used=EitherNumber(n,"usedValue","used_value");var limit=EitherNumber(n,"limitValue","limit_value");var left=EitherNumber(n,"remainingValue","remaining_value");
            if(used<0||limit<0)return;if(Ratio(used,limit) is {} p){var w=Add(r,id,p,resets,exhausted:left is {} l?l<=0:used>=limit,kind:kind);var detail=Either(container,"quotaDetail","quota_detail");if(detail.ValueKind==JsonValueKind.Array)w.ExpiresAt=detail.EnumerateArray().Where(d=>B(d,"isActive")!=false&&B(d,"is_active")!=false&&EitherNumber(d,"remainingValue","remaining_value")>0).Select(d=>D(d,"expiresAt")??D(d,"expires_at")).Where(d=>d>DateTimeOffset.UtcNow).OrderBy(d=>d).FirstOrDefault();}
        }
        Pool(Either(root,"totalQuota","total_quota"),"qoder.credits","credits",reset);
        if(parts) {var count=r.Windows.Count;Pool(Either(root,"planQuota","plan_quota"),"qoder.plan","planCredits",reset);Pool(Either(root,"resourcePackageQuota","resource_package_quota"),"qoder.pack","topUp",null);Pool(Either(root,"dedicatedResourcePackageQuota","dedicated_resource_package_quota"),"qoder.dedicated","dedicatedCredits",null);if(r.Windows.Count-count<=1)r.Windows.RemoveRange(count,r.Windows.Count-count);else if(count>0)r.Windows[0].ExpiresAt=null;}
        Pool(Either(root,"sharedQuota","shared_quota"),"qoder.shared","sharedCredits",null);
    }
    static void StepFun(UsageReading r,JsonElement root,JsonElement[] extra) {
        if(N(root,"status")!=1)return;
        foreach(var (key,seconds) in new[]{("five_hour",18000d),("weekly",604800d)})if(N(root,key+"_usage_left_rate") is {} left&&N(root,key+"_usage_reset_time")>0)Add(r,"stepfun."+key,(1-left)*100,D(root,key+"_usage_reset_time"),seconds,exhausted:left<=0);
        if(r.Windows.Count==0) {
            var credit=A(root,"plan_credit_rate_limit");var buckets=Items(credit,"credit_buckets").Where(n=>N(n,"credit_total")>0&&N(n,"credit_residual")>=0).ToArray();
            if(buckets.Length>0) {var total=buckets.Sum(n=>N(n,"credit_total")!.Value);var remaining=buckets.Sum(n=>Math.Min(N(n,"credit_residual")!.Value,N(n,"credit_total")!.Value));var w=Add(r,"stepfun.credits",(1-remaining/total)*100,exhausted:remaining<=0,kind:"credits");w.ExpiresAt=buckets.Where(n=>N(n,"expire_at")>0).Select(n=>D(n,"expire_at")).Where(n=>n>DateTimeOffset.UtcNow).OrderBy(n=>n).FirstOrDefault();r.CreditBalance=remaining.ToString("0.##",CultureInfo.InvariantCulture);}
            else if(N(credit,"subscription_credit_left_rate") is >0 and {} left)Add(r,"stepfun.subscription",(1-left)*100,kind:"planCredits");
            if(N(credit,"topup_credit_left_rate") is >0 and {} topup)Add(r,"stepfun.topup",(1-topup)*100,kind:"topUp");
        }
        r.Plan=extra.Select(n=>S(n,"subscription.name")).FirstOrDefault(n=>!string.IsNullOrWhiteSpace(n));
    }
    static void CommandCode(UsageReading r,JsonElement root,JsonElement[] extra) {
        if(B(root,"success")==false)return;var credits=A(root,"credits");var sub=extra.FirstOrDefault(n=>Present(A(n,"data.planId"))||Present(A(n,"data.status")));var who=extra.FirstOrDefault(n=>Present(A(n,"orgLimits")));var summary=extra.FirstOrDefault(n=>N(n,"totalCost")!=null);
        var planId=S(sub,"data.planId")??S(credits,"planId");r.Plan=Tidy(planId);var pots=new[]{M(credits,"monthlyCredits"),M(credits,"purchasedCredits"),M(credits,"freeCredits")};if(pots.Any(n=>n!=null))Balance(r,pots.Sum(n=>Math.Max(n??0,0)),"USD");
        if(B(root,"windowLimits.limited")==true)foreach(var (key,seconds) in new[]{("fiveHour",18000d),("weekly",604800d)}){var n=A(root,"windowLimits."+key);if(Ratio(N(n,"used"),N(n,"cap")) is {} p)Add(r,key=="fiveHour"?"five-hour":key,p,D(n,"resetAt"),seconds,exhausted:N(n,"used")>=N(n,"cap"));}
        foreach(var n in Items(who,"orgLimits"))if(Ratio(N(n,"spent"),N(n,"limit")) is {} p) {var interval=S(n,"resetInterval");var w=Add(r,"org."+(S(n,"scope")??"org")+"."+(S(n,"model")??"")+"."+interval,p,D(n,"resetAt"),interval is "daily" or "weekly"?NamedDuration(interval):null,S(n,"scope")=="model"?First(n,"modelLabel","model"):null,B(n,"exceeded")==true,"spend");w.ReportsLength=interval is "daily" or "weekly";}
        var onPlan=Present(sub)?!string.Equals(S(sub,"data.status"),"canceled",StringComparison.OrdinalIgnoreCase)&&S(sub,"data.status") is null or "active":planId!=null;
        var start=D(sub,"data.currentPeriodStart");var end=D(sub,"data.currentPeriodEnd");var periodSeconds=start!=null&&end>start?(end.Value-start.Value).TotalSeconds:(double?)null;
        if(onPlan) {
            var grant=planId?.ToLowerInvariant().Replace('_','-') switch {"individual-go"=>10d,"individual-provider"=>15d,"individual-pro"=>30d,"individual-pro-v1"=>80d,"teams-pro"=>40d,"individual-goat"=>70d,"individual-max"=>150d,"individual-ultra"=>300d,_=>0};if(grant>0&&N(credits,"monthlyCredits") is {} rem){var w=Add(r,"monthly",(grant-Math.Clamp(rem,0,grant))/grant*100,end,periodSeconds,exhausted:rem<=0,kind:"monthly");w.Estimated=true;w.EstimatedFrom="planPrice";}
        } else if(r.Balance!=null&&N(summary,"totalCost") is {} spent&&spent+(double)r.Balance.Amount>0)Add(r,"credits",spent/(spent+(double)r.Balance.Amount)*100,end,periodSeconds,exhausted:r.Balance.Amount<=0,kind:"spend");
        r.Windows=r.Windows.OrderBy(w=>w.WindowSeconds??double.MaxValue).ToList();
    }
    static void Ollama(UsageReading r,string html) {
        // Parse the labelled DOM containers. Never read CSS widths or decode entities into markup.
        if(html.Length>2*1024*1024){Failure(r,"unreadableReply","用量页面超过读取上限。");return;}
        html=Regex.Replace(html,"<!DOCTYPE[^>]*>","",RegexOptions.IgnoreCase);html=Regex.Replace(html,@"<(input|br|hr|meta|link)(\b[^>]*?)(?<!/)>","<$1$2 />",RegexOptions.IgnoreCase);
        if(html.Contains("<!ENTITY",StringComparison.OrdinalIgnoreCase)){Failure(r,"unreadableReply","用量页面包含无效的实体声明。");return;}
        var doc=XDocument.Parse(html,LoadOptions.PreserveWhitespace);foreach(var hidden in doc.Descendants().Where(n=>n.Name.LocalName is "script" or "style" or "template").ToArray())hidden.Remove();
        if(doc.Descendants("form").Any(n=>Regex.IsMatch(n.Attribute("action")?.Value??"","signin|login",RegexOptions.IgnoreCase))){Failure(r,"sessionExpired","请重新登录 Ollama。");return;}
        var found=new List<UsageWindow>();
        foreach(var (label,id) in new[]{("Session usage","session"),("Weekly usage","weekly")}) {
            var labels=doc.Descendants().Where(n=>!n.HasElements&&n.Value.Trim()==label).ToArray();if(labels.Length>1){Failure(r,"unreadableReply","用量页面包含重复的额度标签。");return;}if(labels.Length==0)continue;
            UsageWindow? w=null;
            for(var parent=labels[0].Parent;parent!=null;parent=parent.Parent) {
                if(parent.Descendants().Any(n=>!n.HasElements&&n.Value.Trim()==(id=="session"?"Weekly usage":"Session usage")))break;
                var percentages=parent.DescendantNodes().OfType<XText>().Select(t=>Regex.Match(t.Value,@"^\s*([0-9]+(?:\.[0-9]+)?)\s*%\s*used\s*$",RegexOptions.IgnoreCase)).Where(m=>m.Success).ToArray();
                if(percentages.Length>1){Failure(r,"unreadableReply","用量页面包含多个百分比。");return;}if(percentages.Length==0)continue;
                var p=double.Parse(percentages[0].Groups[1].Value,CultureInfo.InvariantCulture);if(p is <0 or >100){Failure(r,"unreadableReply","用量百分比超出范围。");return;}
                var times=parent.DescendantsAndSelf().Attributes("data-time").ToArray();if(times.Length>1||times.Length==1&&!DateTimeOffset.TryParse(times[0].Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _)){Failure(r,"unreadableReply","用量页面的重置时间无法解析。");return;}
                var reset=times.Length==1?DateTimeOffset.Parse(times[0].Value,CultureInfo.InvariantCulture):(DateTimeOffset?)null;
                w=new(){Id="ollama."+id,Kind=id=="weekly"?"weekly":"fiveHour",UsedPercent=p,ResetsAt=reset,ReportsLength=false,Exhausted=p>=100};
                if(parent.Attribute("data-usage-meter")!=null)break;
            }if(w!=null)found.Add(w);
        }if(found.Count==2)r.Windows.AddRange(found);else Failure(r,"unreadableReply","用量页面没有同时返回会话和每周额度。");
    }
}
