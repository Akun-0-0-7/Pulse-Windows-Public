using Pulse.Core;
namespace Pulse.Tests;
public static class AutomationTests
{
    [Check] public static void CodexToolCallAndCompletionAreLifecycleEvidence()
    {
        var now=DateTimeOffset.Parse("2026-10-20T12:00:00Z");
        var call="{\"timestamp\":\"2026-10-20T11:58:00Z\",\"payload\":{\"type\":\"function_call\"}}";
        Assert.True(AgentActivity.Parse("codex",call,now,now).IsWorking);
        Assert.True(!AgentActivity.Parse("codex",call+"\n{\"payload\":{\"type\":\"task_complete\"}}",now,now).IsWorking);
        Assert.True(!AgentActivity.Parse("codex","{}",now.AddDays(-1),now).IsWorking);
    }
    [Check] public static void ClaudeCompletedTurnDoesNotSpinBecauseOfBookkeeping()
    {
        var now=DateTimeOffset.UtcNow;var lines="{\"type\":\"assistant\",\"message\":{\"stop_reason\":\"end_turn\"}}\n{\"type\":\"file-history-snapshot\"}";
        Assert.True(!AgentActivity.Parse("claudeCode",lines,now,now).IsWorking);
    }
    [Check] public static void ForecastRequiresObservedGrowthAndSameReset()
    {
        var forecast=new BurnForecast();var at=DateTimeOffset.Parse("2026-10-20T12:00:00Z");var window=new UsageWindow{Id="daily",UsedPercent=20,ResetsAt=at.AddHours(4)};
        Assert.Equal<TimeSpan?>(null,forecast.Observe("a",window,at));window.UsedPercent=40;var remaining=forecast.Observe("a",window,at.AddHours(1));Assert.Near(3,remaining!.Value.TotalHours);
        window.ResetsAt=at.AddDays(1);Assert.Equal<TimeSpan?>(null,forecast.Observe("a",window,at.AddHours(2)));
    }
    [Check] public static void StarterRunsAtResetWithinAllowedHours()
    {
        var now=DateTimeOffset.Parse("2026-10-20T12:00:00+08:00");Assert.True(WindowStarter.IsDue(now,now,9,18));Assert.True(WindowStarter.IsDue(now.AddSeconds(-30),now,0,0));Assert.True(!WindowStarter.IsDue(now.AddMinutes(1),now,0,0));Assert.True(!WindowStarter.IsDue(now,now,20,23));
    }
}
