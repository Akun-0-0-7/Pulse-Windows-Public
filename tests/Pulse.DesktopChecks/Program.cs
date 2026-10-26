using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Pulse.Core;
using Pulse.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"pulse-empty-report-checks");
        Directory.CreateDirectory(output);
        FullscreenChecks.Run();
        Ui.ApplyTheme("dark");
        var config=new AppConfig{Currency="USD",MonthlySubscriptionUsd=20m};
        var empty=new SpendReport();
        var coarse=new SpendReport{Records=[new("warp","coarse",DateTimeOffset.Now,"unknown",ReportsTokens:false,IsAggregate:true,ReportedCost:1m,Project:"demo")]};
        if(ReportWindow.Available(coarse,4,config))throw new Exception("Aggregate-only records exposed an hourly report.");
        if(Enumerable.Range(0,8).Any(i=>ReportWindow.Available(empty,i,config)))throw new Exception("Empty report exposed a selectable template.");
        for(var template=0;template<8;template++)
        {
            var card=ReportWindow.RenderCard(empty,"month",template,config,new DateTimeOffset(2026,9,14,0,0,0,TimeSpan.FromHours(8)));
            if(card.PixelWidth!=1080||card.PixelHeight!=1920)throw new Exception($"Invalid card size: {template}");
            CheckPixels(card,template);
            var file=Path.Combine(output,$"empty-report-{template+1}.png");ReportWindow.Write(card,file);
            using var stream=File.OpenRead(file);var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
            if(decoder.Frames.Count!=1||decoder.Frames[0].PixelWidth!=1080||decoder.Frames[0].PixelHeight!=1920)throw new Exception($"Invalid PNG: {file}");
        }
        var one=new SpendReport{Records=[new SpendRecord("codex","demo",new DateTimeOffset(2026,9,14,12,0,0,TimeSpan.FromHours(8)),"test-model",Input:1000,Output:500,EstimatedCost:2m,Project:@"D:\demo\project")]};
        if(Enumerable.Range(0,8).Any(i=>!ReportWindow.Available(one,i,config)))throw new Exception("Populated report omitted a template.");
        for(var template=0;template<8;template++)
        {
            var card=ReportWindow.RenderCard(one,"month",template,config,new DateTimeOffset(2026,9,14,0,0,0,TimeSpan.FromHours(8)));CheckPixels(card,template);ReportWindow.Write(card,Path.Combine(output,$"populated-report-{template+1}.png"));
        }
        var year=new SpendReport{Records=Enumerable.Range(0,366).Select(i=>new SpendRecord("codex","year-demo-"+i,new DateTimeOffset(2024,1,1,12,0,0,TimeSpan.FromHours(8)).AddDays(i),"year-model",Input:1000+i*100,Output:500,EstimatedCost:.01m,Project:@"D:\demo\year")).ToList()};
        var yearly=ReportWindow.RenderCard(year,"year",1,config,new DateTimeOffset(2024,9,14,0,0,0,TimeSpan.FromHours(8)));CheckPixels(yearly,1);ReportWindow.Write(yearly,Path.Combine(output,"year-heatmap.png"));
        foreach(var language in new[]{"zh-CN","zh-Hant","en","ja","ko","ru"}){Localization.Language=language;Ui.ApplyTheme("light");var localized=ReportWindow.RenderCard(one,"month",0,config,new DateTimeOffset(2026,9,14,0,0,0,TimeSpan.FromHours(8)));CheckPixels(localized,0);ReportWindow.Write(localized,Path.Combine(output,"language-"+language+".png"));}Localization.Language="zh-CN";Ui.ApplyTheme("dark");
        if(!BrowserSignIn.Sources["claudeCode"].Cookies.Contains("sessionKeyV3"))throw new Exception("Claude browser capture omitted sessionKeyV3.");
        var period=ReportPeriods.Range("month",new DateTimeOffset(2026,9,14,0,0,0,TimeSpan.FromHours(8)));
        if(SpendPresentation.SubscriptionUsd(config,period)!=20m)throw new Exception("Monthly subscription cost was not calculated.");
        if(SpendPresentation.Money(2m,new AppConfig{Currency="JPY"},null)!="2 USD（未取得 JPY 汇率）")throw new Exception("Missing exchange rate was labelled as a new currency.");
        var store=new AppStorage(Path.Combine(output,"synthetic-storage"));
        store.Save("exchange-rates.json",new PricingCatalog.ExchangeCache(DateTimeOffset.UtcNow,new(){["CNY"]=7m,["JPY"]=150m}));
        var currency=new AppConfig{Currency="CNY"};
        if(SpendPresentation.Rate(currency,store)!=7m)throw new Exception("CNY cache rate was not used.");
        currency.Currency="JPY";
        if(SpendPresentation.Rate(currency,store)!=150m||SpendPresentation.Money(2m,currency,SpendPresentation.Rate(currency,store))!="300 JPY")throw new Exception("Currency change reused the old rate.");
        Console.WriteLine($"PASS: eight empty and eight populated report PNG cards with opaque pixels and title text, 366-day heatmap, template availability, monthly subscription, currency cache switching. Output: {output}");
        return 0;
    }
    private static void CheckPixels(BitmapSource card,int template)
    {
        var pixels=new byte[card.PixelWidth*card.PixelHeight*4];card.CopyPixels(pixels,card.PixelWidth*4,0);
        if(Enumerable.Range(0,card.PixelWidth*card.PixelHeight).Any(i=>pixels[i*4+3]!=255))throw new Exception($"Report {template}: background was not fully opaque");
        var textPixels=0;for(var y=240;y<350;y++)for(var x=90;x<990;x++){var index=(y*card.PixelWidth+x)*4;if(pixels[index+2]>120&&pixels[index]>120)textPixels++;}
        if(textPixels<100)throw new Exception($"Report {template}: title text was not rendered");
    }
}
