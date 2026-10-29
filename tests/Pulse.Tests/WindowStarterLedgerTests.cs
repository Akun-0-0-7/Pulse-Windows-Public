using System.Text.Json;
using Pulse.Core;
namespace Pulse.Tests;
public static class WindowStarterLedgerTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-29T23:59:00+08:00");
    [Check] public static void ReservationsSurviveRestartAndRespectLocalMidnightAndAccount()
    {
        var ledger=new WindowStarterLedger();
        Assert.True(ledger.Reserve("a",Now,1) is not null);
        var restored=new WindowStarterLedger(JsonSerializer.Deserialize<WindowStarterLedgerState>(JsonSerializer.Serialize(ledger.State))!);
        Assert.True(!restored.CanReserve("a",Now,1));
        Assert.True(restored.Reserve("a",Now,1) is null);
        Assert.True(restored.Reserve("b",Now,1) is not null);
        Assert.True(restored.Reserve("a",Now.AddMinutes(2),1) is not null);
        Assert.Equal(1,restored.Today("a",Now).Attempts);
        Assert.Equal(1,restored.Today("a",Now.AddMinutes(2)).Attempts);
    }
    [Check] public static void UnknownOutcomeRetainsSlotAndMeasuredCompletionIsIdempotent()
    {
        var ledger=new WindowStarterLedger();var first=ledger.Reserve("a",Now,0)!;
        Assert.True(first is not null);
        ledger.Reserve("a",Now,0);
        var receipt=new WindowStarterReceipt("success",true,16000,10,null);
        ledger.Complete(first!.Id,Now,receipt,"gpt-6.1-terra","pulseSetting");
        ledger.Complete(first.Id,Now,receipt,"gpt-6-astra","codexConfig");
        var day=ledger.Today("a",Now);
        Assert.Equal(2,day.Attempts);Assert.Equal(1,day.MeasuredAttempts);
        Assert.Equal(16000L,day.InputTokens);Assert.Equal(10L,day.OutputTokens);
        Assert.Equal("gpt-6.1-terra",ledger.State.History.Single(x=>x.Id==first.Id).Model);
        Assert.True(!ledger.CanReserve("a",Now,2));
    }
    [Check] public static void BoundedHistoryDoesNotResetDailyCount()
    {
        var ledger=new WindowStarterLedger();for(var i=0;i<250;i++)Assert.True(ledger.Reserve("a",Now,0) is not null);
        Assert.True(ledger.State.History.Count<=200);Assert.Equal(250,ledger.Today("a",Now).Attempts);
        Assert.True(!ledger.CanReserve("a",Now,250));
        Assert.True(!ledger.CanReserve("a",Now,-1));Assert.True(!ledger.CanReserve("a",Now,1001));
        Assert.True(WindowStarterLedger.IsValid(ledger.State));
    }
    [Check] public static void CorruptLedgerCannotResetTheLimit()
    {
        Assert.True(!WindowStarterLedger.IsValid(new WindowStarterLedgerState()));
        var ledger=new WindowStarterLedger();ledger.Reserve("a",Now,1);
        ledger.State.Days.Add(ledger.State.Days[0]);
        Assert.True(!WindowStarterLedger.IsValid(ledger.State));
        Assert.Throws<InvalidDataException>(()=>new WindowStarterLedger(ledger.State));
    }
}
