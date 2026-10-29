using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace Pulse.Desktop;

internal interface ILaunchEntryStore
{
    LaunchEntries Read();
    void SetStartup(string? command);
    void SetShortcut(string executable);
}
internal sealed class StartupConsistencyService(ILaunchEntryStore store,string executable,Func<string,bool> exists)
{
    public static StartupConsistencyService Create()=>new(new WindowsLaunchEntryStore(),Environment.ProcessPath??"",File.Exists);
    public LaunchEntrySnapshot Inspect(bool enabled)=>LaunchEntryPolicy.Inspect(executable,enabled,store.Read(),exists);
    public bool Repair(LaunchEntrySnapshot expected)
    {
        var current=Inspect(expected.StartWithWindows);
        if(current!=expected||!current.CanRepair)return false;
        if(current.Startup.NeedsRepair)store.SetStartup(expected.StartWithWindows?'"'+executable+'"':null);
        if(current.Shortcut.NeedsRepair)store.SetShortcut(executable);
        var after=Inspect(expected.StartWithWindows);
        return !after.NeedsRepair&&after.Startup.State is LaunchEntryState.Current or LaunchEntryState.Disabled&&after.Shortcut.State==LaunchEntryState.Current;
    }
    private sealed class WindowsLaunchEntryStore:ILaunchEntryStore
    {
        private static string Link=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Pulse Windows.lnk");
        public LaunchEntries Read()
        {
            string? run=null,target=null,arguments=null;var runReadable=true;var shortcutReadable=true;var present=File.Exists(Link);
            try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");var value=key?.GetValue("PulseWindows");run=value as string;if(value is not null&&value is not string)runReadable=false;}
            catch(Exception e)when(e is UnauthorizedAccessException or System.Security.SecurityException or IOException){runReadable=false;}
            if(present)try{WithShortcut(link=>{target=(string)link.TargetPath;arguments=(string)link.Arguments;});}
            catch(Exception e)when(e is COMException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException){shortcutReadable=false;}
            return new(run,present,target,arguments,runReadable,shortcutReadable);
        }
        public void SetStartup(string? command)
        {using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(command is null)key.DeleteValue("PulseWindows",false);else key.SetValue("PulseWindows",command);}
        public void SetShortcut(string executable)=>WithShortcut(link=>{link.TargetPath=executable;link.Arguments="";link.WorkingDirectory=Path.GetDirectoryName(executable);link.IconLocation=executable+",0";link.Description="Pulse Windows";link.Save();});
        private static void WithShortcut(Action<dynamic> action)
        {
            object? shell=null,link=null;
            try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException("Shortcut service unavailable"));link=((dynamic)shell!).CreateShortcut(Link);action(link);}
            finally{if(link is not null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);if(shell is not null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
        }
    }
}
