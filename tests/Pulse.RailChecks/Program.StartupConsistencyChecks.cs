using Pulse.Core;
using Pulse.Desktop;
internal static partial class Program
{
    private sealed class MemoryLaunchEntries:ILaunchEntryStore
    {
        public LaunchEntries Entries=new(null,false,null,null);
        public int Writes;
        public LaunchEntries Read()=>Entries;
        public void SetStartup(string? command){Writes++;Entries=Entries with{RunCommand=command};}
        public void SetShortcut(string executable){Writes++;Entries=Entries with{ShortcutExists=true,ShortcutTarget=executable,ShortcutArguments=""};}
    }
    private static void StartupConsistencyChecks()
    {
        const string current=@"C:\Apps\Pulse\Pulse.exe";
        var store=new MemoryLaunchEntries();var service=new StartupConsistencyService(store,current,_=>true);
        var snapshot=service.Inspect(true);if(store.Writes!=0||!snapshot.CanRepair)throw new Exception("Inspection mutated startup entries or omitted missing entries.");
        store.Entries=store.Entries with{RunCommand=@"C:\Other\Other.exe"};
        if(service.Repair(snapshot)||store.Writes!=0)throw new Exception("Stale inspection overwrote a changed command.");
        store.Entries=new(null,false,null,null);snapshot=service.Inspect(false);
        if(!service.Repair(snapshot)||store.Writes!=1||store.Entries.RunCommand is not null||store.Entries.ShortcutTarget!=current)throw new Exception("Repair did not preserve disabled startup.");
        store.Entries=store.Entries with{RunCommand="\"C:\\Old\\Pulse.exe\""};snapshot=service.Inspect(true);
        if(!service.Repair(snapshot)||store.Entries.RunCommand!='"'+current+'"'||service.Inspect(true).NeedsRepair)throw new Exception("Repair failed to converge to current executable.");
        Console.WriteLine("PASS: read-only startup inspection, stale/foreign entry protection, scoped repair, and disabled-startup preservation.");
    }
}
