using Pulse.Core;
using System.Net;
namespace Pulse.Tests;
public static class ReviewFixTests
{
    private sealed class HangingBody : Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}public override void Flush(){}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){await Task.Delay(Timeout.Infinite,ct);return 0;}
    }
    private sealed class HeadersOnly : HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new HangingBody())});}
    [Check] public static async Task BodyReadHasItsOwnDeadline()
    {
        using var http=new HttpClient(new HeadersOnly());var context=new FetchContext(new(){Enabled=true},null,http,Path.GetTempPath(),Path.GetTempPath(),CancellationToken.None,RequestTimeout:TimeSpan.FromMilliseconds(80));var request=ProviderRequest.SendAsync(context,"https://fixture.invalid/usage");var winner=await Task.WhenAny(request,Task.Delay(2000));Assert.True(ReferenceEquals(request,winner),"Response body exceeded request deadline");try{await request;throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
    }
    [Check] public static void AccountCredentialSlotsAreClearedTogether()
    {
        var secrets=new MemorySecrets();foreach(var key in new[]{"a","console:a","desktop:a","b"})secrets.Set(key,"fixture");CredentialSlots.Clear(secrets,"a");Assert.Equal<string?>(null,secrets.Get("a"));Assert.Equal<string?>(null,secrets.Get("console:a"));Assert.Equal<string?>(null,secrets.Get("desktop:a"));Assert.Equal("fixture",secrets.Get("b"));
    }
    [Check] public static void CsvLeavesUnreportedCountersEmptyAndEscapesFormulaText()
    {
        var report=new SpendReport{Records=[new("warp","no-tokens",DateTimeOffset.UnixEpoch,"=malicious()",ReportsTokens:false,ReportedCost:1m)]};var csv=SpendCsv.Format(report);Assert.True(csv.Contains("\"'=malicious()\",,,,,,"));Assert.True(!csv.Contains(",0,"));
    }
    [Check] public static void AdaptiveRefreshRespectsActivityAndIdleBoundaries()
    {
        Assert.Equal(2,RefreshSchedule.Minutes(2,true,false,TimeSpan.FromHours(1)));
        Assert.Equal(2,RefreshSchedule.Minutes(2,false,true,TimeSpan.FromHours(1)));
        Assert.Equal(2,RefreshSchedule.Minutes(2,false,false,TimeSpan.Zero));
        Assert.Equal(5,RefreshSchedule.Minutes(2,false,false,TimeSpan.FromMinutes(2)));
        Assert.Equal(10,RefreshSchedule.Minutes(2,false,false,TimeSpan.FromMinutes(5)));
        Assert.Equal(30,RefreshSchedule.Minutes(2,false,false,TimeSpan.FromMinutes(10)));
        Assert.Equal(15,RefreshSchedule.Minutes(15,false,false,TimeSpan.Zero));
        Assert.Equal(30,RefreshSchedule.Minutes(90,true,false,TimeSpan.Zero));
    }
    private sealed class HeaderCapture:HttpMessageHandler{public string? Agent;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Agent=request.Headers.UserAgent.ToString();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")});}}
    [Check] public static async Task ServiceUserAgentOverridesDefaultAndProxySettingsReachHelpers()
    {
        var handler=new HeaderCapture();using var http=new HttpClient(handler);await ProviderRequest.SendAsync(new FetchContext(new(),null,http,"","",CancellationToken.None),"https://fixture.invalid/usage",headers:new Dictionary<string,string>{{"User-Agent","Warp/1.0"}});Assert.Equal("Warp/1.0",handler.Agent);
        var none=new Dictionary<string,string>{{"https_proxy","https://old.invalid"},{"No_Proxy","old"},{"PATH","preserved"}};ProxyEnvironment.Apply(none,new(){ProxyMode="none"});Assert.True(!none.ContainsKey("https_proxy"));Assert.Equal("*",none["NO_PROXY"]);Assert.Equal("preserved",none["PATH"]);
        ProxyEnvironment.Apply(none,new(){ProxyMode="custom",ProxyAddress="http://127.0.0.1:7890"});Assert.Equal("http://127.0.0.1:7890/",none["HTTPS_PROXY"]);Assert.True(!none.ContainsKey("NO_PROXY"));
    }
    [Check] public static async Task CancellingAnOwnedHelperLeavesNoTrackedChild()
    {
        if(!OperatingSystem.IsWindows())return;
        using var cancelled=new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try{await ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe"),["-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 60"],cancellationToken:cancelled.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        Assert.Equal(0,ChildProcesses.Count);
    }
    private sealed class MemorySecrets:ISecretStore {readonly Dictionary<string,string> values=[];public string? Get(string key)=>values.GetValueOrDefault(key);public void Set(string key,string? value){if(value is null)values.Remove(key);else values[key]=value;}}
}
