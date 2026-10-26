// Native field meanings translated from Pulse 1.12.2 readers, Apache-2.0.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
internal static class SpendReaders
{
    internal static List<SpendRecord> Read(SpendSource source,IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        if(source.Id is "devin-desktop" or "codebuff" or "jcode")return SpendMissingReaders.Read(source.Id,roots,notes,ct);
        if(source.Id is "cursor" or "trae")return SpendExportReaders.Read(source.Id,roots,notes,ct);
        if(source.Id=="commandcode")return SpendEditorReaders.CommandCode(roots,notes,ct);
        if(source.Id=="gjc")return SpendNativeReaders.Gjc(roots,notes,ct);
        if(source.Id=="cherrystudio")return SpendNativeReaders.Cherry(roots,notes,ct);
        if(source.Id is "pi" or "omp" or "senpi" or "kimchi" or "prime-agent")return SpendPiReaders.Read(source.Id,roots,notes,ct);
        if(source.Id=="openclaw")return SpendOpenClawReaders.Read(roots,notes,ct);
        var records=new List<SpendRecord>(); var importedFiles=new HashSet<string>();
        foreach(var file in Files(roots,notes))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var ext=Path.GetExtension(file).ToLowerInvariant();
                if(ext is ".db" or ".sqlite") { records.AddRange(SpendDatabaseReaders.Read(source.Id,file,notes,ct)); continue; }
                if(source.Id=="gemini"&&ext is ".json" or ".jsonl"){records.AddRange(SpendSpecialReaders.Gemini(file,notes,ct));continue;}
                if(source.Id=="mcode"&&ext==".jsonl"){records.AddRange(SpendSpecialReaders.MiniMax(file,notes,ct));continue;}
                if(source.Id=="qwen"&&ext==".jsonl"){records.AddRange(SpendSessionReaders.Qwen(file,notes,ct));continue;}
                if(source.Id=="amp"&&ext==".json"){records.AddRange(SpendSessionReaders.Amp(file,notes,ct));continue;}
                if(source.Id=="droid"){if(SpendSessionReaders.Droid(file,notes) is {} row)records.Add(row);continue;}
                if(source.Id=="cline"&&file.EndsWith(".messages.json",StringComparison.OrdinalIgnoreCase)){records.AddRange(SpendEditorReaders.ClineCli(file,ct));continue;}
                if(source.Id is "roocode" or "kilocode" or "cline"&&Path.GetFileName(file)=="ui_messages.json"){records.AddRange(SpendEditorReaders.VscodeTask(source.Id,file,ct));continue;}
                if(source.Id=="zcode"&&ext==".jsonl"){records.AddRange(SpendEditorReaders.ZCodeTranscript(file,notes,ct));continue;}
                if(source.Id=="opencodereview"&&ext==".jsonl"){records.AddRange(SpendEditorReaders.OpenCodeReview(file,notes,ct));continue;}
                if(source.Id=="copilot") { if(ext==".jsonl"&&file.Replace('\\','/').Contains("/otel/"))records.AddRange(SpendImportReaders.CopilotOtel(file,notes,ct));else if(ext==".jsonl"&&Path.GetFileName(Path.GetDirectoryName(file))=="chatSessions")records.AddRange(SpendImportReaders.CopilotVsCode(file,notes,ct));continue; }
                if(source.Id=="cursor"&&ext==".csv") { records.AddRange(SpendImportReaders.CursorCsv(file));continue; }
                if(source.Id=="freebuff") continue; // Character estimates never become measured usage.
                if(source.Id=="crush") { notes.Add("crush：来源只提供费用，没有实测 Token 计数。"); continue; }
                if(source.Id is "codebuddy" or "workbuddy"&&ext==".log"){records.AddRange(SpendTencentReaders.ExtensionLog(source.Id,file,ct));continue;}
                var compressed=ext is ".zst" or ".zstd";
                if(source.Id=="lmstudio"&&ext==".log") { records.AddRange(ReadLmStudio(file)); continue; }
                if(!compressed&&ext is not (".jsonl" or ".ndjson" or ".json" or ".settings")) continue;
                if(source.Id is "roocode" or "kilocode" or "cline"&&Path.GetFileName(file)!="ui_messages.json"&&ext==".json") continue;
                if(source.Id is "cursor" or "trae"&&!importedFiles.Add(Hash(ReadBounded(file)))) continue;
                var context=new Context(source.Id,file,notes); long line=0;
                IEnumerable<string> lines=compressed||source.Id=="dsh"?SpendCompression.ReadTranscript(file).Split('\n'):ext is ".jsonl" or ".ndjson"?File.ReadLines(file):[ReadBounded(file)];
                foreach(var text in lines)
                {
                    ct.ThrowIfCancellationRequested(); line++; if(string.IsNullOrWhiteSpace(text)) continue;
                    try { using var document=JsonDocument.Parse(text); context.Line=line; records.AddRange(ParseDocument(document.RootElement,context)); }
                    catch(JsonException) { notes.Add(source.Id+"：包含无法解析或未写完的日志行："+Path.GetFileName(file)); }
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or OverflowException) { notes.Add(source.Id+"：读取失败（"+e.GetType().Name+"）："+Path.GetFileName(file)); }
        }
        if(source.Id=="copilot")records=SpendImportReaders.ReconcileCopilot(records);
        if(source.Id is "qwen" or "droid" or "amp"&&notes.Any(n=>n.StartsWith(source.Id+"：有实测用量",StringComparison.Ordinal)))records=records.Select(r=>r with{IsPartial=true}).ToList();
        if(source.Id=="opencodereview"&&notes.Any(n=>n.StartsWith("opencodereview：跳过",StringComparison.Ordinal)))records=records.Select(r=>r with{IsPartial=true}).ToList();
        if(source.Id is "codebuddy" or "workbuddy")
        {
            var transcript=records.Where(x=>x.Origin=="transcript").ToList();var fallback=records.Where(x=>x.Origin!="transcript").ToList();
            if(transcript.Count>0)records=transcript.Select(x=>x with{IsPartial=x.IsPartial||fallback.Count>0}).ToList();
        }
        if(source.Id=="cursor"&&records.Any(x=>!x.Identity.StartsWith("csv:")))
        {
            var json=records.Where(x=>!x.Identity.StartsWith("csv:")).ToList();var start=json.Min(x=>x.At);var end=json.Max(x=>x.At);records=json.Concat(records.Where(x=>x.Identity.StartsWith("csv:")&&(x.At<start||x.At>end))).Select(x=>x with{IsPartial=true}).ToList();
        }
        return records;
    }
    static string ReadBounded(string path) { if(new FileInfo(path).Length>64*1024*1024) throw new IOException("日志超出 64 MiB。"); return File.ReadAllText(path); }
    internal static IEnumerable<string> Files(IEnumerable<string> roots,List<string> notes)
    {
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if(File.Exists(root)) { if(files.Add(Path.GetFullPath(root))) yield return root; continue; }
            if(!Directory.Exists(root)) continue;
            var stack=new Stack<string>(); stack.Push(root);
            while(stack.Count>0)
            {
                var directory=stack.Pop(); string[] children;
                try { children=Directory.GetFileSystemEntries(directory); }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException) { notes.Add("无法读取来源子目录："+Path.GetFileName(directory)); continue; }
                foreach(var child in children.Order(StringComparer.OrdinalIgnoreCase))
                {
                    FileAttributes attributes;try{attributes=File.GetAttributes(child);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add("来源条目暂时无法读取："+Path.GetFileName(child));continue;}if((attributes&FileAttributes.ReparsePoint)!=0) continue;
                    if((attributes&FileAttributes.Directory)!=0) { if(Path.GetFileName(child)!="binaries") stack.Push(child); }
                    else if(files.Add(Path.GetFullPath(child))) yield return child;
                }
            }
        }
    }
    internal sealed class Context(string agent,string file,List<string> notes)
    {
        public string Agent=agent,File=file,Model="unknown",Session=agent is "mux" or "fx" or "junie" or "grok" or "kimi"?Path.GetFileName(Path.GetDirectoryName(file))!:Path.GetFileNameWithoutExtension(file); public string? Project; public DateTimeOffset? At; public bool HasHeader; public long Line,SeedLength; public long[] Previous=[0,0,0]; public List<string> Notes=notes; public Dictionary<string,int> Occurrences=[];
        public bool Native=>Agent=="mcode"&&File.Replace('\\','/').Contains("/.minimax/v2/sessions/",StringComparison.OrdinalIgnoreCase);
    }
    internal static IEnumerable<SpendRecord> ParseDocument(JsonElement root,Context c)
    {
        if(root.ValueKind==JsonValueKind.Array) { foreach(var row in root.EnumerateArray()) foreach(var r in ParseDocument(row,c)) yield return r; yield break; }
        if(root.ValueKind!=JsonValueKind.Object) yield break;
        var type=S(root,"type","kind")??"";
        if(c.Agent=="codex") { foreach(var r in Codex(root,c)) yield return r; yield break; }
        if(c.Agent=="hindsight"){if(SpendSessionReaders.Hindsight(root) is {} record)yield return record;yield break;}
        if(c.Agent is "codebuddy" or "workbuddy"){var r=SpendTencentReaders.Transcript(root,c);if(r is not null)yield return r;yield break;}
        if(c.Agent!="mux")c.Session=S(root,"sessionId","session_id")??c.Session;
        c.Project=S(root,"cwd","working_dir","workingDirectory","workspace_root","workspaceRoot","project_path")??c.Project;
        c.Model=S(root,"model","modelId","modelID","agentState.modelId","metadata.model","meta.model")??c.Model;
        if(type is "session" or "init" or "session_start") { c.Session=S(root,"id","session_id")??c.Session; c.At=D(root,"timestamp","startTime","created_at")??c.At; c.SeedLength=N(root,"seedLength"); c.HasHeader=true; }
        if(c.Agent is "pi" or "omp" or "senpi" or "kimchi"&& !c.HasHeader) yield break;
        if(c.Agent=="dsh"&&type=="request/header") { c.Model=S(root,"data.header.config.model")??c.Model; yield break; }
        if(c.Agent=="dsh")
        {
            if(type is not ("assistant/message" or "compaction/summary")||N(root,"seq")<c.SeedLength)yield break;
            var data=JsonTools.At(root,"data");var model=S(data,"message.source.replayState.response.responseModel","message.source.model")??c.Model;
            var identity=type=="compaction/summary"?"summary:"+(S(data,"compactionId")??N(root,"seq").ToString()):"msg:"+(S(data,"message.id")??N(root,"seq").ToString());
            var r=Build(JsonTools.At(data,"usage"),c,D(root,"time"),model,"dsh:"+identity+":"+S(root,"time")+":"+model+":"+Hash(JsonTools.At(data,"usage").GetRawText()));if(r is not null)yield return r;yield break;
        }
        if(c.Agent=="warp")
        {
            var at=D(root,"date","timestamp","updatedAt","updated_at"); var money=JsonTools.Decimal(root,"spendCents");
            if(at is not null&&money is not null) yield return new(c.Agent,Hash(root.GetRawText()),at.Value,"unknown",ReportsTokens:false,ReportedCost:money/100,Origin:"imported",IsAggregate:true);
            yield break;
        }
        if(c.Agent is "roocode" or "kilocode" or "cline"&&S(root,"say")=="api_req_started")
        {
            var inner=S(root,"text"); if(inner is null) yield break; JsonDocument? document=null; try { document=JsonDocument.Parse(inner); } catch(JsonException) { }
            if(document is not null) { using(document) { var at=D(root,"ts","timestamp"); var record=Build(document.RootElement,c,at,S(document.RootElement,"model"),S(root,"id")??S(root,"ts")??c.Line.ToString()); if(record is not null) yield return record; } } yield break;
        }
        if(c.Agent=="mux"&&JsonTools.At(root,"byModel").ValueKind==JsonValueKind.Object)
        {
            var at=D(root,"lastRequest.timestamp");
            foreach(var model in JsonTools.At(root,"byModel").EnumerateObject()) { var u=model.Value; var input=N(u,"input.tokens"); var output=N(u,"output.tokens"); var read=N(u,"cached.tokens"); var write=N(u,"cacheCreate.tokens"); if(at is not null&&input+output+read+write>0) yield return new(c.Agent,c.Session+":"+model.Name,at.Value,model.Name.Contains(':')?model.Name[(model.Name.IndexOf(':')+1)..]:model.Name,input,output,read,write,IsAggregate:true,IsPartial:N(u,"reasoning.tokens")>0,Session:c.Session); }
            yield break;
        }
        if(c.Agent=="fx"&&JsonTools.At(root,"snapshot").ValueKind==JsonValueKind.Object)
        {
            var sidecar=Path.Combine(Path.GetDirectoryName(c.File)!,"session.json"); if(!File.Exists(sidecar)) yield break;
            using var timing=JsonDocument.Parse(ReadBounded(sidecar)); var at=D(timing.RootElement,"updated_at_ms","created_at_ms");c.Project=S(timing.RootElement,"workspace_root")??c.Project;
            var models=JsonTools.Array(root,"snapshot.models").ToList(); if(models.Count==0) models.Add(JsonTools.At(root,"snapshot"));
            foreach(var m in models) { var r=Build(m,c,at,S(m,"model"),c.Session+":"+(S(m,"model")??"unknown")); if(r is not null) yield return r with{IsAggregate=true}; }
            yield break;
        }
        if(c.Agent=="augment"&&JsonTools.At(root,"chatHistory").ValueKind==JsonValueKind.Array)
        {
            foreach(var turn in JsonTools.Array(root,"chatHistory"))
            {
                if(JsonTools.Bool(turn,"completed")!=true) continue;
                var usage=JsonTools.Array(turn,"exchange.response_nodes").Select(x=>JsonTools.At(x,"token_usage")).LastOrDefault(x=>x.ValueKind==JsonValueKind.Object&&N(x,"input_tokens")+N(x,"output_tokens")+N(x,"cache_read_input_tokens")+N(x,"cache_creation_input_tokens")>0);
                var r=Build(usage,c,D(turn,"finishedAt"),S(turn,"exchange.model_id")??c.Model,"augment:"+c.Session+":"+(S(turn,"exchange.request_id","sequenceId")??Hash(turn.GetRawText()))); if(r is not null) yield return r;
            }
            yield break;
        }
        if(c.Agent=="kiro")
        {
            foreach(var row in SpendNativeReaders.Kiro(root,c))yield return row;yield break;
        }
        if(c.Agent=="junie")
        {
            var agentEvent=JsonTools.At(root,"event.agentEvent"); if(S(agentEvent,"kind")!="LlmResponseMetadataEvent") yield break;
            DateTimeOffset? fallback=null;var stamp=Regex.Match(c.Session,@"session-(\d{6}-\d{6})");if(stamp.Success&&DateTime.TryParseExact(stamp.Groups[1].Value,"yyMMdd-HHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start))fallback=new DateTimeOffset(start);
            var index=0;foreach(var usage in JsonTools.Array(agentEvent,"modelUsage"))
            {
                var end=D(root,"timestampMs");var at=end??fallback;var latency=N(usage,"time");if(end is {} endAt&&latency>0&&latency<endAt.ToUnixTimeMilliseconds())at=endAt.AddMilliseconds(-latency);
                var r=Build(usage,c,at,S(usage,"model"),"");if(r is not null)yield return r with{Identity="junie:"+c.Session+":"+r.At.ToUnixTimeMilliseconds()+":"+r.Model+":"+r.Input+":"+r.CacheWrite+":"+r.CacheRead+":"+r.Output+":"+N(usage,"cost")+":"+index};index++;
            }yield break;
        }
        if(c.Agent=="grok")
        {
            if(S(root,"params.update.sessionUpdate")!="turn_completed") yield break;
            var u=JsonTools.At(root,"params.update.usage"); var perModel=JsonTools.At(u,"modelUsage");
            if(perModel.ValueKind==JsonValueKind.Object) foreach(var pair in perModel.EnumerateObject()) { var r=Build(pair.Value,c,D(root,"timestamp"),pair.Name,c.File+":"+(S(root,"id")??c.Line.ToString())+":"+pair.Name); if(r is not null) yield return r; }
            else { var r=Build(u,c,D(root,"timestamp"),"grok",c.File+":"+(S(root,"id")??c.Line.ToString())+":grok"); if(r is not null) yield return r; } yield break;
        }
        if(c.Agent=="kimi")
        {
            var r=Build(JsonTools.At(root,"message.payload.token_usage"),c,D(root,"timestamp"),"kimi (unnamed)",c.File+":"+c.Line); if(r is not null) yield return r; yield break;
        }
        if(c.Agent=="reasonix")
        {
            var r=SpendNativeReaders.Reasonix(root,c);if(r is not null)yield return r;yield break;
        }
        // Container forms carry metadata down to the individual response, never add a container total and its children.
        foreach(var container in new[]{"messages","append_messages","events","usageEventsDisplay","data.usageEventsDisplay","usageEvents","requests","generations","entries","items","results","turns"})
        {
            if(JsonTools.At(root,container).ValueKind!=JsonValueKind.Array) continue;
            foreach(var row in JsonTools.Array(root,container)) foreach(var r in ParseDocument(row,c)) yield return r;
            yield break;
        }
        var message=JsonTools.At(root,"message"); var response=message.ValueKind==JsonValueKind.Object?message:root;
        var role=S(response,"role"); if(role is not null&&role!="assistant"&&role!="model") yield break;
        var responseUsage=FirstObject(response,"usage","token_usage","tokenUsage","usageMetadata","tokens","metadata.usage","metadata.codebuff.usage","metadata.metrics","response.usage","contextUsage","extra_info");
        if(responseUsage.ValueKind!=JsonValueKind.Object) responseUsage=FirstObject(root,"usage","token_usage","usageMetadata","tokens","data.usage","response.usage","tokenUsage","usageMetrics","tokenUsageSummary","statistics");
        if(responseUsage.ValueKind!=JsonValueKind.Object&&c.Agent is "cursor" or "trae" or "antigravity" or "hindsight" or "mcode" or "droid") responseUsage=root;
        if(responseUsage.ValueKind!=JsonValueKind.Object) yield break;
        var responseAt=D(root,"timestamp","created_at","createdAt","date","ts","time","requestTime","completedAt","startTime","usage_time")??D(response,"timestamp","created_at","createdAt","time.created","timestampMs")??c.At;
        var responseModel=S(response,"model","modelId","modelID","model.id","model_name","metadata.generation_model")??S(root,"model","modelId","modelName","modelVersion")??c.Model;
        if(c.Agent=="trae"&&responseModel=="unknown")responseModel="trae-"+(S(root,"mode")??"unknown");
        var id=S(response,"id","message_id","requestId","request_id","responseId")??S(root,"requestId","request_id","message_id","id","uuid","responseId");
        // Equal independent calls without an identity stay separate; a line index is not a semantic dedup key.
        id??=c.File+":"+c.Line+":"+Hash(root.GetRawText());
        if(c.Agent is "cursor" or "trae")
        {
            var signature=Hash(root.GetRawText());var occurrence=c.Occurrences.GetValueOrDefault(signature)+1;c.Occurrences[signature]=occurrence;var name=Path.GetFileNameWithoutExtension(c.File);var scope=name.StartsWith("usage.",StringComparison.OrdinalIgnoreCase)?name[6..]:"unscoped";id=scope+":"+signature+":"+occurrence;
        }
        var responseRecord=Build(responseUsage,c,responseAt,responseModel,id); if(responseRecord is not null) yield return responseRecord;
    }
    static IEnumerable<SpendRecord> Codex(JsonElement root,Context c)
    {
        var type=S(root,"type");
        if(type=="session_meta") { c.Session=S(root,"payload.id")??c.Session; c.Project=S(root,"payload.cwd")??c.Project; yield break; }
        if(type=="turn_context") { c.Model=S(root,"payload.model")??c.Model; yield break; }
        if(type!="event_msg"||S(root,"payload.type")!="token_count") yield break;
        var total=JsonTools.At(root,"payload.info.total_token_usage"); var last=JsonTools.At(root,"payload.info.last_token_usage");
        long input,read,output;
        if(total.ValueKind==JsonValueKind.Object)
        {
            var values=new[]{N(total,"input_tokens"),N(total,"cached_input_tokens"),N(total,"output_tokens")};
            if(values[0]<c.Previous[0]||values[2]<c.Previous[2]) { c.Notes.Add("codex：累计计数回退，已重新建立本会话计数基线。"); c.Previous=[0,0,0]; }
            input=Math.Max(0,values[0]-c.Previous[0]); read=Math.Max(0,values[1]-c.Previous[1]); output=Math.Max(0,values[2]-c.Previous[2]); c.Previous=values;
        }
        else { input=N(last,"input_tokens");read=N(last,"cached_input_tokens");output=N(last,"output_tokens"); }
        read=Math.Min(input,read); var at=D(root,"timestamp"); if(input+output==0||at is null) yield break;
        // Cumulative positions identify the same event in sessions and archived_sessions copies.
        var id=c.Session+":"+string.Join(',',c.Previous)+":"+at.Value.ToUnixTimeMilliseconds();
        yield return new("codex",id,at.Value,c.Model,input-read,output,read,Session:c.Session,IsPartial:c.Model=="unknown",Project:c.Project,RequestContext:last.ValueKind==JsonValueKind.Object?N(last,"input_tokens"):null);
    }
    internal static SpendRecord? Build(JsonElement u,Context c,DateTimeOffset? at,string? model,string identity)
    {
        if(u.ValueKind!=JsonValueKind.Object) return null;
        var input=N(u,"input_tokens","inputTokens","input","prompt_tokens","promptTokens","promptTokenCount","input_other","tokensIn","input_token_count","prompt","inputTokenCount","input_token");
        var output=N(u,"output_tokens","outputTokens","output","completion_tokens","completionTokens","candidatesTokenCount","tokensOut","output_token_count","completion","outputTokenCount","output_token");
        var read=N(u,"cache_read_input_tokens","cache_read_tokens","cacheRead","cacheReadTokens","cachedReadTokens","cached_input_tokens","cacheReadInputTokens","cacheInputTokens","input_cache_read","cachedContentTokenCount","cache_read","cache_hit","prompt_tokens_details.cached_tokens","input_tokens_details.cached_tokens","cache.read","cachedTokens","cache_read_token");
        var write=N(u,"cache_creation_input_tokens","cache_creation_tokens","cache_write_tokens","cacheWrite","cacheWriteTokens","cacheCreationTokens","cacheCreateTokens","cacheCreationInputTokens","input_cache_creation","cacheCreationTokens","cache.write","cacheWriteTokens","cache_write_token");
        var hour=N(u,"cache_creation.ephemeral_1h_input_tokens","cacheCreation.ephemeral1hInputTokens");
        var total=N(u,"total_tokens","totalTokens","totalTokenCount","total"); var reasoning=N(u,"reasoning_tokens","reasoningTokens","reasoning_output_tokens","reasoningOutputTokens","thinkingTokens","thoughtsTokenCount","thoughts");
        long unclassified=0; bool partial=false;
        if(c.Agent=="kiro") { read=write=0; }
        if(c.Agent is "codex" or "qwen" or "grok" or "codebuff" or "lmstudio" or "reasonix" or "hindsight" || Has(u,"prompt_tokens_details")||Has(u,"input_tokens_details")) input=Math.Max(0,input-read);
        if(c.Agent=="lmstudio")
        {
            var prompt=N(u,"prompt_tokens","promptTokens","input_tokens","inputTokens");var hasPrompt=new[]{"prompt_tokens","promptTokens","input_tokens","inputTokens","completion_tokens","completionTokens","output_tokens","outputTokens"}.Any(k=>Has(u,k));
            if(!hasPrompt){input=output=read=write=0;unclassified=total;}
            else
            {
                read=Math.Min(prompt,N(u,"prompt_tokens_details.cached_tokens","input_tokens_details.cached_tokens","inputTokensDetails.cached_tokens","cached_tokens"));write=Math.Min(Math.Max(0,prompt-read),N(u,"prompt_tokens_details.cache_creation_input_tokens","input_tokens_details.cache_creation_input_tokens","inputTokensDetails.cache_creation_input_tokens","cache_creation_input_tokens"));input=Math.Max(0,Math.Max(total,prompt+output)-output-read-write);
            }
        }
        if(c.Agent=="reasonix"&&N(u,"cache_miss")>0) input=N(u,"cache_miss");
        if(c.Agent=="gemini")
        {
            input+=N(u,"tool","toolUsePromptTokenCount","tool_tokens"); output+=reasoning;
            var known=input+read+output;
            if(read>0&&total>0) { if(total==input+output) input=Math.Max(0,input-read); else if(total!=known) { unclassified=input;input=read=0;partial=true; } }
            else if(read>0) { unclassified=input;input=read=0;partial=true; }
        }
        if(c.Agent is "droid" or "jcode" or "opencodereview" or "zcode")
        {
            var settled=Has(u,"cache_creation_input_tokens")||Has(u,"prompt_tokens_details")||Has(u,"input_tokens_details");
            if(read+write>0&&!settled)
            {
                if(total==input+output&&total>0) input=Math.Max(0,input-read-write);
                else if(total!=input+output+read+write||total==0) { unclassified=input;input=read=write=0;partial=true; }
            }
        }
        if(c.Agent is "mux" or "fx" or "jcode" or "junie" or "antigravity"&&reasoning>0) partial=true;
        if(c.Agent is "dsh" or "lmstudio" or "reasonix") { /* reasoning is already within output */ }
        var counted=input+output+read+write;
        if(total>counted+unclassified) unclassified+=total-counted-unclassified;
        if(counted+unclassified==0) return null;
        if(at is null) { c.Notes.Add(c.Agent+"：实测用量缺少有效时间，未分配到日期统计。"); return null; }
        model=string.IsNullOrWhiteSpace(model)?"unknown":model;
        if(model=="unknown") partial=true;
        if(c.Agent is "cursor" or "trae")partial=true;
        return new(c.Agent,identity,at.Value,model,input,output,read,write,Math.Min(write,hour),unclassified,IsPartial:partial,IsAggregate:c.Agent=="droid",Session:c.Session,Origin:c.Agent is "cursor" or "trae" or "antigravity" or "warp" or "hindsight" || c.Agent=="mcode"&&!c.Native?"imported":"native",Project:c.Project,RequestContext:c.Agent is "claude" or "cherrystudio"?input+read+write:null);
    }
    static IEnumerable<SpendRecord> ReadLmStudio(string file)
    {
        var text=ReadBounded(file);
        foreach(Match m in Regex.Matches(text,"\"usage\"\\s*:\\s*\\{"))
        {
            int start=text.IndexOf('{',m.Index),end=start,depth=0; bool quoted=false,escape=false;
            for(;end<text.Length;end++) { var ch=text[end]; if(escape){escape=false;continue;} if(ch=='\\'&&quoted){escape=true;continue;} if(ch=='\"'){quoted=!quoted;continue;} if(quoted)continue; if(ch=='{')depth++; if(ch=='}'&&--depth==0)break; }
            if(end>=text.Length) continue;
            var context=text[Math.Max(0,m.Index-8192)..m.Index]; var models=Regex.Matches(context,"\"model\"\\s*:\\s*\"([^\"]+)\""); var dates=Regex.Matches(context,"\\d{4}-\\d{2}-\\d{2}[ T]\\d{2}:\\d{2}:\\d{2}");
            if(models.Count==0||dates.Count==0||!DateTimeOffset.TryParse(dates[^1].Value,out var at)) continue;
            using var document=JsonDocument.Parse(text[start..(end+1)]); var ids=Regex.Matches(context,"\"id\"\\s*:\\s*\"([^\"]+)\""); var id=ids.Count>0?ids[^1].Groups[1].Value:file+":"+start;
            var r=Build(document.RootElement,new("lmstudio",file,[]),at,models[^1].Groups[1].Value,id); if(r is not null) yield return r;
        }
    }
    internal static bool Has(JsonElement r,string p)=>JsonTools.At(r,p).ValueKind!=JsonValueKind.Undefined;
    internal static string? S(JsonElement r,params string[] paths) { foreach(var p in paths) if(JsonTools.String(r,p) is {} s&&!string.IsNullOrWhiteSpace(s)) return s; return null; }
    internal static long N(JsonElement r,params string[] paths) { foreach(var p in paths) if(JsonTools.Number(r,p) is {} n&&n>=0&&n<=long.MaxValue&&double.IsFinite(n)) return (long)n; return 0; }
    internal static DateTimeOffset? D(JsonElement r,params string[] paths) { foreach(var p in paths) if(JsonTools.Date(r,p) is {} d&&d.Year>1970) return d; return null; }
    static JsonElement FirstObject(JsonElement r,params string[] paths) { foreach(var p in paths) if(JsonTools.At(r,p).ValueKind==JsonValueKind.Object) return JsonTools.At(r,p); return default; }
    internal static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
