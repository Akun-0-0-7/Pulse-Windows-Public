using System.Runtime.ExceptionServices;

namespace Pulse.Core;

public sealed class WindowResetState
{
    public Dictionary<string, WindowResetAccountState> Accounts { get; set; } = [];
}

public sealed class WindowResetAccountState
{
    public List<DateTimeOffset> PlannedResets { get; set; } = [];
    public DateTimeOffset? LastClaimedResetAt { get; set; }
    public DateTimeOffset? LastCompletedResetAt { get; set; }
    public DateTimeOffset? LastCompletedQuotaResetAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public bool FullQuotaLatched { get; set; }
    public WindowResetAttempt? Pending { get; set; }
}

public sealed class WindowResetAttempt
{
    public string ProviderId { get; set; } = "";
    public DateTimeOffset ResetAt { get; set; }
    public string Status { get; set; } = "sending";
    public int Attempts { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? FailureKind { get; set; }
    public DateTimeOffset? QuotaResetAt { get; set; }
}

public sealed record WindowResetJob(string AccountId, string ProviderId, DateTimeOffset ResetAt, int Attempt = 1);

public sealed class WindowResetCoordinator
{
    private static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecoveryFreshness = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ResetJitter = TimeSpan.FromSeconds(60);

    public WindowResetCoordinator(WindowResetState? state = null) => State = IsValidState(state) ? state! : new();
    public WindowResetState State { get; }

    public static bool IsValidState(WindowResetState? state) => state?.Accounts is not null &&
        state.Accounts.Values.All(account => account?.PlannedResets is not null &&
            account.PlannedResets.All(reset => reset != default) &&
            (account.Pending is null || account.Pending is { ProviderId: "codex" or "claudeCode", Attempts: > 0 } pending &&
                pending.Attempts < int.MaxValue && pending.ResetAt != default && pending.LastAttemptAt != default &&
                pending.Status is "sending" or "retry" or "quota" or "paused" or "success" &&
                (pending.Status is not ("retry" or "quota") || pending.NextAttemptAt is not null)));

    public WindowResetJob? Claim(AccountConfig account, UsageReading? reading, DateTimeOffset now, int startHour, int endHour)
    {
        if (!Eligible(account)) return null;
        var accountState = GetAccount(account.Id);
        ObservePlans(account, accountState, reading, now);
        var fresh = Fresh(account, reading, now, RecoveryFreshness);
        var available = Available(fresh);
        var full = available && Primary(fresh!).All(window => window.UsedPercent == 0);
        if (fresh is not null && Primary(fresh).Any(window => window.UsedPercent > 0)) accountState.FullQuotaLatched = false;

        var due = accountState.PlannedResets.Where(reset => reset <= now).OrderByDescending(reset => reset).FirstOrDefault();
        if (due != default && WindowStarter.IsDue(due, now, startHour, endHour) &&
            (now - due <= ClaimWindow || available) && (fresh is null || available) && NewScheduledCycle(accountState, due))
        {
            accountState.PlannedResets.RemoveAll(reset => reset <= now);
            return Begin(account, accountState, due, now, fresh);
        }

        if (accountState.Pending is { } pending && pending.ProviderId == account.ProviderId)
        {
            if (pending.Status is "retry" or "quota" && pending.NextAttemptAt <= now &&
                WindowStarter.IsDue(pending.ResetAt, now, startHour, endHour) && available &&
                fresh!.ObservedAt >= pending.LastAttemptAt)
                return Begin(account, accountState, pending.ResetAt, now, fresh, pending.Attempts + 1);
            if (pending.Status != "success") return null;
        }

        if (full && !accountState.FullQuotaLatched && NewAvailabilityCycle(accountState, fresh!, now) &&
            WindowStarter.IsDue(now, now, startHour, endHour))
            return Begin(account, accountState, now, now, fresh);
        return null;
    }

    public bool CanClaimManual(AccountConfig account, UsageReading? reading, DateTimeOffset now)
    {
        if (!Eligible(account)) return false;
        var fresh = Fresh(account, reading, now, RecoveryFreshness);
        if (!Available(fresh)) return false;
        if (!State.Accounts.TryGetValue(account.Id, out var state)) return true;
        return state.Pending is not { Status: "success" } pending || pending.ProviderId != account.ProviderId ||
            NewAvailabilityCycle(state, fresh!, now);
    }

    public WindowResetJob? ClaimManual(AccountConfig account, UsageReading? reading, DateTimeOffset now)
    {
        if (!CanClaimManual(account, reading, now)) return null;
        var fresh = Fresh(account, reading, now, RecoveryFreshness);
        var accountState = GetAccount(account.Id);
        ObservePlans(account, accountState, reading, now);
        if (accountState.Pending is { } pending && pending.ProviderId == account.ProviderId)
        {
            if (pending.Status == "success")
            {
                accountState.PlannedResets.RemoveAll(reset => reset <= now);
                return Begin(account, accountState, now, now, fresh);
            }
            return Begin(account, accountState, pending.ResetAt, now, fresh, pending.Attempts + 1);
        }
        // Older one-shot versions only stored LastClaimedResetAt. A manual action may
        // recover that uncertain attempt, while automatic claims continue deduplicating it.
        return Begin(account, accountState, accountState.LastClaimedResetAt ?? now, now, fresh);
    }

    public void Complete(WindowResetJob job, WindowStarterReceipt receipt, DateTimeOffset now)
    {
        if (!State.Accounts.TryGetValue(job.AccountId, out var accountState) || accountState.Pending is not { } pending ||
            pending.ProviderId != job.ProviderId || pending.ResetAt != job.ResetAt || pending.Attempts != job.Attempt ||
            pending.Status != "sending") return;
        pending.FailureKind = receipt.FailureKind;
        pending.NextAttemptAt = null;
        if (receipt.Completed && receipt.Status == "success")
        {
            pending.Status = "success";
            accountState.LastCompletedResetAt = job.ResetAt;
            accountState.LastCompletedQuotaResetAt = pending.QuotaResetAt;
            accountState.LastCompletedAt = now;
            accountState.FullQuotaLatched = true;
        }
        else if(receipt.FailureKind=="dailyLimit")
        {
            pending.Status="retry";
            pending.NextAttemptAt=new DateTimeOffset(now.Date.AddDays(1),now.Offset);
        }
        else if (receipt.FailureKind is "network" or "quota")
        {
            pending.Status = receipt.FailureKind == "quota" ? "quota" : "retry";
            pending.NextAttemptAt = now.AddMinutes(pending.Attempts == 1 ? 1 : pending.Attempts == 2 ? 2 : 5);
        }
        else pending.Status = "paused";
    }

    private static bool Eligible(AccountConfig account) => account.Enabled && account.ProviderId is "codex" or "claudeCode" &&
        account.Extra.GetValueOrDefault("windowStarter") == "true" && account.Extra.GetValueOrDefault("isAdditionalAccount") != "true";

    private WindowResetAccountState GetAccount(string id)
    {
        if (!State.Accounts.TryGetValue(id, out var state)) State.Accounts[id] = state = new();
        return state;
    }

    private static UsageReading? Fresh(AccountConfig account, UsageReading? reading, DateTimeOffset now, TimeSpan age) =>
        reading is not null && reading.AccountId == account.Id && reading.ProviderId == account.ProviderId && reading.ErrorCode is null &&
        !reading.IsStale && reading.ObservedAt is { } observed && observed <= now && now - observed <= age ? reading : null;

    private static IEnumerable<UsageWindow> Primary(UsageReading reading) =>
        reading.Windows.Where(window => window.Kind == "fiveHour" && window.Scope is null && !window.Estimated);

    private static bool Available(UsageReading? reading) => reading is not null && Primary(reading).Any() &&
        reading.Windows.Where(window => window.Kind is "fiveHour" or "weekly" && window.Scope is null && !window.Estimated)
            .All(window => double.IsFinite(window.UsedPercent) && window.UsedPercent >= 0 && window.UsedPercent < 100 && !window.Exhausted);

    private static DateTimeOffset? QuotaReset(UsageReading reading) => Primary(reading).Where(window => window.ResetsAt is not null)
        .Select(window => window.ResetsAt).OrderBy(reset => reset).FirstOrDefault();

    private static bool NewAvailabilityCycle(WindowResetAccountState state, UsageReading reading, DateTimeOffset now)
    {
        if (state.LastCompletedAt is null) return state.LastClaimedResetAt is null;
        if (state.LastCompletedQuotaResetAt is { } oldReset && QuotaReset(reading) is { } newReset)
            return now >= oldReset && newReset > oldReset + ResetJitter;
        return now - state.LastCompletedAt >= TimeSpan.FromHours(5);
    }

    private static bool NewScheduledCycle(WindowResetAccountState state, DateTimeOffset due)
    {
        if (state.LastClaimedResetAt is not { } previous) return true;
        // A freshly changed timestamp is not proof that an uncertain attempt belongs
        // to a new window. Wait for the previously observed boundary (or a full
        // five-hour cycle for old snapshots that still showed the just-expired reset).
        var boundary = state.Pending?.QuotaResetAt is { } quotaReset && quotaReset > previous + ResetJitter
            ? quotaReset : previous.AddHours(5);
        return due >= boundary - ResetJitter;
    }

    private static WindowResetJob Begin(AccountConfig account, WindowResetAccountState state, DateTimeOffset reset, DateTimeOffset now,
        UsageReading? fresh, int attempts = 1)
    {
        state.LastClaimedResetAt = reset;
        state.FullQuotaLatched = true;
        state.Pending = new()
        {
            ProviderId = account.ProviderId, ResetAt = reset, Status = "sending", Attempts = attempts,
            LastAttemptAt = now, QuotaResetAt = fresh is null ? null : QuotaReset(fresh)
        };
        return new(account.Id, account.ProviderId, reset, attempts);
    }

    private static void ObservePlans(AccountConfig account, WindowResetAccountState state, UsageReading? reading, DateTimeOffset now)
    {
        state.PlannedResets.RemoveAll(reset => SameCycle(reset, state.LastClaimedResetAt));
        var fresh = Fresh(account, reading, now, ClaimWindow);
        if (fresh is null) return;
        var reset = Primary(fresh).Where(window => window.ResetsAt is { } at && at >= now)
            .Select(window => window.ResetsAt!.Value).OrderBy(value => value).FirstOrDefault();
        if (reset == default) return;
        state.PlannedResets.RemoveAll(existing => existing > now);
        if (!SameCycle(reset, state.LastClaimedResetAt) && !state.PlannedResets.Any(existing => SameCycle(existing, reset)))
            state.PlannedResets.Add(reset);
        state.PlannedResets.Sort();
    }

    private static bool SameCycle(DateTimeOffset a, DateTimeOffset? b) =>
        b is { } value && (a - value).Duration() <= ResetJitter;

    public static async Task<ProcessResult> ExecuteAsync(
        Func<CancellationToken, Task<ProcessResult>> send,
        Func<CancellationToken, Task> refresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var initialRefresh = RefreshSafelyAsync(refresh, ct);
        ProcessResult? result = null;
        ExceptionDispatchInfo? sendFailure = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            result = await send(ct);
        }
        catch (Exception exception)
        {
            sendFailure = ExceptionDispatchInfo.Capture(exception);
        }

        await initialRefresh;
        ct.ThrowIfCancellationRequested();
        await RefreshSafelyAsync(refresh, ct);
        sendFailure?.Throw();
        return result!;
    }

    private static async Task RefreshSafelyAsync(Func<CancellationToken, Task> refresh, CancellationToken ct)
    {
        try { await refresh(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
