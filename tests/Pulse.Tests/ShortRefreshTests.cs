using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class ShortRefreshTests
{
    [Check] public static void ActiveIntervalsReachTheTimerInSeconds()
    {
        foreach(var seconds in new[]{10,30,60,120,300,600,900,1800})
        {
            var minutes=RefreshSchedule.Minutes(seconds/60d,true,false,TimeSpan.FromHours(1));
            Assert.Equal(TimeSpan.FromSeconds(seconds),TimeSpan.FromMinutes(minutes));
        }
    }

    [Check] public static void VisibleSettingsPreserveShortIntervalsWhileComputerIsIdle()
    {
        Assert.Equal(TimeSpan.FromSeconds(10),TimeSpan.FromMinutes(RefreshSchedule.Minutes(1d/6,false,true,TimeSpan.FromHours(1))));
        Assert.Equal(TimeSpan.FromSeconds(30),TimeSpan.FromMinutes(RefreshSchedule.Minutes(.5,false,true,TimeSpan.FromHours(1))));
    }

    [Check] public static void RecentWindowsInputPreservesShortIntervalWithoutAgentOrSettings()
    {
        foreach(var idle in new[]{TimeSpan.Zero,TimeSpan.FromMinutes(2)-TimeSpan.FromTicks(1)})
            Assert.Equal(TimeSpan.FromSeconds(10),TimeSpan.FromMinutes(RefreshSchedule.Minutes(1d/6,false,false,idle)));
    }

    [Check] public static void IdleBoundariesSlowToFiveTenAndThirtyMinutes()
    {
        var cases=new (TimeSpan idle,double expected)[]
        {
            (TimeSpan.FromMinutes(2),5),
            (TimeSpan.FromMinutes(5)-TimeSpan.FromTicks(1),5),
            (TimeSpan.FromMinutes(5),10),
            (TimeSpan.FromMinutes(10)-TimeSpan.FromTicks(1),10),
            (TimeSpan.FromMinutes(10),30)
        };
        foreach(var (idle,expected) in cases)Assert.Equal(expected,RefreshSchedule.Minutes(1d/6,false,false,idle));
    }

    [Check] public static void IdleScheduleNeverSpeedsUpALongerUserInterval()
    {
        Assert.Equal(15d,RefreshSchedule.Minutes(15,false,false,TimeSpan.FromMinutes(2)));
        Assert.Equal(15d,RefreshSchedule.Minutes(15,false,false,TimeSpan.FromMinutes(5)));
        Assert.Equal(30d,RefreshSchedule.Minutes(30,false,false,TimeSpan.FromMinutes(10)));
    }

    [Check] public static void RefreshRangeIsTenSecondsThroughThirtyMinutes()
    {
        foreach(var minutes in new[]{-1d,0,.01})
            Assert.Equal(TimeSpan.FromSeconds(10),TimeSpan.FromMinutes(RefreshSchedule.Minutes(minutes,true,false,TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromMinutes(30),TimeSpan.FromMinutes(RefreshSchedule.Minutes(90,true,false,TimeSpan.Zero)));
    }

    [Check] public static void NonFiniteIntervalsFallBackToTwoMinutes()
    {
        foreach(var minutes in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Assert.Equal(2d,RefreshSchedule.Minutes(minutes,true,false,TimeSpan.Zero));
    }

    [Check] public static void LegacyIntegerMinuteSettingsRemainCompatible()
    {
        foreach(var minutes in new[]{2,5,10,15,30})
        {
            var config=JsonSerializer.Deserialize<AppConfig>("{\"refreshMinutes\":"+minutes+"}",StorageJson.Options)!;
            Assert.Equal(TimeSpan.FromMinutes(minutes),TimeSpan.FromMinutes(RefreshSchedule.Minutes(config.RefreshMinutes,true,false,TimeSpan.Zero)));
        }
        var defaults=JsonSerializer.Deserialize<AppConfig>("{}",StorageJson.Options)!;
        Assert.Equal(2d,defaults.RefreshMinutes);
    }

    [Check] public static void FractionalMinuteSettingsKeepTheirSecondsAfterSaving()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-short-refresh-"+Guid.NewGuid().ToString("N"));
        try
        {
            var storage=new AppStorage(root);
            foreach(var seconds in new[]{10,30})
            {
                var config=new AppConfig{RefreshMinutes=seconds/60d};
                storage.SaveConfig(config);
                var saved=storage.LoadConfig();
                Assert.Equal(config.RefreshMinutes,saved.RefreshMinutes);
                Assert.Equal(TimeSpan.FromSeconds(seconds),TimeSpan.FromMinutes(RefreshSchedule.Minutes(saved.RefreshMinutes,true,false,TimeSpan.Zero)));
            }
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
