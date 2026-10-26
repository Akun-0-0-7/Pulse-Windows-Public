using System.Runtime.InteropServices;
using System.Text;
using Pulse.Core;
namespace Pulse.Tests;
public static class SpendAdvancedTests
{
    static string Temp(){var p=Path.Combine(Path.GetTempPath(),"pulse-spend-advanced-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static void Write(string home,string path,string content){var full=Path.Combine(home,path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,content);}
    static AppConfig Config(params string[] ids)=>new(){SpendEnabled=true,EnabledSpendSources=ids.ToList()};
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern void sqlite3_free(IntPtr p);
    static void Db(string home,string path,string sql){var full=Path.Combine(home,path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);Assert.Equal(0,sqlite3_open(full,out var db));try{var code=sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var e);var message=e==IntPtr.Zero?"":Marshal.PtrToStringUTF8(e);if(e!=IntPtr.Zero)sqlite3_free(e);Assert.True(code==0,message??"SQLite fixture");}finally{sqlite3_close(db);}}
    static byte[] Compress(string text){using var compressor=new ZstdSharp.Compressor();return compressor.Wrap(Encoding.UTF8.GetBytes(text)).ToArray();}
    [Check]public static async Task SourceSelectionIsAnActualReadGate()
    {
        var home=Temp();try{Write(home,".claude/projects/bad.jsonl","{bad");var report=await new SpendService().ScanAsync(Config(),home);Assert.Equal(0,report.Records.Count);Assert.Equal(0,report.Notes.Count);report=await new SpendService().ScanAsync(Config("codex"),home);Assert.Equal(0,report.Notes.Count);report=await new SpendService().ScanAsync(Config("claude"),home);Assert.True(report.Notes.Count>0);}finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ZedPlainCompressedAndMislabelledThreadsUseSameCounters()
    {
        var home=Temp();try
        {
            var text="""{"model":{"provider":"zed.dev","model":"x"},"request_token_usage":{"a":{"input_tokens":"100","output_tokens":20,"cache_read_input_tokens":30,"cache_creation_input_tokens":10}},"cumulative_token_usage":{"input_tokens":999}}""";
            var plain=Convert.ToHexString(Encoding.UTF8.GetBytes(text));var compressed=Convert.ToHexString(Compress(text));
            Db(home,"AppData/Local/Zed/threads/threads.db",$"CREATE TABLE threads(id TEXT,created_at INTEGER,updated_at INTEGER,data_type TEXT,data BLOB,folder_paths TEXT,folder_paths_order TEXT);INSERT INTO threads VALUES('a',1717200000,1717300000,'json',X'{plain}','C:/a'||char(10)||'C:/b','1,0');INSERT INTO threads VALUES('b',1717200000,1717300000,'zstd',X'{compressed}',NULL,NULL);INSERT INTO threads VALUES('c',1717200000,1717300000,'json',X'{compressed}',NULL,NULL);");
            var report=await new SpendService().ScanAsync(Config("zed"),home);Assert.Equal(3,report.Records.Count);Assert.Equal(480L,report.TotalTokens);Assert.Equal("C:/b",report.Records.First(r=>r.Session=="a").Project);Assert.True(report.Records.All(r=>r.IsAggregate));
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ZedDamagedFrameProducesDiagnostic()
    {
        var home=Temp();try{Db(home,"AppData/Local/Zed/threads/threads.db","CREATE TABLE threads(id TEXT,data_type TEXT,data BLOB);INSERT INTO threads VALUES('x','zstd',X'28B52FFDFF');");var r=await new SpendService().ScanAsync(Config("zed"),home);Assert.Equal(0,r.Records.Count);Assert.True(r.Notes.Any(n=>n.Contains("zstd")));}finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task WorkBuddyAggregateIsUnclassifiedAndTranscriptWins()
    {
        var home=Temp();try
        {Db(home,".workbuddy/workbuddy.db","CREATE TABLE sessions(id TEXT,cwd TEXT,model TEXT);CREATE TABLE session_usage(session_id TEXT,used INTEGER,updated_at INTEGER);INSERT INTO sessions VALUES('s','C:/project','x');INSERT INTO session_usage VALUES('s',500,1717200000000);");var report=await new SpendService().ScanAsync(Config("workbuddy"),home);Assert.Equal(500L,report.TotalTokens);Assert.Equal(500L,report.Records[0].Unclassified);Assert.True(report.Records[0].IsAggregate);Assert.Equal("C:/project",report.Records[0].Project);
            Write(home,".workbuddy/projects/p/session.jsonl","""{"type":"message","role":"assistant","status":"completed","timestamp":1717200000000,"sessionId":"s","id":"m","message":{"model":"x","usage":{"input_tokens":100,"output_tokens":20}}}""");report=await new SpendService().ScanAsync(Config("workbuddy"),home);Assert.Equal(120L,report.TotalTokens);Assert.True(report.Records.All(r=>r.IsPartial));
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task DshZstdCopiesDeduplicateAndFoldReasoningOnce()
    {
        var home=Temp();try
        {var text="""
{"type":"session","id":"s","cwd":"C:/project","seedLength":1}
{"type":"request/header","data":{"header":{"config":{"model":"x"}}}}
{"type":"assistant/message","seq":2,"time":1717200000000,"data":{"message":{"id":"r"},"usage":{"inputTokens":100,"outputTokens":20,"reasoningTokens":5,"cacheReadTokens":30}}}
""";var file=Path.Combine(home,".dsh/sessions/p/s/session.jsonl.zstd");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,Compress(text));var report=await new SpendService().ScanAsync(Config("dsh"),home);Assert.Equal(150L,report.TotalTokens);Write(home,".dsh/sessions/p/s/session.jsonl",text);report=await new SpendService().ScanAsync(Config("dsh"),home);Assert.Equal(1,report.Records.Count);Assert.Equal(150L,report.TotalTokens);Assert.Equal(20L,report.Records[0].Output);Assert.Equal("C:/project",report.Records[0].Project);
        }finally{Directory.Delete(home,true);}
    }
    static byte[] V(ulong value){var b=new List<byte>();while(value>=128){b.Add((byte)((value&127)|128));value>>=7;}b.Add((byte)value);return b.ToArray();}
    [Check]public static async Task DshUsesFrameMagicEvenInJsonlAndKeepsCompleteConcatenatedFrames()
    {
        var home=Temp();try
        {var header="{\"type\":\"session\",\"id\":\"s\"}\n{\"type\":\"request/header\",\"data\":{\"header\":{\"config\":{\"model\":\"x\"}}}}\n";var row="{\"type\":\"assistant/message\",\"seq\":2,\"time\":1717200000000,\"data\":{\"message\":{\"id\":\"r\"},\"usage\":{\"inputTokens\":10}}}\n";var file=Path.Combine(home,".dsh/sessions/p/s/session.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,Compress(header).Concat(Compress(row)).Concat(new byte[]{0x28,0xB5,0x2F,0xFD}).ToArray());var report=await new SpendService().ScanAsync(Config("dsh"),home);Assert.Equal(10L,report.TotalTokens);Assert.Equal(1,report.Records.Count);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task TencentExtensionLogsKeepEqualCallsAndReportedReasoningArithmetic()
    {
        var home=Temp();try{var line="2026-10-20 12:00:00 [AgentReporter] [a] usage: {\"inputTokens\":100,\"outputTokens\":20,\"cacheTokens\":30,\"reasoningTokens\":5,\"totalTokens\":120}\n";Write(home,".codebuddy/logs/project__extension.log","2026-10-20 11:59:00 [CraftInvokableAgent] [a] Model prepared: Test (x)\n"+line+line);var report=await new SpendService().ScanAsync(Config("codebuddy"),home);Assert.Equal(2,report.Records.Count);Assert.Equal(240L,report.TotalTokens);Assert.Equal("x",report.Records[0].Model);Assert.Equal(70L,report.Records[0].Input);Assert.Equal(20L,report.Records[0].Output);}finally{Directory.Delete(home,true);}
    }
    static byte[] Field(int n,ulong value)=>V((ulong)n<<3).Concat(V(value)).ToArray();
    static byte[] Bytes(int n,byte[] data)=>V(((ulong)n<<3)|2).Concat(V((ulong)data.Length)).Concat(data).ToArray();
    static byte[] Text(int n,string text)=>Bytes(n,Encoding.UTF8.GetBytes(text));
    [Check]public static async Task AntigravityProtobufCountsOnlyEstablishedFieldsAndUsesExplicitTime()
    {
        var home=Temp();try
        {var usage=Field(1,99999).Concat(Field(2,100)).Concat(Field(5,30)).Concat(Field(9,20)).Concat(Field(10,5)).Concat(Text(11,"response")).ToArray();var timestamp=Field(1,1717200000);var chat=Bytes(4,usage).Concat(Text(19,"x")).Concat(Bytes(9,Bytes(4,timestamp))).ToArray();var row=Bytes(1,chat);var hex=Convert.ToHexString(row);Db(home,".gemini/antigravity-cli/conversations/s.db",$"CREATE TABLE gen_metadata(idx INTEGER,data BLOB);INSERT INTO gen_metadata VALUES(1,X'{hex}');INSERT INTO gen_metadata VALUES(2,X'{hex}');");var report=await new SpendService().ScanAsync(Config("antigravity-cli"),home);Assert.Equal(1,report.Records.Count);Assert.Equal(155L,report.TotalTokens);Assert.Equal(100L,report.Records[0].Input);Assert.Equal(25L,report.Records[0].Output);Assert.True(report.Records[0].IsPartial&&!report.Records[0].IsAggregate);Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1717200000),report.Records[0].At);
        }finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task GeminiHeadlessStatsKeepModelsToolsAndUnknownTokenKinds()
    {
        var home=Temp();try{Write(home,".gemini/tmp/headless.jsonl","""
{"type":"init","model":"x","session_id":"s"}
{"id":"r","timestamp":"2026-10-20T01:00:00Z","stats":{"models":{"x":{"tokens":{"input":100,"cached":30,"output":20,"thoughts":5,"tool":7}},"y":{"input_tokens":100,"cached_tokens":30,"output_tokens":20,"total_tokens":130}}}}
""");var report=await new SpendService().ScanAsync(Config("gemini"),home);Assert.Equal(2,report.Records.Count);var x=report.Records.Single(r=>r.Model=="x");Assert.Equal(107L,x.Input);Assert.Equal(25L,x.Output);Assert.Equal(30L,x.CacheRead);var y=report.Records.Single(r=>r.Model=="y");Assert.Equal(70L,y.Input);Assert.Equal(10L,y.Unclassified);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task GeminiSessionPromptIncludesCacheWithoutNeedingAStatedTotal()
    {
        var home=Temp();try{Write(home,".gemini/tmp/p/chats/chat.json","""{"sessionId":"s","messages":[{"type":"gemini","id":"r","timestamp":"2026-10-20T01:00:00Z","model":"x","tokens":{"prompt":100,"cached":30,"candidates":20,"thoughts":5,"tool":7}}]}""");var report=await new SpendService().ScanAsync(Config("gemini"),home);Assert.Equal(132L,report.TotalTokens);Assert.Equal(77L,report.Records[0].Input);Assert.Equal(25L,report.Records[0].Output);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task MiniMaxNativeSkipsReadableSubagentCopiesAndWinsOverCapture()
    {
        var home=Temp();try{var native="""{"message_id":"m","message":{"role":"assistant","model":"x","timestamp":1717200000000,"usage":{"input":10,"output":5,"cacheRead":3}}}""";Write(home,".minimax/v2/sessions/2026/10/20/s/messages.jsonl",native);Write(home,".minimax/v2/sessions/2026/10/20/s/task-1.readable.v1.jsonl",native.Replace("\"m\"","\"other\""));Write(home,"AppData/Roaming/Pulse/UsageImports/mcode/headless.jsonl","""
{"type":"message","message":{"role":"assistant","turnId":"t","timestamp":1717200000000,"usage":{"input":100}}}
{"type":"exec.result","sessionId":"s","turnId":"t","model":{"providerId":"p","modelId":"x"}}
""");var report=await new SpendService().ScanAsync(Config("mcode"),home);Assert.Equal(18L,report.TotalTokens);Assert.Equal(1,report.Records.Count);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task MiniMaxCaptureWaitsForModelAndKeepsDistinctEqualMessages()
    {
        var home=Temp();try{Write(home,"AppData/Roaming/Pulse/UsageImports/mcode/headless.jsonl","""
{"type":"message","message":{"role":"assistant","id":"a","turnId":"t","timestamp":1717200000000,"usage":{"input":100}}}
{"type":"message","message":{"role":"assistant","id":"b","turnId":"t","timestamp":1717200000000,"usage":{"input":100}}}
{"type":"message","message":{"role":"assistant","id":"a","turnId":"t","timestamp":1717200000000,"usage":{"input":200}}}
{"type":"message","message":{"role":"assistant","id":"unmatched","turnId":"z","timestamp":1717200000000,"usage":{"input":999}}}
{"type":"exec.result","sessionId":"s","turnId":"t","model":{"providerId":"p","modelId":"x"}}
""");var report=await new SpendService().ScanAsync(Config("mcode"),home);Assert.Equal(300L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.True(report.Records.All(r=>r.Model=="x"));}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task ZCodeDatabaseUsesItsActualTimestampAndCacheColumns()
    {
        var home=Temp();try{Db(home,".zcode/cli/db/db.sqlite","CREATE TABLE model_usage(id TEXT,session_id TEXT,model_id TEXT,started_at INTEGER,completed_at INTEGER,input_tokens INTEGER,output_tokens INTEGER,cache_read_input_tokens INTEGER,cache_creation_input_tokens INTEGER,computed_total_tokens INTEGER);INSERT INTO model_usage VALUES('r','s','x',1717200000000,1717200001000,100,20,30,10,150);");var report=await new SpendService().ScanAsync(Config("zcode"),home);Assert.Equal(150L,report.TotalTokens);Assert.Equal(60L,report.Records[0].Input);Assert.Equal(30L,report.Records[0].CacheRead);Assert.Equal(10L,report.Records[0].CacheWrite);Assert.Equal(30L,report.Records[0].Unclassified);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task MiCodeUnrelatedDatabasesDoNotShareRowIdentities()
    {
        var home=Temp();try{foreach(var name in new[]{"one","two"})Db(home,".local/share/mimocode/"+name+".db","""CREATE TABLE message(id TEXT,session_id TEXT,data TEXT);INSERT INTO message VALUES('m','s','{"role":"assistant","modelID":"x","time":{"created":1717200000000},"tokens":{"input":10,"output":5}}');""");var report=await new SpendService().ScanAsync(Config("micode"),home);Assert.Equal(30L,report.TotalTokens);Assert.Equal(2,report.Records.Count);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static async Task OpenClawKeepsPerSessionModelAndFoldsDatabaseArchiveCopies()
    {
        var home=Temp();try
        {var one="""{"type":"message","id":"row1","timestamp":"2026-10-20T01:00:00Z","message":{"role":"assistant","usage":{"input":10,"reasoningTokens":5}}}""";var two=one.Replace("row1","row2");Db(home,".openclaw/agents/a/agent/openclaw-agent.sqlite","CREATE TABLE transcript_events(session_id TEXT,seq INTEGER,event_json TEXT);CREATE TABLE session_windows(session_id TEXT,model TEXT);INSERT INTO session_windows VALUES('one','x');INSERT INTO session_windows VALUES('two','y');INSERT INTO transcript_events VALUES('one',1,'"+one+"');INSERT INTO transcript_events VALUES('two',1,'"+two+"');");Write(home,".openclaw/agents/a/sessions/one.jsonl.deleted.2026-10-20",one.Replace("\"role\":\"assistant\"","\"role\":\"assistant\",\"model\":\"x\""));Write(home,".openclaw/agents/a/agent/codex-home/sessions/mirror.jsonl",one.Replace("row1","bad").Replace("\"role\":\"assistant\"","\"role\":\"assistant\",\"model\":\"bad\""));var report=await new SpendService().ScanAsync(Config("openclaw"),home);Assert.Equal(30L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.True(report.Records.Any(r=>r.Model=="x")&&report.Records.Any(r=>r.Model=="y"));}
        finally{Directory.Delete(home,true);}
    }
}
