// Tencent Buddy transcript/log semantics translated from Pulse 1.12.2, Apache-2.0.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
internal static class SpendTencentReaders
{
    internal static SpendRecord? Transcript(JsonElement root,SpendReaders.Context c)
    {
        var type=SpendReaders.S(root,"type");if(!(type=="message"&&SpendReaders.S(root,"role")=="assistant")&&type!="function_call")return null;
        if(SpendReaders.S(root,"status") is {} status&&status!="completed")return null;
        var u=JsonTools.At(root,"message.usage");if(u.ValueKind!=JsonValueKind.Object)u=JsonTools.At(root,"providerData.usage");if(u.ValueKind!=JsonValueKind.Object)u=JsonTools.At(root,"providerData.rawUsage");
        var at=SpendReaders.D(root,"timestamp");var session=SpendReaders.S(root,"sessionId")??c.Session;var id=SpendReaders.S(root,"providerData.messageId","providerData.traceId","id")??c.File+":"+c.Line;
        return Record(c.Agent,u,at,SpendReaders.S(root,"providerData.model","providerData.requestModelId","message.model")??c.Agent,c.Agent+":"+session+":"+id,session,SpendReaders.S(root,"cwd"),"transcript");
    }
    static SpendRecord? Record(string agent,JsonElement u,DateTimeOffset? at,string model,string id,string? session,string? project,string origin)
    {
        if(at is null||u.ValueKind!=JsonValueKind.Object)return null;
        var input=SpendReaders.N(u,"input_tokens","inputTokens","prompt_tokens");var output=SpendReaders.N(u,"output_tokens","outputTokens","completion_tokens");var read=SpendReaders.N(u,"cache_read_input_tokens","cacheReadInputTokens","cacheTokens","prompt_cache_hit_tokens","cached_tokens","cachedReadTokens");var write=SpendReaders.N(u,"cache_creation_input_tokens","cacheCreationInputTokens","cachedWriteTokens","prompt_cache_write_tokens","cacheCreationTokens");var reasoning=SpendReaders.N(u,"completion_thinking_tokens","completionThinkingTokens","reasoningTokens");var total=SpendReaders.N(u,"total_tokens","totalTokens");
        var exclusive=SpendReaders.N(u,"cachedMissTokens","cacheMissTokens");var hasExclusive=JsonTools.At(u,"cachedMissTokens").ValueKind!=JsonValueKind.Undefined||JsonTools.At(u,"cacheMissTokens").ValueKind!=JsonValueKind.Undefined;
        var disjoint=(hasExclusive?exclusive:input)+output+read+write+reasoning;
        if(!hasExclusive&&total>0&&total==input+output&&total!=disjoint)input=Math.Max(0,input-read-write);else if(hasExclusive)input=exclusive;
        if(reasoning>0&&total>0&&total==disjoint)output+=reasoning;
        var unclassified=Math.Max(0,total-input-output-read-write);if(input+output+read+write+unclassified==0)return null;
        return new(agent,id,at.Value,model,input,output,read,write,Unclassified:unclassified,Session:session,Origin:origin,Project:project);
    }
    internal static List<SpendRecord> ExtensionLog(string agent,string file,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var models=new Dictionary<string,string>();var lineIndex=0;
        foreach(var line in File.ReadLines(file))
        {
            ct.ThrowIfCancellationRequested();lineIndex++;var marker=Regex.Match(line,@"\[(CraftInvokableAgent|AgentReporter)\]\s*\[([^\]]+)\]");if(!marker.Success)continue;var id=marker.Groups[2].Value;
            if(marker.Groups[1].Value=="CraftInvokableAgent"){var match=Regex.Match(line,@"Model prepared:.*\(([^)]+)\)");if(match.Success)models[id]=match.Groups[1].Value.Trim();continue;}
            var timestamp=Regex.Match(line,@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?");if(!timestamp.Success||!DateTimeOffset.TryParse(timestamp.Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out var at))continue;
            var start=line.IndexOf("usage:",StringComparison.Ordinal);if(start<0)continue;var json=line.IndexOf('{',start);if(json<0)continue;
            try{var reader=new Utf8JsonReader(Encoding.UTF8.GetBytes(line[json..]));using var document=JsonDocument.ParseValue(ref reader);var r=Record(agent,document.RootElement,at,models.GetValueOrDefault(id,agent),file+":"+lineIndex,id,Path.GetFileName(file).Split("__")[0],"extensionLog");if(r is not null)records.Add(r);}catch(JsonException){ }
        }
        return records;
    }
}
