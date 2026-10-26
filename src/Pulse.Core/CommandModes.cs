using System.Text;
using System.Text.Json;
namespace Pulse.Core;
public static class CommandModes
{
    public static async Task<int> RunAsync(string[] args,string dataRoot)
    {
        var store=new AppStorage(dataRoot);var config=store.LoadConfig();
        if(args.Contains("--json")){Console.WriteLine(UsageExport.ToJson(config,store.LoadCache(),ProviderCatalog.All));return 0;}
        if(args.Contains("--recap")){
            if(!config.SpendEnabled){Console.WriteLine("{\"status\":\"disabled\",\"message\":\"Token spend is disabled\"}");return 0;}
            var index=Array.IndexOf(args,"--period");var period=index>=0&&index+1<args.Length?args[index+1]:"month";var span=ReportPeriods.Range(period,DateTimeOffset.Now);var prices=store.Load<PricingCatalog.PriceCache>("model-prices.json")?.Prices;var report=await new SpendService(prices).ScanAsync(config,LocalPaths.Home);report=report.InRange(span.Start,span.End);Console.WriteLine(JsonSerializer.Serialize(new{period,start=span.Start,end=span.End,report},StorageJson.Options));return 0;
        }
        if(args.Contains("--statusline")){
            if(Console.IsInputRedirected) StatuslineInstaller.Capture(await Console.In.ReadToEndAsync(),config,store);
            var cache=store.LoadCache();Console.WriteLine(string.Join("  ",config.Accounts.Where(a=>a.Enabled).Select(a=>{var window=UsageEngine.Headline(a,cache.FirstOrDefault(r=>r.AccountId==a.Id));return window is null?null:$"{ProviderCatalog.Find(a.ProviderId)?.Name??a.ProviderId} {window.UsedPercent:0}%";}).Where(s=>s is not null)));return 0;
        }
        if(args.Contains("--install-statusline")||args.Contains("--uninstall-statusline"))return InstallStatusline(args.Contains("--install-statusline"),store);
        Console.WriteLine("Pulse Windows: --json | --recap --period day|week|month|year | --statusline | --install-statusline | --uninstall-statusline");return args.Contains("--help")?0:1;
    }
    private static int InstallStatusline(bool install,AppStorage store)
    {
        StatuslineInstaller.Apply(install,LocalPaths.Home,Path.Combine(AppContext.BaseDirectory,"Pulse-cli.exe"),store);Console.WriteLine(install?"Pulse status line installed.":"Previous status line restored.");return 0;
    }
}
public static class ReportPeriods
{
    public static (DateTimeOffset Start,DateTimeOffset End) Range(string period,DateTimeOffset now)
    {
        var today=new DateTimeOffset(now.Date,now.Offset);var start=period switch{"day"=>today,"week"=>today.AddDays(-((int)today.DayOfWeek+6)%7),"year"=>new DateTimeOffset(now.Year,1,1,0,0,0,now.Offset),_=>new DateTimeOffset(now.Year,now.Month,1,0,0,0,now.Offset)};var end=period switch{"day"=>start.AddDays(1),"week"=>start.AddDays(7),"year"=>start.AddYears(1),_=>start.AddMonths(1)};return(start,end);
    }
}
