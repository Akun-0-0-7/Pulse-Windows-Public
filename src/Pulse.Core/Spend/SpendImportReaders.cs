// Export and append/patch semantics translated from Pulse 1.12.2, Apache-2.0.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Pulse.Core;
internal static class SpendImportReaders
{
 internal static List<SpendRecord> CursorCsv(string file)
 {
     var rows=Csv(File.ReadAllText(file)); var result=new List<SpendRecord>(); if(rows.Count<2)return result;
     var headers=rows[0].Select((value,index)=>(value,index)).ToDictionary(x=>x.value,x=>x.index,StringComparer.OrdinalIgnoreCase);
     if(!new[]{"Date","Model","Input (w/ Cache Write)","Input (w/o Cache Write)","Cache Read","Output Tokens"}.All(headers.ContainsKey))return result;
     var occurrences=new Dictionary<string,int>();var name=Path.GetFileNameWithoutExtension(file);var scope=name.StartsWith("usage.",StringComparison.OrdinalIgnoreCase)?name[6..]:"unscoped";
     foreach(var row in rows.Skip(1))
     {
         string? Value(string key)=>headers.TryGetValue(key,out var at)&&at<row.Count?row[at]:null;
         long Count(string key)=>long.TryParse(Value(key)?.Replace(",",""),NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)&&n>=0?n:0;
         if(!DateTimeOffset.TryParse(Value("Date"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)||string.IsNullOrWhiteSpace(Value("Model")))continue;
         var input=Count("Input (w/o Cache Write)");var write=Count("Input (w/ Cache Write)");var read=Count("Cache Read");var output=Count("Output Tokens");if(input+write+read+output==0)continue;
         var model=Value("Model")!;var session=string.IsNullOrWhiteSpace(Value("Cloud Agent ID"))?null:Value("Cloud Agent ID");
         var signature=SpendReaders.Hash($"{scope}:{at:O}:{model}:{input}:{write}:{read}:{output}:{session}");var ordinal=occurrences.GetValueOrDefault(signature)+1;occurrences[signature]=ordinal;
         result.Add(new("cursor","csv:"+scope+":"+signature+":"+ordinal,at,model,input,output,read,write,IsPartial:true,IsAggregate:true,Session:session,Origin:"imported"));
     }
     return result;
 }
 static List<List<string>> Csv(string text)
 {
     var rows=new List<List<string>>();var row=new List<string>();var field=new StringBuilder();bool quoted=false;
     for(var i=0;i<text.Length;i++)
     {
         var ch=text[i];if(ch=='"'){if(quoted&&i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else quoted=!quoted;}
         else if(ch==','&&!quoted){row.Add(field.ToString());field.Clear();}
         else if((ch=='\r'||ch=='\n')&&!quoted){if(ch=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;row.Add(field.ToString());field.Clear();rows.Add(row);row=[];}
         else field.Append(ch);
     }
     if(field.Length>0||row.Count>0){row.Add(field.ToString());rows.Add(row);}return rows;
 }
 sealed record OtelCandidate(int Lane,string? Trace,string? Response,SpendRecord Record);
 internal static List<SpendRecord> CopilotOtel(string file,List<string> notes,CancellationToken ct)
 {
     var rows=new List<JsonElement>(); foreach(var line in File.ReadLines(file)){ct.ThrowIfCancellationRequested();try{using var doc=JsonDocument.Parse(line);rows.Add(doc.RootElement.Clone());}catch(JsonException){notes.Add("copilot：OpenTelemetry 日志含未完成或无效行。");}}
     string? Attribute(JsonElement row,params string[] keys) {var a=JsonTools.At(row,"attributes");foreach(var key in keys)if(a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()))return value.GetString();return null;}
     long Count(JsonElement row,params string[] keys){var a=JsonTools.At(row,"attributes");foreach(var key in keys)if(a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var n)&&n>0)return n;return 0;}
     static string? Valid(string? id)=>string.IsNullOrEmpty(id)||id.All(x=>x=='0')?null:id;
     string? Trace(JsonElement row)=>Valid(SpendReaders.S(row,"traceId","trace_id","spanContext.traceId"));
     var contexts=new Dictionary<string,(string? Model,string? Session,string? Response)>();
     foreach(var row in rows)if(Trace(row) is {} trace){var previous=contexts.GetValueOrDefault(trace);contexts[trace]=(previous.Model??Attribute(row,"gen_ai.response.model","gen_ai.request.model"),previous.Session??Attribute(row,"gen_ai.conversation.id","copilot_chat.session_id","copilot_chat.chat_session_id","session.id","github.copilot.interaction_id"),previous.Response??Attribute(row,"gen_ai.response.id"));}
     var candidates=new List<OtelCandidate>();var index=0;
     foreach(var row in rows)
     {
         ct.ThrowIfCancellationRequested();index++;var operation=Attribute(row,"gen_ai.operation.name");var name=SpendReaders.S(row,"name");var eventName=Attribute(row,"event.name");var body=SpendReaders.S(row,"body","_body")??"";
         var span=SpendReaders.S(row,"type")=="span"||(SpendReaders.S(row,"type") is null&&name is not null);
         var lane=span?(operation=="chat"||name?.StartsWith("chat ")==true?0:operation=="invoke_agent"||name?.StartsWith("invoke_agent ")==true?3:-1):eventName=="gen_ai.client.inference.operation.details"||body.StartsWith("GenAI inference:")?1:eventName=="copilot_chat.agent.turn"||body.StartsWith("copilot_chat.agent.turn")?2:-1;
         if(lane<0)continue;var trace=Trace(row);var shared=trace is null?default:contexts.GetValueOrDefault(trace);var response=Attribute(row,"gen_ai.response.id")??shared.Response;
         var model=Attribute(row,"gen_ai.response.model","gen_ai.request.model")??shared.Model;var session=Attribute(row,"gen_ai.conversation.id","copilot_chat.session_id","copilot_chat.chat_session_id","session.id","github.copilot.interaction_id")??shared.Session;
         var at=SpendReaders.D(row,"startTime","time","timestamp","observedTimestamp");
         if(at is null&&SpendReaders.D(row,"endTime") is {} end)at=end.AddMilliseconds(-(JsonTools.Number(row,"duration")??0));
         if(at is null&&JsonTools.Number(row,"timeUnixNano") is {} nanos&&nanos>0&&nanos/1_000_000_000<253402300799)at=DateTimeOffset.FromUnixTimeSeconds((long)(nanos/1_000_000_000));
         if(at is null&&JsonTools.Number(row,"hrTime.0") is {} seconds&&seconds>0)at=DateTimeOffset.FromUnixTimeSeconds((long)seconds).AddTicks((long)((JsonTools.Number(row,"hrTime.1")??0)/100));
         if(model is null||at is null)continue;
         var input=Count(row,"gen_ai.usage.input_tokens");var output=Count(row,"gen_ai.usage.output_tokens");var read=Count(row,"gen_ai.usage.cache_read.input_tokens","gen_ai.usage.cache_read_input_tokens");var write=Count(row,"gen_ai.usage.cache_write.input_tokens","gen_ai.usage.cache_creation.input_tokens","gen_ai.usage.cache_write_input_tokens","gen_ai.usage.cache_creation_input_tokens");if(output==0)output=Count(row,"gen_ai.usage.reasoning.output_tokens","gen_ai.usage.reasoning_tokens");
         var unclassified=input+output+read+write==0?Count(row,"gen_ai.usage.total_tokens","gen_ai.usage.total.tokens","total_tokens"):0;if(input+output+read+write+unclassified==0)continue;
         var spanId=Valid(SpendReaders.S(row,"spanId","span_id","spanContext.spanId"));string id;
         if(spanId is not null&&(trace??session) is {} spanScope)id=$"{lane}:{spanScope}:{spanId}";
         else if(lane==1&&response is not null)id="log-response:"+response;
         else if(lane==2&&trace is not null)id="agent-turn:"+trace+":"+(Attribute(row,"turn.index","copilot_chat.turn.index")??index.ToString());
         else id="unnamed:"+file+":"+index;
         candidates.Add(new(lane,trace,response,new("copilot","otel:"+id,at.Value,model,Math.Max(0,input-read),output,read,write,Unclassified:unclassified,IsPartial:trace is null&&response is null,Session:session)));
     }
     return candidates.Where(c=>!candidates.Any(higher=>higher.Lane<c.Lane&&(c.Trace is not null&&higher.Trace==c.Trace||c.Response is not null&&higher.Response==c.Response))).Select(x=>x.Record).ToList();
 }
 internal static List<SpendRecord> CopilotVsCode(string file,List<string> notes,CancellationToken ct)
 {
     var requests=new JsonArray();
     foreach(var line in File.ReadLines(file))
     {
         ct.ThrowIfCancellationRequested();JsonObject? row;try{row=JsonNode.Parse(line) as JsonObject;}catch(JsonException){notes.Add("copilot：VS Code 日志含未完成或无效行。");continue;}if(row is null)continue;
         var kind=row["kind"]?.GetValue<int>();
         if(kind==0&&row["v"]?["requests"] is JsonArray initial)foreach(var item in initial)requests.Add(item?.DeepClone());
         else if(kind==2&&row["k"] is JsonArray appendPath&&appendPath.Count==1&&appendPath[0]?.GetValue<string>()=="requests"&&row["v"] is JsonArray appended)foreach(var item in appended)requests.Add(item?.DeepClone());
         else if(kind==1&&row["k"] is JsonArray path&&path.Count>=2&&path[0]?.GetValue<string>()=="requests"&&int.TryParse(path[1]?.ToJsonString(),out var index)&&index>=0&&index<requests.Count)
         {
             if(path.Count==2)requests[index]=row["v"]?.DeepClone();
             else Mutate(requests[index],path.Skip(2).ToList(),row["v"]?.DeepClone());
         }
     }
     var session=Path.GetFileNameWithoutExtension(file);var records=new List<SpendRecord>();var workspaceFile=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(file))!,"workspace.json");string? project=null;
     if(File.Exists(workspaceFile)){using var doc=JsonDocument.Parse(File.ReadAllText(workspaceFile));project=SpendReaders.S(doc.RootElement,"folder","workspace");if(Uri.TryCreate(project,UriKind.Absolute,out var uri)&&uri.IsFile)project=uri.LocalPath;}
     foreach(var node in requests)
     {
         if(node is null)continue;using var doc=JsonDocument.Parse(node.ToJsonString());var row=doc.RootElement;var metadata=JsonTools.At(row,"result.metadata");var model=SpendReaders.S(metadata,"resolvedModel");var modelId=SpendReaders.S(row,"modelId");if(model is null&&modelId?.StartsWith("copilot/")!=true)continue;model??=modelId![8..];
         var at=SpendReaders.D(row,"timestamp")??SpendReaders.D(metadata,"timestamp");if(at is null)continue;var prompt=SpendReaders.N(row,"promptTokens");if(prompt==0)prompt=SpendReaders.N(metadata,"promptTokens");var output=SpendReaders.N(row,"completionTokens");if(output==0)output=SpendReaders.N(metadata,"outputTokens");output+=JsonTools.Array(metadata,"toolCallRounds").Sum(x=>SpendReaders.N(x,"thinking.tokens"));if(prompt+output==0)continue;
         records.Add(new("copilot","vscode:"+session+":"+(SpendReaders.S(row,"requestId")??records.Count.ToString()),at.Value,model,Output:output,Unclassified:prompt,Session:session,Project:project));
     }
     return records;
 }
 static void Mutate(JsonNode? target,List<JsonNode?> path,JsonNode? value)
 {
     if(target is null||path.Count==0)return;
     if(target is JsonObject obj&&path[0] is JsonValue keyNode&&keyNode.TryGetValue<string>(out var key))
     {
         if(path.Count==1)obj[key]=value;else{if(obj[key] is null)obj[key]=int.TryParse(path[1]?.ToJsonString(),out _)?new JsonArray():new JsonObject();Mutate(obj[key],path.Skip(1).ToList(),value);}
     }
     else if(target is JsonArray array&&int.TryParse(path[0]?.ToJsonString(),out var index)&&index>=0&&index<array.Count){if(path.Count==1)array[index]=value;else Mutate(array[index],path.Skip(1).ToList(),value);}
 }
 internal static List<SpendRecord> ReconcileCopilot(List<SpendRecord> records)
 {
     var otel=records.Where(x=>x.Identity.StartsWith("otel:")).ToList();var desktop=records.Where(x=>x.Identity.StartsWith("desktop:")).ToList();var sessions=otel.Where(x=>x.Session is not null).Select(x=>x.Session!).ToHashSet();
     foreach(var session in sessions){var lifetime=desktop.Where(x=>x.Session==session).ToList();if(lifetime.Sum(x=>x.TotalTokens??0)>otel.Where(x=>x.Session==session).Sum(x=>x.TotalTokens??0)||lifetime.Any(x=>x.IsPartial))otel=otel.Select(x=>x.Session==session?x with{IsPartial=true}:x).ToList();}
     var result=otel.Concat(desktop.Where(x=>x.Session is null||!sessions.Contains(x.Session))).ToList();var instants=result.Select(x=>(x.Session,x.At)).ToHashSet();result.AddRange(records.Where(x=>x.Identity.StartsWith("vscode:")&&!instants.Contains((x.Session,x.At))));return result;
 }
}
