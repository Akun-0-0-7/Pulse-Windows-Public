using System.Text.Json;
namespace Pulse.Core;
public sealed record CodexUsageDay(DateTimeOffset At,long Tokens);
public sealed record CodexAccountDetails(List<CodexUsageDay> Days,long? LifetimeTokens,int? ResetCredits,DateTimeOffset? NextExpiry,string? Error=null);
public static class CodexDetailsService
{
    public static CodexAccountDetails Parse(string usage,string limits)
    {
        using var u=JsonDocument.Parse(usage);using var l=JsonDocument.Parse(limits);var credits=JsonTools.At(l.RootElement,"rateLimitResetCredits");var available=JsonTools.Array(credits,"credits").Where(c=>JsonTools.String(c,"status")=="available").ToList();var stated=JsonTools.Number(credits,"availableCount");var count=stated is >=0 and <1000000?(int?)stated:JsonTools.At(credits,"credits").ValueKind==JsonValueKind.Array?available.Count:null;var dates=available.Select(c=>JsonTools.Date(c,"expiresAt")).Where(d=>d is not null).ToList();var days=new List<CodexUsageDay>();foreach(var day in JsonTools.Array(u.RootElement,"dailyUsageBuckets")){var at=JsonTools.Date(day,"startDate");var tokens=JsonTools.Number(day,"tokens");if(at is {} time&&tokens is >=0 and <=long.MaxValue)days.Add(new(time,(long)tokens));}var total=JsonTools.Number(u.RootElement,"summary.lifetimeTokens");return new(days,total is >=0 and <=long.MaxValue?(long?)total:null,count,dates.Count>0?dates.Min():null);
    }
    public static async Task<CodexAccountDetails> FetchAsync(string? executable=null,CancellationToken cancellationToken=default,Dictionary<string,string>? environment=null)
    {
        executable??=LocalPaths.FindExecutable("codex");if(executable is null)return new([],null,null,null,"未找到 Codex 工具");
        try{var usage=HandwrittenRpc.InvokeAsync(executable,["app-server"],"account/usage/read",cancellationToken:cancellationToken,environment:environment);var limits=HandwrittenRpc.InvokeAsync(executable,["app-server"],"account/rateLimits/read",cancellationToken:cancellationToken,environment:environment);await Task.WhenAll(usage,limits);return Parse(await usage,await limits);}
        catch(Exception exception)when(exception is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception){cancellationToken.ThrowIfCancellationRequested();return new([],null,null,null,"Codex 工具未提供账号历史或重置积分");}
    }
}
