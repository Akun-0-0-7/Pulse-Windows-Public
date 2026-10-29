using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse.Core;

public sealed record WindowStarterReceipt(string Status,bool Completed,int? InputTokens,int? OutputTokens,string? FailureKind);

public static partial class WindowStarter
{
    public static WindowStarterReceipt Evaluate(string provider,ProcessResult result)
    {
        if(result.TimedOut)return Failure("timeout");
        if(string.Equals(result.Error,"toolMissing",StringComparison.Ordinal))return Failure("toolMissing");
        if(string.Equals(result.Error,"modelSelectionUnavailable",StringComparison.Ordinal))return Failure("modelSelection");
        if(provider!="codex")return result.ExitCode==0?new("success",true,null,null,null):
            Failure(string.IsNullOrWhiteSpace(result.Output)?FailureKind(result.Error):"turnIncomplete");

        var completed=false;var terminalFailure=false;var workObserved=false;var malformed=false;
        string? reportedFailure=null;
        int? inputTokens=null,outputTokens=null;
        foreach(var line in result.Output.Split('\n'))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            JsonDocument document;
            try{document=JsonDocument.Parse(line);}
            catch(JsonException){malformed=true;continue;}
            using(document)
            {
                var root=document.RootElement;
                if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var typeValue)||typeValue.ValueKind!=JsonValueKind.String)
                {malformed=true;continue;}
                switch(typeValue.GetString())
                {
                    case "turn.started":
                        completed=false;inputTokens=null;outputTokens=null;
                        break;
                    case "error":
                        var errorText=ErrorText(root);
                        reportedFailure=RememberFailure(reportedFailure,FailureKind(errorText),errorText.Length>0);
                        break;
                    case "turn.failed":
                        terminalFailure=true;
                        var failureText=ErrorText(root);
                        reportedFailure=RememberFailure(reportedFailure,FailureKind(failureText),failureText.Length>0);
                        break;
                    case "turn.completed":
                        if(!root.TryGetProperty("usage",out var usage)||usage.ValueKind!=JsonValueKind.Object||
                            !TokenCount(usage,"input_tokens",out var input)||!TokenCount(usage,"output_tokens",out var output)||output==0)
                        {malformed=true;break;}
                        completed=true;inputTokens=input;outputTokens=output;
                        workObserved=true;
                        break;
                    case "item.started":
                    case "item.updated":
                    case "item.completed":
                        workObserved=true;
                        break;
                }
            }
        }
        if(completed&&!terminalFailure&&!malformed&&result.ExitCode==0)
            return new("success",true,inputTokens,outputTokens,null);
        // A received response or interrupted stream makes replay uncertain, even if a later
        // error mentions quota or DNS. Only explicit rejection before work may be retried.
        if(workObserved||malformed)return Failure("turnIncomplete");
        if(reportedFailure is not null)return Failure(reportedFailure);
        if(result.ExitCode!=0)return Failure(FailureKind(result.Error));
        return Failure("turnIncomplete");
    }

    private static string RememberFailure(string? previous,string current,bool hasDetails)
    {
        // Retrying a connection may produce a new DNS/quota error after an earlier
        // request was already received. Later rejection cannot resolve that uncertainty.
        if(previous is null)return current;
        if(current=="failed"&&!hasDetails)return previous;
        return previous is "network" or "quota"?current:previous;
    }

    private static string ErrorText(JsonElement root)
    {
        // Read only error fields, never assistant text. Codex may put a serialized API
        // error inside message; matching its stable codes also handles that representation.
        return string.Join(" ",new[]{"error","message","code","status"}
            .Where(name=>root.TryGetProperty(name,out _))
            .Select(name=>name+":"+root.GetProperty(name).ToString()));
    }

    private static bool TokenCount(JsonElement usage,string name,out int value)
    {
        value=0;
        return usage.TryGetProperty(name,out var token)&&token.ValueKind==JsonValueKind.Number&&token.TryGetInt32(out value)&&value>=0;
    }

    private static string FailureKind(string error)
    {
        if(error.Contains("timed out",StringComparison.OrdinalIgnoreCase)||error.Contains("timeout",StringComparison.OrdinalIgnoreCase))return "timeout";
        if(error.Contains("stream disconnected",StringComparison.OrdinalIgnoreCase)||
            error.Contains("disconnected before completion",StringComparison.OrdinalIgnoreCase)||
            error.Contains("connection reset",StringComparison.OrdinalIgnoreCase)||
            error.Contains("connection closed",StringComparison.OrdinalIgnoreCase)||
            error.Contains("stream closed",StringComparison.OrdinalIgnoreCase))return "turnIncomplete";
        if(error.Contains("model_not_found",StringComparison.OrdinalIgnoreCase)||
            error.Contains("unsupported_model",StringComparison.OrdinalIgnoreCase)||
            error.Contains("invalid model",StringComparison.OrdinalIgnoreCase)||
            error.Contains("unknown model",StringComparison.OrdinalIgnoreCase)||
            error.Contains("model is not supported",StringComparison.OrdinalIgnoreCase)||
            error.Contains("not supported when using Codex",StringComparison.OrdinalIgnoreCase)||
            error.Contains("model does not exist",StringComparison.OrdinalIgnoreCase))return "model";
        if(error.Contains("unauthorized",StringComparison.OrdinalIgnoreCase)||
            error.Contains("authentication",StringComparison.OrdinalIgnoreCase)||
            error.Contains("login",StringComparison.OrdinalIgnoreCase)||
            error.Contains("invalid_api_key",StringComparison.OrdinalIgnoreCase)||
            error.Contains("401",StringComparison.Ordinal)||error.Contains("403",StringComparison.Ordinal))return "auth";
        if(error.Contains("usage_limit_reached",StringComparison.OrdinalIgnoreCase)||
            error.Contains("insufficient_quota",StringComparison.OrdinalIgnoreCase)||
            error.Contains("usage limit",StringComparison.OrdinalIgnoreCase)||
            error.Contains("quota exceeded",StringComparison.OrdinalIgnoreCase)||
            error.Contains("quota exhausted",StringComparison.OrdinalIgnoreCase)||
            error.Contains("rate limit",StringComparison.OrdinalIgnoreCase)||
            error.Contains("rate_limit",StringComparison.OrdinalIgnoreCase)||
            error.Contains("too many requests",StringComparison.OrdinalIgnoreCase)||
            Regex.IsMatch(error,"(?:\\b(?:HTTP(?:/\\d(?:\\.\\d)?)?|status(?:_code)?|status code)\\s*[:=]?\\s*429\\b|\"(?:status|status_code|http_status)\"\\s*:\\s*429\\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return "quota";
        if(error.Contains("dns",StringComparison.OrdinalIgnoreCase)||
            error.Contains("name resolution",StringComparison.OrdinalIgnoreCase)||
            error.Contains("failed to lookup address",StringComparison.OrdinalIgnoreCase)||
            error.Contains("no such host",StringComparison.OrdinalIgnoreCase)||
            error.Contains("connection refused",StringComparison.OrdinalIgnoreCase)||
            error.Contains("network is unreachable",StringComparison.OrdinalIgnoreCase)||
            error.Contains("network unreachable",StringComparison.OrdinalIgnoreCase)||
            error.Contains("host is unreachable",StringComparison.OrdinalIgnoreCase)||
            error.Contains("failed to establish a new connection",StringComparison.OrdinalIgnoreCase)||
            error.Contains("failed to connect",StringComparison.OrdinalIgnoreCase)||
            error.Contains("could not connect",StringComparison.OrdinalIgnoreCase)||
            error.Contains("unable to connect",StringComparison.OrdinalIgnoreCase)||
            error.Contains("proxyconnect",StringComparison.OrdinalIgnoreCase)||
            error.Contains("tunnel connection failed",StringComparison.OrdinalIgnoreCase))return "network";
        if(error.Contains("network",StringComparison.OrdinalIgnoreCase)||
            error.Contains("connection",StringComparison.OrdinalIgnoreCase)||
            error.Contains("proxy",StringComparison.OrdinalIgnoreCase)||
            error.Contains("stream",StringComparison.OrdinalIgnoreCase)||
            error.Contains("disconnected",StringComparison.OrdinalIgnoreCase))return "turnIncomplete";
        return "failed";
    }

    private static WindowStarterReceipt Failure(string kind)=>new("failed",false,null,null,kind);
}
