using System.Text;
using System.Text.Json;
namespace Pulse.Core;
public sealed record HttpReply(int Status,string Body)
{
    public string Url { get; init; } = "";
    public JsonElement Json { get { try { using var doc=JsonDocument.Parse(Body); return doc.RootElement.Clone(); } catch(JsonException) { return default; } } }
    public DiagnosticEntry Diagnostic => new(DateTimeOffset.UtcNow,Redaction.Clean(Url),Status,Redaction.Clean(Body)[..Math.Min(Redaction.Clean(Body).Length,4000)]);
}
public static class ProviderRequest
{
    public static async Task<HttpReply> SendAsync(FetchContext context,string url,string method="GET",object? body=null,IReadOnlyDictionary<string,string>? headers=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);deadline.CancelAfter(context.RequestTimeout??TimeSpan.FromSeconds(25));var cancellationToken=deadline.Token;
        using var request=new HttpRequestMessage(new HttpMethod(method),url);
        request.Headers.UserAgent.ParseAdd("PulseWindows/0.1.0");
        if(body is not null) request.Content=new StringContent(body is string text?text:JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        if(headers is not null) foreach(var header in headers) {
            if(header.Key.StartsWith("Content-",StringComparison.OrdinalIgnoreCase)) {request.Content??=new ByteArrayContent([]);request.Content.Headers.Remove(header.Key);request.Content.Headers.TryAddWithoutValidation(header.Key,header.Value);}
            else {request.Headers.Remove(header.Key);request.Headers.TryAddWithoutValidation(header.Key,header.Value);}
        }
        using var response=await context.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        await using var input=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer=new MemoryStream(); var chunk=new byte[8192]; int count;
        while((count=await input.ReadAsync(chunk,cancellationToken))>0) { if(buffer.Length+count>2*1024*1024) throw new IOException("服务响应超过大小限制"); buffer.Write(chunk,0,count); }
        return new HttpReply((int)response.StatusCode,Encoding.UTF8.GetString(buffer.ToArray())) { Url=url.Contains('?')?url[..url.IndexOf('?')]:url };
    }
}
public static class ProviderParsing
{
    public static UsageReading Reading(FetchContext context,string? plan=null,string source="endpoint") => new() { AccountId=context.Account.Id,ProviderId=context.Account.ProviderId,Plan=plan,ObservedAt=DateTimeOffset.UtcNow,Source=source };
    public static UsageReading Failed(FetchContext context,HttpReply reply)
    {
        var reading=UsageReading.Unavailable(context.Account,reply.Status is 401 or 403?"authRefused":"serverError",reply.Status is 401 or 403?"登录已失效，请重新连接账号":$"服务响应异常（{reply.Status}）"); reading.Diagnostics.Add(reply.Diagnostic); return reading;
    }
    public static UsageWindow Window(string id,double usedPercent,DateTimeOffset? reset=null,double? seconds=null,string? label=null,bool exhausted=false)
    {
        if(!double.IsFinite(usedPercent)||usedPercent<0) throw new ArgumentOutOfRangeException(nameof(usedPercent));
        var kind=seconds switch { >= 599 and <= 601=>"tenMinute",>= 17999 and <= 18001=>"fiveHour",>= 86399 and <= 86401=>"daily",>= 604799 and <= 604801=>"weekly",>= 2419200 and <= 2678400=>"monthly", _=>"other" };
        return new() { Id=id,UsedPercent=usedPercent,ResetsAt=reset,WindowSeconds=seconds,ReportsLength=seconds.HasValue,Label=label,Kind=kind,Exhausted=exhausted };
    }
}
