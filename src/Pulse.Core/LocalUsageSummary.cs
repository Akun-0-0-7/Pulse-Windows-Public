namespace Pulse.Core;
public sealed record LocalUsageSummary(long? Today,long? SevenDays,long? Month,List<MetricPoint> Days,string? TopModel,double? CacheHitRate,decimal? Cost,int Records)
{
    public static string? Source(string provider)=>provider switch{"claudeCode"=>"claude","kimi" or "kimiCode"=>"kimi","antigravity"=>"antigravity-ide","commandCode"=>"commandcode","copilot"=>"copilot","codex" or "grok" or "cursor"=>provider,"devin"=>"devin-cli",_=>null};
    public static LocalUsageSummary? Build(SpendReport report,string provider,DateTimeOffset now)
    {
        var source=Source(provider);if(source is null)return null;
        var midnight=new DateTimeOffset(now.LocalDateTime.Date,TimeZoneInfo.Local.GetUtcOffset(now.LocalDateTime.Date));
        var records=report.Records.Where(r=>r.Agent==source&&r.At>=midnight.AddDays(-30)&&r.At<=now).ToList();if(records.Count==0)return null;
        long? Total(DateTimeOffset start)=>records.Any(r=>r.ReportsTokens&&r.At>=start)?records.Where(r=>r.At>=start).Sum(r=>r.TotalTokens??0):null;
        var days=records.Where(r=>r.ReportsTokens).GroupBy(r=>r.At.LocalDateTime.Date).OrderBy(g=>g.Key).Select(g=>new MetricPoint(new DateTimeOffset(g.Key,TimeZoneInfo.Local.GetUtcOffset(g.Key)),g.Sum(r=>r.TotalTokens??0))).ToList();
        var known=records.Where(r=>r.ReportsTokens).ToList();var cacheInput=known.Sum(r=>r.Input+r.CacheRead+r.CacheWrite);var cache=known.Count>0&&known.All(r=>!r.IsPartial&&r.Unclassified==0)&&cacheInput>0?(double?)known.Sum(r=>r.CacheRead)/cacheInput:null;
        var costs=records.Where(r=>r.EstimatedCost is not null).ToList();return new(Total(midnight),Total(midnight.AddDays(-6)),Total(midnight.AddDays(-30)),days,known.GroupBy(r=>r.Model).OrderByDescending(g=>g.Sum(r=>r.TotalTokens??0)).FirstOrDefault()?.Key,cache,costs.Count>0?costs.Sum(r=>r.EstimatedCost):null,records.Count);
    }
}
