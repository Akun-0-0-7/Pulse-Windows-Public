using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class WindowResetRecoveryTests
{
    private static readonly DateTimeOffset At=DateTimeOffset.Parse("2026-10-27T12:00:00+08:00");
    private static AccountConfig Account()=>new(){Id="retry",ProviderId="codex",Enabled=true,Extra=new(){["windowStarter"]="true"}};
    private static UsageReading Reading(DateTimeOffset now,double used=0,DateTimeOffset? reset=null)=>new()
    {
        AccountId="retry",ProviderId="codex",ObservedAt=now,
        Windows=[new(){Id="primary",Kind="fiveHour",UsedPercent=used,ResetsAt=reset??At.AddHours(5)}]
    };

    [Check] public static void FreshFullQuotaStartsOneAttemptWithoutWaitingForFutureReset()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        Assert.True(coordinator.Claim(account,Reading(At),At,0,0) is not null,"Fresh remaining 100% should initiate one hi.");
        Assert.True(coordinator.Claim(account,Reading(At.AddSeconds(1)),At.AddSeconds(1),0,0) is null,"Persistent full quota must not initiate another attempt.");
    }

    [Check] public static void FreshAvailableQuotaRecoversSavedPlanMoreThanFiveMinutesLate()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        coordinator.Claim(account,Reading(At.AddMinutes(-1),20,At),At.AddMinutes(-1),0,0);
        var now=At.AddMinutes(20);
        Assert.True(coordinator.Claim(account,Reading(now,0),now,0,0) is not null,"A saved reset must survive a long offline period when fresh quota confirms recovery.");
    }

    private static WindowStarterReceipt Failed(string kind)=>new("failed",false,null,null,kind);
    private static WindowStarterReceipt Success()=>new("success",true,2,1,null);
    private static WindowResetCoordinator Reload(WindowResetCoordinator coordinator)=>new(JsonSerializer.Deserialize<WindowResetState>(JsonSerializer.Serialize(coordinator.State)));

    [Check] public static void NetworkRecoveryBackoffSurvivesReloadAndCapsAtFiveMinutes()
    {
        var account=Account();var coordinator=new WindowResetCoordinator();var job=coordinator.Claim(account,Reading(At),At,0,0)!;var now=At;
        foreach(var delay in new[]{1,2,5,5})
        {
            coordinator.Complete(job!,Failed("network"),now);coordinator=Reload(coordinator);
            Assert.Equal(now.AddMinutes(delay),coordinator.State.Accounts[account.Id].Pending!.NextAttemptAt);
            Assert.True(coordinator.Claim(account,Reading(now.AddSeconds(30)),now.AddSeconds(30),0,0) is null);
            now=now.AddMinutes(delay);
            job=coordinator.Claim(account,Reading(now),now,0,0)!;
            Assert.True(job is not null,"Recovered network attempt should retry after backoff.");
        }
        coordinator.Complete(job!,Success(),now);
        Assert.True(coordinator.Claim(account,Reading(now.AddMinutes(5)),now.AddMinutes(5),0,0) is null);
    }

    [Check] public static void QuotaRecoveryRequiresFreshAvailableFiveHourAndWeeklyWindows()
    {
        var account=Account();var coordinator=new WindowResetCoordinator();var job=coordinator.Claim(account,Reading(At),At,0,0)!;
        coordinator.Complete(job!,Failed("quota"),At);
        var now=At.AddMinutes(2);
        var exhausted=Reading(now,100);Assert.True(coordinator.Claim(account,exhausted,now,0,0) is null);
        var weekly=Reading(now);weekly.Windows.Add(new(){Kind="weekly",UsedPercent=100,Exhausted=true});
        Assert.True(coordinator.Claim(account,weekly,now,0,0) is null);
        var stale=Reading(now);stale.IsStale=true;Assert.True(coordinator.Claim(account,stale,now,0,0) is null);
        Assert.True(coordinator.Claim(account,Reading(now.AddSeconds(-91)),now,0,0) is null);
        Assert.True(coordinator.Claim(account,Reading(now),now,0,0) is not null);
    }

    [Check] public static void SuccessfulEpisodeDoesNotRepeatAfterZeroOscillationResetDriftOrRestart()
    {
        var account=Account();var coordinator=new WindowResetCoordinator();var job=coordinator.Claim(account,Reading(At),At,0,0)!;
        coordinator.Complete(job!,Success(),At);
        coordinator=Reload(coordinator);
        Assert.True(coordinator.Claim(account,Reading(At.AddMinutes(1),.01),At.AddMinutes(1),0,0) is null);
        Assert.True(coordinator.Claim(account,Reading(At.AddMinutes(2),0,At.AddHours(5).AddMinutes(2)),At.AddMinutes(2),0,0) is null);
        Assert.True(coordinator.ClaimManual(account,Reading(At.AddMinutes(2)),At.AddMinutes(2)) is null);
    }

    [Check] public static void UnknownInFlightAndTimeoutNeverAutomaticallyReplayButManualCanRetry()
    {
        foreach(var failure in new string?[]{null,"timeout","turnIncomplete","failed","auth","toolMissing"})
        {
            var account=Account();var coordinator=new WindowResetCoordinator();var job=coordinator.Claim(account,Reading(At),At,0,0)!;
            if(failure is not null)coordinator.Complete(job!,Failed(failure),At);
            coordinator=Reload(coordinator);var now=At.AddMinutes(10);
            Assert.True(coordinator.Claim(account,Reading(now),now,0,0) is null);
            var manual=coordinator.ClaimManual(account,Reading(now),now);
            Assert.True(manual is not null,"Manual retry should recover uncertain and repaired attempts.");
            Assert.Equal(2,manual!.Attempt);
            coordinator.Complete(job!,Success(),now);
            Assert.Equal("sending",coordinator.State.Accounts[account.Id].Pending!.Status);
            coordinator.Complete(manual!,Success(),now);
            Assert.Equal("success",coordinator.State.Accounts[account.Id].Pending!.Status);
        }
    }

    [Check] public static void AutomaticHoursApplyToRecoveryButManualBypassesHours()
    {
        var account=Account();var coordinator=new WindowResetCoordinator();
        Assert.True(coordinator.Claim(account,Reading(At),At,13,18) is null);
        var manual=coordinator.ClaimManual(account,Reading(At),At)!;Assert.True(manual is not null);
        coordinator.Complete(manual!,Failed("network"),At);
        var now=At.AddMinutes(1);Assert.True(coordinator.Claim(account,Reading(now),now,13,18) is null);
        Assert.True(coordinator.ClaimManual(account,Reading(now),now) is not null);
    }

    [Check] public static void NewRealScheduledResetCanAdvancePastOldUnknownAttempt()
    {
        var account=Account();var coordinator=new WindowResetCoordinator();
        var first=coordinator.Claim(account,Reading(At),At,0,0)!;
        var now=At.AddHours(5);var next=coordinator.Claim(account,Reading(now,0,At.AddHours(10)),now,0,0)!;
        Assert.True(next is not null);Assert.True(next!.ResetAt>first.ResetAt);
        coordinator.Complete(first,Success(),now);
        Assert.Equal("sending",coordinator.State.Accounts[account.Id].Pending!.Status);
    }

    [Check] public static void FullQuotaRequiresRawZeroAndValidNonEstimatedMatchingReading()
    {
        foreach(var mutate in new Action<UsageReading>[]
        {
            r=>r.Windows[0].UsedPercent=.001,r=>r.Windows[0].Estimated=true,r=>r.Windows[0].Scope="model",
            r=>r.Windows[0].UsedPercent=double.NaN,r=>r.Windows[0].Exhausted=true,r=>r.ProviderId="claudeCode",
            r=>r.AccountId="another",r=>r.ErrorCode="offline",r=>r.ObservedAt=At.AddSeconds(1)
        })
        {
            var reading=Reading(At);mutate(reading);
            Assert.True(new WindowResetCoordinator().Claim(Account(),reading,At,0,0) is null);
        }
    }

    [Check] public static void LegacyClaimedHistoryDoesNotAutoRepeatFullQuotaButSupportsManualRecovery()
    {
        var account=Account();var coordinator=new WindowResetCoordinator(new(){Accounts=new(){[account.Id]=new(){LastClaimedResetAt=At}}});
        Assert.True(coordinator.Claim(account,Reading(At),At,0,0) is null);
        Assert.True(coordinator.ClaimManual(account,Reading(At),At) is not null);
    }

    [Check] public static void InvalidPendingStateIsRejectedWithoutThrowing()
    {
        Assert.True(!WindowResetCoordinator.IsValidState(null));
        var state=new WindowResetState{Accounts=new(){["retry"]=new(){Pending=new(){ProviderId="codex",ResetAt=At,LastAttemptAt=At,Attempts=1,Status="retry"}}}};
        Assert.True(!WindowResetCoordinator.IsValidState(state));
        Assert.True(new WindowResetCoordinator(state).State.Accounts.Count==0);
    }

    [Check] public static void ShortResetTimestampDriftCannotReplayUnknownAttemptAsNewCycle()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        Assert.True(coordinator.Claim(account,Reading(At),At,0,0) is not null);
        var now=At.AddMinutes(2);
        Assert.True(coordinator.Claim(account,Reading(now,0,now),now,0,0) is null,"A changed reset timestamp shortly after an unknown attempt must not replay it.");
    }

    [Check] public static void LatePlanWaitsThroughOfflineReadingThenRecoversWithoutLosingOriginalReset()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        coordinator.Claim(account,Reading(At.AddMinutes(-1),20,At),At.AddMinutes(-1),0,0);
        var now=At.AddMinutes(30);var offline=Reading(now);offline.ErrorCode="network";
        Assert.True(coordinator.Claim(account,offline,now,0,0) is null);
        Assert.Equal(At,coordinator.Claim(account,Reading(now.AddMinutes(1)),now.AddMinutes(1),0,0)!.ResetAt);
    }

    [Check] public static void KnownWeeklyExhaustionBlocksInitialScheduledSend()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        coordinator.Claim(account,Reading(At.AddMinutes(-1),20,At),At.AddMinutes(-1),0,0);
        var reading=Reading(At);reading.Windows.Add(new(){Kind="weekly",UsedPercent=100});
        Assert.True(coordinator.Claim(account,reading,At,0,0) is null);
    }

    [Check] public static void ManualCanStartProvenNewCycleAfterPriorSuccessOutsideAutomaticHours()
    {
        var coordinator=new WindowResetCoordinator();var account=Account();
        var first=coordinator.Claim(account,Reading(At),At,9,18)!;
        coordinator.Complete(first,Success(),At);
        var sameCycle=At.AddHours(1);
        Assert.True(!coordinator.CanClaimManual(account,Reading(sameCycle),sameCycle));
        Assert.True(coordinator.ClaimManual(account,Reading(sameCycle),sameCycle) is null);
        var now=At.AddHours(10);var reading=Reading(now,0,At.AddHours(15));
        Assert.True(coordinator.Claim(account,reading,now,9,18) is null,"Automatic sending must remain restricted by its allowed hours.");
        var before=JsonSerializer.Serialize(coordinator.State);
        Assert.True(coordinator.CanClaimManual(account,reading,now));
        Assert.Equal(before,JsonSerializer.Serialize(coordinator.State));
        var manual=coordinator.ClaimManual(account,reading,now);
        Assert.True(manual is not null,"Prior success must not permanently disable manual sending in a proven new window.");
        Assert.Equal(1,manual!.Attempt);
    }
}
