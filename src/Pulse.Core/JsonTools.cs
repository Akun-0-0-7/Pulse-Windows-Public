using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public static class JsonTools
{
    public static JsonElement At(JsonElement root,string path)
    {
        if(string.IsNullOrEmpty(path)) return root;
        foreach(var part in path.Split('.')) {
            if(root.ValueKind==JsonValueKind.Object && root.TryGetProperty(part,out var child)) root=child;
            else if(root.ValueKind==JsonValueKind.Array && int.TryParse(part,out var index) && index>=0 && index<root.GetArrayLength()) root=root[index];
            else return default;
        }
        return root;
    }
    public static string? String(JsonElement root,string path) { var item=At(root,path); return item.ValueKind==JsonValueKind.String?item.GetString():item.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False?item.ToString():null; }
    public static double? Number(JsonElement root,string path)
    {
        var item=At(root,path); double value;
        return (item.ValueKind==JsonValueKind.Number&&item.TryGetDouble(out value) || item.ValueKind==JsonValueKind.String&&double.TryParse(item.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out value)) && double.IsFinite(value)?value:null;
    }
    public static decimal? Decimal(JsonElement root,string path) { var text=String(root,path); return decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)?number:null; }
    public static bool? Bool(JsonElement root,string path) { var value=At(root,path); return value.ValueKind==JsonValueKind.True?true:value.ValueKind==JsonValueKind.False?false:bool.TryParse(String(root,path),out var result)?result:null; }
    public static DateTimeOffset? Date(JsonElement root,string path)
    {
        var text=String(root,path); if(text is null) return null;
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)) {
            try { return Math.Abs(number)>100_000_000_000?DateTimeOffset.FromUnixTimeMilliseconds((long)number):DateTimeOffset.FromUnixTimeSeconds((long)number); } catch(ArgumentOutOfRangeException) { return null; }
        }
        return DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date)?date:null;
    }
    public static IEnumerable<JsonElement> Array(JsonElement root,string path) { var value=At(root,path); return value.ValueKind==JsonValueKind.Array?value.EnumerateArray().ToArray():[]; }
}
public static partial class Redaction
{
    [GeneratedRegex("(?i)(token|secret|password|authorization|cookie|api.?key|email|account.?id|session|credential)")] private static partial Regex SecretKey();
    [GeneratedRegex(@"(?i)(bearer\s+)[a-z0-9._~+/=-]+|[\w.+-]+@[\w.-]+\.[a-z]{2,}|\b(?:sk-|eyJ)[a-zA-Z0-9._-]{12,}")] private static partial Regex SecretValue();
    public static string Clean(string text)
    {
        try {
            var node=JsonNode.Parse(text); Walk(node); text=node?.ToJsonString(StorageJson.Options)??"";
        } catch(JsonException) { }
        return SecretValue().Replace(text,"[hidden]");
    }
    private static void Walk(JsonNode? node)
    {
        if(node is JsonObject obj) foreach(var key in obj.Select(p=>p.Key).ToArray()) { if(SecretKey().IsMatch(key)) obj[key]="[hidden]"; else Walk(obj[key]); }
        else if(node is JsonArray array) foreach(var child in array) Walk(child);
    }
}
