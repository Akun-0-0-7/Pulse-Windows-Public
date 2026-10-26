namespace Pulse.Core;
// The cache contains normalized counters and metadata only; transcripts and credentials never enter it.
internal sealed record SpendCachedFile(string Agent,string Path,long Length,long LastWriteUtcTicks,List<SpendRecord> Records,List<string> Notes);
internal sealed record SpendCacheState(int SchemaVersion,Dictionary<string,SpendCachedFile> Files);
internal sealed class SpendReadCache
{
    const string Name="spend-file-cache-v1.json";const int Version=1;
    readonly AppStorage storage;readonly Dictionary<string,SpendCachedFile> entries;
    internal SpendReadCache(AppStorage store)
    {
        storage=store;SpendCacheState? state=null;try{state=store.Load<SpendCacheState>(Name);}catch(UnauthorizedAccessException){}
        entries=state?.SchemaVersion==Version&&state.Files is {} files?new(files,StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);
    }
    internal List<SpendRecord> Read(SpendSource source,IEnumerable<string> roots,List<string> notes,CancellationToken ct)
    {
        var records=new List<SpendRecord>();var live=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in SpendReaders.Files(roots,notes))
        {
            ct.ThrowIfCancellationRequested();var path=System.IO.Path.GetFullPath(file);var key=source.Id+":"+path;live.Add(key);
            try
            {
                var before=new FileInfo(path);if(!before.Exists)continue;
                if(entries.TryGetValue(key,out var cached)&&cached is not null&&cached.Records is not null&&cached.Notes is not null&&cached.Agent==source.Id&&string.Equals(cached.Path,path,StringComparison.OrdinalIgnoreCase)&&cached.Length==before.Length&&cached.LastWriteUtcTicks==before.LastWriteTimeUtc.Ticks)
                {records.AddRange(cached.Records);notes.AddRange(cached.Notes);continue;}
                var length=before.Length;var modified=before.LastWriteTimeUtc.Ticks;var fileNotes=new List<string>();var found=SpendReaders.Read(source,[path],fileNotes,ct);ct.ThrowIfCancellationRequested();records.AddRange(found);notes.AddRange(fileNotes);
                // A concurrently growing log is read again on the next scan, even if this pass has complete earlier rows.
                var after=new FileInfo(path);if(after.Exists&&after.Length==length&&after.LastWriteTimeUtc.Ticks==modified&&!fileNotes.Any(n=>n.Contains("读取失败",StringComparison.Ordinal)))entries[key]=new(source.Id,path,length,modified,found.Select(r=>r with{EstimatedCost=null}).ToList(),fileNotes);else entries.Remove(key);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){entries.Remove(key);notes.Add(source.Id+"：增量缓存读取失败（"+e.GetType().Name+"）："+System.IO.Path.GetFileName(path));}
        }
        foreach(var key in entries.Where(p=>p.Value is null||p.Value.Agent==source.Id&&!live.Contains(p.Key)).Select(p=>p.Key).ToList())entries.Remove(key);
        return records;
    }
    internal void Save(List<string> notes)
    {
        try{storage.Save(Name,new SpendCacheState(Version,entries));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){notes.Add("消耗增量缓存未保存（"+e.GetType().Name+"）。");}
    }
}
