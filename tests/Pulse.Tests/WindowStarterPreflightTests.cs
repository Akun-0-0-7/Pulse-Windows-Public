using Pulse.Core;
namespace Pulse.Tests;
public static class WindowStarterPreflightTests
{
    [Check] public static async Task CancelledInspectionNeverStartsTheTool()
    {
        using var fixture=new WindowStarterModelTests.Fixture("CONFIGURED");using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{await WindowStarterPreflight.InspectAsync(fixture.Root,Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe"),null,fixture.Environment,cancellation.Token);throw new Exception("Expected cancellation");}
        catch(OperationCanceledException){}
        Assert.Equal("",string.Join(',',fixture.Methods()));
    }
    [Check] public static async Task InspectionShowsSelectedModelAndVersionWithoutSending()
    {
        using var fixture=new WindowStarterModelTests.Fixture("""{"config":{"model":"gpt-6-astra"}}""");
        var result=await WindowStarterPreflight.InspectAsync(fixture.Root,Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe"),"gpt-6.1-terra",fixture.Environment);
        Assert.Equal("gpt-6.1-terra",result.Model);Assert.Equal("codex-cli 0.165.0-test",result.Version);Assert.Equal(null,result.ErrorCode);
        Assert.Equal("version,config/read",string.Join(',',fixture.Methods()));
    }
    [Check] public static async Task InspectionFailureDoesNotExposeRawErrorOrSend()
    {
        using var fixture=new WindowStarterModelTests.Fixture("RPC_ERROR");
        var result=await WindowStarterPreflight.InspectAsync(fixture.Root,Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe"),null,fixture.Environment);
        Assert.Equal("modelSelection",result.ErrorCode);Assert.True(!result.ToString().Contains("SECRET-fixture"));Assert.True(!fixture.Methods().Contains("exec"));
    }
}
