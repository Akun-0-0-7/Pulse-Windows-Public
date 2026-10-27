using System.Text.Json;
using Pulse.Core;
namespace Pulse.Tests;

public static class CodexSpendHomeTests
{
    static AppConfig Config(string? savedHome=null,params string[] sources)=>JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(new
    {
        SpendEnabled=true,
        EnabledSpendSources=sources.Length==0?["codex"]:sources,
        CodexSpendHome=savedHome
    }),StorageJson.Options)!;

    static Dictionary<string,string> EnvironmentFor(string root)=>new(StringComparer.OrdinalIgnoreCase){{"CODEX_HOME",root}};

    [Check]public static async Task EnvironmentHomeReplacesDefaultWithoutMixingRecords()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"old",999,0);fixture.Codex(fixture.Custom,"new");
        var report=await new SpendService().ScanAsync(Config(),fixture.Home,EnvironmentFor(fixture.Custom));
        Assert.Equal(120L,report.TotalTokens);Assert.Equal(1,report.Records.Count);Assert.Equal("new",report.Records[0].Session);
    }

    [Check]public static async Task SavedHomeOverridesEnvironmentAndDefault()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"old",999,0);fixture.Codex(fixture.Custom,"environment",444,0);
        var saved=Path.Combine(fixture.Home,"saved");fixture.Codex(saved,"saved");
        var report=await new SpendService().ScanAsync(Config(saved),fixture.Home,EnvironmentFor(fixture.Custom));
        Assert.Equal(120L,report.TotalTokens);Assert.Equal(1,report.Records.Count);Assert.Equal("saved",report.Records[0].Session);
    }

    [Check]public static async Task CustomHomeIncludesArchivesAndDeduplicatesCopiedSessions()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"old",999,0);fixture.Codex(fixture.Custom,"copied");
        fixture.Codex(fixture.Custom,"copied",archived:true);fixture.Codex(fixture.Custom,"archive-only",40,10,archived:true);
        var report=await new SpendService().ScanAsync(Config(),fixture.Home,EnvironmentFor(fixture.Custom));
        Assert.Equal(170L,report.TotalTokens);Assert.Equal(2,report.Records.Count);Assert.True(report.Records.Any(r=>r.Session=="archive-only"));
    }

    [Check]public static async Task BlankSavedHomeUsesEnvironmentHome()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"old",999,0);fixture.Codex(fixture.Custom,"new");
        foreach(var saved in new string?[]{null,"","  "})
        {
            var report=await new SpendService().ScanAsync(Config(saved),fixture.Home,EnvironmentFor(fixture.Custom));
            Assert.Equal(120L,report.TotalTokens);
        }
    }

    [Check]public static async Task EmptyOrMissingEnvironmentKeepsAlternateHomeIsolated()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"default");
        foreach(var environment in new[]{new Dictionary<string,string>(),EnvironmentFor(" ")})
        {
            var report=await new SpendService().ScanAsync(Config(" "),fixture.Home,environment);
            Assert.Equal(120L,report.TotalTokens);Assert.Equal("default",report.Records.Single().Session);
        }
        var isolated=await new SpendService().ScanAsync(Config(),fixture.Home);
        Assert.Equal(120L,isolated.TotalTokens);Assert.Equal("default",isolated.Records.Single().Session);
    }

    [Check]public static async Task SavedHomeSupportsTildeRelativeAndSuppliedEnvironmentExpansion()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"old",999,0);fixture.Codex(fixture.Custom,"new");
        var environment=EnvironmentFor(Path.Combine(fixture.Home,"unused"));environment["FIXTURE_HOME"]=fixture.Home;
        foreach(var saved in new[]{"~/custom","custom",Path.Combine("%FIXTURE_HOME%","custom")})
        {
            var report=await new SpendService().ScanAsync(Config(saved),fixture.Home,environment);
            Assert.Equal(120L,report.TotalTokens);Assert.Equal("new",report.Records.Single().Session);
        }
    }

    [Check]public static async Task PreviouslyCachedEmptyDefaultDoesNotHideChangedHome()
    {
        using var fixture=new Fixture();var empty=Path.Combine(fixture.Default,"sessions","empty.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(empty)!);
        File.WriteAllText(empty,"{\"type\":\"session_meta\",\"payload\":{\"id\":\"empty\"}}");
        var storage=new AppStorage(Path.Combine(fixture.Home,"cache"));var service=new SpendService(cacheStore:storage);
        Assert.Equal(0,(await service.ScanAsync(Config(),fixture.Home,new Dictionary<string,string>())).Records.Count);
        fixture.Codex(fixture.Custom,"new");
        var report=await service.ScanAsync(Config(fixture.Custom),fixture.Home,new Dictionary<string,string>());
        Assert.Equal(120L,report.TotalTokens);Assert.Equal("new",report.Records.Single().Session);
    }

    [Check]public static async Task InvalidCodexHomeFallsBackWithoutStoppingOtherSources()
    {
        using var fixture=new Fixture();fixture.Codex(fixture.Default,"default",999,0);fixture.Claude();
        foreach(var saved in new string?[]{null,"bad\0path"})
        {
            var report=await new SpendService().ScanAsync(Config(saved,"codex","claude"),fixture.Home,EnvironmentFor("bad\0path"));
            Assert.Equal(1119L,report.TotalTokens);Assert.Equal(999L,report.ByAgent["codex"].TotalTokens);Assert.Equal(120L,report.ByAgent["claude"].TotalTokens);
        }
    }

    [Check]public static async Task CodexHomeDoesNotChangeOtherSourcePaths()
    {
        using var fixture=new Fixture();fixture.Claude();fixture.Codex(fixture.Custom,"codex",999,0);
        var report=await new SpendService().ScanAsync(Config(fixture.Custom,"claude"),fixture.Home,EnvironmentFor(fixture.Custom));
        Assert.Equal(120L,report.TotalTokens);Assert.Equal("claude",report.Records.Single().Agent);
    }

    sealed class Fixture:IDisposable
    {
        public string Home {get;}=Path.Combine(Path.GetTempPath(),"pulse-codex-spend-home-"+Guid.NewGuid().ToString("N"));
        public string Default=>Path.Combine(Home,".codex");
        public string Custom=>Path.Combine(Home,"custom");
        public Fixture()=>Directory.CreateDirectory(Home);
        public void Codex(string root,string session,long input=100,long output=20,bool archived=false)
        {
            var path=Path.Combine(root,archived?"archived_sessions":"sessions",session+".jsonl");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path,[JsonSerializer.Serialize(new{type="session_meta",payload=new{id=session}}),
                "{\"type\":\"turn_context\",\"payload\":{\"model\":\"fixture-model\"}}",
                JsonSerializer.Serialize(new{type="event_msg",timestamp="2026-10-27T00:00:00Z",payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=input,output_tokens=output,cached_input_tokens=0}}}})]);
        }
        public void Claude()
        {
            var path=Path.Combine(Home,".claude","projects","fixture.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,"{\"type\":\"assistant\",\"timestamp\":\"2026-10-27T00:00:00Z\",\"message\":{\"id\":\"claude-fixture\",\"model\":\"fixture-model\",\"usage\":{\"input_tokens\":100,\"output_tokens\":20}}}");
        }
        public void Dispose()
        {
            var target=Path.GetFullPath(Home);var tempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("pulse-codex-spend-home-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture directory.");
            Directory.Delete(target,true);
        }
    }
}
