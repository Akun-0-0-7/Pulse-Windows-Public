using System.Text.Json;
using Pulse.Core;
namespace Pulse.Tests;
public static class SpendFamilyTests
{
    static string Temp(){var p=Path.Combine(Path.GetTempPath(),"pulse-families-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static void Write(string home,string path,string value){var file=Path.Combine(home,path);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,value);}
    static AppConfig Config(string source)=>new(){SpendEnabled=true,EnabledSpendSources=[source]};
    static string Message(string id,int input)=>JsonSerializer.Serialize(new{type="message",id,timestamp="2026-10-20T01:00:00Z",message=new{role="assistant",model="x",responseId="response-"+id,usage=new{input,output=0,cacheRead=0,cacheWrite=0}}});
    [Check]public static async Task PrimeSubtractsOnlyAChildActuallyPresentAndFoldsForkCopies()
    {
        var home=Temp();try
        {var parent=Path.Combine(home,".prime/agent/sessions/parent.jsonl");var parentHeader=JsonSerializer.Serialize(new{type="session",id="parent",cwd=home});var attr="""{"type":"child_usage_attributed","id":"abcd1234","targetId":"p","childUsage":{"input":20},"aggregateUsage":{"input":100}}""";Write(home,".prime/agent/sessions/parent.jsonl",parentHeader+"\n"+Message("p",100)+"\n"+attr);var service=new SpendService();Assert.Equal(100L,(await service.ScanAsync(Config("prime-agent"),home)).TotalTokens);
            Write(home,".prime/agent/sessions/child.jsonl",JsonSerializer.Serialize(new{type="session",id="child",rlmDepth=1,parentSession=parent})+"\n"+Message("c",20));Write(home,".prime/agent/sessions/fork.jsonl",JsonSerializer.Serialize(new{type="session",id="fork",parentSession=parent})+"\n"+Message("p",100)+"\n"+attr);var report=await service.ScanAsync(Config("prime-agent"),home);Assert.Equal(100L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.Equal(80L,report.Records.Single(r=>r.Identity.Contains("response-p")).Input);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task PrimeAttributionIdsRemainScopedToTheirLineage()
    {
        var home=Temp();try
        {foreach(var n in new[]{"one","two"}){var parent=Path.Combine(home,$".prime/agent/sessions/{n}.jsonl");Write(home,$".prime/agent/sessions/{n}.jsonl",JsonSerializer.Serialize(new{type="session",id=n})+"\n"+Message(n,100)+"\n"+JsonSerializer.Serialize(new{type="child_usage_attributed",id="abcd1234",targetId=n,childUsage=new{input=20},aggregateUsage=new{input=100}}));Write(home,$".prime/agent/sessions/{n}-child.jsonl",JsonSerializer.Serialize(new{type="session",id=n+"-child",rlmDepth=1,parentSession=parent})+"\n"+Message(n+"-child",20));}var report=await new SpendService().ScanAsync(Config("prime-agent"),home);Assert.Equal(200L,report.TotalTokens);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task PiRequiresTheFirstNonTitleLineToBeAValidHeader()
    {
        var home=Temp();try{Write(home,".pi/agent/sessions/bad.jsonl","{\"type\":\"other\"}\n{\"type\":\"session\",\"id\":\"s\"}\n"+Message("m",10));Assert.Equal(0,(await new SpendService().ScanAsync(Config("pi"),home)).Records.Count);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task KimchiIdentitiesStayScopedToSession()
    {
        var home=Temp();try{foreach(var n in new[]{"one","two"})Write(home,$".config/kimchi/harness/sessions/{n}.jsonl",JsonSerializer.Serialize(new{type="session",id=n})+"\n"+Message("same",10));Assert.Equal(20L,(await new SpendService().ScanAsync(Config("kimchi"),home)).TotalTokens);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task QwenUsesDeclaredTotalToDecideCacheAndIncludesThoughts()
    {
        var home=Temp();try{Write(home,".qwen/projects/p/chats/s.jsonl","""
{"type":"assistant","id":"a","sessionId":"s","timestamp":"2026-10-20T01:00:00Z","model":"x","usageMetadata":{"promptTokenCount":100,"cachedContentTokenCount":30,"candidatesTokenCount":20,"thoughtsTokenCount":5,"totalTokenCount":155}}
{"type":"assistant","id":"b","sessionId":"s","timestamp":"2026-10-20T01:00:00Z","model":"x","usageMetadata":{"promptTokenCount":100,"cachedContentTokenCount":30,"candidatesTokenCount":20,"thoughtsTokenCount":5,"totalTokenCount":999}}
""");var report=await new SpendService().ScanAsync(Config("qwen"),home);Assert.Equal(1154L,report.TotalTokens);Assert.Equal(100L,report.Records.Single(r=>r.Identity.EndsWith(":a")).Input);Assert.Equal(25L,report.Records.Single(r=>r.Identity.EndsWith(":a")).Output);Assert.Equal(999L,report.Records.Single(r=>r.Identity.EndsWith(":b")).Unclassified);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task DroidUsesSettingsTimestampAndReconcilesReportedTotal()
    {
        var home=Temp();try{Write(home,".factory/sessions/s.settings.json","""{"model":"custom:claude-opus-4.5 [effort=high]","providerLockTimestamp":"2026-10-20T01:00:00Z","tokenUsage":{"inputTokens":100,"outputTokens":20,"cacheReadTokens":30,"thinkingTokens":5,"totalTokens":155}}""");var report=await new SpendService().ScanAsync(Config("droid"),home);Assert.Equal(155L,report.TotalTokens);var row=report.Records[0];Assert.Equal("claude-opus-4.5",row.Model);Assert.Equal("s",row.Session);Assert.True(row.IsAggregate);Assert.Equal(25L,row.Output);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task HindsightLedgerUsesStartedAtAndIndependentCachedBucket()
    {
        var home=Temp();try{Write(home,".hindsight/usage/ledger.jsonl","""{"id":"r","model":"x","started_at":"2026-10-20T01:00:00Z","input_tokens":100,"output_tokens":20,"cached_tokens":30,"total_tokens":150,"bank":"project"}""");var report=await new SpendService().ScanAsync(Config("hindsight"),home);Assert.Equal(150L,report.TotalTokens);Assert.Equal(100L,report.Records[0].Input);Assert.Equal("project",report.Records[0].Project);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task AmpLedgerWinsMatchedMessagesAndUnmatchedCallsUseThreadAnchor()
    {
        var home=Temp();try{Write(home,".local/share/amp/threads/T-s.json","""{"id":"s","created":1717200000000,"messages":[{"role":"assistant","messageId":1,"usage":{"model":"x","inputTokens":100,"outputTokens":20}},{"role":"assistant","messageId":2,"usage":{"model":"x","inputTokens":10,"outputTokens":5}}],"usageLedger":{"events":[{"model":"x","toMessageId":1,"timestamp":"2026-10-20T01:00:00Z","tokens":{"input":100,"output":20}}]}}""");var report=await new SpendService().ScanAsync(Config("amp"),home);Assert.Equal(135L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.Equal(1,report.Records.Count(r=>r.IsAggregate));}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ClineCliMessagesUseManifestModelAndCacheExclusiveInput()
    {
        var home=Temp();try{Write(home,".cline/data/sessions/s/s.messages.json","""{"sessionId":"s","messages":[{"role":"assistant","id":"m","ts":1717200000000,"metrics":{"inputTokens":100,"outputTokens":20,"cacheReadTokens":30,"cacheWriteTokens":10}}]}""");Write(home,".cline/data/sessions/s/s.json","""{"model":"x","workspace_root":"C:/project"}""");var report=await new SpendService().ScanAsync(Config("cline"),home);Assert.Equal(120L,report.TotalTokens);Assert.Equal(60L,report.Records[0].Input);Assert.Equal("C:/project",report.Records[0].Project);Assert.Equal("x",report.Records[0].Model);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task OpenCodeReviewNeverInventsCacheRelationOrDisagreesWithReportedTotal()
    {
        var home=Temp();try{Write(home,".opencodereview/sessions/s.jsonl","""
{"type":"session_start","sessionId":"s","model":"x","cwd":"C:/project"}
{"type":"llm_response","uuid":"unproven","timestamp":1717200000000,"usage":{"prompt_tokens":100,"completion_tokens":10,"cache_read_tokens":20}}
{"type":"llm_response","uuid":"reported","timestamp":1717200000000,"duration_ms":500,"usage":{"prompt_tokens":100,"completion_tokens":10,"cache_read_tokens":20,"total_tokens":80}}
""");var report=await new SpendService().ScanAsync(Config("opencodereview"),home);Assert.Equal(1,report.Records.Count);Assert.Equal(80L,report.TotalTokens);Assert.Equal(80L,report.Records[0].Unclassified);Assert.True(report.Records[0].IsPartial);Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1717200000000).AddMilliseconds(-500),report.Records[0].At);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task CursorAndTraeReplayByMeaningAndKeepAccountScopesSeparate()
    {
        var home=Temp();try
        {
            var first="""{"conversationId":"s","timestamp":1717200000000,"model":"x","chargedCents":1,"tokenUsage":{"inputTokens":10,"outputTokens":5,"cacheReadTokens":3,"cacheWriteTokens":2}}""";
            var reordered="""{"model":"x","timestamp":1717200000000,"tokenUsage":{"cacheWriteTokens":2,"cacheReadTokens":3,"outputTokens":5,"inputTokens":10},"conversationId":"s","chargedCents":99}""";
            Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/usage.one.json","{\"usageEventsDisplay\":["+first+","+first+"]}");
            Write(home,".config/tokscale/cursor-cache/usage.one.json","{\"usageEventsDisplay\":["+reordered+"]}");
            Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/usage.two.json","{\"usageEventsDisplay\":["+first+"]}");
            var report=await new SpendService().ScanAsync(Config("cursor"),home);Assert.Equal(3,report.Records.Count);Assert.Equal(60L,report.TotalTokens);Assert.Equal(2,report.Records.Select(r=>r.Session).Distinct().Count());Assert.True(report.Records.All(r=>r.ReportedCost is null));
            var trae="""{"session_id":"s","usage_time":1717200000,"model_name":"x","extra_info":{"input_token":10,"output_token":5,"cache_read_token":3,"cache_write_token":2}}""";
            Write(home,"AppData/Roaming/Pulse/UsageImports/trae/first.json","["+trae+","+trae+"]");
            Write(home,"AppData/Roaming/Pulse/UsageImports/trae/second.json","["+trae.Replace("\"model_name\":\"x\"","\"cost\":99,\"model_name\":\"x\"")+"]");
            report=await new SpendService().ScanAsync(Config("trae"),home);Assert.Equal(2,report.Records.Count);Assert.Equal(40L,report.TotalTokens);Assert.True(report.Records.All(r=>r.IsPartial));
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task CursorCsvOverlapIsScopedToItsOwnAccount()
    {
        var home=Temp();try
        {
            Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/usage.one.json","""{"usageEventsDisplay":[{"conversationId":"s","timestamp":1717200000000,"model":"x","tokenUsage":{"inputTokens":10}}]}""");
            var csv="Date,Model,Input (w/o Cache Write),Input (w/ Cache Write),Cache Read,Output Tokens\n2024-06-01T00:00:00Z,x,20,0,0,0\n";
            Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/usage.two.csv",csv);
            Assert.Equal(30L,(await new SpendService().ScanAsync(Config("cursor"),home)).TotalTokens);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task FixedUsageFileNamesRetainTheirParentSession()
    {
        var home=Temp();try
        {
            foreach(var session in new[]{"one","two"})
            {
                Write(home,$".mux/sessions/{session}/session-usage.json","""{"byModel":{"p:x":{"input":{"tokens":10}}},"lastRequest":{"timestamp":1717200000000}}""");
                Write(home,$".fx/sessions/{session}/usage-v2.json","""{"snapshot":{"models":[{"model":"x","input_tokens":10}]}}""");Write(home,$".fx/sessions/{session}/session.json","""{"updated_at_ms":1717200000000}""");
                Write(home,$".junie/sessions/{session}/events.jsonl","""{"timestampMs":1717200000000,"event":{"agentEvent":{"kind":"LlmResponseMetadataEvent","modelUsage":[{"model":"x","inputTokens":10}]}}}""");
            }
            foreach(var agent in new[]{"mux","fx","junie"}){var report=await new SpendService().ScanAsync(Config(agent),home);Assert.Equal(20L,report.TotalTokens);Assert.Equal(2,report.Records.Select(r=>r.Session).Distinct().Count());}
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task AugmentScopesSequenceAndUsesLastNonemptyUsage()
    {
        var home=Temp();try
        {
            foreach(var session in new[]{"one","two"})Write(home,$".augment/sessions/{session}.json",JsonSerializer.Serialize(new{sessionId=session,agentState=new{modelId="x"},chatHistory=new[]{new{completed=true,sequenceId=1,finishedAt="2026-10-20T01:00:00Z",exchange=new{response_nodes=new[]{new{token_usage=new{input_tokens=10}},new{token_usage=new{input_tokens=0}}}}}}}));
            var report=await new SpendService().ScanAsync(Config("augment"),home);Assert.Equal(20L,report.TotalTokens);Assert.Equal(2,report.Records.Count);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task JunieUsesSessionTimeWhenEventTimeIsUnset()
    {
        var home=Temp();try{Write(home,".junie/sessions/session-261020-120000/events.jsonl","""{"timestampMs":0,"event":{"agentEvent":{"kind":"LlmResponseMetadataEvent","modelUsage":[{"model":"x","inputTokens":10,"outputTokens":5,"reasoningOutputTokens":4}]}}}""");var report=await new SpendService().ScanAsync(Config("junie"),home);Assert.Equal(15L,report.TotalTokens);Assert.Equal(2026,report.Records[0].At.Year);Assert.True(report.Records[0].IsPartial);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ScanHonorsAnExplicitPathEnvironmentWithoutReadingDefaultSources()
    {
        var home=Temp();try
        {
            Write(home,".gemini/tmp/p/chats/default.json","""{"sessionId":"default","messages":[{"type":"gemini","id":"d","timestamp":"2026-10-20T01:00:00Z","model":"x","tokens":{"prompt":999}}]}""");
            Write(home,"custom-gemini/tmp/p/chats/session.json","""{"sessionId":"s","messages":[{"type":"gemini","id":"m","timestamp":"2026-10-20T01:00:00Z","model":"x","tokens":{"prompt":10}}]}""");
            var environment=new Dictionary<string,string>{{"GEMINI_CLI_HOME",Path.Combine(home,"custom-gemini")}};
            Assert.Equal(10L,(await new SpendService().ScanAsync(Config("gemini"),home,environment)).TotalTokens);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static void DocumentedEnvironmentPathsResolveAgainstTheSuppliedHome()
    {
        var home=Temp();try
        {
            foreach(var (agent,key,suffix) in new[]{("hermes","HERMES_HOME","state.db"),("unsloth","UNSLOTH_STUDIO_HOME","studio.db"),("jcode","JCODE_HOME","sessions"),("dsh","DSH_HOME","sessions"),("lmstudio","LM_STUDIO_HOME","server-logs"),("reasonix","REASONIX_STATE_HOME",""),("senpi","SENPI_CODING_AGENT_DIR","sessions"),("kimchi","KIMCHI_CODING_AGENT_DIR","sessions"),("prime-agent","PRIME_AGENT_SESSION_DIR",""),("cline","CLINE_SESSION_DATA_DIR",""),("codebuff","CODEBUFF_DATA_DIR",""),("hindsight","HINDSIGHT_HOME","usage")})
            {
                var paths=SpendSources.ResolvePaths(SpendSources.All.Single(s=>s.Id==agent),home,new Dictionary<string,string>{{key,"~/custom"}});Assert.True(paths.Contains(Path.Combine(home,"custom",suffix)),agent+" did not honor "+key);
            }
            var custom=Path.Combine(home,"roaming");Assert.True(SpendSources.ResolvePaths(SpendSources.All.Single(s=>s.Id=="copilot"),home,new Dictionary<string,string>{{"APPDATA",custom}}).Any(p=>p.StartsWith(custom,StringComparison.Ordinal)));
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task VscodeTasksKeepCacheBucketsAndResolveTheirActualModel()
    {
        var home=Temp();try
        {
            foreach(var (agent,extension) in new[]{("roocode","rooveterinaryinc.roo-cline"),("kilocode","kilocode.kilo-code"),("cline","saoudrizwan.claude-dev")})
            {
                var path=$"AppData/Roaming/Code/User/globalStorage/{extension}/tasks/s";
                Write(home,path+"/ui_messages.json","""[{"type":"say","say":"api_req_started","ts":1717200000000,"text":"{\"tokensIn\":10,\"tokensOut\":5,\"cacheReads\":3,\"cacheWrites\":2}"}]""");Write(home,path+"/api_conversation_history.json","""[{"content":"<environment_details><model>x</model></environment_details>"}]""");
                var report=await new SpendService().ScanAsync(Config(agent),home);Assert.Equal(20L,report.TotalTokens);Assert.Equal("x",report.Records[0].Model);Assert.Equal(3L,report.Records[0].CacheRead);Assert.Equal("s",report.Records[0].Session);
            }
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ZCodeTranscriptUsesItsReportedTotalForReasoningAndCache()
    {
        var home=Temp();try
        {
            Write(home,".zcode/projects/p/s.jsonl","""
{"sessionId":"s","timestamp":1717200000000,"model":"provider/x","usage":{"input_tokens":100,"output_tokens":20,"cache_read_tokens":30,"reasoning_tokens":5,"total_tokens":155}}
{"sessionId":"s","timestamp":1717200001000,"model":"provider/x","usage":{"input_tokens":100,"output_tokens":20,"cache_read_tokens":30,"reasoning_tokens":5,"total_tokens":120}}
""");var report=await new SpendService().ScanAsync(Config("zcode"),home);Assert.Equal(275L,report.TotalTokens);Assert.Equal("x",report.Records[0].Model);Assert.Equal(25L,report.Records[0].Output);Assert.Equal(70L,report.Records[1].Input);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task CommandCodeResolvesModelsAlongEachReplysOwnBranch()
    {
        var home=Temp();try
        {
            Write(home,".commandcode/projects/p/s.jsonl","""
{"type":"session","id":"s"}
{"type":"model_change","id":"base","model":"provider/first"}
{"type":"message","id":"a","parentId":"base","role":"assistant","timestamp":"2026-10-20T01:00:00Z","usage":{"inputTokens":10}}
{"type":"model_change","id":"changed","parentId":"a","model":"provider/second"}
{"type":"message","id":"b","parentId":"changed","role":"assistant","timestamp":"2026-10-20T01:00:01Z","usage":{"inputTokens":10}}
{"type":"message","id":"fork","parentId":"a","role":"assistant","timestamp":"2026-10-20T01:00:02Z","usage":{"inputTokens":10}}
""");var report=await new SpendService().ScanAsync(Config("commandcode"),home);Assert.Equal(30L,report.TotalTokens);Assert.Equal("first",report.Records[2].Model);Assert.Equal("second",report.Records[1].Model);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task GrokAndKimiKeepEachSessionAndModelIndependent()
    {
        var home=Temp();try
        {
            foreach(var session in new[]{"one","two"})
            {
                Write(home,$".grok/sessions/p/{session}/updates.jsonl","""{"id":"same","timestamp":"2026-10-20T01:00:00Z","params":{"update":{"sessionUpdate":"turn_completed","usage":{"modelUsage":{"x":{"input_tokens":10},"y":{"input_tokens":20}}}}}}""");
                Write(home,$".kimi/sessions/p/{session}/wire.jsonl","""{"timestamp":"2026-10-20T01:00:00Z","message":{"payload":{"token_usage":{"input_other":10,"output":5}}}}""");
            }
            Assert.Equal(60L,(await new SpendService().ScanAsync(Config("grok"),home)).TotalTokens);Assert.Equal(30L,(await new SpendService().ScanAsync(Config("kimi"),home)).TotalTokens);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task GjcScopesEntryIdsAndFoldsOnlyByteIdenticalMirrors()
    {
        var home=Temp();try
        {
            foreach(var session in new[]{"one","two"})Write(home,$".gjc/agent/sessions/{session}.jsonl",JsonSerializer.Serialize(new{type="session",id=session})+"\n"+"""{"type":"message","id":"entry","timestamp":"2026-10-20T02:00:00Z","message":{"role":"assistant","model":"x","timestamp":1792458000000,"usage":{"input":10}}}""");
            var text="""
{"type":"session","id":"three"}
{"type":"message","message":{"role":"assistant","model":"x","timestamp":1792458000000,"usage":{"input":5}}}
""";Write(home,".gjc/agent/sessions/c.jsonl",text);Write(home,".gjc/agent/sessions/copy/c.jsonl",text);var report=await new SpendService().ScanAsync(Config("gjc"),home);Assert.Equal(25L,report.TotalTokens);Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792458000000),report.Records[0].At);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ReasonixUsesRequestsAsAlternateValidityAndKeepsMeasuredSplit()
    {
        var home=Temp();try{Write(home,".reasonix/stats/2026-10-20.jsonl","""
{"ts":1792458000,"model":"p/x","total":0,"requests":1,"prompt":100,"completion":20,"cache_hit":30,"reasoning":5}
{"ts":1792458001,"model":"p/x","total":999,"requests":1,"prompt":100,"completion":20,"cache_hit":30}
{"ts":1792458002,"total":100,"requests":1,"prompt":100}
""");var report=await new SpendService().ScanAsync(Config("reasonix"),home);Assert.Equal(240L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.Equal(0L,report.Records.Sum(r=>r.Unclassified));}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task KiroDatesMeasuredTurnFromItsPromptSidecar()
    {
        var home=Temp();try{Write(home,".kiro/sessions/cli/s.json","""{"session_id":"s","session_state":{"conversation_metadata":{"user_turn_metadatas":[{"input_token_count":10,"output_token_count":5,"message_ids":["m"]}]}}}""");Write(home,".kiro/sessions/cli/s.jsonl","""{"kind":"Prompt","data":{"message_id":"m","meta":{"timestamp":1792458000}}}""");var report=await new SpendService().ScanAsync(Config("kiro"),home);Assert.Equal(15L,report.TotalTokens);Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792458000),report.Records[0].At);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task LmStudioClampsCacheWithinPromptAndCountsWritesOnce()
    {
        var home=Temp();try{Write(home,".lmstudio/server-logs/s.log","""
2026-10-20 12:00:00 {"id":"one","model":"x","usage":{"prompt_tokens":100,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":200},"total_tokens":120}}
2026-10-20 12:01:00 {"id":"two","model":"x","usage":{"prompt_tokens":100,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":30,"cache_creation_input_tokens":10},"total_tokens":120}}
""");var report=await new SpendService().ScanAsync(Config("lmstudio"),home);Assert.Equal(240L,report.TotalTokens);Assert.Equal(60L,report.Records.Single(r=>r.Identity=="two").Input);Assert.True(report.Records.All(r=>r.EstimatedCost is null));}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task CherryUsesRequestIdentityLatestTimestampAndV2SessionPriority()
    {
        var home=Temp();try{var data="""
{"type":"assistant","requestId":"r","timestamp":"2026-10-20T01:00:00Z","message":{"id":"one","model":"x","usage":{"input_tokens":10,"output_tokens":1}}}
{"type":"assistant","requestId":"r","timestamp":"2026-10-20T01:01:00Z","message":{"id":"two","model":"x","usage":{"input_tokens":10,"output_tokens":5}}}
""";Write(home,"AppData/Roaming/CherryStudio/Data/Agents/.claude/projects/p/s.jsonl",data);Write(home,"AppData/Roaming/CherryStudio/.claude/projects/p/s.jsonl",data.Replace("\"requestId\":\"r\"","\"requestId\":\"old\""));var report=await new SpendService().ScanAsync(Config("cherrystudio"),home);Assert.Equal(15L,report.TotalTokens);Assert.Equal(1,report.Records.Count);Assert.Equal(DateTimeOffset.Parse("2026-10-20T01:01:00Z"),report.Records[0].At);}
        finally{Directory.Delete(home,true);}
    }
}
