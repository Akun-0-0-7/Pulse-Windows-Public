using System.Text.Json;

namespace Pulse.Core;

public sealed record WindowStarterReceipt(string Status,bool Completed,int? InputTokens,int? OutputTokens,string? FailureKind);

public static partial class WindowStarter
{
    public static WindowStarterReceipt Evaluate(string provider,ProcessResult result)
    {
        if(result.TimedOut)return Failure("timeout");
        if(string.Equals(result.Error,"toolMissing",StringComparison.Ordinal))return Failure("toolMissing");
        if(result.ExitCode!=0)return Failure(FailureKind(result.Error));
        if(provider!="codex")return new("success",true,null,null,null);

        var completed=false;
        int? inputTokens=null,outputTokens=null;
        foreach(var line in result.Output.Split('\n'))
        {
            if(string.IsNullOrWhiteSpace(line))continue;
            JsonDocument document;
            try{document=JsonDocument.Parse(line);}
            catch(JsonException){return Failure("turnIncomplete");}
            using(document)
            {
                var root=document.RootElement;
                if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var typeValue)||typeValue.ValueKind!=JsonValueKind.String)
                    return Failure("turnIncomplete");
                switch(typeValue.GetString())
                {
                    case "turn.started":
                        completed=false;inputTokens=null;outputTokens=null;
                        break;
                    case "turn.failed":return Failure("failed");
                    case "turn.completed":
                        if(!root.TryGetProperty("usage",out var usage)||usage.ValueKind!=JsonValueKind.Object||
                            !TokenCount(usage,"input_tokens",out var input)||!TokenCount(usage,"output_tokens",out var output)||output==0)
                            return Failure("turnIncomplete");
                        completed=true;inputTokens=input;outputTokens=output;
                        break;
                }
            }
        }
        return completed?new("success",true,inputTokens,outputTokens,null):Failure("turnIncomplete");
    }

    private static bool TokenCount(JsonElement usage,string name,out int value)
    {
        value=0;
        return usage.TryGetProperty(name,out var token)&&token.ValueKind==JsonValueKind.Number&&token.TryGetInt32(out value)&&value>=0;
    }

    private static string FailureKind(string error)
    {
        if(error.Contains("unauthorized",StringComparison.OrdinalIgnoreCase)||
            error.Contains("authentication",StringComparison.OrdinalIgnoreCase)||
            error.Contains("login",StringComparison.OrdinalIgnoreCase)||
            error.Contains("invalid_api_key",StringComparison.OrdinalIgnoreCase)||
            error.Contains("401",StringComparison.Ordinal)||error.Contains("403",StringComparison.Ordinal))return "auth";
        if(error.Contains("network",StringComparison.OrdinalIgnoreCase)||
            error.Contains("connection",StringComparison.OrdinalIgnoreCase)||
            error.Contains("proxy",StringComparison.OrdinalIgnoreCase)||
            error.Contains("dns",StringComparison.OrdinalIgnoreCase)||
            error.Contains("request timed out",StringComparison.OrdinalIgnoreCase))return "network";
        return "failed";
    }

    private static WindowStarterReceipt Failure(string kind)=>new("failed",false,null,null,kind);
}
