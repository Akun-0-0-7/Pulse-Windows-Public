using System.Text.Json;
using System.Text.Json.Serialization;
namespace Pulse.Core;
public static class StorageJson
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, WriteIndented=true, DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull };
}
public sealed class AppStorage(string root)
{
    public string Root { get; } = root;
    public AppConfig LoadConfig() => Load<AppConfig>("settings.json") ?? new();
    public List<UsageReading> LoadCache() => Load<List<UsageReading>>("usage-cache.json") ?? [];
    public void SaveConfig(AppConfig config) => Save("settings.json",config);
    public void SaveCache(List<UsageReading> readings) => Save("usage-cache.json",readings);
    public T? Load<T>(string name)
    {
        var path=Path.Combine(Root,Path.GetFileName(name));
        try { return File.Exists(path)?JsonSerializer.Deserialize<T>(File.ReadAllText(path),StorageJson.Options):default; }
        catch(JsonException) { return default; } catch(IOException) { return default; }
    }
    public void Save<T>(string name,T value)
    {
        Directory.CreateDirectory(Root);
        var path=Path.Combine(Root,Path.GetFileName(name)); var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,JsonSerializer.Serialize(value,StorageJson.Options)); File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
