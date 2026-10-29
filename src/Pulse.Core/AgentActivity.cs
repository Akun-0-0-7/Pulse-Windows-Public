using System.Text.Json;
namespace Pulse.Core;
public sealed record ActivityState(bool IsWorking,DateTimeOffset? At=null);
public static class AgentActivity
{
    public static ActivityState Parse(string provider,string lines,DateTimeOffset modified,DateTimeOffset now)
    {
        var completed=new HashSet<string>();
        foreach(var line in lines.Split('\n').Reverse()) {
            JsonDocument doc;try{doc=JsonDocument.Parse(line);}catch(JsonException){continue;}using(doc){var json=doc.RootElement;var at=JsonTools.Date(json,"timestamp")??modified;var type=JsonTools.String(json,"type");var payload=JsonTools.String(json,"payload.type");string? wait=null;
                if(provider=="codex"){if(payload is "task_complete" or "turn_aborted")return new(false,at);wait=payload switch{"task_started" or "function_call_output" or "custom_tool_call_output" or "tool_search_output"=>"model","function_call" or "custom_tool_call" or "local_shell_call" or "web_search_call" or "tool_search_call"=>"tool",_=>null};}
                else if(provider=="claudeCode"){if(type=="assistant"){var stop=JsonTools.String(json,"message.stop_reason");if(stop is not null&&stop!="tool_use")return new(false,at);wait=stop=="tool_use"?"tool":"model";}else if(type=="user"){if(line.Contains("[Request interrupted by user",StringComparison.Ordinal))return new(false,at);wait="model";}}
                else if(provider=="kiro"){if(payload is "turn_end" or "pending_interaction")return new(false,at);wait=payload switch{"turn_start" or "tool_result" or "interaction_resolved" or "assistant" or "sub_agent_complete"=>"model","tool_call" or "sub_agent_start"=>"tool",_=>null};var kind=JsonTools.String(json,"kind");if(kind is "Prompt" or "ToolResults")wait="model";if(kind=="AssistantMessage"){if(line.Contains("toolUse",StringComparison.Ordinal))wait="tool";else return new(false,at);}}
                else if(provider is "zai" or "glmCoding"){var turn=JsonTools.String(json,"turnId");var evt=JsonTools.String(json,"event");if(turn is null)continue;if(evt is "turn.completed" or "turn.failed" or "turn.cancelled"){completed.Add(turn);continue;}if(completed.Contains(turn))continue;wait=evt switch{"tool.call.started"=>"tool","turn.started" or "tool.call.completed" or "tool.call.failed" or "model.request.started" or "model.request.completed" or "model.request.failed"=>"model",_=>null};}
                if(wait is not null)return new(now-at<=TimeSpan.FromSeconds(wait=="tool"?300:90),at);
            }
        }
        return new(false);
    }
    public static Dictionary<string,bool> Read(AppConfig config,string home)
    {
        var states=new Dictionary<string,bool>();foreach(var account in config.Accounts.Where(a=>a.Enabled&&a.Extra.GetValueOrDefault("isAdditionalAccount")!="true")) {
            var suffix=account.ProviderId switch{"codex"=>".codex/sessions","claudeCode"=>".claude/projects","kiro"=>".kiro/sessions","zai" or "glmCoding"=>".zcode/cli/log",_=>null};if(suffix is null)continue;
            if(account.ProviderId is "zai" or "glmCoding"){var text=LocalPaths.ReadText(Path.Combine(home,".zcode/cli/config.json"));if(text is null||!(account.ProviderId=="zai"?text.Contains("api.z.ai",StringComparison.OrdinalIgnoreCase):text.Contains("bigmodel.cn",StringComparison.OrdinalIgnoreCase)))continue;}
            var root=Path.Combine(home,suffix);if(!Directory.Exists(root))continue;
            try{foreach(var file in Directory.EnumerateFiles(root,"*.jsonl",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true}).Select(p=>new FileInfo(p)).Where(f=>DateTimeOffset.UtcNow-f.LastWriteTimeUtc<TimeSpan.FromMinutes(5)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(40)){
                using var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var offset=Math.Max(0,stream.Length-(account.ProviderId is "zai" or "glmCoding"?2*1024*1024:128*1024));stream.Seek(offset,SeekOrigin.Begin);using var reader=new StreamReader(stream);var tail=reader.ReadToEnd();if(offset>0&&tail.IndexOf('\n') is var cut&&cut>=0)tail=tail[(cut+1)..];if(Parse(account.ProviderId,tail,file.LastWriteTimeUtc,DateTimeOffset.UtcNow).IsWorking){states[account.Id]=true;break;}}
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        return states;
    }
}
public sealed class BurnForecast
{
    private readonly Dictionary<string,(double Used,DateTimeOffset At,DateTimeOffset? Reset)> points=[];
    public TimeSpan? Observe(string account,UsageWindow window,DateTimeOffset at)
    {
        lock(points){var key=account+":"+window.Id;var previous=points.GetValueOrDefault(key);points[key]=(window.UsedPercent,at,window.ResetsAt);if(previous.At==default||previous.Reset!=window.ResetsAt||window.Estimated||window.UsedPercent<=previous.Used||at-previous.At<TimeSpan.FromSeconds(60)||window.UsedPercent>=100)return null;var rate=(window.UsedPercent-previous.Used)/(at-previous.At).TotalSeconds;return rate>0?TimeSpan.FromSeconds((100-window.UsedPercent)/rate):null;}
    }
}
public static partial class WindowStarter
{
    public static bool IsDue(DateTimeOffset reset,DateTimeOffset now,int startHour,int endHour)=>now>=reset&&(startHour==endHour||startHour<endHour&&now.Hour>=startHour&&now.Hour<endHour||startHour>endHour&&(now.Hour>=startHour||now.Hour<endHour));
    public static async Task<ProcessResult> StartAsync(string provider,string dataRoot,CancellationToken cancellationToken=default,string? configuredExecutable=null,Dictionary<string,string>? environment=null)
    {
        var name=provider=="codex"?"codex":"claude";
        var executable=provider=="codex"?CodexToolLocator.Find(configuredExecutable,environment):
            string.IsNullOrWhiteSpace(configuredExecutable)?LocalPaths.FindExecutable(name):LocalPaths.FindExecutable(configuredExecutable)??LocalPaths.FindExecutable(name);
        if(executable is null)return new(-1,"","toolMissing",false);var folder=Path.Combine(dataRoot,"window-starter");Directory.CreateDirectory(folder);
        var model=provider=="codex"?await CodexStarterModel.ResolveAsync(executable,folder,cancellationToken,environment):null;
        if(provider=="codex"&&model is null)return new(-1,"","modelSelectionUnavailable",false);
        string[] args=provider=="codex"?["exec","--model",model!.Model,"--ignore-user-config","--json","--ephemeral","--skip-git-repo-check","-s","read-only","-C",folder,"hi"]:["-p","hi","--model","haiku","--no-session-persistence","--tools","","--strict-mcp-config","--setting-sources","project"];
        cancellationToken.ThrowIfCancellationRequested();
        var result=await ProcessRunner.RunAsync(executable,args,TimeSpan.FromSeconds(120),cancellationToken,folder,environment);
        return result with{Model=model?.Model,ModelSource=model?.Source};
    }
}
