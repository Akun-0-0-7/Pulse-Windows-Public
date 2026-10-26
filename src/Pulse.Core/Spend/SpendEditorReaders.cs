// Cline CLI and OpenCodeReview schemas translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendEditorReaders
{
    internal static string? BareModel(string? value)=>value is null?null:value.Contains('/')?value[(value.LastIndexOf('/')+1)..]:value;
    internal static List<SpendRecord> VscodeTask(string agent,string file,CancellationToken ct)
    {
        var records=new List<SpendRecord>();using var document=JsonDocument.Parse(File.ReadAllText(file));if(document.RootElement.ValueKind!=JsonValueKind.Array)return records;
        var directory=Path.GetDirectoryName(file)!;var session=Path.GetFileName(directory);var history=LocalPaths.ReadText(Path.Combine(directory,"api_conversation_history.json"));string? fallback=null;
        if(history is not null){try{using var doc=JsonDocument.Parse(history);history=JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});}catch(JsonException){}var models=System.Text.RegularExpressions.Regex.Matches(history,@"<model>\s*([^<]+?)\s*</model>");if(models.Count>0)fallback=models[^1].Groups[1].Value;}
        var ordinal=0;foreach(var row in document.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();ordinal++;if(SpendReaders.S(row,"type")!="say"||SpendReaders.S(row,"say")!="api_req_started"||SpendReaders.D(row,"ts") is not {} at||SpendReaders.S(row,"text") is not {} text)continue;
            using var usage=JsonDocument.Parse(text);var u=usage.RootElement;var model=SpendReaders.S(row,"modelInfo.modelId")??fallback;if(model is null)continue;
            var input=SpendReaders.N(u,"tokensIn");var output=SpendReaders.N(u,"tokensOut");var read=SpendReaders.N(u,"cacheReads");var write=SpendReaders.N(u,"cacheWrites");if(input+output+read+write>0)records.Add(new(agent,"task:"+session+":"+ordinal,at,model,input,output,read,write,Session:session));
        }
        return records;
    }
    internal static List<SpendRecord> ZCodeTranscript(string file,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var ordinal=0;foreach(var line in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();ordinal++;try
            {
                using var document=JsonDocument.Parse(line);var root=document.RootElement;if(SpendReaders.D(root,"timestamp") is not {} at||BareModel(SpendReaders.S(root,"model")) is not {} model)continue;
                foreach(var key in new[]{"usage","token_usage"})
                {
                    var u=JsonTools.At(root,key);if(u.ValueKind!=JsonValueKind.Object)continue;var input=SpendReaders.N(u,"input_tokens","prompt_tokens","inputTokens");var output=SpendReaders.N(u,"output_tokens","completion_tokens","outputTokens");var read=SpendReaders.N(u,"input_cache_read","cache_read_tokens","cacheReadTokens");var write=SpendReaders.N(u,"input_cache_creation","cache_write_tokens","cacheCreationTokens");var reasoning=SpendReaders.N(u,"reasoningTokens","reasoning_tokens");var total=JsonTools.Number(u,"totalTokens")??JsonTools.Number(u,"total_tokens");
                    var disjoint=input+output+read+write+reasoning;var unknown=total is {} reported?Math.Max(0,(long)reported-disjoint):0;if(total==input+output&&total!=disjoint)input=Math.Max(0,input-read-write);if(total==disjoint)output+=reasoning;
                    if(input+output+read+write+unknown<=0)continue;var session=SpendReaders.S(root,"sessionId")??Path.GetFileNameWithoutExtension(file);records.Add(new("zcode","zcode:"+file+":"+ordinal+":"+session+":"+at.ToUnixTimeMilliseconds(),at,model,input,output,read,write,Unclassified:unknown,Session:session));break;
                }
            }catch(JsonException){notes.Add("zcode：包含无效或未完成行。");}
        }
        return records;
    }
    internal static List<SpendRecord> CommandCode(IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var files=SpendReaders.Files(roots,notes).ToList();string? config=null;
        foreach(var file in files.Where(f=>Path.GetFileName(f)=="config.json"))try{using var doc=JsonDocument.Parse(File.ReadAllText(file));config=BareModel(SpendReaders.S(doc.RootElement,"model"))??config;}catch(Exception e)when(e is JsonException or IOException or UnauthorizedAccessException){notes.Add("commandcode：配置文件无法读取。");}
        var records=new List<SpendRecord>();foreach(var file in files.Where(f=>Path.GetExtension(f)==".jsonl"&&!f.EndsWith(".checkpoints.jsonl",StringComparison.Ordinal)))
        {
            var rows=new List<JsonElement>();try{foreach(var line in File.ReadLines(file)){ct.ThrowIfCancellationRequested();try{using var doc=JsonDocument.Parse(line);rows.Add(doc.RootElement.Clone());}catch(JsonException){notes.Add("commandcode：包含无效或未完成行。");}}}catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add("commandcode：读取失败（"+e.GetType().Name+"）："+Path.GetFileName(file));}
            var session=rows.Where(r=>SpendReaders.S(r,"type")=="session").Select(r=>SpendReaders.S(r,"id")).FirstOrDefault(id=>id is not null)??Path.GetFileNameWithoutExtension(file);var tree=rows.Any(r=>SpendReaders.S(r,"parentId") is not null);var byId=new Dictionary<string,JsonElement>();foreach(var row in rows)if(SpendReaders.S(row,"id") is {} id)byId[id]=row;
            string? Ancestor(JsonElement row){var seen=new HashSet<string>();while(SpendReaders.S(row,"parentId") is {} parent&&seen.Add(parent)&&byId.TryGetValue(parent,out row)){if(BareModel(SpendReaders.S(row,"model")) is {} model)return model;}return null;}
            string? current=null;var index=0;foreach(var row in rows)
            {
                ct.ThrowIfCancellationRequested();index++;if(SpendReaders.S(row,"type")=="model_change"){current=BareModel(SpendReaders.S(row,"model"))??current;continue;}
                if((SpendReaders.S(row,"message.role")??SpendReaders.S(row,"role"))!="assistant"||SpendReaders.D(row,"timestamp") is not {} at)continue;var model=BareModel(SpendReaders.S(row,"model"))??(tree?Ancestor(row):current)??config;if(model is null)continue;
                var context=new SpendReaders.Context("commandcode",file,notes){Session=session};var record=SpendReaders.Build(JsonTools.At(row,"usage"),context,at,model,"commandcode:"+session+":"+(SpendReaders.S(row,"id")??"line"+index+":"+at.ToUnixTimeMilliseconds()));if(record is not null)records.Add(record);
            }
        }
        return records;
    }
    internal static List<SpendRecord> ClineCli(string file,CancellationToken ct)
    {
        var stem=Path.GetFileName(file)[..^".messages.json".Length];using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;
        var sidecar=Path.Combine(Path.GetDirectoryName(file)!,stem+".json");JsonElement manifest=default;if(File.Exists(sidecar)){using var m=JsonDocument.Parse(File.ReadAllText(sidecar));manifest=m.RootElement.Clone();}
        var session=SpendReaders.S(root,"sessionId")??SpendReaders.S(manifest,"sessionId")??stem;var project=SpendReaders.S(manifest,"workspace_root","cwd");var records=new List<SpendRecord>();var ordinal=0;
        foreach(var message in JsonTools.Array(root,"messages"))
        {
            ct.ThrowIfCancellationRequested();ordinal++;if(SpendReaders.S(message,"role")!="assistant"||SpendReaders.D(message,"ts") is not {} at)continue;var u=JsonTools.At(message,"metrics");if(u.ValueKind!=JsonValueKind.Object)continue;var input=SpendReaders.N(u,"inputTokens");var output=SpendReaders.N(u,"outputTokens");var read=SpendReaders.N(u,"cacheReadTokens");var write=SpendReaders.N(u,"cacheWriteTokens");input=Math.Max(0,input-read-write);if(input+output+read+write<=0)continue;var model=SpendReaders.S(message,"modelInfo.id")??SpendReaders.S(manifest,"model");if(model is null)continue;
            records.Add(new("cline","cline:"+session+":"+(SpendReaders.S(message,"id")??"line"+ordinal),at,model,input,output,read,write,Session:session,Project:project));
        }
        return records;
    }
    internal static List<SpendRecord> OpenCodeReview(string file,List<string> notes,CancellationToken ct)
    {
        var rows=new List<JsonElement>();var digest=SpendReaders.Hash(File.ReadAllText(file));foreach(var line in File.ReadLines(file)){ct.ThrowIfCancellationRequested();try{using var doc=JsonDocument.Parse(line);rows.Add(doc.RootElement.Clone());}catch(JsonException){notes.Add("opencodereview：包含无效或未完成行。");}}
        var session=Path.GetFileNameWithoutExtension(file);string? model=null,project=null;foreach(var row in rows.Where(r=>SpendReaders.S(r,"type")=="session_start")){session=SpendReaders.S(row,"sessionId")??session;model=SpendReaders.S(row,"model")??model;project=SpendReaders.S(row,"cwd")??project;}
        var records=new List<SpendRecord>();var ordinal=0;foreach(var row in rows)
        {
            ordinal++;if(SpendReaders.S(row,"type")!="llm_response"||SpendReaders.D(row,"timestamp") is not {} end)continue;var u=JsonTools.At(row,"usage");if(u.ValueKind!=JsonValueKind.Object)continue;
            var input=SpendReaders.N(u,"prompt_tokens","promptTokens");var output=SpendReaders.N(u,"completion_tokens","completionTokens");var read=SpendReaders.N(u,"cache_read_tokens","cacheReadTokens");var write=SpendReaders.N(u,"cache_write_tokens","cacheWriteTokens");var total=JsonTools.Number(u,"total_tokens")??JsonTools.Number(u,"totalTokens")??JsonTools.Number(u,"total");long unknown=0;
            if(total is {} reported){if(reported==input+output+read+write){ }else if(reported==input+output)input=Math.Max(0,input-read-write);else{unknown=(long)Math.Max(0,reported);input=output=read=write=0;}}
            else if(read+write>0){notes.Add("opencodereview：跳过无法确认缓存重叠关系的用量。");continue;}
            if(input+output+read+write+unknown<=0)continue;var resolved=SpendReaders.S(row,"model")??model;if(resolved is null)continue;var currentSession=SpendReaders.S(row,"sessionId")??session;var duration=SpendReaders.N(row,"duration_ms");var at=end.AddMilliseconds(-duration);
            var identity=SpendReaders.S(row,"uuid") is {} uuid?"uuid:"+uuid:"fragment:"+digest+":line:"+ordinal;records.Add(new("opencodereview","opencodereview:"+currentSession+":"+identity,at,resolved,input,output,read,write,Unclassified:unknown,Session:currentSession,Project:project));
        }
        return records;
    }
}
