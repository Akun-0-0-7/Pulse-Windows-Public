using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pulse.Desktop;

internal static partial class Program
{
    private static void MotionChecks(PulseApp app,string output,bool render)
    {
        var config=app.Engine.Config;
        var prior=(config.ShowPanel,config.AutoCollapse,config.DockEdge,config.HideFullscreen,config.FollowDisplay,config.PanelOpacity,config.SettingsShortcut,config.PanelShortcut);
        var accounts=config.Accounts.Select(a=>(Account:a,a.Enabled,a.ProviderId,a.Detailed)).ToArray();
        config.ShowPanel=true;config.AutoCollapse=true;config.DockEdge="right";
        config.HideFullscreen=false;config.FollowDisplay=false;config.PanelOpacity=.01;
        config.SettingsShortcut=null;config.PanelShortcut=null;
        foreach(var item in config.Accounts)item.Enabled=false;
        var account=config.Accounts[0];account.Enabled=true;account.ProviderId="unknown-offline-fixture";account.Detailed=false;
        var rail=new RailWindow(app);
        try
        {
            rail.Show();RunFor(80);Field<System.Windows.Threading.DispatcherTimer>(rail,"screenTick").Stop();SetMouseOver(rail,true);
            var tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().First();
            rail.Render();
            if(!ReferenceEquals(tile,Field<StackPanel>(rail,"rings").Children[0]))
                throw new Exception("Reading refresh rebuilt the hovered ring instead of preserving its visual identity.");
            foreach(var edge in new[]{"left","right","top","bottom"})
            {
                config.DockEdge=edge;rail.ApplySettings();SetMouseOver(rail,true);Field<System.Windows.Threading.DispatcherTimer>(rail,"idle").Stop();RunFor(100);WaitUntil(()=>!Field<bool>(rail,"placementQueued"),"Rail placement did not settle before motion.");
                var width=rail.ActualWidth;var height=rail.ActualHeight;var left=rail.Left;var top=rail.Top;
                Invoke(rail,"SetRailCollapsed",true);RunFor(65);
                var progress=MotionValue(rail,"railMotion");
                if(SystemParameters.ClientAreaAnimation&&!(progress>0&&progress<1))
                    throw new Exception($"Rail did not render an intermediate spring state on {edge}: value={progress}, running={MotionRunning(rail,"railMotion")}, collapsed={Field<bool>(rail,"collapsed")}");
                Equal(width,rail.ActualWidth,"Fixed rail window width");Equal(height,rail.ActualHeight,"Fixed rail window height");
                Equal(left,rail.Left,"Fixed rail window left");Equal(top,rail.Top,"Fixed rail window top");
                var chrome=Field<FrameworkElement>(rail,"chrome");
                var rect=(Rect)chrome.GetType().GetProperty("VisibleBounds")!.GetValue(chrome)!;
                var edgeBefore=edge switch{"left"=>rect.Left,"right"=>rect.Right,"top"=>rect.Top,_=>rect.Bottom};
                RunFor(65);
                rect=(Rect)chrome.GetType().GetProperty("VisibleBounds")!.GetValue(chrome)!;
                var edgeAfter=edge switch{"left"=>rect.Left,"right"=>rect.Right,"top"=>rect.Top,_=>rect.Bottom};
                Equal(edgeBefore,edgeAfter,"Visible chrome dock anchor");
                progress=MotionValue(rail,"railMotion");Invoke(rail,"SetRailCollapsed",false);
                Equal(SystemParameters.ClientAreaAnimation?progress:1,MotionValue(rail,"railMotion"),"Reversal continuity or reduced-motion snap");
                RunFor(650);Equal(1,MotionValue(rail,"railMotion"),"Rail spring settles open");
            }
            Invoke(rail,"SetRailCollapsed",true);RunFor(650);
            var ringHost=Field<ScrollViewer>(rail,"expandedHost");Equal(0,ringHost.Opacity,"Closed ring opacity");
            Invoke(rail,"SetRailCollapsed",false);Equal(SystemParameters.ClientAreaAnimation?0:1,ringHost.Opacity,"Delayed fade or reduced-motion visibility");
            RunFor(650);
            foreach(var monitor in System.Windows.Forms.Screen.AllScreens)
            {
                // Corrupt only the cached viewport, then exercise the same placement path as WM_DPICHANGED.
                Field<Canvas>(rail,"stage").Height=10000;ringHost.Height=9990;
                Invoke(rail,"Place",monitor);RunFor(40);
                if(Field<Canvas>(rail,"stage").Height>rail.MaxHeight||ringHost.Height>rail.MaxHeight)
                    throw new Exception("Monitor placement did not recompute its rail viewport.");
            }
            tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().First();
            Invoke(rail,"ShowDetail",account,tile);var popup=Field<Window>(rail,"detail");popup.Opacity=0;
            RunFor(65);
            if(SystemParameters.ClientAreaAnimation&&!(MotionValue(rail,"detailMotion")>0&&MotionValue(rail,"detailMotion")<1))
                throw new Exception("Detail card did not render an intermediate spring state.");
            var detailProgress=MotionValue(rail,"detailMotion");rail.Render();
            if(!ReferenceEquals(popup,Field<Window>(rail,"detail"))||MotionValue(rail,"detailMotion")<detailProgress)
                throw new Exception("A reading refresh restarted or replaced the opening detail card.");
            Invoke(rail,"CloseDetail",true);RunFor(45);Invoke(rail,"ShowDetail",account,tile);RunFor(650);
            if(!ReferenceEquals(popup,Field<Window>(rail,"detail"))||!popup.IsVisible)
                throw new Exception("A stale closing animation removed the reopened card.");
            Equal(1,MotionValue(rail,"detailMotion"),"Detail spring settles open");
            if(render)CaptureMotion(rail,account,output);
            config.ShowPanel=false;rail.ApplySettings();
            if(Field<Window?>(rail,"detail") is not null||MotionRunning(rail,"railMotion"))
                throw new Exception("Hiding the panel left a detail or spring active.");
            config.ShowPanel=true;rail.ApplySettings();SetMouseOver(rail,true);
            Invoke(rail,"SetRailCollapsed",true);RunFor(35);rail.Close();RunFor(100);
            if(MotionRunning(rail,"railMotion"))throw new Exception("Closing the owner left a rendering subscription running.");
            Console.WriteLine("PASS: stable ring visuals, fixed-window dock anchors, spring reversal, detail reopen, and motion cleanup.");
        }
        finally
        {
            rail.Close();
            (config.ShowPanel,config.AutoCollapse,config.DockEdge,config.HideFullscreen,config.FollowDisplay,config.PanelOpacity,config.SettingsShortcut,config.PanelShortcut)=prior;
            foreach(var state in accounts){state.Account.Enabled=state.Enabled;state.Account.ProviderId=state.ProviderId;state.Account.Detailed=state.Detailed;}
        }
    }
    private static double MotionValue(object owner,string field)=>(double)Field<object>(owner,field).GetType().GetProperty("Value")!.GetValue(Field<object>(owner,field))!;
    private static bool MotionRunning(object owner,string field)=>(bool)Field<object>(owner,field).GetType().GetProperty("IsRunning")!.GetValue(Field<object>(owner,field))!;
    private static void CaptureMotion(RailWindow rail,Pulse.Core.AccountConfig account,string output)
    {
        var folder=Path.Combine(output,"motion-frames");Directory.CreateDirectory(folder);
        Invoke(rail,"CloseDetail",false);rail.ApplySettings();SetMouseOver(rail,true);RunFor(60);
        Invoke(rail,"SetRailCollapsed",true);
        var previous=0;
        foreach(var milliseconds in new[]{0,60,120,200,350,600})
        {
            if(milliseconds>previous)RunFor(milliseconds-previous);previous=milliseconds;
            Capture((FrameworkElement)rail.Content,Path.Combine(folder,$"rail-close-{milliseconds:000}.png"));
        }
        Invoke(rail,"SetRailCollapsed",false);RunFor(650);
        var tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().First();
        Invoke(rail,"ShowDetail",account,tile);var popup=Field<Window>(rail,"detail");popup.Opacity=0;previous=0;
        foreach(var milliseconds in new[]{0,60,120,200,350,600})
        {
            if(milliseconds>previous)RunFor(milliseconds-previous);previous=milliseconds;
            Capture((FrameworkElement)popup.Content,Path.Combine(folder,$"detail-open-{milliseconds:000}.png"));
        }
        Console.WriteLine("PASS: actual rail/card transition frame captures. Output: "+folder);
    }
    private static void Capture(FrameworkElement element,string path)
    {
        element.UpdateLayout();var width=Math.Max(1,(int)Math.Ceiling(element.ActualWidth));var height=Math.Max(1,(int)Math.Ceiling(element.ActualHeight));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
