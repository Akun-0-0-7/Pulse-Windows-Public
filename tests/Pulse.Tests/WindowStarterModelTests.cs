using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class WindowStarterModelTests
{
    private sealed class Fixture:IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"pulse-model-selection-"+Guid.NewGuid().ToString("N"));
        public Dictionary<string,string> Environment {get;}
        public string Calls=>Path.Combine(Root,"calls.txt");
        public Fixture(string config,string? models=null)
        {
            Directory.CreateDirectory(Root);
            Environment=new()
            {
                ["DOTNET_ROOT"]=Directory.GetParent(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!.Parent!.Parent!.FullName,
                ["SystemRoot"]=System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),
                ["TEMP"]=Path.GetTempPath(),["TMP"]=Path.GetTempPath(),["CODEX_HOME"]=Path.Combine(Root,"chosen-home"),
                ["PATH"]=Root,["PULSE_TEST_STARTER_EXPECTED_PATH"]=Root,["PULSE_TEST_STARTER_EXPECTED_HOME"]=Path.Combine(Root,"chosen-home"),
                ["PULSE_TEST_WINDOW_STARTER_FIXTURE"]="true",["PULSE_TEST_STARTER_CONFIG"]=config,["PULSE_TEST_STARTER_CALLS"]=Calls
            };
            if(models is not null)Environment["PULSE_TEST_STARTER_MODELS"]=models;
        }
        public Task<ProcessResult> Start(CancellationToken ct=default)=>WindowStarter.StartAsync("codex",Root,ct,Path.Combine(AppContext.BaseDirectory,"Pulse.Tests.exe"),Environment);
        public string[] Methods()=>File.Exists(Calls)?File.ReadAllLines(Calls):[];
        public void Dispose()=>Directory.Delete(Root,true);
    }

    [Check] public static async Task ConfiguredModelSurvivesIsolationAndStaleBundledCatalog()
    {
        using var fixture=new Fixture("""{"config":{"model":"fixture-new-model","model_provider":null,"model_reasoning_effort":"ultra","unrelated":"SECRET-fixture"}}""",
            """{"data":[{"model":"fixture-obsolete","isDefault":true,"hidden":false}],"nextCursor":null}""");
        var result=await fixture.Start();Assert.Equal(0,result.ExitCode);
        Assert.Equal("fixture-new-model",result.Model);Assert.Equal("codexConfig",result.ModelSource);
        using var doc=JsonDocument.Parse(result.Output);
        var args=doc.RootElement.GetProperty("args").EnumerateArray().Select(x=>x.GetString()).ToArray();
        Assert.True(args.Contains("--model"),"Configured model was discarded before the send");
        Assert.Equal("fixture-new-model",args[Array.IndexOf(args,"--model")+1]);
        Assert.True(args.Contains("--ignore-user-config"));
        Assert.True(!args.Any(x=>x?.Contains("model_reasoning_effort")==true),"Starter forced a possibly unsupported reasoning level");
        Assert.True(!result.Output.Contains("SECRET-fixture"));
        Assert.Equal("config/read,exec",string.Join(',',fixture.Methods()));
        Assert.Equal(Path.Combine(fixture.Root,"window-starter"),doc.RootElement.GetProperty("cwd").GetString());
    }

    [Check] public static async Task UnsetModelUsesVisibleCatalogDefaultBeforeOneSend()
    {
        using var fixture=new Fixture("""{"config":{"model":null,"model_provider":"openai"}}""",
            """{"data":[{"model":"fixture-hidden","hidden":true,"isDefault":true},{"model":"fixture-other","isDefault":false},{"model":"fixture-current","hidden":false,"isDefault":true}],"nextCursor":null}""");
        var result=await fixture.Start();Assert.Equal(0,result.ExitCode);
        using var doc=JsonDocument.Parse(result.Output);
        var args=doc.RootElement.GetProperty("args").EnumerateArray().Select(x=>x.GetString()).ToArray();
        Assert.True(args.Contains("--model"));Assert.Equal("fixture-current",args[Array.IndexOf(args,"--model")+1]);
        Assert.Equal("config/read,model/list,exec",string.Join(',',fixture.Methods()));
    }

    [Check] public static async Task UnreadableOrUnsafeModelSettingsNeverStartATurn()
    {
        foreach(var config in new[]{"RPC_ERROR","{}","[]","""{"config":{"model":42}}""",
            """{"config":{"model":"--other-option"}}""","""{"config":{"model":"a\nb"}}""",
            """{"config":{"model":"fixture-custom","model_provider":"third-party"}}""",
            """{"config":{"model":"fixture-custom","model_provider":false}}"""})
        {
            using var fixture=new Fixture(config);var result=await fixture.Start();
            Assert.True(result.ExitCode!=0,"Invalid selection still sent hi");
            Assert.Equal("modelSelection",WindowStarter.Evaluate("codex",result).FailureKind);
            Assert.True(!fixture.Methods().Contains("exec"));Assert.True(!result.Error.Contains("SECRET-fixture"));
        }
    }

    [Check] public static async Task MissingCatalogDefaultDoesNotGuessOrSend()
    {
        foreach(var models in new[]{"{}","[]","""{"data":[]}""","""{"data":[{"model":"fixture-other","isDefault":false}]}""",
            """{"data":[{"model":"--bad","isDefault":true}]}"""})
        {
            using var fixture=new Fixture("""{"config":{"model":null}}""",models);
            var result=await fixture.Start();Assert.True(result.ExitCode!=0);
            Assert.Equal("modelSelection",WindowStarter.Evaluate("codex",result).FailureKind);
            Assert.Equal("config/read,model/list",string.Join(',',fixture.Methods()));
        }
    }

    [Check] public static async Task CancelledSelectionNeverStartsATurn()
    {
        using var fixture=new Fixture("""{"config":{"model":"fixture-selected"}}""");
        using var ct=new CancellationTokenSource();ct.Cancel();
        try{await fixture.Start(ct.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        Assert.True(!fixture.Methods().Contains("exec"));
    }

    [Check] public static async Task CatalogPaginationFindsDefaultAndStopsRepeatedCursors()
    {
        const string first="""{"data":[{"model":"fixture-other","isDefault":false}],"nextCursor":"page-two"}""";
        using(var fixture=new Fixture("""{"config":{}}""",first))
        {
            fixture.Environment["PULSE_TEST_STARTER_MODELS_NEXT"]="""{"data":[{"model":"fixture-second-page","isDefault":true}],"nextCursor":null}""";
            var result=await fixture.Start();Assert.Equal(0,result.ExitCode);Assert.Equal("fixture-second-page",result.Model);
            Assert.Equal("codexDefault",result.ModelSource);
            Assert.Equal("config/read,model/list,model/list,exec",string.Join(',',fixture.Methods()));
        }
        using(var fixture=new Fixture("""{"config":{}}""",first))
        {
            var result=await fixture.Start();Assert.Equal("modelSelection",WindowStarter.Evaluate("codex",result).FailureKind);
            Assert.Equal("config/read,model/list,model/list",string.Join(',',fixture.Methods()));
        }
    }
}
