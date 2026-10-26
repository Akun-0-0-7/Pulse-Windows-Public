// Additional native store identity and timing rules translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendNativeReaders
{
    internal static List<SpendRecord> Gjc(IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var mirrors=new HashSet<string>();foreach(var file in SpendReaders.Files(roots,notes).Where(f=>Path.GetExtension(f)==".jsonl"))
        {
            var rows=Rows(file,notes,"gjc",ct);var session=rows.Where(r=>SpendReaders.S(r,"type")=="session").Select(r=>SpendReaders.S(r,"id")).FirstOrDefault(id=>id is not null);var project=rows.Where(r=>SpendReaders.S(r,"type")=="session").Select(r=>SpendReaders.S(r,"cwd")).FirstOrDefault(cwd=>cwd is not null);
            string digest;try{digest=SpendReaders.Hash(File.ReadAllText(file));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add("gjc：会话文件暂时无法读取。");continue;}
            if(session is not null&&!mirrors.Add(session+":"+Path.GetFileName(file)+":"+digest))continue;session??=Path.GetFileNameWithoutExtension(file);
            var index=0;foreach(var row in rows)
            {
                index++;var m=JsonTools.At(row,"message");if(SpendReaders.S(row,"type")!="message"||!string.Equals(SpendReaders.S(m,"role"),"assistant",StringComparison.OrdinalIgnoreCase)||SpendReaders.S(m,"model") is not {} model||(SpendReaders.D(m,"timestamp")??SpendReaders.D(row,"timestamp")) is not {} at)continue;
                var u=JsonTools.At(m,"usage");var input=SpendReaders.N(u,"input");var output=SpendReaders.N(u,"output");var read=SpendReaders.N(u,"cacheRead");var write=SpendReaders.N(u,"cacheWrite");if(input+output+read+write<=0)continue;
                var identity=SpendReaders.S(row,"id") is {} id?"gjc:"+session+":"+id:file+":"+index;records.Add(new("gjc",identity,at,model,input,output,read,write,Session:session,Project:project));
            }
        }
        return records;
    }
    sealed class Snapshot{public long Input,Output,Read,Write;public DateTimeOffset? At;public string? Model;}
    internal static List<SpendRecord> Cherry(IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var records=new List<SpendRecord>();foreach(var root in roots)foreach(var file in SpendReaders.Files([root],notes).Where(f=>Path.GetExtension(f)==".jsonl"))
        {
            var relative=Directory.Exists(root)?Path.GetRelativePath(root,file):Path.GetFileName(file);if(!seen.Add(relative))continue;var session=Path.GetFileNameWithoutExtension(file);var project=Path.GetFileName(Path.GetDirectoryName(file));var snapshots=new Dictionary<string,Snapshot>();var index=0;
            foreach(var row in Rows(file,notes,"cherrystudio",ct))
            {
                index++;if(SpendReaders.S(row,"type")!="assistant"||SpendReaders.S(row,"message.model") is not {} model)continue;var u=JsonTools.At(row,"message.usage");var input=SpendReaders.N(u,"input_tokens");var output=SpendReaders.N(u,"output_tokens");var read=SpendReaders.N(u,"cache_read_input_tokens");var write=SpendReaders.N(u,"cache_creation_input_tokens");if(input+output+read+write<=0)continue;var at=SpendReaders.D(row,"timestamp")??SpendReaders.D(row,"message.timestamp");var id=SpendReaders.S(row,"requestId","message.requestId","message.id","uuid");
                if(id is null){if(at is {} time)records.Add(new("cherrystudio",file+":"+index,time,model,input,output,read,write,Session:session,Project:project,RequestContext:input+read+write));continue;}
                if(!snapshots.TryGetValue(id,out var snapshot))snapshots[id]=snapshot=new();snapshot.Input=Math.Max(snapshot.Input,input);snapshot.Output=Math.Max(snapshot.Output,output);snapshot.Read=Math.Max(snapshot.Read,read);snapshot.Write=Math.Max(snapshot.Write,write);snapshot.Model??=model;if(at is {} timestamp&&(snapshot.At is null||timestamp>snapshot.At))snapshot.At=timestamp;
            }
            foreach(var (id,s) in snapshots)if(s.At is {} at&&s.Model is {} model)records.Add(new("cherrystudio","cherrystudio:"+relative+":"+id,at,model,s.Input,s.Output,s.Read,s.Write,Session:session,Project:project,RequestContext:s.Input+s.Read+s.Write));
        }
        return records;
    }
    internal static IEnumerable<SpendRecord> Kiro(JsonElement root,SpendReaders.Context context)
    {
        var prompts=new Dictionary<string,DateTimeOffset>();var sidecar=Path.ChangeExtension(context.File,".jsonl");if(File.Exists(sidecar))foreach(var r in Rows(sidecar,context.Notes,"kiro",CancellationToken.None))if(SpendReaders.S(r,"kind")=="Prompt"&&SpendReaders.S(r,"data.message_id") is {} id&&SpendReaders.D(r,"data.meta.timestamp") is {} promptTime)prompts[id]=promptTime;
        var model=SpendReaders.S(root,"session_state.rts_model_state.model_info.model_id")??"auto";var index=0;foreach(var turn in JsonTools.Array(root,"session_state.conversation_metadata.user_turn_metadatas"))
        {
            DateTimeOffset? at=JsonTools.Array(turn,"message_ids").Where(id=>id.ValueKind==JsonValueKind.String&&prompts.ContainsKey(id.GetString()!)).Select(id=>(DateTimeOffset?)prompts[id.GetString()!]).Min()??SpendReaders.D(turn,"end_timestamp");var input=SpendReaders.N(turn,"input_token_count");var output=SpendReaders.N(turn,"output_token_count");if(at is {} time&&input+output>0)yield return new("kiro",context.Session+":"+index,time,model,input,output,Session:context.Session,Project:context.Project);index++;
        }
    }
    internal static SpendRecord? Reasonix(JsonElement root,SpendReaders.Context context)
    {
        if(JsonTools.Bool(root,"turn")==true||SpendReaders.S(root,"model") is not {} model||SpendReaders.N(root,"total")==0&&SpendReaders.N(root,"requests")==0||SpendReaders.D(root,"ts") is not {} at)return null;
        var prompt=JsonTools.Number(root,"prompt");var completion=JsonTools.Number(root,"completion");long input=0,output=0,read=0,unknown=0;if(prompt is null&&completion is null)unknown=SpendReaders.N(root,"total");else{read=SpendReaders.N(root,"cache_hit");input=SpendReaders.N(root,"cache_miss");if(input==0)input=Math.Max(0,SpendReaders.N(root,"prompt")-read);output=SpendReaders.N(root,"completion");}if(input+output+read+unknown<=0)return null;
        return new("reasonix","reasonix:"+context.File+":"+context.Line+":"+SpendReaders.N(root,"requests")+":"+SpendReaders.N(root,"total"),at,model,input,output,read,Unclassified:unknown,Session:"reasonix-stats:"+context.File);
    }
    static List<JsonElement> Rows(string file,List<string> notes,string agent,CancellationToken ct)
    {
        var rows=new List<JsonElement>();try{foreach(var line in File.ReadLines(file)){ct.ThrowIfCancellationRequested();try{using var document=JsonDocument.Parse(line);rows.Add(document.RootElement.Clone());}catch(JsonException){notes.Add(agent+"：包含无效或未完成行。");}}}catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add(agent+"：读取失败（"+e.GetType().Name+"）："+Path.GetFileName(file));}return rows;
    }
}
