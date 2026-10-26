// Cursor/Trae export scope and multiset replay rules translated from Pulse 1.12.2, Apache-2.0.
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
internal static class SpendExportReaders
{
    sealed record Lane(List<SpendRecord> Records,bool Overlapped);
    internal static List<SpendRecord> Read(string agent,IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var files=SpendReaders.Files(roots,notes).Where(f=>agent=="cursor"?Path.GetExtension(f).Equals(".json",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(f).Equals(".csv",StringComparison.OrdinalIgnoreCase):Path.GetExtension(f).Equals(".json",StringComparison.OrdinalIgnoreCase)).Where(f=>!Path.GetFileNameWithoutExtension(f).StartsWith("usage.backup",StringComparison.OrdinalIgnoreCase)).ToList();
        var output=new List<SpendRecord>();
        foreach(var scope in files.GroupBy(f=>agent=="cursor"?Scope(f):"import",StringComparer.Ordinal))
        {
            var seen=new HashSet<string>();var jsonFiles=new List<List<SpendRecord>>();var csvFiles=new List<List<SpendRecord>>();
            foreach(var file in scope.Order(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();try
                {
                    if(new FileInfo(file).Length>64*1024*1024)throw new IOException("Export exceeds 64 MiB.");
                    var text=File.ReadAllText(file);var csv=Path.GetExtension(file).Equals(".csv",StringComparison.OrdinalIgnoreCase);if(!seen.Add((csv?"csv:":"json:")+SpendReaders.Hash(text)))continue;
                    if(csv){csvFiles.Add(SpendImportReaders.CursorCsv(file).Select(r=>r with{Session=r.Session is null?null:"cursor:"+scope.Key+":cloud:"+r.Session,IsPartial=false}).ToList());continue;}
                    using var document=JsonDocument.Parse(text);var root=document.RootElement;
                    if(agent=="cursor"&&JsonTools.At(root,"usageEventsDisplay").ValueKind!=JsonValueKind.Array||agent=="trae"&&root.ValueKind!=JsonValueKind.Array)continue;
                    IEnumerable<JsonElement> rows=agent=="cursor"?JsonTools.Array(root,"usageEventsDisplay"):root.EnumerateArray();var records=new List<SpendRecord>();
                    foreach(var row in rows)
                    {
                        ct.ThrowIfCancellationRequested();var at=SpendReaders.D(row,agent=="cursor"?"timestamp":"usage_time");if(at is null)continue;
                        var model=agent=="cursor"?SpendReaders.S(row,"model"):TraeModel(SpendReaders.S(row,"model_name")??"trae-"+(SpendReaders.S(row,"mode")??"unknown"));if(model is null)continue;
                        var session=SpendReaders.S(row,agent=="cursor"?"conversationId":"session_id");if(agent=="trae"&&session is null)continue;if(agent=="cursor"&&session is not null)session="cursor:"+scope.Key+":"+session;
                        var u=JsonTools.At(row,agent=="cursor"?"tokenUsage":"extra_info");var input=SpendReaders.N(u,agent=="cursor"?"inputTokens":"input_token");var reply=SpendReaders.N(u,agent=="cursor"?"outputTokens":"output_token");var read=SpendReaders.N(u,agent=="cursor"?"cacheReadTokens":"cache_read_token");var write=SpendReaders.N(u,agent=="cursor"?"cacheWriteTokens":"cache_write_token");if(input+reply+read+write<=0)continue;
                        records.Add(new(agent,"",at.Value,model,input,reply,read,write,Session:session,Origin:"imported"));
                    }
                    jsonFiles.Add(records);
                }
                catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){notes.Add(agent+"：导出读取失败（"+e.GetType().Name+"）："+Path.GetFileName(file));}
            }
            var json=Reconcile(jsonFiles,scope.Key,"json");var csvLane=Reconcile(csvFiles,scope.Key,"csv");var chosen=new List<SpendRecord>(json.Records);bool partial=json.Overlapped||csvLane.Overlapped||agent=="cursor"&&scope.Key=="import";
            if(json.Records.Count>0)
            {
                var start=json.Records.Min(r=>r.At);var end=json.Records.Max(r=>r.At);
                foreach(var row in csvLane.Records)if(row.At<start||row.At>end)chosen.Add(row);else partial=true;
            }
            else chosen.AddRange(csvLane.Records);
            output.AddRange(partial?chosen.Select(r=>r with{IsPartial=true}):chosen);
        }
        return output;
    }
    static string Signature(SpendRecord r)=>SpendReaders.Hash($"{r.Session}:{r.At.ToUnixTimeMilliseconds()}:{r.Model}:{r.Input}:{r.CacheWrite}:{r.CacheRead}:{r.Output}");
    static Lane Reconcile(List<List<SpendRecord>> files,string scope,string lane)
    {
        var maximum=new Dictionary<string,int>();var total=new Dictionary<string,int>();var representative=new Dictionary<string,SpendRecord>();
        foreach(var file in files)foreach(var group in file.GroupBy(Signature)){var count=group.Count();maximum[group.Key]=Math.Max(maximum.GetValueOrDefault(group.Key),count);total[group.Key]=total.GetValueOrDefault(group.Key)+count;representative.TryAdd(group.Key,group.First());}
        var output=new List<SpendRecord>();foreach(var (signature,row) in representative)for(var index=0;index<maximum[signature];index++)output.Add(row with{Identity=lane+":"+scope+":"+signature+":"+index});
        return new(output,maximum.Any(p=>total[p.Key]>p.Value));
    }
    static string Scope(string file)
    {
        var parts=Path.GetFileNameWithoutExtension(file).Split('.');if(!parts[0].Equals("usage",StringComparison.OrdinalIgnoreCase))return "import";
        var account=parts.Length>1?Regex.Replace(parts[1],"[^a-zA-Z0-9]+","-").Trim('-').ToLowerInvariant():"active";return "account:"+(account.Length>0?account:"active");
    }
    static string TraeModel(string name)
    {
        var lower=name.Trim().ToLowerInvariant();if(lower.StartsWith("claude sonnet 4.5",StringComparison.Ordinal)||lower.StartsWith("claude sonnet 4.6",StringComparison.Ordinal))lower=lower.Replace("4.5","4-5").Replace("4.6","4-6");
        else if(!lower.StartsWith("gpt-5",StringComparison.Ordinal)&&!lower.StartsWith("gemini 3.1",StringComparison.Ordinal)&&!lower.StartsWith("gemini-3.1",StringComparison.Ordinal)&&lower is not ("glm 5.1" or "glm-5.1"))return name.Trim();
        return Regex.Replace(lower,"[ _]+","-");
    }
}
