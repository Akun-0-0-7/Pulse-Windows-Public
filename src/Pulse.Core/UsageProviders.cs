namespace Pulse.Core;
public static class UsageProviders
{
    public static IUsageProvider? Create(string id)=>id=="extension"?new ExtensionReader():HandwrittenProviders.TryCreate(id)??ProfiledProviders.TryCreate(id);
    private sealed class ExtensionReader : IUsageProvider
    {
        public async Task<UsageReading> FetchAsync(FetchContext context)
        {
            var id=context.Account.Extra.GetValueOrDefault("extensionId");var service=new ExtensionService();var extension=service.Discover(Path.Combine(context.DataDirectory,"extensions")).FirstOrDefault(e=>e.Id==id);
            if(extension is null)return UsageReading.Unavailable(context.Account,"missing","扩展未找到，请重新扫描");
            var config=new AppConfig {EnabledExtensions=[extension.Id]};return await service.RunAsync(extension,config,context.CancellationToken);
        }
    }
}
