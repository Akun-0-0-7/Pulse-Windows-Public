using Pulse.Core;

namespace Pulse.Tests;

public static class LaunchEntryPolicyTests
{
    private const string Current=@"C:\Apps\Pulse current\Pulse.exe";
    private const string Old=@"C:\Apps\Pulse old\Pulse.exe";
    private static LaunchEntrySnapshot Inspect(string? run,bool enabled=true,bool shortcut=true,string? target=Current,string? args="",bool currentExists=true)=>
        LaunchEntryPolicy.Inspect(Current,enabled,new(run,shortcut,target,args),path=>currentExists&&path.Equals(Current,StringComparison.OrdinalIgnoreCase));

    [Check] public static void MissingAndOldPulseEntriesCanBeRepairedWithoutChangingStartupPreference()
    {
        var missing=Inspect(null,shortcut:false);
        Assert.Equal(LaunchEntryState.Missing,missing.Startup.State);Assert.Equal(LaunchEntryState.Missing,missing.Shortcut.State);Assert.True(missing.CanRepair);
        var old=Inspect('"'+Old+'"',target:Old);
        Assert.Equal(LaunchEntryState.DifferentVersion,old.Startup.State);Assert.Equal(LaunchEntryState.DifferentVersion,old.Shortcut.State);Assert.True(old.CanRepair);
        var disabled=Inspect(null,enabled:false);
        Assert.Equal(LaunchEntryState.Disabled,disabled.Startup.State);Assert.True(!disabled.NeedsRepair);
        var offWithOldEntry=Inspect('"'+Old+'"',enabled:false);
        Assert.Equal(LaunchEntryState.DisabledButPresent,offWithOldEntry.Startup.State);Assert.True(offWithOldEntry.CanRepair);
    }

    [Check] public static void ValidCurrentEntriesAreComparedAsPathsRatherThanText()
    {
        var current=Inspect("  \"c:\\apps\\Pulse current\\Pulse.exe\"  ",target:Current.ToUpperInvariant());
        Assert.Equal(LaunchEntryState.Current,current.Startup.State);Assert.Equal(LaunchEntryState.Current,current.Shortcut.State);Assert.True(!current.NeedsRepair);
        var unquoted=Inspect(Current);Assert.Equal(LaunchEntryState.Current,unquoted.Startup.State);
    }

    [Check] public static void ForeignOrAmbiguousCommandsFailClosedBeforeAnyRepair()
    {
        foreach(var command in new[]{@"C:\Other\Other.exe",'"'+Old+"\" --settings",'"'+Old+"\" && calc.exe",@"%LOCALAPPDATA%\Pulse.exe",@"Pulse.exe",@"C:\Apps\Pulse.exe\other.exe",'"'+Old,"\"\"",@"\\server\share\Pulse.exe"})
        {
            var snapshot=Inspect(command,shortcut:false);
            Assert.Equal(LaunchEntryState.Unrecognized,snapshot.Startup.State);Assert.True(!snapshot.CanRepair,"Unexpected startup command permitted a repair");
        }
        foreach(var args in new[]{"--settings","pulse://account/codex"})
        {
            var snapshot=Inspect('"'+Current+'"',args:args);
            Assert.Equal(LaunchEntryState.Unrecognized,snapshot.Shortcut.State);Assert.True(!snapshot.CanRepair);
        }
        var foreign=Inspect(null,target:@"C:\Other\Other.exe");Assert.True(!foreign.CanRepair);
    }

    [Check] public static void MissingCurrentExecutableAndUnreadableEntriesCannotBeRepairTargets()
    {
        Assert.True(!Inspect(null,currentExists:false).CanRepair);
        var unreadable=LaunchEntryPolicy.Inspect(Current,true,new(null,false,null,null,RunReadable:false),_=>true);
        Assert.Equal(LaunchEntryState.Unavailable,unreadable.Startup.State);Assert.True(!unreadable.CanRepair);
    }
}
