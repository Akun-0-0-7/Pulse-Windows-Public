namespace Pulse.Core;
public sealed record SpendPrice(decimal Input,decimal Output,decimal? CacheRead=null,decimal? CacheWrite=null,IReadOnlyList<SpendPriceTier>? Tiers=null,string? Name=null);
public sealed record SpendPriceTier(long Threshold,decimal Input,decimal Output,decimal? CacheRead=null,decimal? CacheWrite=null);
public sealed record SpendSource(string Id,string Name,string UpstreamEvidence,bool ReportsTokens,string[] Paths);
public sealed record SpendRecord(string Agent,string Identity,DateTimeOffset At,string Model,long Input=0,long Output=0,long CacheRead=0,long CacheWrite=0,long CacheWrite1h=0,long Unclassified=0,bool ReportsTokens=true,bool IsPartial=false,bool IsAggregate=false,decimal? EstimatedCost=null,decimal? ReportedCost=null,string? Session=null,string? Origin=null,string? Project=null,long? RequestContext=null) { public long? TotalTokens=>ReportsTokens?Input+Output+CacheRead+CacheWrite+Unclassified:null; }
public sealed record SpendAggregate(long? TotalTokens,decimal? EstimatedCost,decimal? ReportedCost,int Records);
public sealed class SpendReport
{
 public List<SpendRecord> Records {get;init;}=[]; public List<string> Notes {get;init;}=[]; public long? TotalTokens=>Records.Any(x=>x.ReportsTokens)?Records.Sum(x=>x.TotalTokens??0):null;
 public decimal? EstimatedCost=>SumMoney(Records.Select(x=>x.EstimatedCost)); public decimal? ReportedCost=>SumMoney(Records.Select(x=>x.ReportedCost));
 public Dictionary<string,SpendAggregate> ByDay=>Group(x=>x.At.ToLocalTime().ToString("yyyy-MM-dd")); public Dictionary<string,SpendAggregate> ByModel=>Group(x=>x.Model); public Dictionary<string,SpendAggregate> ByAgent=>Group(x=>x.Agent);
 public long UnpricedTokens=>Records.Where(x=>x.EstimatedCost is null).Sum(x=>x.TotalTokens??0);
 public SpendReport InRange(DateTimeOffset start,DateTimeOffset end)=>new(){Records=Records.Where(x=>x.At>=start&&x.At<end).ToList(),Notes=[..Notes]};
 Dictionary<string,SpendAggregate> Group(Func<SpendRecord,string> key)=>Records.GroupBy(key).ToDictionary(g=>g.Key,g=>new SpendAggregate(g.Any(x=>x.ReportsTokens)?g.Sum(x=>x.TotalTokens??0):null,SumMoney(g.Select(x=>x.EstimatedCost)),SumMoney(g.Select(x=>x.ReportedCost)),g.Count()));
 static decimal? SumMoney(IEnumerable<decimal?> values) { var list=values.Where(x=>x.HasValue).ToList(); return list.Count>0?list.Sum(x=>x!.Value):null; }
}
public sealed class SpendService(IReadOnlyDictionary<string,SpendPrice>? prices=null,AppStorage? cacheStore=null)
{
 public Task<SpendReport> ScanAsync(AppConfig config,string home,CancellationToken cancellationToken=default)
 {
     // Alternate homes are isolated unless the caller supplies their path environment explicitly.
     IReadOnlyDictionary<string,string> environment=Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(LocalPaths.Home).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)
         ?Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(e=>(string)e.Key,e=>(string)e.Value!,StringComparer.OrdinalIgnoreCase):new Dictionary<string,string>();
     return ScanAsync(config,home,environment,cancellationToken);
 }
 public Task<SpendReport> ScanAsync(AppConfig config,string home,IReadOnlyDictionary<string,string> environment,CancellationToken cancellationToken=default)
 {
     var snapshot=new Dictionary<string,string>(environment,StringComparer.OrdinalIgnoreCase);
     return Task.Run(()=>Scan(config,home,snapshot,cancellationToken),cancellationToken);
 }
 SpendReport Scan(AppConfig config,string home,IReadOnlyDictionary<string,string> environment,CancellationToken ct)
 {
     if(!config.SpendEnabled) return new();
     var records=new List<SpendRecord>(); var notes=new List<string>();var cache=cacheStore is null?null:new SpendReadCache(cacheStore);
     foreach(var source in SpendSources.All)
     {
         if(config.EnabledSpendSources is {} selected&&!selected.Contains(source.Id,StringComparer.Ordinal))continue;
         ct.ThrowIfCancellationRequested(); var paths=SpendSources.ResolvePaths(source,home,environment).ToList();
         // Explicit imports live under a source-id subdirectory to retain a declared format.
         foreach(var path in config.SpendPaths) paths.Add(Path.Combine(LocalPaths.Expand(path,home),source.Id));
         var found=cache is not null&&source.Id is "claude" or "codex"?cache.Read(source,paths,notes,ct):SpendReaders.Read(source,paths,notes,ct);
         // Native MiniMax logs win over imported captures when measured records exist.
         if(source.Id=="mcode"&&found.Any(x=>x.Origin=="native")) found=found.Where(x=>x.Origin=="native").ToList();
         records.AddRange(found);
     }
     var devinCliSessions=records.Where(r=>r.Agent=="devin-cli"&&r.Session is not null).Select(r=>r.Session!).ToHashSet(StringComparer.Ordinal);
     records=records.Where(r=>r.Agent!="devin-desktop"||r.Session is null||!devinCliSessions.Contains(r.Session)).GroupBy(x=>(x.Agent,x.Identity)).Select(g=>Merge(g)).OrderBy(x=>x.At).ToList();
     records=records.Select(Price).ToList();ct.ThrowIfCancellationRequested();cache?.Save(notes);
     return new(){Records=records,Notes=notes.Distinct().ToList()};
 }
 SpendRecord Price(SpendRecord record)
 {
     if(!record.ReportsTokens||record.Unclassified>0||record.Model=="unknown"||record.Agent=="lmstudio"||prices is null||PricingCatalog.Resolve(record.Model,prices,record.Agent switch{"opencode"=>"opencode-go","kilo" or "kilocode"=>"kilo","cline"=>"cline-pass",_=>null}) is not {} price) return record;
     if(!record.IsAggregate&&record.RequestContext is {} context&&price.Tiers?.Where(t=>context>t.Threshold).OrderByDescending(t=>t.Threshold).FirstOrDefault() is {} tier)price=new(tier.Input,tier.Output,tier.CacheRead,tier.CacheWrite);
     var hour=Math.Min(record.CacheWrite,record.CacheWrite1h);
     var cost=(record.Input*price.Input+record.Output*price.Output+record.CacheRead*(price.CacheRead??price.Input)+(record.CacheWrite-hour)*(price.CacheWrite??price.Input)+hour*price.Input*2)/1_000_000m;
     return record with{EstimatedCost=cost};
 }
 static SpendRecord Merge(IEnumerable<SpendRecord> values)
 {
     var rows=values.ToList(); var first=rows[0];
     return first with{Input=rows.Max(x=>x.Input),Output=rows.Max(x=>x.Output),CacheRead=rows.Max(x=>x.CacheRead),CacheWrite=rows.Max(x=>x.CacheWrite),CacheWrite1h=rows.Max(x=>x.CacheWrite1h),Unclassified=rows.Max(x=>x.Unclassified),IsPartial=rows.Any(x=>x.IsPartial),RequestContext=rows.Max(x=>x.RequestContext)};
 }
}
