using Pulse.Core;
namespace Pulse.Tests;
public static class HistoryAlertTests
{
    static AppConfig Enabled()=>new(){AlertsEnabled=true,ResetAlerts=true,StatusAlerts=true,Accounts=[new(){Id="a",ProviderId="codex",Enabled=true}]};
    static UsageReading Reading(DateTimeOffset at,double used,DateTimeOffset? reset=null,string kind="session")=>new(){AccountId="a",ProviderId="codex",ObservedAt=at,Windows=[new(){Id="main",Kind=kind,UsedPercent=used,ResetsAt=reset}]};
    [Check] public static void ResetNotifiesOnlyAfterAnAlertAndRealTurnover()
    {
        var at=DateTimeOffset.UtcNow;var config=Enabled();var rules=new AlertRules();
        Assert.True(rules.Evaluate(config,Reading(at,90,at.AddHours(1))).Count>0);
        Assert.Equal(0,rules.Evaluate(config,Reading(at.AddMinutes(1),88,at.AddHours(1))).Count);
        var reset=rules.Evaluate(config,Reading(at.AddHours(1),2,at.AddHours(6)));Assert.Equal(1,reset.Count);Assert.True(reset[0].Contains("重置"));
        Assert.Equal(0,rules.Evaluate(config,Reading(at.AddHours(1),2,at.AddHours(6))).Count);
        Assert.Equal(0,new AlertRules().Evaluate(config,Reading(at,2,at.AddHours(6))).Count);
    }
    [Check] public static void TopUpAndStaleDropsNeverAnnounceReset()
    {
        var at=DateTimeOffset.UtcNow;foreach(var kind in new[]{"balance","topUp","credits","sharedCredits","planCredits","dedicatedCredits"})
        { var rules=new AlertRules();rules.Evaluate(Enabled(),Reading(at,90,null,kind));Assert.Equal(0,rules.Evaluate(Enabled(),Reading(at.AddMinutes(1),2,null,kind)).Count); }
        var r=new AlertRules();r.Evaluate(Enabled(),Reading(at,90));var stale=Reading(at.AddMinutes(1),2);stale.IsStale=true;Assert.Equal(0,r.Evaluate(Enabled(),stale).Count);
    }
    [Check] public static void LowBalanceRearmsAfterRecharge()
    {
        var config=Enabled();config.LowBalanceThreshold=10;var r=new AlertRules();var v=new UsageReading{AccountId="a",Balance=new(2,"USD")};Assert.Equal(1,r.Evaluate(config,v).Count);Assert.Equal(0,r.Evaluate(config,v).Count);v.Balance=new(20,"USD");r.Evaluate(config,v);v.Balance=new(2,"USD");Assert.Equal(1,r.Evaluate(config,v).Count);
    }
    [Check]public static void ResetTimestampJitterDoesNotRepeatQuotaWarnings()
    {
        var at=DateTimeOffset.UtcNow;var config=Enabled();config.AlertThresholds=[90];var r=new AlertRules();Assert.Equal(1,r.Evaluate(config,Reading(at,90,at.AddHours(1))).Count);Assert.Equal(0,r.Evaluate(config,Reading(at.AddMinutes(1),91,at.AddHours(1).AddSeconds(10))).Count);
        var reloaded=new AlertRules{Seen=[..r.Seen]};Assert.Equal(1,reloaded.Evaluate(config,Reading(at.AddHours(1),2,at.AddHours(6))).Count);
    }
    [Check] public static void HistoryBoundsDeduplicatesReloadsAndIgnoresFailures()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-history-"+Guid.NewGuid().ToString("N"));try
        {var store=new AppStorage(root);var h=new UsageHistoryStore(store);var at=DateTimeOffset.UtcNow.AddHours(-20);
            for(var i=0;i<725;i++)h.Observe(Reading(at.AddMinutes(i),i%100));
            Assert.Equal(720,h.Read("a","main").Count);Assert.True(!h.Observe(Reading(at.AddMinutes(724),20)));
            var failed=Reading(at.AddDays(1),50);failed.IsStale=true;Assert.True(!h.Observe(failed));
            Assert.Equal(720,new UsageHistoryStore(store).Read("a","main").Count);h.Remove("a");Assert.Equal(0,h.Read("a","main").Count);
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Check] public static void StatusAlertsAreSelectedDeduplicatedAndRearmOnRecovery()
    {
        var config=Enabled();var r=new StatusAlertRules();var report=new StatusReport{ProviderId="codex",Components=[new("x","CLI","fullOutage","Codex")]};
        Assert.Equal(1,r.Evaluate(config,report).Count);Assert.Equal(0,r.Evaluate(config,report).Count);
        report.Components=[new("x","CLI","operational","Codex")];Assert.Equal(1,r.Evaluate(config,report).Count);Assert.Equal(0,r.Evaluate(config,report).Count);
        report.Components=[new("x","CLI","fullOutage","Codex")];Assert.Equal(1,r.Evaluate(config,report).Count);config.Accounts[0].Enabled=false;Assert.Equal(0,r.Evaluate(config,report).Count);
        config.Accounts[0].Enabled=true;report.Error="offline";Assert.Equal(0,r.Evaluate(config,report).Count);
    }
}
