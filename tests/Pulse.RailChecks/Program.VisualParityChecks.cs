using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void VisualParityChecks(PulseApp app,string output,bool capturePixels=true)
    {
        var config=app.Engine.Config;var saved=System.Text.Json.JsonSerializer.Serialize(config,StorageJson.Options);
        var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");var priorReadings=readings.ToArray();
        var priorAccounts=config.Accounts;var rail=new RailWindow(app);
        try
        {
            config.Accounts=[new(){Id="visual-claude",ProviderId="claudeCode",Enabled=true},new(){Id="visual-codex",ProviderId="codex",Enabled=true},new(){Id="visual-deepseek",ProviderId="deepSeek",Enabled=true},new(){Id="visual-zai",ProviderId="zai",Enabled=true}];
            config.ShowPanel=true;config.AutoCollapse=false;config.DockEdge="top";config.Horizontal=true;config.HideFullscreen=false;config.FollowDisplay=false;config.PanelOpacity=.01;config.SettingsShortcut=null;config.PanelShortcut=null;config.ShowRemaining=false;config.ShowClock=false;config.ShowSecondaryRing=false;config.Theme="dark";config.RingSize=36;config.RingGap=12;
            readings.Clear();
            readings["visual-claude"]=new(){AccountId="visual-claude",ProviderId="claudeCode",Windows=[new(){Id="primary",Kind="fiveHour",UsedPercent=100,Exhausted=true}]};
            readings["visual-codex"]=new(){AccountId="visual-codex",ProviderId="codex",Plan="fixture-plan-hidden-in-compact",ObservedAt=DateTimeOffset.Now,Source="fixture",Windows=[new(){Id="weekly",Kind="weekly",UsedPercent=0,ResetsAt=new DateTimeOffset(2026,10,27,14,34,0,TimeSpan.FromHours(8))}]};
            readings["visual-deepseek"]=new(){AccountId="visual-deepseek",ProviderId="deepSeek",Balance=new(34.53m,"CNY")};
            readings["visual-zai"]=new(){AccountId="visual-zai",ProviderId="zai",Balance=new(19.93m,"CNY")};
            rail.Show();RunFor(150);Field<System.Windows.Threading.DispatcherTimer>(rail,"screenTick").Stop();SetMouseOver(rail,true);
            var account=config.Accounts[1];var content=RailWindow.DetailContent(app,account);
            var labels=Descendants(content).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            if(!labels.Contains("Codex 用量"))throw new Exception("Compact card still lacks the upstream provider-and-usage header.");
            if(labels.Any(t=>t.Contains("fixture-plan",StringComparison.Ordinal)||t.Contains("读取于",StringComparison.Ordinal)))throw new Exception("Compact card leaked detailed-only metadata.");
            var tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().ElementAt(1);
            Invoke(rail,"ShowDetail",account,tile);var popup=Field<Window>(rail,"detail");popup.Opacity=0;RunFor(700);
            var bubble=Field<object>(rail,"detailBubble");
            if((string)bubble.GetType().GetProperty("ArrowSide")!.GetValue(bubble)! != "top")throw new Exception("Top dock card pointer is not on the rail-facing edge.");
            var anchor=(double)bubble.GetType().GetProperty("Anchor")!.GetValue(bubble)!;
            Invoke(rail,"RefreshDetail",true);bubble=Field<object>(rail,"detailBubble");
            Equal(anchor,(double)bubble.GetType().GetProperty("Anchor")!.GetValue(bubble)!,"Same-size refresh must preserve the pointer anchor");RunFor(60);
            var folder=Path.Combine(output,"visual-parity");Directory.CreateDirectory(folder);
            if(capturePixels)
            {
                CaptureDpi((FrameworkElement)rail.Content,Path.Combine(folder,"top-rail.png"));CaptureDpi((FrameworkElement)popup.Content,Path.Combine(folder,"codex-card.png"));
                using var image=File.OpenRead(Path.Combine(folder,"top-rail.png"));
                var frame=new PngBitmapDecoder(image,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
                var pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];frame.CopyPixels(pixels,frame.PixelWidth*4,0);
                var firstRed=int.MaxValue;
                for(var y=0;y<frame.PixelHeight;y++)for(var x=0;x<frame.PixelWidth;x++){var index=(y*frame.PixelWidth+x)*4;if(pixels[index+2]>150&&pixels[index+1]<80&&pixels[index]<100&&pixels[index+3]>150)firstRed=Math.Min(firstRed,y);}
                if(firstRed>18)throw new Exception("The source's outward ring stroke was clipped by the scroll viewport.");
            }
            else Console.WriteLine("SKIP: native pixel captures explicitly disabled for a session without WPF raster output.");
            foreach(var (edge,side) in new[]{("left","left"),("right","right"),("bottom","bottom")})
            {
                config.DockEdge=edge;rail.ApplySettings();SetMouseOver(rail,true);RunFor(80);
                tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().ElementAt(1);Invoke(rail,"ShowDetail",account,tile);popup=Field<Window>(rail,"detail");popup.Opacity=0;RunFor(650);
                bubble=Field<object>(rail,"detailBubble");if((string)bubble.GetType().GetProperty("ArrowSide")!.GetValue(bubble)! !=side)throw new Exception("Card pointer direction disagrees with dock edge "+edge);
            }
            Console.WriteLine("PASS: upstream compact card, branded rail and four pointer directions. Output: "+folder);
        }
        finally
        {
            rail.Close();readings.Clear();foreach(var item in priorReadings)readings[item.Key]=item.Value;
            var restored=System.Text.Json.JsonSerializer.Deserialize<AppConfig>(saved,StorageJson.Options)!;
            foreach(var property in typeof(AppConfig).GetProperties().Where(p=>p.CanRead&&p.CanWrite))property.SetValue(config,property.GetValue(restored));
            config.Accounts=priorAccounts;
        }
    }
    private static void CaptureDpi(FrameworkElement element,string path)
    {
        element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth*2),(int)Math.Ceiling(element.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }

}
