using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class DockProgressTests
{
    private static (double? Used,double? Fraction,string Color) State(AppConfig config,IEnumerable<UsageReading> readings)
    {
        var state=AppearancePalette.DockState(config,readings);
        return (state.UsedPercent,state.Fraction,state.Color);
    }
    private static AppConfig Config()=>new(){Accounts=[new(){Id="one",ProviderId="fixture",Enabled=true,PrimaryWindowId="five"}],Appearance=new(){QuotaColorMode="gradient",DockColor="#123456"}};
    private static UsageReading Reading(double used=20)=>new(){AccountId="one",ProviderId="fixture",Windows=[new(){Id="five",Kind="fiveHour",UsedPercent=used},new(){Id="week",Kind="weekly",UsedPercent=100,Exhausted=true}]};

    [Check] public static void SelectedWindowDrivesRemainingUsedAndGradientTogether()
    {
        var config=Config();var reading=Reading();var state=State(config,[reading]);
        Assert.Equal(20d,state.Used);Assert.Near(.8,state.Fraction!.Value);Assert.Equal(AppearancePalette.Quota(config,20),state.Color);
        config.ShowRemaining=false;var used=State(config,[reading]);
        Assert.Near(.2,used.Fraction!.Value);Assert.Equal(state.Color,used.Color);
        config.Accounts[0].PrimaryWindowId="week";var week=State(config,[reading]);
        Assert.Equal(100d,week.Used);Assert.Near(1,week.Fraction!.Value);Assert.Equal(AppearancePalette.Quota(config,100,true),week.Color);
    }
    [Check] public static void FixedColorStillHasQuotaLengthAndMultiAccountSelection()
    {
        var config=Config();config.Accounts.Add(new(){Id="two",ProviderId="fixture",Enabled=true,PrimaryWindowId="main"});
        var second=new UsageReading{AccountId="two",ProviderId="fixture",Windows=[new(){Id="main",UsedPercent=45}]};
        config.Appearance.DockColorMode="fixed";var state=State(config,[Reading(),second]);
        Assert.Near(.55,state.Fraction!.Value);Assert.Equal(45d,state.Used);Assert.Equal("#123456",state.Color);
        config.Accounts[1].Enabled=false;Assert.Near(.8,State(config,[Reading(),second]).Fraction!.Value);
        second.ProviderId="wrong";config.Accounts[1].Enabled=true;Assert.Near(.8,State(config,[Reading(),second]).Fraction!.Value);
    }
    [Check] public static void ExhaustedAndOverflowClampWhileUnknownNeverFabricatesFill()
    {
        var config=Config();Assert.Near(1,State(config,[Reading(0)]).Fraction!.Value);
        Assert.Near(0,State(config,[Reading(150)]).Fraction!.Value);
        var exhausted=Reading(8);exhausted.Windows[0].Exhausted=true;
        Assert.Equal(100d,State(config,[exhausted]).Used);Assert.Near(0,State(config,[exhausted]).Fraction!.Value);
        foreach(var bad in new[]{double.NaN,double.PositiveInfinity,-1d})
        {
            var state=State(config,[Reading(bad)]);Assert.True(state.Used is null&&state.Fraction is null);Assert.Equal("#123456",state.Color);
        }
        var stale=Reading();stale.IsStale=true;Assert.True(State(config,[stale]).Fraction is null);
        var error=Reading();error.ErrorCode="fixture-error";Assert.True(State(config,[error]).Fraction is null);
        Assert.True(State(config,[]).Fraction is null);
    }
    [Check] public static void ProgressDefaultsAndSavedSwitchPreserveExistingColors()
    {
        var old=JsonSerializer.Deserialize<AppConfig>("{}",StorageJson.Options)!;
        Assert.True(old.Appearance.DockShowProgress);
        var before=AppearancePalette.Signature(old);old.Appearance.DockShowProgress=false;
        var saved=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(old,StorageJson.Options),StorageJson.Options)!;
        Assert.True(!saved.Appearance.DockShowProgress);Assert.True(before!=AppearancePalette.Signature(saved));
        saved.Appearance.QuotaColorMode="fixed";saved.Appearance.QuotaNormal="#AABBCC";saved.Accounts=Config().Accounts;
        var state=State(saved,[Reading()]);Assert.Equal("#AABBCC",state.Color);Assert.Near(.8,state.Fraction!.Value);
        saved.Appearance=null!;Assert.Near(.8,State(saved,[Reading()]).Fraction!.Value);
    }
    [Check] public static void StateEnumeratesTheReadingSnapshotOnlyOnce()
    {
        var enumerated=0;
        IEnumerable<UsageReading> Once(){if(++enumerated>1)throw new Exception("Readings selected twice.");yield return Reading();}
        var state=State(Config(),Once());Assert.Near(.8,state.Fraction!.Value);Assert.Equal(1,enumerated);
    }
}
