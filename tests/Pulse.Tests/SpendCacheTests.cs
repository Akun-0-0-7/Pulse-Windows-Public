using Pulse.Core;
namespace Pulse.Tests;
public static class SpendCacheTests
{
    [Check]public static async Task UnchangedClaudeAndCodexFilesReusePersistentUnpricedRecords()
    {
        var home=Path.Combine(Path.GetTempPath(),"pulse-spend-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);try
        {
            foreach(var agent in new[]{"claude","codex"})
            {
                var file=Path.Combine(home,agent=="claude"?".claude/projects/a.jsonl":".codex/sessions/a.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var data=agent=="claude"?"""{"type":"assistant","timestamp":"2026-10-20T01:00:00Z","sessionId":"s","message":{"id":"m","model":"x","usage":{"input_tokens":100,"output_tokens":20}}}""":"""
{"type":"session_meta","payload":{"id":"s"}}
{"type":"turn_context","payload":{"model":"x"}}
{"type":"event_msg","timestamp":"2026-10-20T01:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":20}}}}
""";
                File.WriteAllText(file,data);var config=new AppConfig{SpendEnabled=true,EnabledSpendSources=[agent]};var store=new AppStorage(Path.Combine(home,"cache-"+agent));
                var first=await new SpendService(new Dictionary<string,SpendPrice>{{"x",new(1m,2m)}},store).ScanAsync(config,home);Assert.Equal(120L,first.TotalTokens);
                using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    var second=await new SpendService(new Dictionary<string,SpendPrice>{{"x",new(2m,4m)}},store).ScanAsync(config,home);Assert.Equal(120L,second.TotalTokens);Assert.Equal(first.EstimatedCost*2,second.EstimatedCost);
                }
                File.WriteAllText(file,data.Replace("100","150"));var changed=await new SpendService(cacheStore:store).ScanAsync(config,home);Assert.Equal(170L,changed.TotalTokens);
                File.Delete(file);Assert.Equal(0,(await new SpendService(cacheStore:store).ScanAsync(config,home)).Records.Count);
            }
        }finally{Directory.Delete(home,true);}
    }
}
