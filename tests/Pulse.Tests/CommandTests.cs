using Pulse.Core;
using System.Text.Json;
namespace Pulse.Tests;
public static class CommandTests
{
    [Check] public static async Task NpmShimPreservesArgumentsWithoutShellEvaluation()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-shim-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"node_modules"));File.WriteAllText(Path.Combine(root,"node_modules","fixture.js"),"process.stdout.write(JSON.stringify(process.argv.slice(2)))");File.WriteAllText(Path.Combine(root,"fixture.cmd"),"@\"%dp0%\\node_modules\\fixture.js\" %*");
        try{var result=await ProcessRunner.RunAsync(Path.Combine(root,"fixture.cmd"),["a&b","value with spaces","$literal"]);Assert.Equal(0,result.ExitCode);Assert.Equal("[\"a&b\",\"value with spaces\",\"$literal\"]",result.Output);}
        finally{Directory.Delete(root,true);}
    }
    [Check] public static void ReinstallingStatuslineStillRestoresOriginal()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-command-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"home",".claude"));var path=Path.Combine(root,"home",".claude","settings.json");File.WriteAllText(path,"{\"statusLine\":{\"type\":\"command\",\"command\":\"original-helper\"},\"theme\":\"dark\"}");
        try{var store=new AppStorage(Path.Combine(root,"data"));StatuslineInstaller.Apply(true,Path.Combine(root,"home"),"C:\\Pulse\\Pulse-cli.exe",store);StatuslineInstaller.Apply(true,Path.Combine(root,"home"),"C:\\Pulse\\Pulse-cli.exe",store);StatuslineInstaller.Apply(false,Path.Combine(root,"home"),"C:\\Pulse\\Pulse-cli.exe",store);using var doc=JsonDocument.Parse(File.ReadAllText(path));Assert.Equal("original-helper",JsonTools.String(doc.RootElement,"statusLine.command"));Assert.Equal("dark",JsonTools.String(doc.RootElement,"theme"));}
        finally{Directory.Delete(root,true);}
    }
    [Check] public static void StatuslineCaptureFeedsTheActualClaudeReader()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-command-"+Guid.NewGuid().ToString("N"));
        try{var config=new AppConfig{Accounts=[new(){Id="claude",ProviderId="claudeCode",Enabled=true}]};var store=new AppStorage(root);StatuslineInstaller.Capture("{\"rate_limits\":{\"five_hour\":{\"used_percentage\":12,\"resets_at\":\"2099-01-01T00:00:00Z\"}}}",config,store);var text=File.ReadAllText(Path.Combine(root,"statusline","claudeCode.json"));var reading=HandwrittenProviders.Parse("claudeCode",text);Assert.True(reading.Success);Assert.Near(12,reading.Windows[0].UsedPercent);}
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
