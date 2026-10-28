using System.Diagnostics;
namespace Pulse.Core;
public static class LocalPaths
{
    public static string Home=>Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Expand(string path,string home) => Environment.ExpandEnvironmentVariables(path.StartsWith("~/")||path.StartsWith("~\\")?Path.Combine(home,path[2..]):path);
    public static string? ReadText(string path) { try { return File.Exists(path)?File.ReadAllText(path):null; } catch(IOException) { return null; } catch(UnauthorizedAccessException) { return null; } }
    public static string? FindExecutable(string name)
    {
        if(Path.IsPathRooted(name)) return File.Exists(name)?name:null;
        var dirs=(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Concat([Path.Combine(Home,".local","bin"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm")]);
        foreach(var dir in dirs.Where(d=>!string.IsNullOrWhiteSpace(d))) foreach(var extension in new[]{".exe",".cmd",".bat",""}) { var path=Path.Combine(dir.Trim('"'),Path.HasExtension(name)?name:name+extension); if(File.Exists(path)) return path; }
        if(name.Equals("codex",StringComparison.OrdinalIgnoreCase)){
            var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","OpenAI","Codex","bin","codex.exe");if(File.Exists(installed))return installed;
            var root=Path.Combine(Home,"AppData","Local","OpenAI","Codex","bin");if(Directory.Exists(root)){var bundled=Directory.EnumerateFiles(root,"codex.exe",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true}).Select(p=>new FileInfo(p)).OrderByDescending(p=>p.LastWriteTimeUtc).FirstOrDefault();if(bundled is not null)return bundled.FullName;}
        }
        var local=Path.Combine(Home,".claude","local",name+".exe");return File.Exists(local)?local:null;
    }
}
public sealed record ProcessResult(int ExitCode,string Output,string Error,bool TimedOut);
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string file,IEnumerable<string> arguments,TimeSpan? timeout=null,CancellationToken cancellationToken=default,string? workingDirectory=null,Dictionary<string,string>? environment=null,int maxOutputBytes=262144)
    {
        using var process=new Process(); var start=WindowsCommand.StartInfo(file,arguments);
        if(workingDirectory is not null) start.WorkingDirectory=workingDirectory;
        if(environment is not null) { start.Environment.Clear(); foreach(var pair in environment) start.Environment[pair.Key]=pair.Value; }
        process.StartInfo=start; process.Start();using var lease=ChildProcesses.Track(process); process.StandardInput.Close();
        var exceeded=false; int outputBytes=0;
        async Task<string> ReadBounded(StreamReader reader) { var builder=new System.Text.StringBuilder(); var chunk=new char[4096]; int count; while((count=await reader.ReadAsync(chunk))>0) { if(Interlocked.Add(ref outputBytes,System.Text.Encoding.UTF8.GetByteCount(chunk,0,count))>maxOutputBytes) { exceeded=true; try { process.Kill(true); } catch(InvalidOperationException) { } break; } builder.Append(chunk,0,count); } return builder.ToString(); }
        var output=ReadBounded(process.StandardOutput); var error=ReadBounded(process.StandardError);
        using var timer=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timer.CancelAfter(timeout??TimeSpan.FromSeconds(20));
        bool timedOut=false;
        try { await process.WaitForExitAsync(timer.Token); } catch(OperationCanceledException) { timedOut=true; try { process.Kill(true); } catch(InvalidOperationException) { } await process.WaitForExitAsync(CancellationToken.None); }
        var result=new ProcessResult(exceeded?-2:process.ExitCode,await output,await error,timedOut);
        cancellationToken.ThrowIfCancellationRequested(); return result;
    }
}
