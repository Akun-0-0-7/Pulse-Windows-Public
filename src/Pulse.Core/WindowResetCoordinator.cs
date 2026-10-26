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
}

public sealed record WindowResetJob(string AccountId, string ProviderId, DateTimeOffset ResetAt);

public sealed class WindowResetCoordinator
{
    private static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResetJitter = TimeSpan.FromSeconds(60);

    public WindowResetCoordinator(WindowResetState? state = null) => State = state ?? new();
    public WindowResetState State { get; }

    public WindowResetJob? Claim(AccountConfig account, UsageReading? reading, DateTimeOffset now, int startHour, int endHour)
    {
        if (!account.Enabled || account.ProviderId is not ("codex" or "claudeCode") ||
            account.Extra.GetValueOrDefault("windowStarter") != "true" ||
            account.Extra.GetValueOrDefault("isAdditionalAccount") == "true") return null;

        if (!State.Accounts.TryGetValue(account.Id, out var accountState))
            State.Accounts[account.Id] = accountState = new();

        accountState.PlannedResets.RemoveAll(reset =>
            now - reset > ClaimWindow || SameCycle(reset, accountState.LastClaimedResetAt));

        if (reading is not null && reading.AccountId == account.Id && reading.ProviderId == account.ProviderId &&
            reading.ErrorCode is null && !reading.IsStale && reading.ObservedAt is { } observedAt &&
            observedAt <= now && now - observedAt <= ClaimWindow)
        {
            var reset = reading.Windows
                .Where(window => window.Kind == "fiveHour" && window.Scope is null && !window.Estimated && window.ResetsAt is { } resetAt && resetAt >= now)
                .Select(window => window.ResetsAt!.Value)
                .OrderBy(value => value)
                .FirstOrDefault();
            if (reset != default && reset >= now)
            {
                accountState.PlannedResets.RemoveAll(existing => existing > now);
                if (!SameCycle(reset, accountState.LastClaimedResetAt) &&
                    !accountState.PlannedResets.Any(existing => SameCycle(existing, reset)))
                    accountState.PlannedResets.Add(reset);
            }
        }

        accountState.PlannedResets.Sort();
        var due = accountState.PlannedResets.FirstOrDefault(reset =>
            WindowStarter.IsDue(reset, now, startHour, endHour));
        if (due == default) return null;

        accountState.PlannedResets.Remove(due);
        accountState.LastClaimedResetAt = due;
        return new(account.Id, account.ProviderId, due);
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
