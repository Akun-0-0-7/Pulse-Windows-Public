namespace Pulse.Core;

public enum LaunchEntryState { Current, Missing, DifferentVersion, Disabled, DisabledButPresent, Unrecognized, Unavailable }
public sealed record LaunchEntryAssessment(LaunchEntryState State,bool NeedsRepair);
public sealed record LaunchEntries(string? RunCommand,bool ShortcutExists,string? ShortcutTarget,string? ShortcutArguments,
    bool RunReadable=true,bool ShortcutReadable=true);
public sealed record LaunchEntrySnapshot(string CurrentExecutable,bool StartWithWindows,LaunchEntries Entries,
    LaunchEntryAssessment Startup,LaunchEntryAssessment Shortcut,bool CurrentExecutableExists)
{
    public bool NeedsRepair=>Startup.NeedsRepair||Shortcut.NeedsRepair;
    public bool CanRepair=>CurrentExecutableExists&&NeedsRepair&&Startup.State is not(LaunchEntryState.Unrecognized or LaunchEntryState.Unavailable)&&
        Shortcut.State is not(LaunchEntryState.Unrecognized or LaunchEntryState.Unavailable);
}

public static class LaunchEntryPolicy
{
    public static LaunchEntrySnapshot Inspect(string currentExecutable,bool startWithWindows,LaunchEntries entries,Func<string,bool> exists)
    {
        var current=PulsePath(currentExecutable);
        LaunchEntryAssessment Classify(string? path,bool present,bool readable,bool enabled)
        {
            if(!readable)return new(LaunchEntryState.Unavailable,false);
            if(!present)return enabled?new(LaunchEntryState.Missing,true):new(LaunchEntryState.Disabled,false);
            if(path is null)return new(LaunchEntryState.Unrecognized,false);
            if(!enabled)return new(LaunchEntryState.DisabledButPresent,true);
            return string.Equals(path,current,StringComparison.OrdinalIgnoreCase)?new(LaunchEntryState.Current,false):new(LaunchEntryState.DifferentVersion,true);
        }
        var command=entries.RunCommand?.Trim();
        var run=command is null?null:command.StartsWith('"')&&command.EndsWith('"')&&command.Length>=2?PulsePath(command[1..^1]):PulsePath(command);
        var shortcut=string.IsNullOrWhiteSpace(entries.ShortcutArguments)?PulsePath(entries.ShortcutTarget):null;
        var startup=Classify(run,!string.IsNullOrWhiteSpace(command),entries.RunReadable,startWithWindows);
        if(startup.State==LaunchEntryState.Current&&command is not null&&!command.StartsWith('"')&&command.Contains(' '))startup=startup with{NeedsRepair=true};
        return new(currentExecutable,startWithWindows,entries,startup,Classify(shortcut,entries.ShortcutExists,entries.ShortcutReadable,true),current is not null&&exists(current));
    }
    private static string? PulsePath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)||path.Any(char.IsControl)||path.Contains('"')||
            !System.Text.RegularExpressions.Regex.IsMatch(path,@"\A[A-Za-z]:[\\/]"))return null;
        try{var full=Path.GetFullPath(path);return Path.GetFileName(full).Equals("Pulse.exe",StringComparison.OrdinalIgnoreCase)?full:null;}
        catch(Exception e)when(e is ArgumentException or NotSupportedException or PathTooLongException){return null;}
    }
}
