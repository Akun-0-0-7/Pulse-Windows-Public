using System.Net;
using Pulse.Core;
namespace Pulse.Tests;
public static class PricingTests
{
    sealed class FixtureHttp(string body):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body)});}
    [Check]public static async Task CatalogKeepsLongContextTiersAndFirstPartyPriority()
    {
        var directory=Path.Combine(Path.GetTempPath(),"pulse-prices-"+Guid.NewGuid().ToString("N"));try
        {using var http=new HttpClient(new FixtureHttp("""{"reseller":{"models":{"x":{"cost":{"input":99,"output":99}}}},"openai":{"models":{"x":{"name":"Test model","cost":{"input":1,"output":2,"tiers":[{"tier":{"type":"context","size":272001},"input":4,"output":6},{"tier":{"type":"other","size":4},"input":99,"output":99}]}}}},"anthropic":{"models":{"claude-test":{"cost":{"input":2,"output":3,"context_over_200k":{"input":4,"output":5,"cache_read":0.5}}}}}}"""));var prices=await PricingCatalog.LoadAsync(new(directory),http);Assert.Equal(1m,prices["x"].Input);Assert.Equal("Test model",prices["x"].Name);Assert.Equal(272000L,prices["x"].Tiers![0].Threshold);Assert.Equal(200000L,prices["claude-test"].Tiers![0].Threshold);Assert.Equal(.5m,prices["claude-test"].Tiers![0].CacheRead);Assert.Equal(99m,prices["reseller/x"].Input);}
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Check]public static async Task ContextTierPricesWholeRequestOnlyWhenRequestContextIsKnown()
    {
        var home=Path.Combine(Path.GetTempPath(),"pulse-price-context-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(home,".claude","projects"));try
        {var record="""{"type":"assistant","timestamp":"2026-10-20T01:00:00Z","message":{"id":"a","model":"x","usage":{"input_tokens":200000,"output_tokens":100}}}""";var file=Path.Combine(home,".claude","projects","a.jsonl");File.WriteAllText(file,record);var prices=new Dictionary<string,SpendPrice>{{"x",new(1,2,Tiers:[new(200000,4,6)])}};var config=new AppConfig{SpendEnabled=true,EnabledSpendSources=["claude"]};var service=new SpendService(prices);var report=await service.ScanAsync(config,home);Assert.Equal(.2002m,report.EstimatedCost);File.WriteAllText(file,record.Replace("200000","200001"));report=await service.ScanAsync(config,home);Assert.Equal(.800604m,report.EstimatedCost);}
        finally{Directory.Delete(home,true);}
    }
    [Check]public static void KnownAliasesResolveButUnknownModelsStayUnpriced()
    {
        var prices=new Dictionary<string,SpendPrice>{{"gpt-5.6-sol",new(1,2)},{"gemini-3.5-flash",new(3,4)},{"kimi-k2.6",new(5,6)}};
        Assert.Equal(1m,PricingCatalog.Resolve("gpt-5-6-sol-medium",prices)!.Input);Assert.Equal(3m,PricingCatalog.Resolve("gemini-3.5-flash-extra-low",prices)!.Input);Assert.Equal(5m,PricingCatalog.Resolve("k2p6-256k",prices)!.Input);Assert.True(PricingCatalog.Resolve("not-a-real-model",prices) is null);
    }
    [Check]public static async Task ReplayedSnapshotsRetainLargestKnownRequestContext()
    {
        var home=Path.Combine(Path.GetTempPath(),"pulse-price-replay-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(home,".claude","projects"));try
        {var line="""{"type":"assistant","timestamp":"2026-10-20T01:00:00Z","message":{"id":"a","model":"x","usage":{"input_tokens":100,"output_tokens":100}}}""";File.WriteAllText(Path.Combine(home,".claude","projects","a.jsonl"),line+"\n"+line.Replace("input_tokens\":100","input_tokens\":200001"));var r=await new SpendService(new Dictionary<string,SpendPrice>{{"x",new(1,2,Tiers:[new(200000,4,6)])}}).ScanAsync(new(){SpendEnabled=true,EnabledSpendSources=["claude"]},home);Assert.Equal(.800604m,r.EstimatedCost);Assert.Equal(1,r.Records.Count);}
        finally{Directory.Delete(home,true);}
    }
}
