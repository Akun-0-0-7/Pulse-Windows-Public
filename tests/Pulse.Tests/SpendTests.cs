using System.Text.Json;
using System.Runtime.InteropServices;
using Pulse.Core;
namespace Pulse.Tests;

public static class SpendTests
{
    static string Temp() { var p = Path.Combine(Path.GetTempPath(), "pulse-spend-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    static void Write(string home, string path, string contents) { var p=Path.Combine(home,path); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p,contents); }
    [Check] public static async Task OptInDoesNotReadMalformedSource()
    {
        var home=Temp(); try { Write(home,".claude/projects/a.jsonl","{bad"); var r=await new SpendService().ScanAsync(new AppConfig(),home); Assert.Equal(0,r.Records.Count); Assert.Equal(0,r.Notes.Count); } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task ClaudeReplayKeepsMaximumAndHourCachePrice()
    {
        var home=Temp(); try {
            var line="""{"type":"assistant","timestamp":"2026-10-20T01:00:00Z","sessionId":"s","message":{"id":"m","model":"fixture-model","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":200,"cache_creation_input_tokens":300,"cache_creation":{"ephemeral_1h_input_tokens":200}}}}"""+"\n";
            Write(home,".claude/projects/a.jsonl",line+line); Write(home,".claude/projects/fork.jsonl",line);
            var r=await new SpendService(new Dictionary<string,SpendPrice>{{"fixture-model",new(3m,15m,.3m,3.75m)}}).ScanAsync(new AppConfig{SpendEnabled=true},home);
            Assert.Equal(1,r.Records.Count); Assert.Equal(620L,r.TotalTokens); Assert.Near(.002235,(double)r.EstimatedCost!.Value); Assert.Equal(620L,r.ByAgent["claude"].TotalTokens);
        } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task CodexCumulativeSnapshotsAreDifferencedAndCacheIsSubset()
    {
        var home=Temp(); try {
            Write(home,".codex/sessions/a.jsonl","""
{"type":"session_meta","payload":{"id":"s"}}
{"type":"turn_context","payload":{"model":"fixture-model"}}
{"timestamp":"2026-10-19T01:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":50,"output_tokens":20}}}}
{"timestamp":"2026-10-20T01:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":140,"cached_input_tokens":60,"output_tokens":30}}}}
{"timestamp":"2026-10-20T01:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":140,"cached_input_tokens":60,"output_tokens":30}}}}
""");
            var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home); Assert.Equal(170L,r.TotalTokens); Assert.Equal(50L,r.InRange(DateTimeOffset.Parse("2026-10-20T00:00:00Z"),DateTimeOffset.Parse("2026-10-21T00:00:00Z")).TotalTokens); Assert.Equal(60L,r.Records.Sum(x=>x.CacheRead));
        } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task PiForkDeduplicatesByResponseIdentity()
    {
        var home=Temp(); try { var s="""
{"type":"session","id":"s","timestamp":"2026-10-20T01:00:00Z"}
{"type":"message","timestamp":"2026-10-20T01:01:00Z","message":{"id":"response","role":"assistant","model":"x","usage":{"input":100,"output":20,"cacheRead":40,"cacheWrite":10}}}
"""; Write(home,".pi/agent/sessions/a.jsonl",s); Write(home,".pi/agent/sessions/b.jsonl",s); var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home); Assert.Equal(170L,r.TotalTokens); Assert.Equal(1,r.Records.Count); Assert.True(r.EstimatedCost is null); } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task WarpMoneyDoesNotBecomeTokenZero()
    {
        var home=Temp(); try { Write(home,"AppData/Roaming/Pulse/UsageImports/warp/usage.json","""{"requestsUsed":3,"spendCents":125,"date":"2026-10-20"}"""); var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home); Assert.Equal(1,r.Records.Count); Assert.True(r.TotalTokens is null); Assert.Near(1.25,(double)r.ReportedCost!.Value); } finally { Directory.Delete(home,true); }
    }
    [Check] public static void ExtensionRejectsInvalidAndDoesNotInventLimits()
    {
        var r=ExtensionService.Parse("acme","""{"schemaVersion":1,"limits":[{"label":"missing"},{"label":"negative","usedPercent":-1}],"metrics":[{"label":"speed","value":12}]}"""); Assert.Equal("noLimits",r.ErrorCode); Assert.Equal(0,r.Windows.Count);
        r=ExtensionService.Parse("acme","""{"schemaVersion":1,"limits":[{"label":"calls","used":11,"limit":10}],"balance":{"amount":-1,"currency":"usd"}}"""); Assert.Near(110,r.Windows[0].UsedPercent); Assert.True(r.Windows[0].Exhausted); Assert.Equal("USD",r.Balance!.Currency);
    }
    [Check] public static void ExtensionMetricsSortDeduplicateAndBound()
    {
        var r=ExtensionService.Parse("acme","""{"schemaVersion":1,"balance":{"amount":2,"currency":"CNY"},"metrics":[{"id":"x","label":"Speed","value":2,"history":[{"t":"2026-10-20T02:00:00Z","v":2},{"t":"2026-10-20T01:00:00Z","v":1},{"t":"2026-10-20T01:00:00Z","v":99}]},{"id":"x","label":"duplicate","value":4}]}"""); Assert.Equal(1,r.Metrics.Count); Assert.Equal(2,r.Metrics[0].History!.Count); Assert.Near(1,r.Metrics[0].History![0].V);
    }
    [Check] public static void ExtensionManifestRejectsEscapedExecutable()
    {
        var home=Temp(); try { Write(home,"evil/pulse-extension.json","""{"schemaVersion":1,"id":"evil","name":"Evil","executable":"../outside.exe"}"""); Write(home,"outside.exe",""); var found=new ExtensionService().Discover(home); Assert.Equal(1,found.Count); Assert.True(found[0].Error is not null); } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task ExtensionDisabledCannotRun()
    {
        var r=await new ExtensionService().RunAsync(new ExtensionDefinition("acme","Acme","z:/missing.exe","z:/",20,null),new AppConfig()); Assert.Equal("disabled",r.ErrorCode);
    }
    [Check] public static void StatusDoesNotTurnUnknownIntoOperational()
    {
        var r=StatusService.Parse("claudeCode","""{"components":[{"id":"a","name":"API","status":"mysterious"}],"incidents":[{"id":"i","name":"Outage","status":"investigating","created_at":"2026-10-20T00:00:00Z"}]}"""); Assert.Equal("unrecognised",r.Components[0].State); Assert.Equal(1,r.Incidents.Count);
    }
    [Check] public static void StatusIncidentIoFiltersCodexGroup()
    {
        var r=StatusService.Parse("codex","""{"summary":{"affected_components":[{"component_id":"c","status":"full_outage"}],"structure":{"items":[{"group":{"name":"Codex","components":[{"component_id":"c","name":"CLI"}]}},{"group":{"name":"APIs","components":[{"component_id":"a","name":"API"}]}}]}}}"""); Assert.Equal(1,r.Components.Count); Assert.Equal("fullOutage",r.Components[0].State); Assert.True(r.HasOutage);
    }
    [Check] public static void StatusBetterStackKeepsMissingDayUnknown()
    {
        var r=StatusService.Parse("huggingFace","""{"included":[{"id":"1","type":"status_page_resource","attributes":{"public_name":"API","status":"operational","availability":0.995,"status_history":[{"day":"2026-10-19","status":"not_monitored"},{"day":"2026-10-20","status":"downtime"}]}}]}"""); Assert.Near(99.5,r.Components[0].Uptime!.Value); Assert.True(r.Components[0].History[0].State is null); Assert.Equal("fullOutage",r.Components[0].History[1].State);
    }
    [Check] public static void CataloguesRetainEvidenceLevels()
    {
        Assert.Equal(54,SpendSources.All.Count); Assert.Equal(7,SpendSources.All.Count(x=>x.UpstreamEvidence=="machine")); Assert.Equal(44,StatusService.Pages.Count); Assert.True(SpendSources.All.Single(x=>x.Id=="warp").ReportsTokens==false);
    }
    [Check] public static async Task CursorEqualCallsStayTwoButCopiedExportsDoNotDouble()
    {
        var home=Temp(); try { var row="""{"conversationId":"c","timestamp":1717200000000,"model":"x","tokenUsage":{"inputTokens":100,"outputTokens":20,"cacheReadTokens":30,"cacheWriteTokens":40}}"""; var a="{\"usageEventsDisplay\":["+row+","+row+"]}"; Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/a.json",a);Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/copy.json",a);var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(2,r.Records.Count);Assert.Equal(380L,r.TotalTokens);Assert.True(r.Records.All(x=>x.IsPartial)); } finally { Directory.Delete(home,true); }
    }
    [Check] public static async Task TraeReadsActualExportExtraInfo()
    {
        var home=Temp();try{Write(home,"AppData/Roaming/Pulse/UsageImports/trae/page.json","""[{"session_id":"s","usage_time":1717200000,"model_name":"x","extra_info":{"input_token":100,"output_token":20,"cache_read_token":30,"cache_write_token":40}}]""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(190L,r.TotalTokens);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task PiRequiresSessionHeader()
    {
        var home=Temp();try{Write(home,".pi/agent/sessions/no-header.jsonl","""{"type":"message","id":"m","timestamp":"2026-10-20T01:00:00Z","message":{"role":"assistant","model":"x","usage":{"input":10}}}""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(0,r.Records.Count);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task AugmentUsesFinalCumulativeNodeOnly()
    {
        var home=Temp();try{Write(home,".augment/sessions/s.json","""{"sessionId":"s","agentState":{"modelId":"x"},"chatHistory":[{"completed":true,"sequenceId":"1","finishedAt":"2026-10-20T01:00:00Z","exchange":{"request_id":"r","response_nodes":[{"token_usage":{"input_tokens":10,"output_tokens":1}},{"token_usage":{"input_tokens":10,"output_tokens":5}}]}},{"completed":false,"finishedAt":"2026-10-20T01:01:00Z","exchange":{"response_nodes":[{"token_usage":{"input_tokens":100}}]}}]}""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(15L,r.TotalTokens);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task MuxAndFxDoNotDoubleAddReasoning()
    {
        var home=Temp();try{Write(home,".mux/sessions/s/session-usage.json","""{"byModel":{"p:x":{"input":{"tokens":10},"cached":{"tokens":20},"cacheCreate":{"tokens":5},"output":{"tokens":30},"reasoning":{"tokens":7}}},"lastRequest":{"timestamp":1717200000000}}""");Write(home,".fx/sessions/f/usage-v2.json","""{"snapshot":{"models":[{"model":"y","input_tokens":10,"output_tokens":20,"reasoning_tokens":5}]}}""");Write(home,".fx/sessions/f/session.json","""{"updated_at_ms":1717200000000}""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(95L,r.TotalTokens);Assert.True(r.Records.All(x=>x.IsAggregate&&x.IsPartial));}finally{Directory.Delete(home,true);}
    }
    [Check] public static void ExtensionRefusesWrongSchemaAndOversizeUtf8()
    {
        Assert.Equal("invalidReply",ExtensionService.Parse("x","""{"schemaVersion":2,"balance":{"amount":1,"currency":"CNY"}}""").ErrorCode);
        Assert.Equal("invalidReply",ExtensionService.Parse("x","{\"schemaVersion\":1,\"x\":\""+new string('中',100000)+"\"}").ErrorCode);
    }
    [Check] public static void StatusFlashcatDecodesNextFlightAndFiltersHidden()
    {
        var payload="1:[\"$\",\"div\",null,{\"initialData\":{\"page\":{\"components\":[{\"component_id\":\"api\",\"name\":\"API\",\"status\":\"degraded\"},{\"component_id\":\"hidden\",\"name\":\"Hidden\",\"hide_all\":true}]},\"component_impacts\":[],\"component_uptimes\":[]}}]\n";
        var html="<script>self.__next_f.push([1,"+JsonSerializer.Serialize(payload)+"])</script>";var r=StatusService.Parse("deepSeek",html);Assert.Equal(1,r.Components.Count);Assert.Equal("degraded",r.Components[0].State);Assert.Equal(90,r.Components[0].History.Count);
    }
    [Check] public static void StatusFeedFamiliesReadTheirActualShapes()
    {
        Assert.Equal("degraded",StatusService.Parse("clinePass","""[{"key":"api","name":"API","results":[{"success":true,"status":200},{"success":false,"status":200}]}]""").Components[0].State);
        Assert.Equal("fullOutage",StatusService.Parse("commandCode","""[{"slug":"api","name":"API","status":"down"}]""").Components[0].State);
        Assert.Equal("fullOutage",StatusService.Parse("deepInfra","""{"services":[{"publicId":"api","displayName":"API","status":"OUTAGE"}]}""").Components[0].State);
        Assert.Equal("fullOutage",StatusService.Parse("ibmBob","""{"regions":[{"services":[{"name":"API","status":"operational"}]},{"services":[{"name":"API","status":"outage"}]}]}""").Components[0].State);
        Assert.Equal(56,StatusService.Parse("openCodeGo","""{"components":[{"service":"go","status":"operational"}],"history":[]}""").Components[0].History.Count);
    }
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path,out IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern void sqlite3_free(IntPtr p);
    static void Database(string home,string relative,string sql)
    {
        var path=Path.Combine(home,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);Assert.Equal(0,sqlite3_open(path,out var db));
        try { var code=sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var error);var message=error==IntPtr.Zero?"":Marshal.PtrToStringUTF8(error);if(error!=IntPtr.Zero)sqlite3_free(error);Assert.True(code==0,message??"SQLite fixture error"); }finally{sqlite3_close(db);}
    }
    [Check] public static async Task SqliteOpenCodeV2MigrationCountsOneResponse()
    {
        var home=Temp();try{Database(home,".local/share/opencode/opencode.db","""
CREATE TABLE session_message(id TEXT, session_id TEXT,type TEXT,data TEXT);
CREATE TABLE message(id TEXT,session_id TEXT,data TEXT);
INSERT INTO session_message VALUES('m','s','assistant','{"model":{"id":"x"},"time":{"created":1717200000000},"tokens":{"input":100,"output":20,"reasoning":5,"cache":{"read":30,"write":10},"total":165}}');
INSERT INTO message VALUES('m','s','{"role":"assistant","modelID":"x","time":{"created":1717200000000},"tokens":{"input":100,"output":20,"cache":{"read":30,"write":10}}}');
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(1,r.Records.Count);Assert.Equal(165L,r.TotalTokens);Assert.Equal(25L,r.Records[0].Output);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task SqliteHermesCacheInputIsUnclassified()
    {
        var home=Temp();try{Database(home,".hermes/state.db","""
CREATE TABLE sessions(id TEXT,model TEXT,started_at INTEGER,input_tokens INTEGER,output_tokens INTEGER,cache_read_tokens INTEGER,cache_write_tokens INTEGER,reasoning_tokens INTEGER);
INSERT INTO sessions VALUES('s','x',1717200000,100,20,30,0,5);
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(120L,r.TotalTokens);Assert.Equal(100L,r.Records[0].Unclassified);Assert.True(r.Records[0].IsPartial&&r.Records[0].IsAggregate);Assert.Equal(0L,r.Records[0].CacheRead);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task SqliteGoosePrefersAccumulatedAndKeepsRemainder()
    {
        var home=Temp();try{Database(home,"AppData/Roaming/goose/sessions/sessions.db","""
CREATE TABLE sessions(id TEXT,model_config_json TEXT,created_at TEXT,input_tokens INTEGER,output_tokens INTEGER,total_tokens INTEGER,accumulated_input_tokens INTEGER,accumulated_output_tokens INTEGER,accumulated_total_tokens INTEGER);
INSERT INTO sessions VALUES('s','{"model_name":"x"}','2026-10-20 01:00:00',1,1,2,100,20,140);
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(140L,r.TotalTokens);Assert.Equal(20L,r.Records[0].Unclassified);Assert.Equal(100L,r.Records[0].Input);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task SqliteDevinSiblingNodesDeduplicateByMessageId()
    {
        var home=Temp();try{Database(home,".local/share/devin/cli/sessions.db","""
CREATE TABLE message_nodes(session_id TEXT,chat_message TEXT,created_at INTEGER);
INSERT INTO message_nodes VALUES('s','{"role":"assistant","message_id":"m","metadata":{"generation_model":"x","created_at":1717200000,"metrics":{"input_tokens":100,"output_tokens":20,"cache_read_tokens":30,"cache_creation_tokens":10}}}',1717200000);
INSERT INTO message_nodes SELECT * FROM message_nodes;
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(160L,r.TotalTokens);Assert.Equal(1,r.Records.Count);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task CursorCsvKeepsQuotedCommasAndRealBuckets()
    {
        var home=Temp();try{Write(home,"AppData/Roaming/Pulse/UsageImports/cursor/usage.csv","Date,Cloud Agent ID,Model,Input (w/ Cache Write),Input (w/o Cache Write),Cache Read,Output Tokens\r\n2024-06-02T12:00:00Z,cloud-9,\"Model, mini\",\"1,000\",250,300,50\r\n");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(1600L,r.TotalTokens);Assert.Equal(250L,r.Records[0].Input);Assert.Equal(1000L,r.Records[0].CacheWrite);Assert.Equal("Model, mini",r.Records[0].Model);Assert.True(r.Records[0].IsAggregate);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task CopilotOtelHigherLaneWinsAndCacheIsWithinInput()
    {
        var home=Temp();try{Write(home,".copilot/otel/usage.jsonl","""
{"type":"log","timestamp":"2026-10-20T01:00:00Z","traceId":"t","attributes":{"event.name":"gen_ai.client.inference.operation.details","gen_ai.usage.input_tokens":100,"gen_ai.usage.output_tokens":20}}
{"type":"span","name":"chat model","startTime":"2026-10-20T01:00:00Z","traceId":"t","attributes":{"gen_ai.operation.name":"chat","gen_ai.request.model":"x","gen_ai.conversation.id":"s","gen_ai.usage.input_tokens":100,"gen_ai.usage.output_tokens":20,"gen_ai.usage.cache_read.input_tokens":30}}
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(1,r.Records.Count);Assert.Equal(120L,r.TotalTokens);Assert.Equal(70L,r.Records[0].Input);Assert.Equal(30L,r.Records[0].CacheRead);}finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task CopilotVsCodeReplaysPatchBeforeCounting()
    {
        var home=Temp();try{Write(home,"AppData/Roaming/Code/User/workspaceStorage/w/chatSessions/s.jsonl","""
{"kind":0,"v":{"requests":[{"requestId":"r","timestamp":1717200000000,"modelId":"copilot/x","promptTokens":100,"result":{"metadata":{"outputTokens":5}}}]}}
{"kind":1,"k":["requests",0,"result","metadata","outputTokens"],"v":20}
""");var r=await new SpendService().ScanAsync(new AppConfig{SpendEnabled=true},home);Assert.Equal(120L,r.TotalTokens);Assert.Equal(100L,r.Records[0].Unclassified);Assert.Equal(20L,r.Records[0].Output);}finally{Directory.Delete(home,true);}
    }
}
