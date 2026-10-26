// Gemini CLI and MiniMax Code formats translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
namespace Pulse.Core;
internal static class SpendSpecialReaders
{
    static IEnumerable<JsonElement> JsonLines(string file,List<string> notes,CancellationToken ct)
    {
        foreach(var line in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();JsonElement? value=null;try{using var d=JsonDocument.Parse(line.TrimStart('\uFEFF'));value=d.RootElement.Clone();}catch(JsonException){notes.Add("日志含无效或尚未写完的行："+Path.GetFileName(file));}if(value is {} v)yield return v;
        }
    }
    internal static List<SpendRecord> Gemini(string file,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var indexed=new Dictionary<string,int>();var context=new SpendReaders.Context("gemini",file,notes);bool incomplete=false;
        void Submit(JsonElement u,DateTimeOffset? at,string? model,string id,bool session,bool wrapper)
        {
            var r=GeminiUsage(u,context,at,model,id,session,wrapper);if(r is null){if(CountAny(u)>0&&(at is null||model is null))incomplete=true;return;}if(indexed.TryGetValue(id,out var index))records[index]=r;else{indexed[id]=records.Count;records.Add(r);}
        }
        if(Path.GetExtension(file).Equals(".json",StringComparison.OrdinalIgnoreCase))
        {
            var normalized=file.Replace('\\','/');if(!Path.GetFileName(file).StartsWith("session-",StringComparison.Ordinal)&&!System.Text.RegularExpressions.Regex.IsMatch(normalized,@"/tmp/[^/]+/chats/[^/]+\.json$"))return [];
            if(new FileInfo(file).Length>SpendCompression.MaximumBytes)throw new IOException("Gemini 日志超过大小上限。");using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;context.Session=SpendReaders.S(root,"sessionId","session_id")??context.Session;
            var ordinal=0;foreach(var message in JsonTools.Array(root,"messages")){ct.ThrowIfCancellationRequested();ordinal++;if(SpendReaders.S(message,"type")!="gemini")continue;Submit(JsonTools.At(message,"tokens"),SpendReaders.D(message,"timestamp","created_at"),SpendReaders.S(message,"model"),"gemini:session:"+context.Session+":"+(SpendReaders.S(message,"id")??file+":"+ordinal),true,false);}
        }
        else
        {
            var ordinal=0;foreach(var row in JsonLines(file,notes,ct))
            {
                ordinal++;if(SpendReaders.S(row,"type")=="init"){context.Model=SpendReaders.S(row,"model")??context.Model;context.Session=SpendReaders.S(row,"session_id","sessionId")??context.Session;continue;}
                context.Session=SpendReaders.S(row,"session_id","sessionId")??context.Session;var id=SpendReaders.S(row,"id")??file+":"+ordinal;var time=SpendReaders.D(row,"timestamp","created_at");
                var u=JsonTools.At(row,"tokens");if(u.ValueKind==JsonValueKind.Object){Submit(u,time,SpendReaders.S(row,"model")??(context.Model=="unknown"?null:context.Model),"gemini:line:"+id,false,true);continue;}
                var stats=JsonTools.At(row,"stats");if(stats.ValueKind!=JsonValueKind.Object)stats=JsonTools.At(row,"result.stats");if(stats.ValueKind!=JsonValueKind.Object)continue;
                var models=JsonTools.At(stats,"models");if(models.ValueKind==JsonValueKind.Object)foreach(var entry in models.EnumerateObject()){u=JsonTools.At(entry.Value,"tokens");if(u.ValueKind!=JsonValueKind.Object)u=entry.Value;Submit(u,SpendReaders.D(entry.Value,"timestamp")??time,entry.Name,"gemini:line:"+id+":"+entry.Name,false,false);}
                else Submit(stats,SpendReaders.D(stats,"timestamp")??time,SpendReaders.S(stats,"model")??(context.Model=="unknown"?null:context.Model),"gemini:line:"+id,false,false);
            }
        }
        return incomplete?records.Select(r=>r with{IsPartial=true}).ToList():records;
    }
    static long CountAny(JsonElement u)=>u.ValueKind==JsonValueKind.Object?u.EnumerateObject().Sum(p=>Math.Min(SpendReaders.N(u,p.Name),long.MaxValue/32)):0;
    static SpendRecord? GeminiUsage(JsonElement u,SpendReaders.Context context,DateTimeOffset? at,string? model,string id,bool session,bool wrapper)
    {
        if(u.ValueKind!=JsonValueKind.Object||at is null||model is null)return null;
        var inputKeys=new[]{"input","prompt","input_tokens","prompt_tokens","promptTokenCount"};var inputKey=inputKeys.FirstOrDefault(k=>SpendReaders.Has(u,k));
        var input=SpendReaders.N(u,inputKeys);var output=SpendReaders.N(u,"output","candidates","output_tokens","completion_tokens","candidatesTokenCount");var read=SpendReaders.N(u,"cached","cached_tokens","cachedContentTokenCount");var reasoning=SpendReaders.N(u,"thoughts","reasoning","thoughts_tokens");var tool=SpendReaders.N(u,"tool","tool_tokens");var total=SpendReaders.N(u,"total","totalTokenCount","total_tokens");
        var hasTotal=new[]{"total","totalTokenCount","total_tokens"}.Any(k=>SpendReaders.Has(u,k));var hasReasoning=new[]{"thoughts","reasoning","thoughts_tokens"}.Any(k=>SpendReaders.Has(u,k));var hasTool=new[]{"tool","tool_tokens"}.Any(k=>SpendReaders.Has(u,k));
        var anyKind=inputKey is not null||new[]{"output","candidates","output_tokens","completion_tokens","candidatesTokenCount","cached","cached_tokens","cachedContentTokenCount","thoughts","reasoning","thoughts_tokens","tool","tool_tokens"}.Any(k=>SpendReaders.Has(u,k));long unclassified=0;
        if(!anyKind){unclassified=total;input=output=read=reasoning=tool=0;}
        else if(session){var raw=input+tool;var inclusive=hasTotal?total==raw+output+reasoning&&total!=raw+output+reasoning+read:inputKey is not null&&inputKey!="input";input=inclusive?Math.Max(0,raw-read):raw;}
        else{var inclusive=wrapper||inputKey is not null&&inputKey!="input";if(!hasReasoning&&!hasTool&&hasTotal)unclassified=Math.Max(0,total-(inclusive?input:input+read)-output);input=(inclusive?Math.Max(0,input-read):input)+tool;}
        output+=reasoning;if(input+output+read+unclassified==0)return null;
        return new("gemini",id,at.Value,model,input,output,read,Unclassified:unclassified,Session:context.Session);
    }
    internal static List<SpendRecord> MiniMax(string file,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var context=new SpendReaders.Context("mcode",file,notes);
        if(context.Native)
        {
            if(Path.GetFileName(file)!="messages.jsonl")return [];context.Session=Path.GetFileName(Path.GetDirectoryName(file))!;
            foreach(var row in JsonLines(file,notes,ct)){var message=JsonTools.At(row,"message");if(SpendReaders.S(message,"role")!="assistant"||SpendReaders.S(message,"model") is not {} model)continue;var r=SpendReaders.Build(JsonTools.At(message,"usage"),context,SpendReaders.D(message,"timestamp")??SpendReaders.D(row,"timestamp"),model,"mcode:message:"+(SpendReaders.S(row,"message_id")??file+":"+records.Count));if(r is not null)records.Add(r);}
            return records;
        }
        var turns=new Dictionary<string,List<JsonElement>>();var positions=new Dictionary<(string Turn,string Id),int>();var results=new Dictionary<string,(string Session,string Model)>();
        foreach(var row in JsonLines(file,notes,ct))
        {
            var type=SpendReaders.S(row,"type");var message=JsonTools.At(row,"message");
            if(type=="message"&&SpendReaders.S(message,"role")=="assistant"&&SpendReaders.S(message,"turnId") is {} turn&&JsonTools.At(message,"usage").ValueKind==JsonValueKind.Object)
            {
                if(!turns.TryGetValue(turn,out var messages)){messages=[];turns[turn]=messages;}
                var identity=SpendReaders.S(message,"id","messageId","responseId");if(identity is not null&&positions.TryGetValue((turn,identity),out var index))messages[index]=message;else{if(identity is not null)positions[(turn,identity)]=messages.Count;messages.Add(message);}
            }
            else if(type=="exec.result"&&SpendReaders.S(row,"sessionId") is {} session&&SpendReaders.S(row,"turnId") is {} turnId&&SpendReaders.S(row,"model.providerId") is not null&&SpendReaders.S(row,"model.modelId") is {} model)results[turnId]=(session,model);
        }
        foreach(var(turn,messages) in turns)
        {
            if(!results.TryGetValue(turn,out var result))continue;context.Session=result.Session;for(var i=0;i<messages.Count;i++){var message=messages[i];var r=SpendReaders.Build(JsonTools.At(message,"usage"),context,SpendReaders.D(message,"timestamp"),result.Model,"mcode:"+result.Session+":"+turn+":"+i+":"+SpendReaders.Hash(JsonTools.At(message,"usage").GetRawText()));if(r is not null)records.Add(r);}
        }
        return records;
    }
}
