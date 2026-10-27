using System.Text.Json;
namespace Pulse.Desktop;
public sealed partial class PulseApp
{
    private readonly SemaphoreSlim spendGate=new(1,1);
    public async Task<SpendReport?> ScanSpendAsync(CancellationToken cancellationToken=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,Engine.LifetimeToken);var token=linked.Token;
        await spendGate.WaitAsync(token);
        try {
            if(!Engine.Config.SpendEnabled)return null;
            var config=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(Engine.Config,StorageJson.Options),StorageJson.Options)!;
            var sourceSettings=JsonSerializer.Serialize(new{config.EnabledSpendSources,config.SpendPaths,config.CodexSpendHome});SpendReport report;
            if(config.EnabledSpendSources is {Count:0})report=new();
            else {using var http=UsageEngine.CreateHttp(config);var prices=await PricingCatalog.LoadAsync(Storage,http,token);await PricingCatalog.ExchangeAsync(config,Storage,http,token);report=await new SpendService(prices,Storage).ScanAsync(config,LocalPaths.Home,token);}
            token.ThrowIfCancellationRequested();
            if(!Engine.Config.SpendEnabled||sourceSettings!=JsonSerializer.Serialize(new{Engine.Config.EnabledSpendSources,Engine.Config.SpendPaths,Engine.Config.CodexSpendHome}))return null;
            Storage.Save("spend-cache.json",report);settings?.ApplySpendReport(report);Rail?.Render();return report;
        }finally{spendGate.Release();}
    }
}
