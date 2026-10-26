// SQLite schemas translated from Pulse 1.12.2, Apache-2.0. All database access is read-only.
using System.Globalization;
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendDatabaseReaders
{
    internal static List<SpendRecord> Read(string agent,string path,List<string> notes,CancellationToken ct)
    {
        var output=new List<SpendRecord>();
        try
        {
            if(agent=="zed")return SpendBinaryReaders.ReadZed(path,notes,ct);
            if(agent is "antigravity-cli" or "antigravity-ide")return SpendBinaryReaders.ReadAntigravity(agent,path,notes,ct);
            var tables=SqliteReader.Query(path,"SELECT name FROM sqlite_master WHERE type='table'").Select(r=>r.GetValueOrDefault("name")).ToHashSet();
            IEnumerable<Dictionary<string,string?>> Rows(string table)=>tables.Contains(table)?SqliteReader.Query(path,"SELECT * FROM \""+table+"\""):[];
            var context=new SpendReaders.Context(agent,path,notes);
            if(agent is "opencode" or "kilo" or "micode")
            {
                var seen=new HashSet<string>();
                foreach(var table in new[]{"session_message","message"}) foreach(var row in Rows(table))
                {
                    ct.ThrowIfCancellationRequested(); var id=V(row,"id"); if(id is not null&&!seen.Add(id)) continue;
                    var json=V(row,"data"); if(json is null) continue; using var document=JsonDocument.Parse(json); var r=document.RootElement;
                    if((V(row,"type")??SpendReaders.S(r,"role"))!="assistant") continue;
                    var u=JsonTools.At(r,"tokens"); var input=SpendReaders.N(u,"input"); var read=SpendReaders.N(u,"cache.read"); var write=SpendReaders.N(u,"cache.write"); var reply=SpendReaders.N(u,"output"); var reasoning=SpendReaders.N(u,"reasoning"); var total=SpendReaders.N(u,"total");
                    // A stated total settles whether reasoning already belongs to output. Legacy messages use folded output.
                    var result=total>0&&total==input+read+write+reply+reasoning?reply+reasoning:reply;
                    if(input+read+write+result==0) continue;
                    var at=SpendReaders.D(r,"time.created")??Date(V(row,"time_created")); if(at is null) continue;
                    var model=SpendReaders.S(r,"modelID","model.id"); if(model is null) continue;
                    output.Add(new(agent,SpendReaders.S(r,"id")??(agent=="micode"?path+":"+id:id)??path+":"+output.Count,at.Value,model,input,result,read,write,Session:V(row,"session_id")));
                }
            }
            else if(agent=="devin-cli") foreach(var row in Rows("message_nodes"))
            {
                ct.ThrowIfCancellationRequested(); var text=V(row,"chat_message"); if(text is null) continue;
                using var doc=JsonDocument.Parse(text); var r=doc.RootElement; if(SpendReaders.S(r,"role")!="assistant") continue;
                var metadata=JsonTools.At(r,"metadata"); var u=JsonTools.At(metadata,"metrics");
                var record=SpendReaders.Build(u,context,SpendReaders.D(metadata,"created_at")??Date(V(row,"created_at")),SpendReaders.S(metadata,"generation_model"),SpendReaders.S(r,"message_id","id")??SpendReaders.S(metadata,"request_id")??SpendReaders.Hash(text));
                if(record is not null) output.Add(record with{Session=V(row,"session_id")});
            }
            else if(agent=="openclaw") foreach(var row in Rows("transcript_events"))
            {
                ct.ThrowIfCancellationRequested(); if(V(row,"event_json") is not {} text) continue; using var doc=JsonDocument.Parse(text); context.Session=V(row,"session_id")??context.Session; output.AddRange(SpendReaders.ParseDocument(doc.RootElement,context));
            }
            else if(agent=="hermes")
            {
                var sessions=Rows("sessions").Where(x=>V(x,"id") is not null).ToDictionary(x=>V(x,"id")!,x=>x); var covered=new HashSet<string>();
                foreach(var group in Rows("session_model_usage").GroupBy(x=>(V(x,"session_id"),V(x,"model"),V(x,"billing_provider"))))
                {
                    var key=group.Key; if(key.Item1 is null||key.Item2 is null||!sessions.TryGetValue(key.Item1,out var session)) continue;
                    var input=group.Sum(x=>Number(x,"input_tokens")); var reply=group.Sum(x=>Number(x,"output_tokens")); var cache=group.Sum(x=>Number(x,"cache_read_tokens")+Number(x,"cache_write_tokens")); var reasoning=group.Sum(x=>Number(x,"reasoning_tokens"));
                    AddHermes(key.Item1+":"+key.Item2+":"+key.Item3,key.Item1,key.Item2,Date(V(session,"started_at")),input,reply,cache,reasoning); covered.Add(key.Item1);
                }
                foreach(var (id,session) in sessions) if(!covered.Contains(id)) AddHermes(id,id,V(session,"model"),Date(V(session,"started_at")),Number(session,"input_tokens"),Number(session,"output_tokens"),Number(session,"cache_read_tokens")+Number(session,"cache_write_tokens"),Number(session,"reasoning_tokens"));
                void AddHermes(string id,string session,string? model,DateTimeOffset? at,long input,long reply,long cache,long reasoning) { if(model is not null&&at is not null&&input+reply>0) output.Add(new(agent,id,at.Value,model,cache>0?0:input,reply,Unclassified:cache>0?input:0,IsPartial:cache>0||reasoning>0,IsAggregate:true,Session:session)); }
            }
            else if(agent=="goose") foreach(var row in Rows("sessions"))
            {
                var modelConfig=V(row,"model_config_json"); if(modelConfig is null) continue; using var doc=JsonDocument.Parse(modelConfig); var model=SpendReaders.S(doc.RootElement,"model_name"); var at=Date(V(row,"created_at")); if(model is null||at is null) continue;
                var input=Number(row,"accumulated_input_tokens","input_tokens"); var reply=Number(row,"accumulated_output_tokens","output_tokens"); var total=Number(row,"accumulated_total_tokens","total_tokens");
                if(input+reply+total>0) output.Add(new(agent,V(row,"id")??SpendReaders.Hash(JsonSerializer.Serialize(row)),at.Value,model,input,reply,Unclassified:Math.Max(0,total-input-reply),IsAggregate:true,Session:V(row,"id")));
            }
            else if(agent=="unsloth")
            {
                var threads=Rows("chat_threads").Where(x=>V(x,"id") is not null).ToDictionary(x=>V(x,"id")!,x=>V(x,"model_id"));
                foreach(var row in Rows("chat_messages"))
                {
                    if(V(row,"role")!="assistant"||V(row,"metadata_json") is not {} text) continue;
                    using var doc=JsonDocument.Parse(text); var u=JsonTools.At(doc.RootElement,"contextUsage"); var model=SpendReaders.S(doc.RootElement,"responseDetails.responseModelId","contextUsage.modelId")??threads.GetValueOrDefault(V(row,"thread_id")??"")??"unknown";
                    var at=Date(V(row,"created_at")); if(at is null) continue; var prompt=SpendReaders.N(u,"promptTokens"); var reply=SpendReaders.N(u,"completionTokens"); var read=Math.Min(prompt,SpendReaders.N(u,"cachedTokens")); var write=Math.Min(prompt-read,SpendReaders.N(u,"cacheWriteTokens")); var total=Math.Max(prompt+reply,SpendReaders.N(u,"totalTokens"));
                    if(total>0) output.Add(new(agent,"chat:"+V(row,"id"),at.Value,model,total-reply-read-write,reply,read,write,Session:V(row,"thread_id")));
                }
                foreach(var row in Rows("api_usage_events")) { var at=Date(V(row,"created_at")); var model=V(row,"model"); var input=Number(row,"prompt_tokens");var reply=Number(row,"completion_tokens");var total=Math.Max(input+reply,Number(row,"total_tokens")); if(at is not null&&model is not null&&total>0) output.Add(new(agent,"api:"+V(row,"id"),at.Value,model,total-reply,reply)); }
            }
            else if(agent=="zcode") foreach(var row in Rows("model_usage"))
            {
                var at=Date(V(row,"started_at","completed_at","timestamp","created_at","createdAt")); var model=V(row,"model_id","model")??"auto"; var input=Number(row,"input_tokens");var reply=Number(row,"output_tokens");var read=Number(row,"cache_read_input_tokens","cache_read_tokens","cached_tokens");var write=Number(row,"cache_creation_input_tokens");var total=Number(row,"computed_total_tokens","total_tokens");
                if(at is not null&&input+reply+total>0) output.Add(new(agent,"zcode:"+path+":"+(V(row,"id")??SpendReaders.Hash(JsonSerializer.Serialize(row))),at.Value,model,Math.Max(0,input-read-write),reply,read,write,Unclassified:Math.Max(0,total-input-reply),Session:V(row,"session_id")));
            }
            else if(agent=="copilot") foreach(var row in Rows("sessions"))
            {
                var at=Date(V(row,"created_at")); var input=Number(row,"total_input_tokens");var reply=Number(row,"total_output_tokens");var read=Number(row,"total_cached_tokens");
                if(at is not null&&input+reply+read>0) output.Add(new(agent,"desktop:"+V(row,"id"),at.Value,V(row,"model")??"unknown",input,reply,read,IsPartial:Number(row,"total_reasoning_tokens")>0,IsAggregate:true,Session:V(row,"id")));
            }
            else if(agent=="workbuddy")
            {
                var sessions=Rows("sessions").Where(x=>V(x,"id") is not null).ToDictionary(x=>V(x,"id")!,x=>x);
                foreach(var row in Rows("session_usage"))
                {
                    ct.ThrowIfCancellationRequested();var id=V(row,"session_id");var used=Number(row,"used");var at=Date(V(row,"updated_at"));if(id is null||used<=0||at is null)continue;var session=sessions.GetValueOrDefault(id);
                    output.Add(new(agent,"workbuddy:"+id+":"+V(row,"updated_at"),at.Value,session is not null?V(session,"model")??"auto":"auto",Unclassified:used,IsAggregate:true,Session:id,Project:session is not null?V(session,"cwd"):null,Origin:"aggregate"));
                }
            }
            if(output.Count==0&&agent is not ("crush" or "freebuff")) notes.Add(agent+"：数据库没有可识别的实测用量记录："+Path.GetFileName(path));
        }
        catch(Exception e) when(e is InvalidOperationException or IOException or JsonException or System.Runtime.InteropServices.ExternalException or DllNotFoundException) { notes.Add(agent+"：数据库读取失败（"+e.GetType().Name+"）："+Path.GetFileName(path)); }
        return output;
    }
    static string? V(Dictionary<string,string?> row,params string[] keys) { foreach(var key in keys) if(row.GetValueOrDefault(key) is {} value) return value; return null; }
    static long Number(Dictionary<string,string?> row,params string[] keys)=>double.TryParse(V(row,keys),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&value>=0&&value<long.MaxValue?(long)value:0;
    static DateTimeOffset? Date(string? text)
    {
        if(text is null) return null;
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)) { if(number<=0||number>253402300799000) return null; return number>100000000000?DateTimeOffset.FromUnixTimeMilliseconds((long)number):DateTimeOffset.FromUnixTimeSeconds((long)number); }
        return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date:null;
    }
}
