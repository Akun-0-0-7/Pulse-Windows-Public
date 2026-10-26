using System.Runtime.InteropServices;
using Pulse.Core;
namespace Pulse.Tests;

public static class SpendMissingTests
{
    private static string Temp(){var path=Path.Combine(Path.GetTempPath(),"pulse-missing-readers-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
    private static void Write(string home,string relative,string text){var path=Path.Combine(home,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text);}
    private static Task<SpendReport> Scan(string home,params string[] sources)=>new SpendService().ScanAsync(new AppConfig{SpendEnabled=true,EnabledSpendSources=sources.ToList()},home);
    [Check] public static async Task CodebuffMergesEachFieldAndRestoresChatTime()
    {
        var home=Temp();try
        {
            Write(home,".config/manicode/projects/project/chats/2026-10-20T01-02-03.000Z/chat-messages.json","""
[{"variant":"ai","id":"m","timestamp":0,"metadata":{"usage":{"inputTokens":0,"outputTokens":0,"credits":999},"codebuff":{"usage":{"inputTokens":1000,"cacheReadInputTokens":100}},"runState":{"sessionState":{"mainAgentState":{"messageHistory":[{"providerOptions":{"usage":{"outputTokens":20,"cacheCreationInputTokens":50},"codebuff":{"model":"fixture-codebuff"}}}]}}}}},
 {"role":"user","metadata":{"model":"ignored","usage":{"inputTokens":999999}}}]
""");
            var report=await Scan(home,"codebuff");Assert.Equal(1,report.Records.Count);var row=report.Records[0];Assert.Equal(900L,row.Input);Assert.Equal(20L,row.Output);Assert.Equal(100L,row.CacheRead);Assert.Equal(50L,row.CacheWrite);Assert.Equal(1070L,row.TotalTokens);Assert.Equal("fixture-codebuff",row.Model);Assert.Equal(DateTimeOffset.Parse("2026-10-20T01:02:03Z"),row.At);Assert.Equal("manicode/project/2026-10-20T01-02-03.000Z",row.Session);
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task CodebuffCopiesUsePriorityAndNestedDetails()
    {
        var home=Temp();try
        {
            Write(home,".config/manicode-dev/projects/p/chats/2026-10-20T01-02-03Z/chat-messages.json","""
[{"variant":"agent","createdAt":"2026-10-20T03:00:00Z","metadata":{"model":"primary-model","usage":{"inputTokens":500,"outputTokens":10,"prompt_tokens_details":{"cached_tokens":125}},"codebuff":{"usage":{"inputTokens":999,"outputTokens":999}},"runState":{"sessionState":{"mainAgentState":{"messageHistory":[{"providerOptions":{"codebuff":{"model":"secondary-model","usage":{"inputTokens":3000}}}}]}}}}},
 {"role":"assistant","metadata":{"usage":{"inputTokens":100,"credits":200}}},
 {"role":"assistant","metadata":{"model":"credits-only","usage":{"credits":200}}}]
""");
            var report=await Scan(home,"codebuff");var row=report.Records.Single();Assert.Equal(375L,row.Input);Assert.Equal(125L,row.CacheRead);Assert.Equal(10L,row.Output);Assert.Equal("primary-model",row.Model);Assert.Equal(DateTimeOffset.Parse("2026-10-20T03:00:00Z"),row.At);Assert.Equal("manicode-dev/p/2026-10-20T01-02-03Z",row.Session);
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task JcodeJournalReplaysOnceAndTracksAppendModel()
    {
        var home=Temp();try
        {
            Write(home,".jcode/sessions/session_a.json","""{"id":"session-a","model":"model-one","working_dir":"D:/fixture","messages":[{"id":"message-a","timestamp":"2026-10-20T01:00:00Z","token_usage":{"input_tokens":100,"output_tokens":10,"cache_creation_input_tokens":0}}]}""");
            Write(home,".jcode/sessions/session_a.journal.jsonl","""
{"append_messages":[{"timestamp":"2026-10-20T01:00:00Z","id":"message-a","token_usage":{"output_tokens":10,"input_tokens":100,"cache_creation_input_tokens":0}}]}
{"meta":{"model":"model-two"},"append_messages":[{"id":"message-b","timestamp":"2026-10-20T02:00:00Z","token_usage":{"input_tokens":200,"output_tokens":20,"cache_creation_input_tokens":0}}]}
""");
            var report=await Scan(home,"jcode");Assert.Equal(2,report.Records.Count);Assert.Equal(330L,report.TotalTokens);Assert.Equal("model-one",report.Records[0].Model);Assert.Equal("model-two",report.Records[1].Model);Assert.True(report.Records.All(r=>r.Project=="D:/fixture"));
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task JcodeAmbiguousCacheRemainsUnclassified()
    {
        var home=Temp();try
        {
            Write(home,".jcode/sessions/session_a.json","""{"id":"s","model":"fixture","messages":[{"id":"a","timestamp":"2026-10-20T01:00:00Z","token_usage":{"input_tokens":100,"cache_read_input_tokens":60,"output_tokens":20,"reasoning_output_tokens":4}}]}""");
            var report=await Scan(home,"jcode");var row=report.Records.Single();Assert.Equal(120L,row.TotalTokens);Assert.Equal(100L,row.Unclassified);Assert.Equal(0L,row.Input);Assert.Equal(0L,row.CacheRead);Assert.True(row.IsPartial);Assert.True(row.EstimatedCost is null);
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task JcodeAnonymousReplayUsesSemanticIdentityAndExplicitCacheShapes()
    {
        var home=Temp();try
        {
            Write(home,".jcode/sessions/session_b.json","""{"id":"s","model":"fixture","messages":[{"timestamp":"2026-10-20T01:00:00Z","token_usage":{"input_tokens":100,"cache_read_input_tokens":60,"cache_creation_input_tokens":0,"output_tokens":20}},{"id":"native","timestamp":"2026-10-20T02:00:00Z","token_usage":{"input_tokens":100,"cache_read_input_tokens":60,"input_tokens_details":{},"output_tokens":20}},{"id":"no-time","token_usage":{"input_tokens":100}},{"id":"no-usage","timestamp":"2026-10-20T02:00:00Z"}]}""");
            Write(home,".jcode/sessions/session_b.journal.jsonl","""
{"append_messages":[{"token_usage":{"output_tokens":20,"cache_creation_input_tokens":0,"cache_read_input_tokens":60,"input_tokens":100},"timestamp":"2026-10-20T01:00:00Z"}]}
{"unfinished":
{"append_messages":[{"id":"new","timestamp":"2026-10-20T03:00:00Z","token_usage":{"input_tokens":30,"output_tokens":5}}]}
""");
            var report=await Scan(home,"jcode");Assert.Equal(3,report.Records.Count);Assert.Equal(335L,report.TotalTokens);var native=report.Records.Single(r=>r.Identity.EndsWith(":native"));Assert.Equal(40L,native.Input);Assert.Equal(60L,native.CacheRead);Assert.True(!native.IsPartial);Assert.True(report.Notes.Any(n=>n.Contains("未写完")));
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task DevinCanonicalPromptOverwritesWhileOutputAccumulates()
    {
        var home=Temp();try
        {
            Write(home,"AppData/Roaming/Devin/User/acp-events/capture.ndjson","""
{"notification":{"sessionUpdate":"usage_update","created_at":"2026-10-20T01:00:00Z","notification_model":"adaptive","_meta":{"cognition.ai/inputTokens":100,"cognition.ai/cachedReadTokens":20,"cognition.ai/cachedWriteTokens":5,"cognition.ai/outputTokens":10}}}
{"notification":{"sessionUpdate":"usage_update","created_at":"2026-10-20T02:00:00Z","_meta":{"cognition.ai/inputTokens":150,"cognition.ai/cachedReadTokens":30,"cognition.ai/cachedWriteTokens":7,"cognition.ai/outputTokens":20,"cognition.ai/model":"fixture-devin"}}}
""");
            var report=await Scan(home,"devin-desktop");var row=report.Records.Single();Assert.Equal(120L,row.Input);Assert.Equal(30L,row.Output);Assert.Equal(30L,row.CacheRead);Assert.Equal(7L,row.CacheWrite);Assert.Equal(187L,row.TotalTokens);Assert.Equal("fixture-devin",row.Model);Assert.True(row.IsAggregate);
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task DevinLegacyUsesEventTimeAndDoesNotInventAdaptiveModel()
    {
        var home=Temp();try
        {
            Write(home,"AppData/Roaming/Devin/User/acp-events/old.ndjson","""
{"notification":{"metadata":{"metrics":{"input_tokens":100,"output_tokens":20},"created_at":"2026-10-20T01:00:00Z","generation_model":"adaptive"}}}
{"notification":{"metadata":{"metrics":{"input_tokens":200,"output_tokens":20}}}}
""");
            var report=await Scan(home,"devin-desktop");var row=report.Records.Single();Assert.Equal(120L,row.TotalTokens);Assert.Equal("unknown",row.Model);Assert.True(!row.IsAggregate);
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task DevinCanonicalZeroBucketsReplacePriorValues()
    {
        var home=Temp();try
        {
            Write(home,".config/devin/User/acp-events/zero.ndjson","""
{"notification":{"sessionUpdate":"usage_update","created_at":"2026-10-20T01:00:00Z","_meta":{"cognition.ai/inputTokens":100,"cognition.ai/cachedReadTokens":20,"cognition.ai/cachedWriteTokens":5,"cognition.ai/outputTokens":10}}}
{"unfinished":
{"notification":{"sessionUpdate":"usage_update","_meta":{"cognition.ai/inputTokens":50,"cognition.ai/cachedReadTokens":0,"cognition.ai/cachedWriteTokens":0,"cognition.ai/outputTokens":5}}}
""");
            var report=await Scan(home,"devin-desktop");var row=report.Records.Single();Assert.Equal(65L,row.TotalTokens);Assert.Equal(50L,row.Input);Assert.Equal(15L,row.Output);Assert.Equal(0L,row.CacheRead);Assert.Equal(0L,row.CacheWrite);Assert.Equal(DateTimeOffset.Parse("2026-10-20T01:00:00Z"),row.At);Assert.True(report.Notes.Any(n=>n.Contains("未写完")));
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task DevinAmbiguousTitleStaysUnresolvedAndLookupDoesNotChangeDatabase()
    {
        var home=Temp();try
        {
            var file=Path.Combine(home,".local/share/devin/cli/sessions.db");Directory.CreateDirectory(Path.GetDirectoryName(file)!);CreateDatabase(file,"""
CREATE TABLE sessions(id TEXT,title TEXT,model TEXT,working_directory TEXT);
INSERT INTO sessions VALUES('a','Same title','a-model','D:/a');
INSERT INTO sessions VALUES('b','Same title','b-model','D:/b');
""");
            var before=File.ReadAllBytes(file);
            Write(home,"AppData/Roaming/Devin/User/acp-events/capture.ndjson","""
{"notification":{"sessionUpdate":"session_info_update","title":"Same title"}}
{"notification":{"sessionUpdate":"usage_update","created_at":"2026-10-20T01:00:00Z","_meta":{"cognition.ai/inputTokens":25,"cognition.ai/outputTokens":5}}}
""");
            var report=await Scan(home,"devin-desktop","devin-cli");var row=report.Records.Single();Assert.Equal("capture",row.Session);Assert.Equal("unknown",row.Model);Assert.True(row.Project is null);Assert.True(before.SequenceEqual(File.ReadAllBytes(file)));
        }finally{Directory.Delete(home,true);}
    }
    [Check] public static async Task DevinTitleLookupExcludesDesktopOnlyWhenCliIsCounted()
    {
        var home=Temp();try
        {
            var file=Path.Combine(home,".local/share/devin/cli/sessions.db");Directory.CreateDirectory(Path.GetDirectoryName(file)!);CreateDatabase(file,"""
CREATE TABLE sessions(id TEXT,title TEXT,model TEXT,working_directory TEXT);
INSERT INTO sessions VALUES('session-1','Native task','resolved-model','D:/fixture');
CREATE TABLE message_nodes(session_id TEXT,chat_message TEXT,created_at TEXT);
INSERT INTO message_nodes VALUES('session-1','{"role":"assistant","message_id":"cli-message","metadata":{"generation_model":"resolved-model","created_at":"2026-10-20T01:00:00Z","metrics":{"input_tokens":100,"output_tokens":10}}}','2026-10-20T01:00:00Z');
""");
            Write(home,"AppData/Roaming/Devin/User/acp-events/uuid.ndjson","""
{"notification":{"sessionUpdate":"session_info_update","title":"Native task"}}
{"notification":{"sessionUpdate":"usage_update","created_at":"2026-10-20T01:00:00Z","_meta":{"cognition.ai/inputTokens":100,"cognition.ai/outputTokens":10}}}
""");
            var desktop=await Scan(home,"devin-desktop");Assert.Equal(1,desktop.Records.Count);Assert.Equal("session-1",desktop.Records[0].Session);Assert.Equal("resolved-model",desktop.Records[0].Model);Assert.Equal("D:/fixture",desktop.Records[0].Project);
            var both=await Scan(home,"devin-desktop","devin-cli");Assert.Equal(1,both.Records.Count);Assert.Equal("devin-cli",both.Records[0].Agent);
        }finally{Directory.Delete(home,true);}
    }
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern void sqlite3_free(IntPtr value);
    private static void CreateDatabase(string file,string sql){Assert.Equal(0,sqlite3_open(file,out var db));try{var code=sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var error);try{if(code!=0)throw new Exception(Marshal.PtrToStringUTF8(error));}finally{if(error!=IntPtr.Zero)sqlite3_free(error);}}finally{sqlite3_close(db);}}
}
