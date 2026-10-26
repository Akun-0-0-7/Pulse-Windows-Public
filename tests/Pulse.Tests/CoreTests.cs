using Pulse.Core;
using System.Text.Json;
namespace Pulse.Tests;
public static class CoreTests
{
    [Check] public static void ZeroBalanceIsAReadingButMissingDataIsNot()
    {
        Assert.True(new UsageReading { Balance = new MoneyBalance(0,"CNY") }.Success);
        Assert.True(!new UsageReading().Success);
    }
    [Check] public static void CacheRoundtripPreservesObservedAtAndDoesNotCreateAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(),"pulse-test-"+Guid.NewGuid().ToString("N"));
        try {
            var store = new AppStorage(root);
            Assert.Equal(0,store.LoadConfig().Accounts.Count);
            var time = DateTimeOffset.Parse("2026-10-20T08:00:00Z");
            store.SaveCache([new UsageReading { AccountId="codex",ProviderId="codex",ObservedAt=time,Windows=[new() { UsedPercent=0 }] }]);
            Assert.Equal(time,store.LoadCache()[0].ObservedAt);
            Assert.Equal(0,store.LoadConfig().Accounts.Count);
        } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Check] public static void MissingJsonNumberDoesNotBecomeZero()
    {
        using var document = JsonDocument.Parse("{\"zero\":0}");
        Assert.Equal<double?>(null,JsonTools.Number(document.RootElement,"missing"));
        Assert.Equal<double?>(0,JsonTools.Number(document.RootElement,"zero"));
    }
    [Check] public static void DiagnosticsNeverKeepAuthenticationSecrets()
    {
        var text = Redaction.Clean("{\"access_token\":\"private-value\",\"balance\":0,\"email\":\"someone@example.com\"}");
        Assert.True(!text.Contains("private-value")); Assert.True(!text.Contains("someone@example.com"));
        Assert.True(text.Contains("balance"));
    }
}
