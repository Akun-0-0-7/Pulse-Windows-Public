using Pulse.Core;
using System.Text.Json;
namespace Pulse.Tests;
public static class RuntimeTests
{
    private sealed class Secrets : ISecretStore { public int Reads; public string? Get(string key) { Reads++; return null; } public void Set(string key,string? value) { } }
    private sealed class Reader : IUsageProvider { public int Calls; public Task<UsageReading> FetchAsync(FetchContext context) { Calls++; return Task.FromResult(withReading()); } }
    private static UsageReading withReading() => new() { AccountId="a",ProviderId="codex",ObservedAt=DateTimeOffset.UtcNow,Windows=[new() {UsedPercent=12}] };
    [Check] public static async Task DisabledAccountNeverReadsSecretsOrProvider()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-test-"+Guid.NewGuid().ToString("N"));
        try { var secrets=new Secrets(); var reader=new Reader(); var engine=new UsageEngine(new AppStorage(root),secrets,_=>reader); engine.Config.Accounts.Add(new() {Id="a",ProviderId="codex",Enabled=false}); await engine.RefreshAsync(); Assert.Equal(0,secrets.Reads); Assert.Equal(0,reader.Calls); }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Check] public static void JsonExportUsesActualWindowsAndRetainsZero()
    {
        var config=new AppConfig { Accounts=[new(){Id="a",ProviderId="codex",Enabled=true}] };
        var time=DateTimeOffset.Parse("2026-10-20T08:00:00Z");
        var json=UsageExport.ToJson(config,[new(){AccountId="a",ProviderId="codex",ObservedAt=time,Source="endpoint",Windows=[new(){Id="daily",UsedPercent=0,Kind="daily"},new(){Id="model",UsedPercent=83,Kind="weekly"}]}],[]);
        using var doc=JsonDocument.Parse(json); var account=doc.RootElement.GetProperty("accounts")[0];
        Assert.Equal(2,account.GetProperty("windows").GetArrayLength());
        Assert.Equal(0d,account.GetProperty("windows")[0].GetProperty("usedPercent").GetDouble());
        Assert.Equal("model",account.GetProperty("headline").GetProperty("windowId").GetString());
    }
    [Check] public static void AlertDeduplicatesAndDoesNotInferExhaustionFromPercent()
    {
        var config=new AppConfig { AlertsEnabled=true,AlertThresholds=[75] };
        var reading=new UsageReading { AccountId="a",Windows=[new(){Id="limit",UsedPercent=101,Exhausted=false}] };
        var rules=new AlertRules(); Assert.Equal(1,rules.Evaluate(config,reading).Count); Assert.Equal(0,rules.Evaluate(config,reading).Count);
        reading.Windows[0].Exhausted=true; Assert.Equal(1,rules.Evaluate(config,reading).Count);
    }
    [Check] public static void DisplayPercentagePreservesSomeAndRawFraction()
    {
        Assert.Equal(1,UsageFigures.Percent(.03));Assert.Equal(99,UsageFigures.Percent(99.6));Assert.Equal(1,UsageFigures.Percent(99.6,true));
        var config=new AppConfig{Accounts=[new(){Id="a",ProviderId="codex",Enabled=true}]};var json=UsageExport.ToJson(config,[new(){AccountId="a",Windows=[new(){UsedPercent=.03}]}],[]);using var doc=JsonDocument.Parse(json);Assert.Near(.0003,doc.RootElement.GetProperty("accounts")[0].GetProperty("windows")[0].GetProperty("usedFraction").GetDouble());
    }
}
