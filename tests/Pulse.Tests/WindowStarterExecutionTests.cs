using Pulse.Core;
using System.Text.Json;

namespace Pulse.Tests;

public static class WindowStarterExecutionTests
{
    private const string Completed="{\"type\":\"thread.started\"}\n{\"type\":\"turn.started\"}\n{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":14420,\"output_tokens\":13}}";

    [Check] public static void CodexNeedsCompletedTurnWithMeasuredOutput()
    {
        var receipt=WindowStarter.Evaluate("codex",new ProcessResult(0,Completed,"",false));
        Assert.Equal("success",receipt.Status);Assert.True(receipt.Completed);
        Assert.Equal(14420,receipt.InputTokens);Assert.Equal(13,receipt.OutputTokens);
        Assert.Equal<string?>(null,receipt.FailureKind);
        foreach(var output in new[]{"", "garbled", "{\"type\":\"thread.started\"}\n{\"type\":\"turn.started\"}",
            "{\"type\":\"turn.completed\"}", "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"output_tokens\":0}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":-1,\"output_tokens\":1}}",
            Completed+"\n{\"type\":\"turn.failed\"}"})
        {
            var failure=WindowStarter.Evaluate("codex",new ProcessResult(0,output,"",false));
            Assert.Equal("failed",failure.Status);Assert.True(!failure.Completed);Assert.Equal<int?>(null,failure.InputTokens);Assert.Equal<int?>(null,failure.OutputTokens);
        }
    }

    [Check] public static void FailureKindsContainNoRawProcessOutput()
    {
        foreach(var (result,kind) in new[]{
            (new ProcessResult(0,Completed,"",true),"timeout"),
            (new ProcessResult(-1,"","toolMissing",false),"toolMissing"),
            (new ProcessResult(1,"","401 unauthorized SECRET-fixture",false),"auth"),
            (new ProcessResult(1,"","request timed out SECRET-fixture",false),"network"),
            (new ProcessResult(0,"{\"type\":\"turn.started\"}","",false),"turnIncomplete"),
            (new ProcessResult(1,"","unclassified SECRET-fixture",false),"failed")})
        {
            var receipt=WindowStarter.Evaluate("codex",result);
            Assert.Equal("failed",receipt.Status);Assert.True(!receipt.Completed);Assert.Equal(kind,receipt.FailureKind);
            Assert.True(!receipt.ToString().Contains("SECRET-fixture",StringComparison.Ordinal));
        }
        var claude=WindowStarter.Evaluate("claudeCode",new ProcessResult(0,"plain reply","",false));
        Assert.Equal("success",claude.Status);Assert.True(claude.Completed);
        Assert.Equal<int?>(null,claude.InputTokens);Assert.Equal<int?>(null,claude.OutputTokens);
    }

    [Check] public static async Task CodexExecutionUsesIsolatedJsonArguments()
    {
        var root=Path.Combine(Path.GetTempPath(),"pulse-window-starter-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var environment=new Dictionary<string,string>
            {
                ["DOTNET_ROOT"]=Directory.GetParent(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!.Parent!.Parent!.FullName,
                ["SystemRoot"]=Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                ["TEMP"]=Path.GetTempPath(),["TMP"]=Path.GetTempPath(),
                ["PULSE_TEST_WINDOW_STARTER_FIXTURE"]="true"
            };
            var result=await WindowStarter.StartAsync("codex",root,configuredExecutable:Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe"),environment:environment);
            Assert.Equal(0,result.ExitCode);
            using var document=JsonDocument.Parse(result.Output);
            Assert.Equal(Path.Combine(root,"window-starter"),document.RootElement.GetProperty("cwd").GetString());
            var arguments=document.RootElement.GetProperty("args").EnumerateArray().Select(item=>item.GetString()!).ToArray();
            foreach(var flag in new[]{"exec","--ignore-user-config","--json","--ephemeral","--skip-git-repo-check","read-only"})
                Assert.True(arguments.Contains(flag),"Missing isolated Codex argument: "+flag);
            Assert.Equal("hi",arguments[^1]);
            Assert.Equal(Path.Combine(root,"window-starter"),arguments[Array.IndexOf(arguments,"-C")+1]);
        }
        finally{Directory.Delete(root,true);}
    }
}
