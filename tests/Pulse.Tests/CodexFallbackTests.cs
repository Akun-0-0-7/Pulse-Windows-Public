using System.Net;
using Pulse.Core;
namespace Pulse.Tests;

public static class CodexFallbackTests
{
    const string UsageUrl="https://chatgpt.com/backend-api/wham/usage";
    sealed class Transport(Func<CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Assert.Equal(UsageUrl,request.RequestUri!.ToString());
            Assert.Equal("Bearer fixture-access",request.Headers.Authorization!.ToString());
            return reply(ct);
        }
    }
    sealed class Fixture:IDisposable
    {
        readonly string root=Path.Combine(Path.GetTempPath(),"pulse-codex-fallback-"+Guid.NewGuid().ToString("N"));
        public FetchContext Context {get;}
        public string Marker {get;}
        public Fixture(bool timeout=false)
        {
            Directory.CreateDirectory(Path.Combine(root,".codex"));
            File.WriteAllText(Path.Combine(root,".codex","auth.json"),"""{"tokens":{"access_token":"fixture-access","account_id":"fixture-account"}}""");
            Marker=Path.Combine(root,"helper-started");
            var environment=new Dictionary<string,string>
            {
                ["DOTNET_ROOT"]=Directory.GetParent(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!.Parent!.Parent!.FullName,
                ["SystemRoot"]=Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                ["TEMP"]=Path.GetTempPath(),["TMP"]=Path.GetTempPath(),
                ["PULSE_TEST_CODEX_FIXTURE"]="true",["PULSE_TEST_CODEX_MARKER"]=Marker
            };
            var transport=new Transport(timeout?async ct=>{await Task.Delay(Timeout.Infinite,ct);return new(HttpStatusCode.OK);}:_=>throw new HttpRequestException("Bearer fixture-secret-do-not-log user@example.invalid"));
            var account=new AccountConfig{Id="fixture-codex",ProviderId="codex",Enabled=true,Route="automatic"};
            account.Extra["executable"]=Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe");
            Context=new(account,null,new HttpClient(transport),root,root,CancellationToken.None,RequestTimeout:TimeSpan.FromMilliseconds(40),ProcessEnvironment:environment);
        }
        public Task<UsageReading> Read(FetchContext? context=null)=>HandwrittenProviders.TryCreate("codex")!.FetchAsync(context??Context);
        public void Dispose(){Context.Http.Dispose();Directory.Delete(root,true);}
    }
    static void ConnectionDiagnostic(UsageReading reading,string kind)
    {
        Assert.Equal(1,reading.Diagnostics.Count);
        var diagnostic=reading.Diagnostics[0];Assert.Equal(UsageUrl,diagnostic.Url);Assert.Equal(0,diagnostic.Status);
        Assert.True(diagnostic.Body.Contains(kind,StringComparison.OrdinalIgnoreCase));
        Assert.True(!diagnostic.Body.Contains("fixture-secret",StringComparison.Ordinal));
        Assert.True(!diagnostic.Body.Contains("user@example.invalid",StringComparison.Ordinal));
    }
    [Check] public static async Task AutomaticConnectionFailureUsesAppServer()
    {
        if(!OperatingSystem.IsWindows())return;
        using var fixture=new Fixture();var reading=await fixture.Read();
        Assert.True(reading.Success,"Automatic Codex route did not recover from a transport error");
        Assert.Equal("app-server",reading.Source);Assert.Near(17,reading.Windows[0].UsedPercent);Assert.True(File.Exists(fixture.Marker));
        ConnectionDiagnostic(reading,"connectionFailed");
    }
    [Check] public static async Task AutomaticRequestTimeoutUsesAppServer()
    {
        if(!OperatingSystem.IsWindows())return;
        using var fixture=new Fixture(timeout:true);var reading=await fixture.Read();
        Assert.True(reading.Success,"Automatic Codex route did not recover from a request timeout");
        Assert.Equal("app-server",reading.Source);Assert.Near(17,reading.Windows[0].UsedPercent);Assert.True(File.Exists(fixture.Marker));
        ConnectionDiagnostic(reading,"requestTimeout");
    }
    [Check] public static async Task PinnedEndpointAndSuppliedAccountsNeverUseLocalFallback()
    {
        foreach(var timeout in new[]{false,true})foreach(var mode in new[]{"endpoint","supplied","additional"})
        {
            using var fixture=new Fixture(timeout);var context=fixture.Context;
            if(mode=="endpoint")context.Account.Route="endpoint";
            else context=context with{Credential="fixture-access"};
            if(mode=="additional")context.Account.Extra["isAdditionalAccount"]="true";
            var reading=await fixture.Read(context);Assert.Equal("unreachable",reading.ErrorCode);Assert.True(!File.Exists(fixture.Marker),"Local login crossed an account or endpoint boundary");
            ConnectionDiagnostic(reading,timeout?"requestTimeout":"connectionFailed");
        }
    }
    [Check] public static async Task CallerCancellationDoesNotUseLocalFallback()
    {
        using var fixture=new Fixture(timeout:true);using var cancellation=new CancellationTokenSource();
        var request=fixture.Read(fixture.Context with{CancellationToken=cancellation.Token,RequestTimeout=TimeSpan.FromSeconds(5)});cancellation.Cancel();
        try {await request;throw new Exception("Expected caller cancellation");}
        catch(OperationCanceledException){}
        Assert.True(!File.Exists(fixture.Marker));
    }
    [Check] public static async Task HttpServerFailureStillDoesNotUseLocalFallback()
    {
        using var fixture=new Fixture();using var http=new HttpClient(new Transport(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("{}")})));
        var reading=await fixture.Read(fixture.Context with{Http=http});Assert.Equal("serverError",reading.ErrorCode);Assert.True(!File.Exists(fixture.Marker));Assert.Equal(503,reading.Diagnostics[0].Status);
    }
    [Check] public static async Task UnparseableReplyStillDoesNotUseLocalFallback()
    {
        using var fixture=new Fixture();using var http=new HttpClient(new Transport(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("invalid fixture JSON")})));
        var reading=await fixture.Read(fixture.Context with{Http=http});Assert.Equal("unreadableReply",reading.ErrorCode);Assert.True(!File.Exists(fixture.Marker));Assert.Equal(200,reading.Diagnostics[0].Status);
    }
}
