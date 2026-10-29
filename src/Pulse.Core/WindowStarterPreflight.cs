namespace Pulse.Core;
public sealed record WindowStarterPreflightResult(string? ToolPath,string? Version,string? Model,string? Source,string? ErrorCode);
public static class WindowStarterPreflight
{
    public static async Task<WindowStarterPreflightResult> InspectAsync(string dataRoot,string? executable,string? model,Dictionary<string,string>? environment=null,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tool=CodexToolLocator.Find(executable,environment);
        if(tool is null)return new(null,null,null,null,"toolMissing");
        try
        {
            var folder=Path.Combine(dataRoot,"window-starter");Directory.CreateDirectory(folder);
            var versionResult=await ProcessRunner.RunAsync(tool,["--version"],TimeSpan.FromSeconds(5),cancellationToken,folder,environment,4096);
            var version=versionResult.ExitCode==0&&System.Text.RegularExpressions.Regex.IsMatch(versionResult.Output.Trim(),@"\Acodex(?:-cli)? [0-9A-Za-z.+-]{1,80}\z")?versionResult.Output.Trim():null;
            var selected=await CodexStarterModel.ResolveAsync(tool,folder,cancellationToken,environment,model);
            return new(tool,version,selected?.Model,selected?.Source,selected is null?"modelSelection":null);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {return new(tool,null,null,null,"toolUnavailable");}
    }
}
