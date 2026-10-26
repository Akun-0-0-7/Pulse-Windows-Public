using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;
public static class PricingCatalog
{
    public static async Task<Dictionary<string,SpendPrice>> LoadAsync(AppStorage store,HttpClient client,CancellationToken cancellationToken=default)
    {
        var cached=store.Load<PriceCache>("model-prices.json"); if(cached is not null&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromDays(1))return cached.Prices;
        try {
            using var response=await client.GetAsync("https://models.dev/api.json",cancellationToken);response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(cancellationToken);if(json.Length>20_000_000)throw new IOException("价格数据过大");using var doc=JsonDocument.Parse(json);var prices=new Dictionary<string,SpendPrice>();
            var firstParty=new[]{"anthropic","openai","xai","moonshotai","zhipuai","minimax","deepseek","google","xiaomi","alibaba","mistral","meta"};
            var vendors=new[]{"opencode-go","kilo","cline-pass"};
            foreach(var provider in doc.RootElement.EnumerateObject().OrderBy(p=>Array.IndexOf(firstParty,p.Name)<0?int.MaxValue:Array.IndexOf(firstParty,p.Name)))
            {
                var models=JsonTools.At(provider.Value,"models");if(models.ValueKind!=JsonValueKind.Object)continue;
                foreach(var model in models.EnumerateObject())
                {
                    var cost=JsonTools.At(model.Value,"cost");if(JsonTools.Decimal(cost,"input") is not {} input||JsonTools.Decimal(cost,"output") is not {} output||input<0||output<0)continue;
                    var price=new SpendPrice(input,output,JsonTools.Decimal(cost,"cache_read"),JsonTools.Decimal(cost,"cache_write"),Tiers(cost),JsonTools.String(model.Value,"name"));
                    prices[provider.Name+"/"+model.Name]=price;
                    if(firstParty.Contains(provider.Name))prices.TryAdd(model.Name,price);
                    if(vendors.Contains(provider.Name))prices[provider.Name+"|"+model.Name]=price;
                }
            }
            store.Save("model-prices.json",new PriceCache(DateTimeOffset.UtcNow,prices));return prices;
        }catch(Exception exception) when(exception is System.Net.Http.HttpRequestException or TaskCanceledException or JsonException or IOException){cancellationToken.ThrowIfCancellationRequested();return cached?.Prices??[];}
    }
    static IReadOnlyList<SpendPriceTier>? Tiers(JsonElement cost)
    {
        var tiers=new List<SpendPriceTier>();
        void Add(JsonElement rates,long threshold){if(JsonTools.Decimal(rates,"input") is {} input&&input>=0&&JsonTools.Decimal(rates,"output") is {} output&&output>=0)tiers.Add(new(threshold,input,output,JsonTools.Decimal(rates,"cache_read"),JsonTools.Decimal(rates,"cache_write")));}
        foreach(var tier in JsonTools.Array(cost,"tiers"))if(JsonTools.String(tier,"tier.type")=="context"&&JsonTools.Number(tier,"tier.size") is >1 and <long.MaxValue&&JsonTools.Number(tier,"tier.size") is {} size){var threshold=(long)size;if((threshold-1)%1000==0)threshold--;Add(tier,threshold);}
        if(tiers.Count==0)Add(JsonTools.At(cost,"context_over_200k"),200000);
        return tiers.Count>0?tiers.GroupBy(t=>t.Threshold).Select(g=>g.First()).OrderBy(t=>t.Threshold).ToArray():null;
    }
    public static SpendPrice? Resolve(string model,IReadOnlyDictionary<string,SpendPrice> prices,string? vendor=null)
    {
        SpendPrice? Lookup(string id)=>prices.TryGetValue(id,out var exact)?exact:prices.FirstOrDefault(p=>p.Key.Equals(id,StringComparison.OrdinalIgnoreCase)).Value;
        var aliases=Aliases(model).Distinct().ToArray();
        foreach(var candidate in new[]{model}.Concat(aliases))if(Lookup(candidate) is {} price)return price;
        if(vendor is not null)foreach(var candidate in new[]{model}.Concat(aliases))if(Lookup(vendor+"|"+candidate) is {} price)return price;
        return null;
    }
    static IEnumerable<string> Aliases(string model)
    {
        static string Dotted(string text)=>Regex.Replace(text,@"(?<=\d)-(?=\d)",".");
        if(model.EndsWith("-build",StringComparison.Ordinal)&&model!="grok-build-0.1")yield return model[..^6];
        foreach(var effort in new[]{"-extra-low","-medium","-high","-low","-minimal"})if(model.EndsWith(effort,StringComparison.Ordinal)){var core=model[..^effort.Length];yield return core;yield return Dotted(core);break;}
        yield return Dotted(model);
        var context=Regex.Match(model,@"-[0-9]+[kKmM]$");if(context.Success){var core=model[..context.Index];yield return core;foreach(var alias in Aliases(core))yield return alias;}
        if(Regex.IsMatch(model,@"^k[0-9p]+$"))yield return "kimi-"+model.Replace('p','.');
    }
    public static async Task<decimal?> ExchangeAsync(AppConfig config,AppStorage store,HttpClient client,CancellationToken cancellationToken=default)
    {
        if(config.Currency=="USD")return 1;if(config.ManualExchangeRate is >0)return config.ManualExchangeRate;
        var cached=store.Load<ExchangeCache>("exchange-rates.json");if(cached is not null&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromHours(24))return cached.Rates.GetValueOrDefault(config.Currency) is >0?cached.Rates[config.Currency]:null;
        try{using var doc=JsonDocument.Parse(await client.GetStringAsync("https://open.er-api.com/v6/latest/USD",cancellationToken));var rates=new Dictionary<string,decimal>();foreach(var pair in doc.RootElement.GetProperty("rates").EnumerateObject())if(pair.Value.TryGetDecimal(out var rate)&&rate>0)rates[pair.Name]=rate;store.Save("exchange-rates.json",new ExchangeCache(DateTimeOffset.UtcNow,rates));return rates.TryGetValue(config.Currency,out var selected)?selected:null;}
        catch(Exception exception) when(exception is System.Net.Http.HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException){cancellationToken.ThrowIfCancellationRequested();return cached?.Rates.GetValueOrDefault(config.Currency) is >0?cached.Rates[config.Currency]:null;}
    }
    public sealed record PriceCache(DateTimeOffset At,Dictionary<string,SpendPrice> Prices);
    public sealed record ExchangeCache(DateTimeOffset At,Dictionary<string,decimal> Rates);
}
