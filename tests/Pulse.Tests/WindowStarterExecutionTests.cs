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
            (new ProcessResult(1,"","request timed out SECRET-fixture",false),"timeout"),
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

    [Check] public static void CodexStructuredRejectionsOverrideUnrelatedStderrWarnings()
    {
        foreach(var (error,kind) in new[]{
            ("{\"code\":\"usage_limit_reached\",\"message\":\"You've hit your usage limit. Try again later.\"}","quota"),
            ("{\"status\":429,\"message\":\"Rate limit reached\"}","quota"),
            ("{\"message\":\"DNS lookup failed for api.example.test\"}","network"),
            ("{\"message\":\"Connection refused (os error 10061)\"}","network"),
            ("{\"code\":\"invalid_api_key\",\"message\":\"Authentication failed\"}","auth"),
            ("{\"code\":\"model_not_found\",\"message\":\"The configured model is not supported\"}","model")})
        {
            var output="{\"type\":\"turn.started\"}\n{\"type\":\"turn.failed\",\"error\":"+error+"}";
            var receipt=WindowStarter.Evaluate("codex",new ProcessResult(1,output,"MCP authentication warning: connection closed SECRET-fixture",false));
            Assert.Equal(kind,receipt.FailureKind);Assert.True(!receipt.Completed);
            Assert.True(!receipt.ToString().Contains("SECRET-fixture",StringComparison.Ordinal));
        }
    }

    [Check] public static void CodexJsonEncodedErrorMessageRecognizesQuotaRejection()
    {
        var nested=JsonSerializer.Serialize("{\"error\":{\"type\":\"usage_limit_reached\",\"message\":\"You've hit your usage limit\"}}");
        var output="{\"type\":\"error\",\"message\":"+nested+"}\n{\"type\":\"turn.failed\",\"error\":{\"message\":"+nested+"}}";
        Assert.Equal("quota",WindowStarter.Evaluate("codex",new ProcessResult(1,output,"",false)).FailureKind);
    }

    [Check] public static void CodexCompletionSurvivesUnrelatedStderrWarnings()
    {
        var receipt=WindowStarter.Evaluate("codex",new ProcessResult(0,Completed,"MCP authentication required; network connection closed",false));
        Assert.True(receipt.Completed);Assert.Equal("success",receipt.Status);
        Assert.Equal(13,receipt.OutputTokens);
    }

    [Check] public static void InterruptedOrPartiallyCompletedTurnNeverBecomesRetryableNetworkFailure()
    {
        foreach(var (output,error) in new[]{
            ("{\"type\":\"turn.started\"}","stream disconnected before completion: connection reset"),
            ("{\"type\":\"turn.started\"}\n{\"type\":\"turn.failed\",\"error\":{\"message\":\"stream disconnected before completion\"}}",""),
            ("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"hi\"}}\n{\"type\":\"turn.failed\",\"error\":{\"message\":\"DNS lookup failed\"}}",""),
            (Completed+"\n{\"type\":\"turn.failed\",\"error\":{\"message\":\"usage_limit_reached\"}}",""),
            (Completed,"connection refused during cleanup")})
        {
            var receipt=WindowStarter.Evaluate("codex",new ProcessResult(1,output,error,false));
            Assert.True(!receipt.Completed);Assert.Equal("turnIncomplete",receipt.FailureKind);
        }
    }

    [Check] public static void PlainExplicitConnectFailureIsRetryableButTimeoutIsUncertain()
    {
        foreach(var (error,kind) in new[]{
            ("failed to lookup address information: DNS error","network"),
            ("unable to connect: connection refused","network"),
            ("network is unreachable","network"),
            ("request timed out","timeout"),
            ("error sending request for url: connection reset","turnIncomplete"),
            ("model_not_found: model is not supported","model"),
            ("429 Too Many Requests: quota exceeded","quota")})
            Assert.Equal(kind,WindowStarter.Evaluate("codex",new ProcessResult(1,"",error,false)).FailureKind);
    }

    [Check] public static void IncidentalErrorIdsDoNotCreateQuotaRecoveryRetries()
    {
        foreach(var error in new[]{"Unknown error request id 54291", "Failed operation 429-example-id"})
            Assert.Equal("failed",WindowStarter.Evaluate("codex",new ProcessResult(1,"",error,false)).FailureKind);
        Assert.Equal("quota",WindowStarter.Evaluate("codex",new ProcessResult(1,"{\"type\":\"turn.failed\",\"error\":{\"status\":429,\"message\":\"Please try later\"}}","",false)).FailureKind);
    }

    [Check] public static void ClaudeFailedProcessWithReplyCannotAutomaticallyReplay()
    {
        var receipt=WindowStarter.Evaluate("claudeCode",new ProcessResult(1,"hi","connection refused during cleanup",false));
        Assert.Equal("turnIncomplete",receipt.FailureKind);Assert.True(!receipt.Completed);
    }

    [Check] public static void EarlierUncertaintyOrPermanentErrorCannotBecomeRetryableAfterLaterErrors()
    {
        foreach(var (message,kind) in new[]{
            ("stream disconnected before completion","turnIncomplete"),
            ("request timed out","timeout"),
            ("unknown internal failure","failed"),
            ("Authentication failed: invalid_api_key","auth"),
            ("model_not_found","model")})
        foreach(var later in new[]{"DNS lookup failed", "usage_limit_reached"})
        foreach(var eventType in new[]{"error","turn.failed"})
        {
            var output="{\"type\":\"turn.started\"}\n"+
                JsonSerializer.Serialize(new{type="error",message})+"\n"+
                JsonSerializer.Serialize(new{type=eventType,error=new{message=later}});
            var receipt=WindowStarter.Evaluate("codex",new ProcessResult(1,output,"",false));
            Assert.True(!receipt.Completed);Assert.Equal(kind,receipt.FailureKind);
        }
        var combined="{\"type\":\"turn.failed\",\"error\":{\"message\":\"stream disconnected before completion; retry failed: DNS lookup failed\"}}";
        Assert.Equal("turnIncomplete",WindowStarter.Evaluate("codex",new ProcessResult(1,combined,"",false)).FailureKind);
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
