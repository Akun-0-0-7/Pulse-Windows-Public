using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class WindowResetTests
{
    private static readonly DateTimeOffset Reset = DateTimeOffset.Parse("2026-10-23T12:00:00+08:00");

    private static AccountConfig Account(string provider = "codex") => new()
    {
        Id = "account-1", ProviderId = provider, Enabled = true,
        Extra = new() { ["windowStarter"] = "true" }
    };

    private static UsageReading Reading(AccountConfig account, DateTimeOffset observed, params UsageWindow[] windows) => new()
    {
        AccountId = account.Id, ProviderId = account.ProviderId, ObservedAt = observed, Windows = [.. windows]
    };

    private static UsageWindow FiveHour(DateTimeOffset at) => new() { Id = "primary", Kind = "fiveHour", UsedPercent = 20, ResetsAt = at };

    [Check] public static void ExactResetClaimsOnlyOnceAndSurvivesJsonReload()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        var reading = Reading(account, Reset, FiveHour(Reset));
        var job = coordinator.Claim(account, reading, Reset, 0, 0);
        Assert.True(job is not null);
        Assert.Equal(account.Id, job!.AccountId);
        Assert.Equal(account.ProviderId, job.ProviderId);
        Assert.Equal(Reset, job.ResetAt);
        var saved = JsonSerializer.Deserialize<WindowResetState>(JsonSerializer.Serialize(coordinator.State));
        Assert.True(new WindowResetCoordinator(saved).Claim(account, reading, Reset.AddSeconds(1), 0, 0) is null);
    }

    [Check] public static void FutureReadingPreservesDueOldCycleAndTracksNextCycle()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        Assert.True(coordinator.Claim(account, Reading(account, Reset.AddMinutes(-1), FiveHour(Reset)), Reset.AddMinutes(-1), 0, 0) is null);
        var next = Reset.AddHours(5);
        var job = coordinator.Claim(account, Reading(account, Reset, FiveHour(next)), Reset, 0, 0);
        Assert.Equal(Reset, job!.ResetAt);
        Assert.Equal(next, coordinator.Claim(account, Reading(account, next, FiveHour(next)), next, 0, 0)!.ResetAt);
    }

    [Check] public static void FreshLaterResetReplacesFuturePlanBeforeOldTimeArrives()
    {
        var account = Account();
        var now = Reset.AddHours(-1);
        var later = Reset.AddHours(2);
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, now, FiveHour(Reset)), now, 0, 0);
        coordinator.Claim(account, Reading(account, now.AddMinutes(1), FiveHour(later)), now.AddMinutes(1), 0, 0);
        Assert.Equal(1, coordinator.State.Accounts[account.Id].PlannedResets.Count);
        Assert.Equal(later, coordinator.State.Accounts[account.Id].PlannedResets[0]);
        Assert.True(coordinator.Claim(account, null, Reset, 0, 0) is null);
        Assert.Equal(later, coordinator.Claim(account, null, later, 0, 0)!.ResetAt);
    }

    [Check] public static void FreshEarlierResetReplacesFuturePlan()
    {
        var account = Account();
        var now = Reset.AddHours(-2);
        var later = Reset.AddHours(2);
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, now, FiveHour(later)), now, 0, 0);
        coordinator.Claim(account, Reading(account, now.AddMinutes(1), FiveHour(Reset)), now.AddMinutes(1), 0, 0);
        Assert.Equal(1, coordinator.State.Accounts[account.Id].PlannedResets.Count);
        Assert.Equal(Reset, coordinator.Claim(account, null, Reset, 0, 0)!.ResetAt);
        Assert.True(coordinator.Claim(account, null, later, 0, 0) is null);
    }

    [Check] public static void LatestReadingCollapsesSeveralUnreachedPlansToOne()
    {
        var account = Account();
        var now = Reset.AddHours(-1);
        var coordinator = new WindowResetCoordinator();
        foreach (var hours in new[] { 0, 2, 3, 4, 5 })
        {
            var at = now.AddMinutes(hours + 1);
            coordinator.Claim(account, Reading(account, at, FiveHour(Reset.AddHours(hours))), at, 0, 0);
        }
        Assert.Equal(1, coordinator.State.Accounts[account.Id].PlannedResets.Count);
        Assert.Equal(Reset.AddHours(5), coordinator.State.Accounts[account.Id].PlannedResets[0]);
    }

    [Check] public static void SmallFutureCorrectionUsesLatestTimeWithoutReclaimingCycle()
    {
        var account = Account();
        var now = Reset.AddMinutes(-10);
        var revised = Reset.AddSeconds(30);
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, now, FiveHour(Reset)), now, 0, 0);
        coordinator.Claim(account, Reading(account, now.AddMinutes(1), FiveHour(revised)), now.AddMinutes(1), 0, 0);
        Assert.True(coordinator.Claim(account, null, Reset, 0, 0) is null);
        Assert.Equal(revised, coordinator.Claim(account, null, revised, 0, 0)!.ResetAt);
        Assert.True(coordinator.Claim(account, Reading(account, revised.AddSeconds(20), FiveHour(revised.AddSeconds(20))), revised.AddSeconds(20), 0, 0) is null);
    }

    [Check] public static void BadReadingKeepsExistingFuturePlan()
    {
        var account = Account();
        var now = Reset.AddMinutes(-10);
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, now, FiveHour(Reset)), now, 0, 0);
        var failed = Reading(account, now.AddMinutes(1), FiveHour(Reset.AddHours(5)));
        failed.ErrorCode = "failed";
        coordinator.Claim(account, failed, now.AddMinutes(1), 0, 0);
        coordinator.Claim(account, Reading(account, now.AddMinutes(2), new UsageWindow { Kind = "weekly", ResetsAt = Reset.AddHours(5) }), now.AddMinutes(2), 0, 0);
        Assert.Equal(1, coordinator.State.Accounts[account.Id].PlannedResets.Count);
        Assert.Equal(Reset, coordinator.Claim(account, null, Reset, 0, 0)!.ResetAt);
    }

    [Check] public static void OnlyPrimaryRealFiveHourWindowCanCreatePlan()
    {
        var account = Account();
        var windows = new[]
        {
            new UsageWindow { Id = "weekly", Kind = "weekly", ResetsAt = Reset },
            new UsageWindow { Id = "model", Kind = "fiveHour", Scope = "gpt", ResetsAt = Reset },
            new UsageWindow { Id = "estimate", Kind = "fiveHour", Estimated = true, ResetsAt = Reset },
            FiveHour(Reset.AddHours(5))
        };
        var coordinator = new WindowResetCoordinator();
        Assert.True(coordinator.Claim(account, Reading(account, Reset, windows), Reset, 0, 0) is null);
        Assert.Equal(Reset.AddHours(5), coordinator.Claim(account, Reading(account, Reset.AddHours(5), windows), Reset.AddHours(5), 0, 0)!.ResetAt);
    }

    [Check] public static void RejectsIneligibleAccountsAndMismatchedReadings()
    {
        var now = Reset;
        var reading = Reading(Account(), now, FiveHour(now));
        var coordinator = new WindowResetCoordinator();
        var disabled = Account(); disabled.Enabled = false;
        Assert.True(coordinator.Claim(disabled, reading, now, 0, 0) is null);
        var extra = Account(); extra.Extra["isAdditionalAccount"] = "true";
        Assert.True(coordinator.Claim(extra, reading, now, 0, 0) is null);
        var noOptIn = Account(); noOptIn.Extra.Clear();
        Assert.True(coordinator.Claim(noOptIn, reading, now, 0, 0) is null);
        Assert.True(coordinator.Claim(Account("kiro"), reading, now, 0, 0) is null);
        reading.AccountId = "other";
        Assert.True(coordinator.Claim(Account(), reading, now, 0, 0) is null);
    }

    [Check] public static void FailedOrStaleReadingCannotChangeSavedPlan()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, Reset.AddMinutes(-1), FiveHour(Reset)), Reset.AddMinutes(-1), 0, 0);
        var bad = Reading(account, Reset, FiveHour(Reset.AddHours(5))); bad.ErrorCode = "failed";
        Assert.Equal(Reset, coordinator.Claim(account, bad, Reset, 0, 0)!.ResetAt);
        var stale = Reading(account, Reset, FiveHour(Reset.AddHours(5))); stale.IsStale = true;
        Assert.True(coordinator.Claim(account, stale, Reset.AddSeconds(1), 0, 0) is null);
        var old = Reading(account, Reset.AddMinutes(-6), FiveHour(Reset.AddHours(5)));
        Assert.True(coordinator.Claim(account, old, Reset.AddSeconds(2), 0, 0) is null);
        Assert.True(coordinator.Claim(account, null, Reset.AddHours(5), 0, 0) is null);
    }

    [Check] public static void LateClaimAllowedForFiveMinutesButNotAfter()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, Reset.AddMinutes(-1), FiveHour(Reset)), Reset.AddMinutes(-1), 0, 0);
        Assert.True(coordinator.Claim(account, null, Reset.AddMinutes(5).AddTicks(1), 0, 0) is null);
        var within = new WindowResetCoordinator();
        within.Claim(account, Reading(account, Reset.AddMinutes(-1), FiveHour(Reset)), Reset.AddMinutes(-1), 0, 0);
        Assert.Equal(Reset, within.Claim(account, null, Reset.AddMinutes(5), 0, 0)!.ResetAt);
    }

    [Check] public static void FreshPastResetDoesNotCreateNewLateClaim()
    {
        var account = Account();
        var now = Reset.AddMinutes(4);
        var coordinator = new WindowResetCoordinator();
        Assert.True(coordinator.Claim(account, Reading(account, now, FiveHour(Reset)), now, 0, 0) is null);
        Assert.True(coordinator.State.Accounts[account.Id].PlannedResets.Count == 0);
    }

    [Check] public static void SavedFuturePlanCanBeClaimedFourMinutesLateAfterRestart()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        coordinator.Claim(account, Reading(account, Reset.AddMinutes(-1), FiveHour(Reset)), Reset.AddMinutes(-1), 0, 0);
        var saved = JsonSerializer.Deserialize<WindowResetState>(JsonSerializer.Serialize(coordinator.State));
        var restarted = new WindowResetCoordinator(saved);
        Assert.Equal(Reset, restarted.Claim(account, null, Reset.AddMinutes(4), 0, 0)!.ResetAt);
    }

    [Check] public static void ExpiredFiveHourWindowDoesNotHideFutureWindowInSameReading()
    {
        var account = Account();
        var now = Reset.AddMinutes(4);
        var next = Reset.AddHours(5);
        var coordinator = new WindowResetCoordinator();
        var reading = Reading(account, now,
            new UsageWindow { Id = "expired-group", Kind = "fiveHour", UsedPercent = 20, ResetsAt = Reset },
            new UsageWindow { Id = "future-group", Kind = "fiveHour", UsedPercent = 20, ResetsAt = next });
        Assert.True(coordinator.Claim(account, reading, now, 0, 0) is null);
        Assert.Equal(1, coordinator.State.Accounts[account.Id].PlannedResets.Count);
        Assert.Equal(next, coordinator.State.Accounts[account.Id].PlannedResets[0]);
        Assert.Equal(next, coordinator.Claim(account, null, next, 0, 0)!.ResetAt);
    }

    [Check] public static void MinuteJitterAndMultipleFiveHourWindowsMakeOneJob()
    {
        var account = Account();
        var coordinator = new WindowResetCoordinator();
        var reading = Reading(account, Reset, FiveHour(Reset), new UsageWindow { Id = "secondary", Kind = "fiveHour", ResetsAt = Reset.AddSeconds(30) });
        Assert.Equal(Reset, coordinator.Claim(account, reading, Reset, 0, 0)!.ResetAt);
        Assert.True(coordinator.Claim(account, Reading(account, Reset.AddSeconds(30), FiveHour(Reset.AddSeconds(30))), Reset.AddSeconds(30), 0, 0) is null);
    }

    [Check] public static async Task ExecutionRefreshesBeforeAndAfterSingleSend()
    {
        var steps = new List<string>();
        var result = await WindowResetCoordinator.ExecuteAsync(
            _ => { steps.Add("send"); return Task.FromResult(new ProcessResult(1, "", "failure", false)); },
            _ => { steps.Add("refresh"); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("refresh,send,refresh", string.Join(',', steps));
    }

    [Check] public static async Task FirstRefreshMayOverlapSendButSecondRefreshWaitsForIt()
    {
        var firstRefreshRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        var sends = 0;
        var execution = WindowResetCoordinator.ExecuteAsync(
            _ => { sends++; sendStarted.SetResult(true); return Task.FromResult(new ProcessResult(0, "", "", false)); },
            async _ => { if (++refreshes == 1) await firstRefreshRelease.Task; }, CancellationToken.None);
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, sends);
        Assert.Equal(1, refreshes);
        firstRefreshRelease.SetResult(true);
        await execution;
        Assert.Equal(2, refreshes);
        Assert.Equal(1, sends);
    }

    [Check] public static async Task CancellationDuringFirstRefreshPreventsSecondRefresh()
    {
        using var cancel = new CancellationTokenSource();
        var firstRefreshRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        var sends = 0;
        var execution = WindowResetCoordinator.ExecuteAsync(
            _ => { sends++; sendStarted.SetResult(true); return Task.FromResult(new ProcessResult(0, "", "", false)); },
            async _ => { if (++refreshes == 1) await firstRefreshRelease.Task; }, cancel.Token);
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancel.Cancel();
        firstRefreshRelease.SetResult(true);
        try { await execution; throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        Assert.Equal(1, refreshes);
        Assert.Equal(1, sends);
    }

    [Check] public static async Task FatalRefreshFailurePropagates()
    {
        var sends = 0;
        try
        {
            await WindowResetCoordinator.ExecuteAsync(
                _ => { sends++; return Task.FromResult(new ProcessResult(0, "", "", false)); },
                _ => throw new OutOfMemoryException("fatal"), CancellationToken.None);
            throw new Exception("Expected fatal refresh failure");
        }
        catch (OutOfMemoryException exception) { Assert.Equal("fatal", exception.Message); }
        Assert.Equal(1, sends);
    }

    [Check] public static async Task RefreshFailureDoesNotSuppressSendOrLeaveTaskUnobserved()
    {
        var steps = new List<string>();
        await WindowResetCoordinator.ExecuteAsync(
            _ => { steps.Add("send"); return Task.FromResult(new ProcessResult(0, "", "", false)); },
            _ => { steps.Add("refresh"); throw new InvalidOperationException("refresh failed"); }, CancellationToken.None);
        Assert.Equal("refresh,send,refresh", string.Join(',', steps));
    }

    [Check] public static async Task SendExceptionStillRefreshesAndPropagates()
    {
        var steps = new List<string>();
        try
        {
            await WindowResetCoordinator.ExecuteAsync(
                _ => { steps.Add("send"); throw new InvalidOperationException("send failed"); },
                _ => { steps.Add("refresh"); return Task.CompletedTask; }, CancellationToken.None);
            throw new Exception("Expected send failure");
        }
        catch (InvalidOperationException exception) { Assert.Equal("send failed", exception.Message); }
        Assert.Equal("refresh,send,refresh", string.Join(',', steps));
    }

    [Check] public static async Task CancellationDoesNotStartSendOrSecondRefresh()
    {
        using var cancel = new CancellationTokenSource();
        var steps = new List<string>();
        try
        {
            await WindowResetCoordinator.ExecuteAsync(
                _ => { steps.Add("send"); return Task.FromResult(new ProcessResult(0, "", "", false)); },
                _ => { steps.Add("refresh"); cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Assert.Equal("refresh", string.Join(',', steps));
    }
}
