using Pulse.Core;
namespace Pulse.Tests;
public static class LocalHistoryTests
{
    [Check] public static void CodexCacheUsesRequestTimeAndKnownModelOnly()
    {
        const string text="""
        {"type":"turn_context","payload":{"model":"gpt-5.6-codex"}}
        {"type":"event_msg","timestamp":"2026-10-21T01:00:00Z","payload":{"type":"task_started"}}
        {"type":"token_usage_record","timestamp":"2026-10-21T01:05:00Z","payload":{"usage":{"cached_input_tokens":100}}}
        """;
        var session=PromptCacheService.Parse("codex",text,"demo")!;Assert.Equal(DateTimeOffset.Parse("2026-10-21T01:30:00Z"),session.ExpiresAt);Assert.True(session.IsMinimum);
        Assert.Equal<PromptCacheSession?>(null,PromptCacheService.Parse("codex",text.Replace("gpt-5.6","gpt-5.4"),"demo"));
    }
    [Check] public static void ClaudeCacheRequiresRecordedTierAndFollowsParent()
    {
        const string text="""
        {"type":"user","uuid":"u","timestamp":"2026-10-21T01:00:00Z"}
        {"type":"assistant","uuid":"a","parentUuid":"u","timestamp":"2026-10-21T01:02:00Z","message":{"usage":{"cache_creation_input_tokens":100,"cache_creation":{"ephemeral_1h_input_tokens":100}}}}
        """;
        var session=PromptCacheService.Parse("claudeCode",text,"demo")!;Assert.Equal(DateTimeOffset.Parse("2026-10-21T02:00:00Z"),session.ExpiresAt);Assert.True(!session.IsMinimum);
        Assert.Equal<PromptCacheSession?>(null,PromptCacheService.Parse("claudeCode",text.Replace("ephemeral_1h_input_tokens","unknown_tier"),"demo"));
    }
    [Check] public static void SummaryDoesNotInventCacheHitRateForPartialTokens()
    {
        var now=DateTimeOffset.Now;var report=new SpendReport{Records=[new("codex","a",now,"gpt",Input:100,CacheRead:400,IsPartial:true),new("claude","b",now,"claude",Input:9999)]};var result=LocalUsageSummary.Build(report,"codex",now)!;Assert.Equal<long?>(500,result.Today);Assert.Equal<double?>(null,result.CacheHitRate);Assert.Equal("gpt",result.TopModel);Assert.Equal<LocalUsageSummary?>(null,LocalUsageSummary.Build(report,"unrelated",now));
    }
}
