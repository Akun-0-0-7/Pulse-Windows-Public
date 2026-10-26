using System.Windows.Media.Imaging;
namespace Pulse.Desktop;
public static class VisualChecks
{
    public static int Run(string output)
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PULSE_DATA_DIR")))return 2;
        try{return Render(output);}catch(InvalidOperationException exception){new AppStorage(PulseApp.DataRoot).Save("visual-verification.json",new{at=DateTimeOffset.UtcNow,syntheticData=true,passed=false,error=exception.Message,realPointerInput=false});return 3;}
    }
    private static int Render(string output)
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PULSE_DATA_DIR")))throw new InvalidOperationException("Visual checks require an isolated PULSE_DATA_DIR.");
        Directory.CreateDirectory(output);var store=new AppStorage(PulseApp.DataRoot);var now=DateTimeOffset.UtcNow;var config=new AppConfig{OnboardingCompleted=true,Accounts=[new(){Id="codex-demo",ProviderId="codex",Label="Codex · 演示数据",Enabled=true},new(){Id="deepseek-demo",ProviderId="deepSeek",Label="DeepSeek · 演示数据",Enabled=true}]};
        store.SaveConfig(config);store.SaveCache([new(){AccountId="codex-demo",ProviderId="codex",Plan="Pro",Source="fixture",ObservedAt=now,Windows=[new(){Id="weekly",Kind="weekly",UsedPercent=62,ResetsAt=now.AddDays(4),WindowSeconds=604800,ReportsLength=true},new(){Id="model",Label="模型额度",Kind="daily",UsedPercent=18,ResetsAt=now.AddHours(10),WindowSeconds=86400,ReportsLength=true}]},new(){AccountId="deepseek-demo",ProviderId="deepSeek",Source="fixture",ObservedAt=now,Balance=new(36.50m,"CNY")}]);
        var app=new PulseApp();Ui.ApplyTheme("dark");var settings=new SettingsWindow(app);settings.Navigate("dashboard");Capture((FrameworkElement)settings.Content,1100,790,Path.Combine(output,"dashboard-dark.png"));settings.Navigate("accounts");Capture((FrameworkElement)settings.Content,1100,790,Path.Combine(output,"services-dark.png"));settings.Navigate("onboarding");Capture((FrameworkElement)settings.Content,1100,790,Path.Combine(output,"onboarding.png"));Ui.ApplyTheme("light");settings.Navigate("dashboard");Capture((FrameworkElement)settings.Content,1100,790,Path.Combine(output,"dashboard-light.png"));Ui.ApplyTheme("dark");
        var rail=new StackPanel {Margin=new Thickness(15)};foreach(var account in app.Engine.Config.Accounts)rail.Children.Add(new RingTile(account,app.Engine.Reading(account.Id),ProviderCatalog.Find(account.ProviderId),config,false){Margin=new Thickness(10)});Capture(Ui.Box(rail),110,260,Path.Combine(output,"rail.png"));
        var records=new List<SpendRecord>();for(int i=0;i<15;i++)records.Add(new("codex","example-"+i,DateTimeOffset.Now.AddDays(-i),"demo-model",Input:2000+i*200,Output:800,CacheRead:1200,EstimatedCost:.11m+i*.01m,Project:@"D:\example\project"));var report=new SpendReport{Records=records};for(int template=0;template<8;template++)ReportWindow.Write(ReportWindow.RenderCard(report,"month",template,config),Path.Combine(output,$"report-{template+1}.png"));
        store.Save("visual-verification.json",new{rendered=DateTimeOffset.Now,syntheticData=true,images=13,networkRequests=0,realPointerInput=false});settings.Close();app.Shutdown();return 0;
    }
    private static void Capture(FrameworkElement element,int width,int height,string path){element.Width=width;element.Height=height;element.Measure(new Size(width,height));element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);if(!Enumerable.Range(0,width*height).Any(i=>pixels[i*4+3]>0))throw new InvalidOperationException("当前桌面会话没有生成 WPF 界面像素；此次界面呈现检查未通过。");ReportWindow.Write(bitmap,path);}
}
