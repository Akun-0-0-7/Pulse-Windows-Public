// Qwen, Droid, Amp and Hindsight formats translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
internal static class SpendSessionReaders
{
    internal static List<SpendRecord> Qwen(string file,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var digest=SpendReaders.Hash(File.ReadAllText(file));var project=Regex.Match(file.Replace('\\','/'),@"/projects/([^/]+)/").Groups[1].Value;var ordinal=0;
        foreach(var line in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();try
            {
                using var document=JsonDocument.Parse(line);var row=document.RootElement;if(SpendReaders.S(row,"type")!="assistant")continue;var u=JsonTools.At(row,"usageMetadata");if(u.ValueKind!=JsonValueKind.Object)continue;
                var prompt=SpendReaders.N(u,"promptTokenCount");var read=SpendReaders.N(u,"cachedContentTokenCount");var output=SpendReaders.N(u,"candidatesTokenCount")+SpendReaders.N(u,"thoughtsTokenCount");var total=JsonTools.Number(u,"totalTokenCount")??JsonTools.Number(u,"total")??JsonTools.Number(u,"total_tokens");long unknown=0;var input=Math.Max(0,prompt-read);
                if(total is {} reported){if(reported==prompt+output){input=Math.Max(0,prompt-read);}else if(reported==prompt+read+output)input=prompt;else{unknown=(long)Math.Max(0,reported);input=read=output=0;}}
                if(input+read+output+unknown<=0)continue;if(SpendReaders.S(row,"model") is not {} model||SpendReaders.D(row,"timestamp") is not {} at){notes.Add("qwen：有实测用量缺少模型或时间。");continue;}
                var session=SpendReaders.S(row,"sessionId")??project+"-"+Path.GetFileNameWithoutExtension(file);var id=SpendReaders.S(row,"id","messageId")??digest+":"+ordinal++;records.Add(new("qwen","qwen:"+session+":"+id,at,model,input,output,read,Unclassified:unknown,Session:session,Project:project));
            }catch(JsonException){notes.Add("qwen：包含无效或未完成行。");}
        }
        return records;
    }
    internal static SpendRecord? Droid(string file,List<string> notes)
    {
        if(!file.EndsWith(".settings.json",StringComparison.OrdinalIgnoreCase))return null;
        using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;var u=JsonTools.At(root,"tokenUsage");if(u.ValueKind!=JsonValueKind.Object)return null;
        var input=SpendReaders.N(u,"inputTokens");var output=SpendReaders.N(u,"outputTokens");var thinking=SpendReaders.N(u,"thinkingTokens");var read=SpendReaders.N(u,"cacheReadTokens");var write=SpendReaders.N(u,"cacheCreationTokens");var total=JsonTools.Number(u,"totalTokens")??JsonTools.Number(u,"total")??JsonTools.Number(u,"total_tokens");long unknown=0;bool partial=false;
        if(total is {} number)
        {
            var variants=new[]{(I:input,O:output+thinking),(I:Math.Max(0,input-read-write),O:output+thinking),(I:input,O:output),(I:Math.Max(0,input-read-write),O:output)};var found=false;
            foreach(var variant in variants)if(variant.I+variant.O+read+write==number){input=variant.I;output=variant.O;found=true;break;}
            if(!found){input=output=read=write=0;unknown=(long)Math.Max(0,number);}
        }
        else
        {
            if(!SpendReaders.Has(u,"outputTokens"))output=thinking;
            if(read+write>0){unknown=input;input=read=write=0;partial=true;}else partial=thinking>0;
        }
        if(input+output+read+write+unknown<=0)return null;
        var session=Path.GetFileName(file)[..^".settings.json".Length];var at=SpendReaders.D(root,"providerLockTimestamp");
        static string? Normalize(string? value){if(value is null)return null;if(value.StartsWith("custom:",StringComparison.Ordinal))value=value[7..];value=Regex.Replace(value,@"\[[^\]]*\]","").Trim();return value.Length>0?value:null;}
        var model=Normalize(SpendReaders.S(root,"model"));var sibling=Path.Combine(Path.GetDirectoryName(file)!,session+".jsonl");
        if(model is null&&File.Exists(sibling))foreach(var line in File.ReadLines(sibling)){var match=Regex.Match(line,"Model:\\s*([^\\s<\"\\\\]+)");if(match.Success){model=Normalize(match.Groups[1].Value);if(model is not null)break;}}
        if(model is null&&SpendReaders.S(root,"providerLock") is {} provider){provider=provider.ToLowerInvariant();model=(provider.Contains("anthropic")||provider.Contains("claude")?"claude":provider.Contains("openai")||provider.Contains("gpt")?"gpt":provider.Contains("google")||provider.Contains("gemini")?"gemini":provider.Contains("xai")||provider.Contains("grok")?"grok":provider)+"-unknown";}
        if(at is null||model is null){notes.Add("droid：有实测用量缺少模型或时间。");return null;}
        return new("droid","droid:"+session,at.Value,model,input,output,read,write,Unclassified:unknown,IsAggregate:true,IsPartial:partial,Session:session);
    }
    internal static SpendRecord? Hindsight(JsonElement root)
    {
        if(SpendReaders.S(root,"id") is not {} id||SpendReaders.S(root,"model") is not {} model||SpendReaders.D(root,"started_at") is not {} at)return null;
        if(JsonTools.Number(root,"total_tokens") is <=0)return null;var input=SpendReaders.N(root,"input_tokens");var output=SpendReaders.N(root,"output_tokens");if(input+output<=0)return null;
        return new("hindsight","hindsight:"+id,at,model,input,output,SpendReaders.N(root,"cached_tokens"),Session:SpendReaders.S(root,"trace_id")??id,Project:SpendReaders.S(root,"bank"),Origin:"imported");
    }
    sealed record AmpCall(string? Id,string Model,long Input,long Output,long Read,long Write,DateTimeOffset? At=null,string? From=null)
    {public long Total=>Input+Output+Read+Write;public bool Same(AmpCall other)=>Model==other.Model&&Input==other.Input&&Output==other.Output&&Read==other.Read&&Write==other.Write;}
    internal static List<SpendRecord> Amp(string file,List<string> notes,CancellationToken ct)
    {
        if(!Path.GetFileName(file).StartsWith("T-",StringComparison.Ordinal))return [];
        using var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;var session=SpendReaders.S(root,"id")??Path.GetFileNameWithoutExtension(file);var created=SpendReaders.D(root,"created");var messages=new List<AmpCall>();var events=new List<AmpCall>();bool incomplete=false;
        foreach(var row in JsonTools.Array(root,"messages")){ct.ThrowIfCancellationRequested();if(SpendReaders.S(row,"role")!="assistant")continue;var u=JsonTools.At(row,"usage");var call=new AmpCall(SpendReaders.S(row,"messageId"),SpendReaders.S(u,"model")??"",SpendReaders.N(u,"inputTokens"),SpendReaders.N(u,"outputTokens"),SpendReaders.N(u,"cacheReadInputTokens"),SpendReaders.N(u,"cacheCreationInputTokens"));if(call.Total<=0)continue;if(call.Model==""){incomplete=true;continue;}messages.Add(call);}
        var ordinal=0;foreach(var row in JsonTools.Array(root,"usageLedger.events")){ct.ThrowIfCancellationRequested();var u=JsonTools.At(row,"tokens");var call=new AmpCall(SpendReaders.S(row,"toMessageId"),SpendReaders.S(row,"model")??"",SpendReaders.N(u,"input"),SpendReaders.N(u,"output"),SpendReaders.N(u,"cacheReadInputTokens"),SpendReaders.N(u,"cacheCreationInputTokens"),SpendReaders.D(row,"timestamp"),SpendReaders.S(row,"fromMessageId")??ordinal.ToString());ordinal++;if(call.Total<=0)continue;if(call.Model==""){incomplete=true;continue;}events.Add(call);}
        var consumed=new HashSet<int>();var unmatched=new List<AmpCall>();
        foreach(var message in messages){var index=events.FindIndex(e=>message.Id is not null&&e.Id==message.Id&&!consumed.Contains(events.IndexOf(e)));if(index<0)index=events.FindIndex(e=>e.Same(message)&&!consumed.Contains(events.IndexOf(e)));if(index>=0)consumed.Add(index);else unmatched.Add(message);}
        var records=new List<SpendRecord>();
        foreach(var e in events){var at=e.At??created;if(at is null){incomplete=true;continue;}records.Add(new("amp","amp:"+session+":event:"+(e.Id??e.From),at.Value,e.Model,e.Input,e.Output,e.Read,e.Write,Session:session,IsAggregate:e.At is null));}
        foreach(var m in unmatched){if(created is null){incomplete=true;continue;}records.Add(new("amp","amp:"+session+":message:"+(m.Id??records.Count.ToString()),created.Value,m.Model,m.Input,m.Output,m.Read,m.Write,Session:session,IsAggregate:true));}
        if(incomplete)notes.Add("amp：有实测用量缺少模型或时间。");return incomplete?records.Select(r=>r with{IsPartial=true}).ToList():records;
    }
}
