// Native field meanings translated from Pulse 1.12.2 readers, Apache-2.0.
using System.Globalization;
using System.Text.Json;
namespace Pulse.Core;

/// <summary>Codebuff response copies, Jcode session journals, and Devin Desktop ACP captures.</summary>
internal static class SpendMissingReaders
{
    internal static List<SpendRecord> Read(string agent,IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var files=SpendReaders.Files(roots,notes).ToList();
        var output=new List<SpendRecord>();
        var sessions=agent=="devin-desktop"?DevinSessions(files,notes,ct):[];
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if(agent=="codebuff"&&Path.GetFileName(file)=="chat-messages.json")output.AddRange(Codebuff(file,ct));
                else if(agent=="jcode"&&Path.GetFileName(file).StartsWith("session_",StringComparison.Ordinal)&&Path.GetExtension(file)==".json")output.AddRange(Jcode(file,notes,ct));
                else if(agent=="devin-desktop"&&Path.GetExtension(file)==".ndjson")output.AddRange(Devin(file,sessions,notes,ct));
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                notes.Add(agent+"：读取失败（"+e.GetType().Name+"）："+Path.GetFileName(file));
            }
        }
        // A session file and its replayed journal describe the same message, even when JSON property order differs.
        return output.DistinctBy(r=>r.Identity,StringComparer.Ordinal).ToList();
    }

    static IEnumerable<SpendRecord> Codebuff(string file,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(ReadBounded(file));
        if(document.RootElement.ValueKind!=JsonValueKind.Array)yield break;
        var parts=Path.GetFullPath(file).Replace('\\','/').Split('/');
        var location=Array.LastIndexOf(parts,"projects");
        var chat=Path.GetFileName(Path.GetDirectoryName(file))!;
        var channel=location>0?parts[location-1]:parts.Length>3?parts[^4]:"unknown";
        var project=location>=0&&location+1<parts.Length?parts[location+1]:parts.Length>2?parts[^3]:"unknown";
        var session=channel+"/"+project+"/"+chat;
        var index=0;
        foreach(var message in document.RootElement.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();var rowIndex=index++;
            if(!new[]{SpendReaders.S(message,"variant"),SpendReaders.S(message,"role")}.Any(x=>x?.ToLowerInvariant() is "ai" or "agent" or "assistant"))continue;
            var sources=new List<JsonElement>();
            AddObject(sources,message,"metadata.usage");AddObject(sources,message,"metadata.codebuff.usage");
            var history=JsonTools.Array(message,"metadata.runState.sessionState.mainAgentState.messageHistory").Reverse().ToList();
            foreach(var row in history){AddObject(sources,row,"providerOptions.usage");AddObject(sources,row,"providerOptions.codebuff.usage");}
            if(sources.Count==0)continue;
            var input=Merged(sources,"inputTokens","input_tokens","promptTokens","prompt_tokens");
            var reply=Merged(sources,"outputTokens","output_tokens","completionTokens","completion_tokens");
            var read=Merged(sources,"cacheReadInputTokens","cache_read_input_tokens","cachedTokensCreated","cached_tokens_created");
            if(read==0)read=NestedCacheRead(sources);
            var write=Merged(sources,"cacheCreationInputTokens","cache_creation_input_tokens","cacheCreationTokens","cache_creation_tokens");
            input=Math.Max(0,input-read);
            if(input+reply+read+write==0)continue;
            var model=SpendReaders.S(message,"metadata.model")??history.Select(r=>SpendReaders.S(r,"providerOptions.codebuff.model")).FirstOrDefault(x=>x is not null)??sources.Select(s=>SpendReaders.S(s,"model")).FirstOrDefault(x=>x is not null);
            if(model is null)continue;
            var at=EventTime(message,"timestamp","createdAt","metadata.timestamp")??ChatTime(chat);
            if(at is null)continue;
            var identity=SpendReaders.S(message,"id")??$"{rowIndex}:{at.Value.ToUnixTimeMilliseconds()}:{model}:{input}:{write}:{read}:{reply}";
            yield return new("codebuff","codebuff:"+session+":"+identity,at.Value,model,input,reply,read,write,Session:session,Origin:"native");
        }
    }

    static IEnumerable<SpendRecord> Jcode(string file,List<string> notes,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(ReadBounded(file));
        var session=document.RootElement;if(session.ValueKind!=JsonValueKind.Object)yield break;
        var sessionId=SpendReaders.S(session,"id")??Path.GetFileNameWithoutExtension(file);
        var model=SpendReaders.S(session,"model");
        var workspace=SpendReaders.S(session,"working_dir");
        var messages=JsonTools.Array(session,"messages").Select(m=>(Message:m,Model:model)).ToList();
        var journal=Path.Combine(Path.GetDirectoryName(file)!,Path.GetFileNameWithoutExtension(file)+".journal.jsonl");
        if(File.Exists(journal))
        {
            foreach(var line in JsonLines(journal,"jcode",notes,ct))
            {
                model=SpendReaders.S(line,"meta.model")??model;
                messages.AddRange(JsonTools.Array(line,"append_messages").Select(m=>(m,model)));
            }
        }
        foreach(var entry in messages)
        {
            ct.ThrowIfCancellationRequested();var message=entry.Message;var usage=JsonTools.At(message,"token_usage");
            if(usage.ValueKind!=JsonValueKind.Object||entry.Model is null||EventTime(message,"timestamp") is not {} at)continue;
            var statedInput=Count(usage,"input_tokens")??0;var reply=Count(usage,"output_tokens")??0;
            var read=Count(usage,"cache_read_input_tokens")??0;var write=Count(usage,"cache_creation_input_tokens")??0;
            var input=statedInput;long unclassified=0;var partial=Count(usage,"reasoning_output_tokens")>0;
            if(SpendReaders.Has(usage,"cache_creation_input_tokens")) { /* Anthropic explicitly reports disjoint input and cache. */ }
            else if(new[]{"prompt_tokens_details","promptTokensDetails","input_tokens_details","inputTokensDetails"}.Any(p=>SpendReaders.Has(usage,p)))input=Math.Max(0,input-Math.Min(read,input));
            else if(read>0){input=0;unclassified=statedInput;read=0;write=0;partial=true;}
            if(input+reply+read+write+unclassified==0)continue;
            var name=SpendReaders.S(message,"id")??$"{at.ToUnixTimeMilliseconds()}:{entry.Model}:{statedInput}:{Count(usage,"cache_creation_input_tokens")??0}:{Count(usage,"cache_read_input_tokens")??0}:{reply}";
            yield return new("jcode","jcode:"+sessionId+":"+name,at,entry.Model,input,reply,read,write,Unclassified:unclassified,IsPartial:partial,Session:sessionId,Origin:"native",Project:workspace);
        }
    }

    sealed record DevinSession(string Id,string? Model,string? Directory);
    static Dictionary<string,List<DevinSession>> DevinSessions(IEnumerable<string> files,List<string> notes,CancellationToken ct)
    {
        var sessions=new Dictionary<string,List<DevinSession>>(StringComparer.Ordinal);
        foreach(var database in files.Where(f=>Path.GetFileName(f)=="sessions.db"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var columns=SqliteReader.Query(database,"PRAGMA table_info(sessions)").Select(r=>r.GetValueOrDefault("name")).ToHashSet();
                if(!columns.Contains("id")||!columns.Contains("title"))continue;
                var sql="SELECT id,title,"+(columns.Contains("model")?"model":"NULL AS model")+","+(columns.Contains("working_directory")?"working_directory":"NULL AS working_directory")+" FROM sessions";
                foreach(var row in SqliteReader.Query(database,sql))
                {
                    if(NonBlank(row.GetValueOrDefault("id")) is not {} id||NonBlank(row.GetValueOrDefault("title")) is not {} title)continue;
                    if(!sessions.TryGetValue(title,out var matches))sessions[title]=matches=[];
                    matches.Add(new(id,NonBlank(row.GetValueOrDefault("model")),NonBlank(row.GetValueOrDefault("working_directory"))));
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.ExternalException or DllNotFoundException)
            {notes.Add("devin-desktop：会话身份查询失败（"+e.GetType().Name+"）："+Path.GetFileName(database));}
        }
        return sessions;
    }

    static IEnumerable<SpendRecord> Devin(string file,Dictionary<string,List<DevinSession>> lookup,List<string> notes,CancellationToken ct)
    {
        string? title=null,model=null;DateTimeOffset? at=null;long? input=null;long read=0,write=0,reply=0;var index=0;
        DevinSession? Resolve()=>title is not null&&lookup.TryGetValue(title,out var matches)&&matches.Count==1?matches[0]:null;
        foreach(var root in JsonLines(file,"devin-desktop",notes,ct))
        {
            var eventIndex=index++;var notification=JsonTools.At(root,"notification");if(notification.ValueKind!=JsonValueKind.Object)continue;
            if(SpendReaders.S(notification,"sessionUpdate")=="session_info_update"&&SpendReaders.S(notification,"title") is {} value)title=value;
            var meta=JsonTools.At(notification,"_meta");
            var canonicalInput=Count(meta,"cognition.ai/inputTokens");var canonicalRead=Count(meta,"cognition.ai/cachedReadTokens");
            var canonicalWrite=Count(meta,"cognition.ai/cachedWriteTokens");var canonicalOutput=Count(meta,"cognition.ai/outputTokens");
            if(SpendReaders.S(notification,"sessionUpdate")=="usage_update"&&(canonicalInput is not null||canonicalRead is not null||canonicalWrite is not null||canonicalOutput is not null))
            {
                input=canonicalInput??input;read=canonicalRead??read;write=canonicalWrite??write;reply+=canonicalOutput??0;
                model=SpendReaders.S(notification,"notification_model")??PropertyText(meta,"cognition.ai/model")??model;
                at=EventTime(notification,"created_at")??at;continue;
            }
            foreach(var path in new[]{"content.metadata.metrics","metadata.metrics","metrics","content.metadata","metadata"})
            {
                var usage=JsonTools.At(notification,path);
                if(!new[]{"input_tokens","output_tokens","cache_read_tokens","cache_creation_tokens"}.Any(k=>Count(usage,k) is not null))continue;
                var legacyInput=Count(usage,"input_tokens")??0;var legacyOutput=Count(usage,"output_tokens")??0;
                var legacyRead=Count(usage,"cache_read_tokens")??0;var legacyWrite=Count(usage,"cache_creation_tokens")??0;
                if(legacyInput+legacyOutput+legacyRead+legacyWrite==0)continue;
                var legacyAt=EventTime(notification,"content.metadata.created_at","metadata.created_at","created_at","timestamp");
                if(legacyAt is null)continue;
                var resolved=Resolve();var hinted=SpendReaders.S(notification,"content.metadata.generation_model","metadata.generation_model")??PropertyText(meta,"cognition.ai/model");
                yield return new("devin-desktop","devin-desktop:"+Path.GetFullPath(file)+":"+eventIndex,legacyAt.Value,DevinModel(hinted??resolved?.Model),legacyInput,legacyOutput,legacyRead,legacyWrite,Session:resolved?.Id??Path.GetFileNameWithoutExtension(file),Origin:"native",Project:resolved?.Directory);
                break;
            }
        }
        if(input is {} prompt&&at is {} timestamp&&Math.Max(0,prompt-read)+read+write+reply>0)
        {
            var resolved=Resolve();
            yield return new("devin-desktop","devin-desktop:"+Path.GetFullPath(file)+":usage",timestamp,DevinModel(model??resolved?.Model),Math.Max(0,prompt-read),reply,read,write,IsAggregate:true,Session:resolved?.Id??Path.GetFileNameWithoutExtension(file),Origin:"native",Project:resolved?.Directory);
        }
    }

    static string DevinModel(string? model)=>model is null or "adaptive"?"unknown":model;
    static string? NonBlank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    static void AddObject(List<JsonElement> list,JsonElement root,string path){var value=JsonTools.At(root,path);if(value.ValueKind==JsonValueKind.Object)list.Add(value);}
    static long Merged(IEnumerable<JsonElement> sources,params string[] aliases){foreach(var source in sources)foreach(var alias in aliases)if(Count(source,alias) is >0 and var value)return value;return 0;}
    static long NestedCacheRead(IEnumerable<JsonElement> sources)
    {
        foreach(var source in sources)
        foreach(var detail in new[]{"promptTokensDetails","prompt_tokens_details"})
        foreach(var alias in new[]{"cachedTokens","cached_tokens"})
            if(Count(JsonTools.At(source,detail),alias) is {} value)return value;
        return 0;
    }
    // Direct property access matters: canonical ACP keys literally contain a dot and a slash.
    static long? Count(JsonElement root,string key)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty(key,out var value))return null;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var integer)&&integer>=0)return integer;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var number)&&number>=0&&number<=long.MaxValue&&decimal.Truncate(number)==number)return (long)number;
        return value.ValueKind==JsonValueKind.String&&long.TryParse(value.GetString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out integer)&&integer>=0?integer:null;
    }
    static DateTimeOffset? EventTime(JsonElement root,params string[] paths){foreach(var path in paths)if(JsonTools.Date(root,path) is {} at&&at>DateTimeOffset.UnixEpoch)return at;return null;}
    static string? PropertyText(JsonElement root,string key)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?NonBlank(value.GetString()):null;
    static DateTimeOffset? ChatTime(string chat)
    {
        var marker=chat.IndexOf('T');if(marker<0)return null;
        var restored=chat[..(marker+1)]+chat[(marker+1)..].Replace('-',':');
        return DateTimeOffset.TryParse(restored,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)&&at> DateTimeOffset.UnixEpoch?at:null;
    }
    static string ReadBounded(string file){if(new FileInfo(file).Length>64*1024*1024)throw new IOException("日志超出 64 MiB。");return File.ReadAllText(file);}
    static IEnumerable<JsonElement> JsonLines(string file,string agent,List<string> notes,CancellationToken ct)
    {
        foreach(var text in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(text))continue;JsonElement? root=null;
            try{using var document=JsonDocument.Parse(text);root=document.RootElement.Clone();}
            catch(JsonException){notes.Add(agent+"：包含无法解析或未写完的日志行："+Path.GetFileName(file));}
            if(root is {} value)yield return value;
        }
    }
}
