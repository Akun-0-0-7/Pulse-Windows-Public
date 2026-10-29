namespace Pulse.Core;

public static class CodexToolLocator
{
    public static string? Find(string? configuredExecutable=null,Dictionary<string,string>? environment=null)
    {
        if(!string.IsNullOrWhiteSpace(configuredExecutable)&&LocalPaths.FindExecutable(configuredExecutable) is {} configured)return configured;
        string? Env(string key)=>environment is null?Environment.GetEnvironmentVariable(key):environment.GetValueOrDefault(key);
        var active=Env("CODEX_APP_RUNTIME_CLI_PATH");
        if(!string.IsNullOrWhiteSpace(active)&&Path.IsPathFullyQualified(active)&&File.Exists(active))return active;

        // Desktop upgrades keep runtime executables in version directories. A separate
        // PATH installation can still exist but understand an older model catalog.
        var local=Env("LOCALAPPDATA");
        if(!string.IsNullOrWhiteSpace(local)&&Path.IsPathFullyQualified(local))
        {
            var root=Path.Combine(local,"OpenAI","Codex","bin");
            try
            {
                if(Directory.Exists(root))
                {
                    var runtime=Directory.EnumerateDirectories(root,"*",new EnumerationOptions{IgnoreInaccessible=true})
                        .Select(folder=>new FileInfo(Path.Combine(folder,"codex.exe"))).Where(file=>file.Exists)
                        .OrderByDescending(file=>file.LastWriteTimeUtc).ThenBy(file=>file.FullName,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if(runtime is not null)return runtime.FullName;
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
        return LocalPaths.FindExecutable("codex");
    }
}
