using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public sealed record PromptCacheSession(string Session,DateTimeOffset LastRequest,TimeSpan Lifetime,bool IsMinimum)
{
    public DateTimeOffset ExpiresAt=>LastRequest+Lifetime;
}
public static class PromptCacheService
{
    public static PromptCacheSession? Parse(string provider,string tail,string session)
    {
        var rows=new List<JsonElement>();foreach(var line in tail.Split('\n'))try{using var doc=JsonDocument.Parse(line);rows.Add(doc.RootElement.Clone());}catch(JsonException){}
        if(provider=="codex"){
            string? model=null;DateTimeOffset? input=null;PromptCacheSession? found=null;
            foreach(var row in rows){var type=JsonTools.String(row,"type");var kind=JsonTools.String(row,"payload.type");var at=JsonTools.Date(row,"timestamp");
                if(type=="turn_context")model=JsonTools.String(row,"payload.model")??model;
                if(type=="session_meta")model=JsonTools.String(row,"payload.base_instructions.provenance.model")??model;
                if(at is {} sent&&(type=="response_item"&&(kind is "function_call_output" or "custom_tool_call_output"||kind=="message"&&JsonTools.String(row,"payload.role")=="user")||type=="event_msg"&&kind is "user_message" or "task_started"))input=sent;
                var usage=type=="token_usage_record"?JsonTools.At(row,"payload.usage"):kind=="token_count"?JsonTools.At(row,"payload.info.last_token_usage"):default;
                if(at is {} replied&&(JsonTools.Number(usage,"cached_input_tokens")>0||JsonTools.Number(usage,"cache_write_input_tokens")>0)){
                    var match=Regex.Match(model??"","^gpt-(\\d+)\\.(\\d+)(?:[-.]|$)",RegexOptions.IgnoreCase);var supported=match.Success&&(int.Parse(match.Groups[1].Value)>5||int.Parse(match.Groups[1].Value)==5&&int.Parse(match.Groups[2].Value)>=6);
                    found=supported?new(session,input is {} request&&replied-request>=TimeSpan.Zero&&replied-request<=TimeSpan.FromHours(1)?request:replied,TimeSpan.FromMinutes(30),true):null;
                }
            }return found;
        }
        if(provider!="claudeCode")return null;
        DateTimeOffset? last=null;
        foreach(var row in rows.AsEnumerable().Reverse()){
            if(JsonTools.String(row,"type")!="assistant"||JsonTools.Bool(row,"isSidechain")==true)continue;var usage=JsonTools.At(row,"message.usage");
            if(last is null){if(!(JsonTools.Number(usage,"cache_read_input_tokens")>0||JsonTools.Number(usage,"cache_creation_input_tokens")>0)||JsonTools.Date(row,"timestamp") is not {} reply)continue;last=reply;var parent=JsonTools.String(row,"parentUuid");for(var depth=0;depth<64&&parent is not null;depth++){var prior=rows.LastOrDefault(r=>JsonTools.String(r,"uuid")==parent);if(prior.ValueKind!=JsonValueKind.Object)break;if(JsonTools.String(prior,"type")!="assistant"){if(JsonTools.Date(prior,"timestamp") is {} request&&reply-request>=TimeSpan.Zero&&reply-request<=TimeSpan.FromHours(1))last=request;break;}parent=JsonTools.String(prior,"parentUuid");}}
            if(JsonTools.Number(usage,"cache_creation.ephemeral_1h_input_tokens")>0)return new(session,last.Value,TimeSpan.FromHours(1),false);
            if(JsonTools.Number(usage,"cache_creation.ephemeral_5m_input_tokens")>0)return new(session,last.Value,TimeSpan.FromMinutes(5),false);
        }return null;
    }
    public static List<PromptCacheSession> Read(string provider,string home,DateTimeOffset now)
    {
        var folder=provider switch{"codex"=>".codex/sessions","claudeCode"=>".claude/projects",_=>null};if(folder is null)return [];var root=Path.Combine(home,folder);if(!Directory.Exists(root))return [];
        var result=new List<PromptCacheSession>();try{foreach(var file in Directory.EnumerateFiles(root,"*.jsonl",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint}).Select(f=>new FileInfo(f)).Where(f=>now-f.LastWriteTimeUtc<TimeSpan.FromHours(2)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(80)){
            try{using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var offset=Math.Max(0,stream.Length-512*1024);stream.Seek(offset,SeekOrigin.Begin);using var reader=new StreamReader(stream);var tail=reader.ReadToEnd();if(offset>0&&tail.IndexOf('\n') is var cut&&cut>=0)tail=tail[(cut+1)..];if(Parse(provider,tail,file.Name) is {} session&&session.ExpiresAt>now)result.Add(session);}catch(IOException){}catch(UnauthorizedAccessException){}
        }}catch(IOException){}catch(UnauthorizedAccessException){}return result.OrderBy(s=>s.ExpiresAt).ToList();
    }
}
