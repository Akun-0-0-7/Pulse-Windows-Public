using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
namespace Pulse.Core;
public sealed class UsageEngine : IDisposable
{
    private readonly AppStorage storage; private readonly ISecretStore secrets; private readonly Func<string,IUsageProvider?> factory;
    private readonly ConcurrentDictionary<string,UsageReading> readings=new(); private readonly SemaphoreSlim refreshLock=new(1,1); private long revision;
    private readonly BurnForecast forecast=new();private readonly ConcurrentDictionary<string,TimeSpan> forecasts=new();
    private readonly CancellationTokenSource lifetime=new();
    public CancellationToken LifetimeToken=>lifetime.Token;
    public AppConfig Config { get; }
    public IReadOnlyList<UsageReading> Readings=>readings.Values.ToList();
    public event Action? Changed;
    public event Action<UsageReading>? ReadingUpdated;
    public bool IsRefreshing { get; private set; }
    public UsageEngine(AppStorage storage,ISecretStore secrets,Func<string,IUsageProvider?> factory)
    {
        this.storage=storage; this.secrets=secrets; this.factory=factory; Config=storage.LoadConfig();
        foreach(var reading in storage.LoadCache()) readings[reading.AccountId]=reading;
    }
    public void Save() { Interlocked.Increment(ref revision); storage.SaveConfig(Config); Changed?.Invoke(); }
    public UsageReading? Reading(string id)=>readings.GetValueOrDefault(id);
    public TimeSpan? Forecast(string id)=>forecasts.TryGetValue(id,out var value)?value:null;
    public static UsageWindow? Headline(AccountConfig account,UsageReading? reading)=>reading?.Windows.FirstOrDefault(w=>w.Id==account.PrimaryWindowId)??reading?.Windows.OrderByDescending(w=>w.UsedPercent).FirstOrDefault();
    public static HttpClient CreateHttp(AppConfig config)
    {
        var handler=new HttpClientHandler { UseProxy=config.ProxyMode!="none",AllowAutoRedirect=false };
        if(config.ProxyMode=="custom"&&Uri.TryCreate(config.ProxyAddress,UriKind.Absolute,out var proxy)) handler.Proxy=new WebProxy(proxy);
        return new HttpClient(handler) { Timeout=TimeSpan.FromSeconds(25) };
    }
    public async Task RefreshAsync(CancellationToken cancellationToken=default,bool waitForCurrent=false)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,lifetime.Token);cancellationToken=linked.Token;
        if(waitForCurrent)await refreshLock.WaitAsync(cancellationToken);
        else if(!await refreshLock.WaitAsync(0,cancellationToken)) return;
        var currentRevision=Volatile.Read(ref revision); IsRefreshing=true; Changed?.Invoke();
        try {
            var accounts=Config.Accounts.Where(a=>a.Enabled).Select(a=>JsonSerializer.Deserialize<AccountConfig>(JsonSerializer.Serialize(a,StorageJson.Options),StorageJson.Options)!).ToArray();
            using var http=CreateHttp(Config);
            await Parallel.ForEachAsync(accounts,new ParallelOptions {MaxDegreeOfParallelism=4,CancellationToken=cancellationToken},async(account,ct)=> {
                var provider=factory(account.ProviderId); if(provider is null) return;
                UsageReading result;
                try { using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(60));var credential=secrets.Get(account.Id); result=await provider.FetchAsync(new FetchContext(account,credential,http,LocalPaths.Home,storage.Root,deadline.Token,secrets,ProcessEnvironment:ProxyEnvironment.For(Config))); }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { return; }
                catch(Exception exception) when(exception is not OutOfMemoryException) { result=UsageReading.Unavailable(account,"unreachable",exception is TaskCanceledException?"读取超时，请检查网络和代理":"暂时无法读取，请查看连接设置"); }
                if(currentRevision!=Volatile.Read(ref revision)) return;
                result.AccountId=account.Id; result.ProviderId=account.ProviderId;
                if(!result.Success&&readings.TryGetValue(account.Id,out var previous)&&(previous.Windows.Count>0||previous.Balance is not null||previous.CreditBalance is not null)) {
                    result.Windows=previous.Windows; result.Balance=previous.Balance; result.Plan=previous.Plan; result.CreditBalance=previous.CreditBalance; result.Source=previous.Source; result.ObservedAt=previous.ObservedAt; result.IsStale=true;
                }
                if(result.Success) {ApplyBalance(account,result);forecasts.TryRemove(account.Id,out _);if(Config.ForecastEnabled&&Headline(account,result) is {} window&&result.ObservedAt is {} at&&forecast.Observe(account.Id,window,at) is {} eta&&window.ResetsAt-DateTimeOffset.UtcNow>eta)forecasts[account.Id]=eta;}
                readings[account.Id]=result; ReadingUpdated?.Invoke(result); Changed?.Invoke();
            });
            storage.SaveCache(readings.Values.ToList());
        } catch(OperationCanceledException)when(lifetime.IsCancellationRequested){ }finally { IsRefreshing=false; refreshLock.Release(); Changed?.Invoke(); }
    }
    public void Dispose()=>lifetime.Cancel();
    private readonly object balanceLock=new();
    private void ApplyBalance(AccountConfig account,UsageReading reading)
    {
        if(reading.Balance is not { } balance||reading.Windows.Count>0) return;
        lock(balanceLock) {
            var peaks=storage.Load<Dictionary<string,decimal>>("balance-peaks.json")??[]; var key=account.Id+":"+balance.Currency;
            var peak=Math.Max(peaks.GetValueOrDefault(key),balance.Amount); peaks[key]=peak; storage.Save("balance-peaks.json",peaks);
            var basis=account.BalanceMode=="budget"?account.Budget:account.BalanceMode=="balanceOnly"?null:peak;
            if(basis is >0) reading.Windows.Add(new() {Id="balance",Kind="balance",Label=account.BalanceMode=="budget"?"余额 · 你的预算":"余额 · 已观察充值额度",UsedPercent=(double)Math.Max(0,(basis.Value-balance.Amount)/basis.Value*100),Estimated=true,EstimatedFrom=account.BalanceMode=="budget"?"yourBudget":"sinceTopUp",ReportsLength=false});
        }
    }
}
public static class UsageExport
{
    public static string ToJson(AppConfig config,IEnumerable<UsageReading> readings,IReadOnlyList<ProviderDefinition> providers)
    {
        var cache=readings.ToDictionary(r=>r.AccountId); var now=DateTimeOffset.UtcNow;
        return JsonSerializer.Serialize(new { generatedAt=now,accounts=config.Accounts.Where(a=>a.Enabled).Select(account=> {
            var definition=providers.FirstOrDefault(p=>p.Id==account.ProviderId); var reading=cache.GetValueOrDefault(account.Id); var headline=UsageEngine.Headline(account,reading);
            return new { id=account.Id,provider=account.ProviderId,name=definition?.Name??account.ProviderId,label=string.IsNullOrWhiteSpace(account.Label)?definition?.Name??account.ProviderId:account.Label,plan=reading?.Plan,creditBalance=reading?.CreditBalance,balance=reading?.Balance,observedAt=reading?.ObservedAt,ageSeconds=reading?.ObservedAt is {} at?(double?)Math.Max(0,(now-at).TotalSeconds):null,source=UsageFigures.Source(reading?.Source),settingsURL="pulse://account/"+Uri.EscapeDataString(account.Id),headline=headline is null?null:new {windowId=headline.Id,usedPercent=UsageFigures.Percent(headline.UsedPercent),exhausted=headline.Exhausted,resetsAt=headline.ResetsAt},windows=reading?.Windows.Select(w=>new {w.Id,w.Kind,w.Scope,w.Label,usedPercent=UsageFigures.Percent(w.UsedPercent),usedFraction=w.UsedPercent/100,w.Exhausted,w.WindowSeconds,w.ReportsLength,w.Estimated,w.EstimatedFrom,w.ResetsAt,w.ExpiresAt}),metrics=reading?.Metrics,error=reading?.ErrorMessage,stale=reading?.IsStale??false };
        }) },StorageJson.Options);
    }
}
public sealed class AlertRules
{
    public HashSet<string> Seen { get; set; } = [];
    private readonly Dictionary<string,int> failures=[];
    public List<string> Evaluate(AppConfig config,UsageReading reading)
    {
        var alerts=new List<string>(); if(!config.AlertsEnabled) return alerts;
        void Once(string key,string message) { if(Seen.Add(reading.AccountId+":"+key)) alerts.Add(message); }
        if(reading.ErrorCode is not null) { var count=failures.GetValueOrDefault(reading.AccountId)+1; failures[reading.AccountId]=count; if(config.FailureAlerts&&count>=3&&(reading.ObservedAt is null||DateTimeOffset.UtcNow-reading.ObservedAt>TimeSpan.FromMinutes(6))) Once("fail","连续读取失败，正在显示上次读数"); return alerts; }
        if(reading.IsStale)return alerts;
        failures[reading.AccountId]=0; Seen.Remove(reading.AccountId+":fail");
        foreach(var window in reading.Windows) {
            if(!double.IsFinite(window.UsedPercent)||window.UsedPercent<0)continue;
            var statePrefix=reading.AccountId+":state:"+Uri.EscapeDataString(window.Id)+":";
            var previous=Seen.FirstOrDefault(key=>key.StartsWith(statePrefix,StringComparison.Ordinal));
            var parts=previous?[statePrefix.Length..].Split('|');
            var remembered=parts is {Length:>=4}&&double.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var storedPercent)&&double.IsFinite(storedPercent);
            var reminded=remembered&&parts![3]=="1";
            var previousPercent=remembered?double.Parse(parts![0],System.Globalization.CultureInfo.InvariantCulture):0;
            var previousReset=remembered&&long.TryParse(parts![1],out var ticks)&&ticks>0&&ticks<=DateTimeOffset.MaxValue.UtcTicks?new DateTimeOffset(ticks,TimeSpan.Zero):(DateTimeOffset?)null;
            var at=reading.ObservedAt??DateTimeOffset.UtcNow;
            var isNewer=!remembered||!long.TryParse(parts![2],out var previousAt)||at.UtcTicks>previousAt;
            var turnedOver=isNewer&&remembered&&HasTurnedOver(window,previousPercent,previousReset);
            if(turnedOver)
            {
                if(config.ResetAlerts&&reminded)alerts.Add($"{window.Label??window.Kind}：已提醒的额度已重置");
                Seen.RemoveWhere(key=>key.StartsWith(reading.AccountId+":"+window.Id+":",StringComparison.Ordinal));reminded=false;
            }
            var scopeReset=remembered&&!turnedOver?(parts!.Length>=5?parts[4]:previousReset?.ToUnixTimeSeconds().ToString()??"none"):window.ResetsAt?.ToUnixTimeSeconds().ToString()??"none";
            var scope=window.Id+":"+scopeReset;
            foreach(var threshold in config.AlertThresholds.Where(t=>t is >=0 and <=100)) if(window.UsedPercent>=threshold) {Once(scope+":"+threshold,$"{window.Label??window.Kind} 已用 {window.UsedPercent:0.#}%");reminded=true;}
            if(window.Exhausted) {Once(scope+":spent",$"{window.Label??window.Kind}：服务商报告额度已用尽");reminded=true;}
            if(isNewer){Seen.RemoveWhere(key=>key.StartsWith(statePrefix,StringComparison.Ordinal));Seen.Add(statePrefix+window.UsedPercent.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+(window.ResetsAt?.UtcTicks??0)+"|"+at.UtcTicks+"|"+(reminded?"1":"0")+"|"+scopeReset);}
        }
        if(reading.Balance is {} balance&&config.LowBalanceThreshold is {} thresholdBalance){if(balance.Amount<thresholdBalance)Once("low:"+balance.Currency,$"余额为 {balance.Amount:0.##} {balance.Currency}");else Seen.Remove(reading.AccountId+":low:"+balance.Currency);}
        return alerts;
    }
    public static bool HasTurnedOver(UsageWindow window,double previousPercent,DateTimeOffset? previousReset)
    {
        if(window.Kind is "balance" or "topUp"||window.Id=="balance")return false;
        var moved=window.ResetsAt is {} reset&&previousReset is {} old&&reset-old>TimeSpan.FromMinutes(1);
        return window.Kind is "credits" or "sharedCredits" or "planCredits" or "dedicatedCredits"?moved:moved||previousPercent-window.UsedPercent>=40;
    }
}
