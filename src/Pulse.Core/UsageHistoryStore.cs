namespace Pulse.Core;
/// <summary>Local quota percentages only; no credential, conversation or project text is stored.</summary>
public sealed class UsageHistoryStore
{
    public const int MaximumPointsPerWindow=720, MaximumWindows=256;
    public const int RetentionDays=90;
    readonly AppStorage storage;
    readonly object sync=new();
    List<UsageHistorySeries> series;
    public UsageHistoryStore(AppStorage storage)
    {
        this.storage=storage;series=storage.Load<List<UsageHistorySeries>>("usage-history.json")??[];
        Prune(DateTimeOffset.UtcNow);
    }
    public bool Observe(UsageReading reading)
    {
        if(!reading.Success||reading.IsStale||reading.ObservedAt is not {} at||string.IsNullOrEmpty(reading.AccountId))return false;
        lock(sync)
        {
            bool changed=false;
            foreach(var window in reading.Windows.Where(w=>double.IsFinite(w.UsedPercent)&&w.UsedPercent>=0).GroupBy(w=>w.Id).Select(g=>g.First()))
            {
                var item=series.FirstOrDefault(s=>s.AccountId==reading.AccountId&&s.WindowId==window.Id);
                if(item is null){item=new(){AccountId=reading.AccountId,WindowId=window.Id};series.Add(item);}
                if(item.Points.Any(p=>p.T==at))continue;
                item.Points.Add(new(at,window.UsedPercent));changed=true;
            }
            if(changed){Prune(DateTimeOffset.UtcNow);storage.Save("usage-history.json",series);}
            return changed;
        }
    }
    public IReadOnlyList<MetricPoint> Read(string accountId,string windowId)
    {lock(sync)return series.FirstOrDefault(s=>s.AccountId==accountId&&s.WindowId==windowId)?.Points.ToArray()??[];}
    public void Remove(string accountId)
    {lock(sync)if(series.RemoveAll(s=>s.AccountId==accountId)>0)storage.Save("usage-history.json",series);}
    void Prune(DateTimeOffset now)
    {
        var cutoff=now.AddDays(-RetentionDays);
        foreach(var item in series)item.Points=item.Points.Where(p=>p.T>=cutoff&&p.T<=now.AddMinutes(5)&&double.IsFinite(p.V)&&p.V>=0).GroupBy(p=>p.T).Select(g=>g.First()).OrderBy(p=>p.T).TakeLast(MaximumPointsPerWindow).ToList();
        series=series.Where(s=>s.Points.Count>0).GroupBy(s=>(s.AccountId,s.WindowId)).Select(g=>g.First()).OrderByDescending(s=>s.Points[^1].T).Take(MaximumWindows).ToList();
    }
}
public sealed class UsageHistorySeries
{
    public string AccountId {get;set;}="";
    public string WindowId {get;set;}="";
    public List<MetricPoint> Points {get;set;}=[];
}
