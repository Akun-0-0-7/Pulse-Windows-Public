using System.Diagnostics;
namespace Pulse.Desktop;

public sealed record UpdateInstallResult(bool Scheduled,string Message,string PreparedDirectory,string LogPath);
public static class UpdateInstaller
{
    private static readonly HashSet<string> scheduled=new(StringComparer.OrdinalIgnoreCase);
    public static UpdateInstallResult Schedule(PreparedUpdate update,string targetDirectory,int processId,bool confirmedByUser)
    {
        if(!confirmedByUser)return new(false,"更新包已准备，点击确认安装后才会替换程序。",update.PreparationDirectory,"");
        lock(scheduled)
        {
            if(scheduled.Contains(update.PreparationDirectory))return new(false,"此更新已经安排安装，请等待 Pulse 退出。",update.PreparationDirectory,"");
            try
            {
                // A successful create/delete checks the user's actual access, without elevation.
                var probe=Path.Combine(Path.GetFullPath(targetDirectory),".pulse-update-write-"+Guid.NewGuid().ToString("N"));using(var writable=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){}
                var installer=UpdatePackage.WriteInstaller(update,targetDirectory,processId);var powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");var start=new ProcessStartInfo(powershell){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",installer.ScriptPath,"-PlanFile",installer.PlanPath})start.ArgumentList.Add(argument);
                using var process=Process.Start(start)??throw new InvalidOperationException("无法启动更新安装器。");scheduled.Add(update.PreparationDirectory);return new(true,"更新包已校验，Pulse 退出后将安装并重新打开。",update.PreparationDirectory,installer.LogPath);
            }
            catch(InvalidDataException){return new(false,"准备好的更新文件已变化或路径不适用，请重新下载更新包。",update.PreparationDirectory,"");}
            catch(Exception exception)when(exception is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {return new(false,"安装目录不可写或安装器无法启动。更新包已保留，可打开准备目录中的发布文件夹手动安装。",update.PreparationDirectory,"");}
        }
    }
}
