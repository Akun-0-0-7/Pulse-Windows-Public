using System.Text.Json;
using System.Text.Json.Nodes;
namespace Pulse.Core;
public static class StatuslineInstaller
{
    public static void Capture(string input,AppConfig config,AppStorage store)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(input)>262144||!config.Accounts.Any(a=>a.Enabled&&a.ProviderId=="claudeCode"))return;
        try {using var doc=JsonDocument.Parse(input);var limits=JsonTools.At(doc.RootElement,"rate_limits");if(limits.ValueKind!=JsonValueKind.Object)return;new AppStorage(Path.Combine(store.Root,"statusline")).Save("claudeCode.json",new{capturedAt=DateTimeOffset.UtcNow,rate_limits=limits.Clone()});}catch(JsonException){}
    }
    public static void Apply(bool install,string home,string executable,AppStorage store)
    {
        var path=Path.Combine(home,".claude","settings.json");var json=JsonNode.Parse(File.Exists(path)?File.ReadAllText(path):"{}")!.AsObject();var current=json["statusLine"];var ours=current?["command"]?.ToString().Contains("Pulse-cli.exe",StringComparison.OrdinalIgnoreCase)==true&&current?["command"]?.ToString().Contains("--statusline",StringComparison.Ordinal)==true;
        if(install){if(!ours)store.Save("statusline-backup.json",new{present=current is not null,value=current?.DeepClone()});json["statusLine"]=new JsonObject{{"type","command"},{"command","\""+executable+"\" --statusline"}};}
        else {if(!ours)return;var backup=store.Load<JsonElement>("statusline-backup.json");if(JsonTools.Bool(backup,"present")==true)json["statusLine"]=JsonNode.Parse(JsonTools.At(backup,"value").GetRawText());else json.Remove("statusLine");}
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+".pulse.tmp";File.WriteAllText(temporary,json.ToJsonString(StorageJson.Options));File.Move(temporary,path,true);
    }
}
