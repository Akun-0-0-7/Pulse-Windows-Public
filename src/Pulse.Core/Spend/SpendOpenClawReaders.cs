// OpenClaw cross-store identities and per-session model state translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendOpenClawReaders
{
    internal static List<SpendRecord> Read(IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var files=SpendReaders.Files(roots,notes).ToList();var result=new List<SpendRecord>();var registry=new Dictionary<string,string>();bool incomplete=false;
        foreach(var file in files.Where(f=>Path.GetFileName(f)=="sessions.json"))try{using var doc=JsonDocument.Parse(File.ReadAllText(file));if(doc.RootElement.ValueKind==JsonValueKind.Object)foreach(var p in doc.RootElement.EnumerateObject())if(SpendReaders.S(p.Value,"sessionId") is {} id&&SpendReaders.S(p.Value,"sessionFile") is {} path)registry[Path.GetFileName(path)]=id;}catch(Exception e)when(e is IOException or JsonException){notes.Add("openclaw：会话索引无法读取。");}
        void Parse(JsonElement row,SpendReaders.Context context,string fallback)
        {
            var type=SpendReaders.S(row,"type");if(type=="model_change"){context.Model=SpendReaders.S(row,"modelId")??"unknown";return;}
            if(type=="custom"&&SpendReaders.S(row,"customType")=="model-snapshot"){context.Model=SpendReaders.S(row,"data.modelId")??"unknown";return;}
            if(type!="message")return;var message=JsonTools.At(row,"message");if(SpendReaders.S(message,"role")!="assistant"||SpendReaders.S(row,"api")=="openclaw-transcript"||SpendReaders.S(message,"api")=="openclaw-transcript")return;
            var provider=SpendReaders.S(message,"provider")??SpendReaders.S(row,"provider");var ownModel=SpendReaders.S(message,"model")??SpendReaders.S(row,"model");if(provider=="openclaw"&&ownModel is "delivery-mirror" or "gateway-injected")return;
            var u=JsonTools.At(message,"usage");if(u.ValueKind!=JsonValueKind.Object)return;var input=SpendReaders.N(u,"input");var output=SpendReaders.Has(u,"output")?SpendReaders.N(u,"output"):SpendReaders.N(u,"reasoningTokens");var read=SpendReaders.N(u,"cacheRead");var write=SpendReaders.N(u,"cacheWrite");var total=SpendReaders.N(u,"totalTokens");var anyKnown=new[]{"input","output","cacheRead","cacheWrite"}.Any(k=>SpendReaders.Has(u,k));long unknown=0;
            if(SpendReaders.Has(u,"totalTokens")){if(!anyKnown){unknown=total;input=output=read=write=0;}else unknown=Math.Max(0,total-input-output-read-write);}
            if(input+output+read+write+unknown<=0)return;var at=SpendReaders.D(message,"timestamp")??SpendReaders.D(row,"timestamp");var model=ownModel??(context.Model=="unknown"?null:context.Model);if(at is null||model is null){incomplete=true;return;}
            var id=SpendReaders.S(row,"id")??fallback;result.Add(new("openclaw","openclaw:"+id+":"+at.Value.ToUnixTimeMilliseconds()+":"+input+":"+output,at.Value,model,input,output,read,write,Unclassified:unknown,Session:context.Session));
        }
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();try
            {
                if(Path.GetFileName(file)=="openclaw-agent.sqlite")
                {
                    var tables=SqliteReader.Query(file,"SELECT name FROM sqlite_master WHERE type='table'").Select(r=>r.GetValueOrDefault("name")).ToHashSet();if(!tables.Contains("transcript_events"))continue;var metadata=new Dictionary<string,string>();
                    foreach(var table in new[]{"session_windows","sessions"})if(tables.Contains(table)&&metadata.Count==0)foreach(var row in SqliteReader.Query(file,"SELECT * FROM \""+table+"\""))if(row.GetValueOrDefault("session_id") is {} id&&row.GetValueOrDefault("model") is {} model)metadata[id]=model;
                    var contexts=new Dictionary<string,SpendReaders.Context>();foreach(var row in SqliteReader.Query(file,"SELECT session_id,seq,event_json FROM transcript_events ORDER BY session_id,seq")){ct.ThrowIfCancellationRequested();if(row.GetValueOrDefault("session_id") is not {} session||row.GetValueOrDefault("event_json") is not {} json)continue;if(!contexts.TryGetValue(session,out var context)){context=new("openclaw",file,notes){Session=session,Model=metadata.GetValueOrDefault(session,"unknown")};contexts[session]=context;}try{using var doc=JsonDocument.Parse(json);Parse(doc.RootElement,context,"seq-"+row.GetValueOrDefault("seq"));}catch(JsonException){notes.Add("openclaw：数据库含未完成事件。");}}
                }
                else
                {
                    var path=file.Replace('\\','/');var name=Path.GetFileName(file);if(!name.Contains(".jsonl",StringComparison.Ordinal)||name.EndsWith(".zst",StringComparison.Ordinal)||!path.Contains("/sessions/",StringComparison.Ordinal)&&!path.Contains("/session-sqlite-import-archive/",StringComparison.Ordinal)||path.Contains("/codex-home/",StringComparison.Ordinal)||path.Contains("/cli-auth/",StringComparison.Ordinal))continue;
                    var context=new SpendReaders.Context("openclaw",file,notes){Session=registry.GetValueOrDefault(name,name[..name.IndexOf(".jsonl",StringComparison.Ordinal)])};var index=0;foreach(var line in File.ReadLines(file)){ct.ThrowIfCancellationRequested();try{using var doc=JsonDocument.Parse(line);Parse(doc.RootElement,context,"line-"+index);}catch(JsonException){notes.Add("openclaw：日志含无效或未完成行。");}index++;}
                }
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add("openclaw：读取失败："+Path.GetFileName(file));}
        }
        if(incomplete)notes.Add("openclaw：有实测用量缺少模型或时间。");return incomplete?result.Select(r=>r with{IsPartial=true}).ToList():result;
    }
}
