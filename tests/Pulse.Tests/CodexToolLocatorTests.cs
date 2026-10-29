using Pulse.Core;

namespace Pulse.Tests;

public static class CodexToolLocatorTests
{
    [Check] public static void RemovedRuntimeHintDiscoversNewestInstalledRuntimeWithoutPinning()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-runtime-order-"+Guid.NewGuid().ToString("N"));
        var old=Path.Combine(root,"OpenAI","Codex","bin","old","codex.exe");
        var current=Path.Combine(root,"OpenAI","Codex","bin","current","codex.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        try
        {
            File.WriteAllText(old,"");File.WriteAllText(current,"");
            File.SetLastWriteTimeUtc(old,new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(current,new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc));
            var env=new Dictionary<string,string>{{"LOCALAPPDATA",root},{"CODEX_APP_RUNTIME_CLI_PATH",Path.Combine(root,"removed","codex.exe")}};
            Assert.Equal(current,CodexToolLocator.Find(environment:env));
            Assert.Equal(old,CodexToolLocator.Find(old,env)); // An intentional valid manual path remains authoritative.
            Assert.Equal(current,CodexToolLocator.Find(Path.Combine(root,"removed-manual.exe"),env));
            File.Delete(current);
            Assert.Equal(old,CodexToolLocator.Find(environment:env)); // No path is cached across upgrades/removals.
        }
        finally{Directory.Delete(root,true);}
    }

    [Check] public static async Task ActiveDesktopRuntimeWinsOverOlderPathEntryForStarter()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-runtime-discovery-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous=Environment.GetEnvironmentVariable("PATH");
        try
        {
            // An invalid earlier PATH file catches selecting the obsolete entry, without
            // starting a real Codex process. The active runtime is the synthetic tool.
            File.WriteAllText(Path.Combine(root,"codex.exe"),"obsolete-fixture");
            Environment.SetEnvironmentVariable("PATH",root);
            var runtime=Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe");
            var env=new Dictionary<string,string>
            {
                ["DOTNET_ROOT"]=Directory.GetParent(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!.Parent!.Parent!.FullName,
                ["SystemRoot"]=Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                ["TEMP"]=Path.GetTempPath(),["TMP"]=Path.GetTempPath(),
                ["CODEX_APP_RUNTIME_CLI_PATH"]=runtime,["PULSE_TEST_WINDOW_STARTER_FIXTURE"]="true"
            };
            var result=await WindowStarter.StartAsync("codex",root,environment:env);
            Assert.Equal(0,result.ExitCode);Assert.Equal("fixture-configured",result.Model);
        }
        finally{Environment.SetEnvironmentVariable("PATH",previous);Directory.Delete(root,true);}
    }
}
