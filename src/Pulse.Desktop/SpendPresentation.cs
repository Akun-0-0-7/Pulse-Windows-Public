namespace Pulse.Desktop;

public static class SpendPresentation
{
    public static decimal? Rate(AppConfig config, AppStorage storage)
    {
        if (config.Currency == "USD") return 1m;
        if (config.ManualExchangeRate is > 0m) return config.ManualExchangeRate;
        var cache = storage.Load<PricingCatalog.ExchangeCache>("exchange-rates.json");
        return cache?.Rates.TryGetValue(config.Currency, out var rate) == true && rate > 0m ? rate : null;
    }

    public static string Money(decimal? usd, AppConfig config, decimal? rate)
    {
        if (usd is null) return "—";
        if (config.Currency == "USD") return $"{usd.Value:0.##} USD";
        return rate is > 0m ? $"{usd.Value * rate.Value:0.##} {config.Currency}" : $"{usd.Value:0.##} USD（未取得 {config.Currency} 汇率）";
    }

    public static decimal SubscriptionUsd(AppConfig config, (DateTimeOffset Start, DateTimeOffset End) span)
    {
        if (config.MonthlySubscriptionUsd is not > 0m) return 0m;
        var day = span.Start.Date;
        var end = span.End.Date;
        decimal months = 0;
        while (day < end)
        {
            var first = new DateTime(day.Year, day.Month, 1);
            var next = first.AddMonths(1);
            var until = next < end ? next : end;
            months += (decimal)(until - day).TotalDays / DateTime.DaysInMonth(day.Year, day.Month);
            day = until;
        }
        return config.MonthlySubscriptionUsd.Value * months;
    }
}
