using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void DockProgressChecks(PulseApp app)
    {
        var config=app.Engine.Config;var saved=JsonSerializer.Serialize(config,StorageJson.Options);var accounts=config.Accounts;
        var enabled=accounts.Select(account=>account.Enabled).ToArray();
        var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");var cached=readings.ToArray();
        RailWindow? rail=null;SettingsWindow? settings=null;var originalRail=app.Rail;
        try
        {
            foreach(var configured in config.Accounts)configured.Enabled=false;
            config.OnboardingCompleted=true;config.SpendEnabled=false;config.WindowStarterEnabled=false;config.AlertsEnabled=false;
            config.ShowPanel=false;config.SettingsShortcut=null;config.PanelShortcut=null;
            var drained=false;app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>drained=true));
            WaitUntil(()=>app.Rail is not null&&drained,"Synthetic startup did not settle before progress checks.");
            foreach(var name in new[]{"refresh","activityTimer","windowStarterTimer"})Field<DispatcherTimer>(app,name).Stop();
            originalRail=app.Rail;
            var account=new AccountConfig{Id="dock-progress-fixture",ProviderId="unknown-offline-fixture",Enabled=true,PrimaryWindowId="five"};
            var reading=new UsageReading{AccountId=account.Id,ProviderId=account.ProviderId,Windows=[new(){Id="five",Kind="fiveHour",UsedPercent=25},new(){Id="week",Kind="weekly",UsedPercent=100,Exhausted=true}]};
            config.Accounts=[account];readings.Clear();readings[account.Id]=reading;
            config.Appearance=new(){QuotaColorMode="gradient"};config.ShowRemaining=true;config.ShowPanel=true;
            config.AutoCollapse=false;config.FollowDisplay=false;config.HideFullscreen=false;config.PanelOpacity=.01;
            rail=new RailWindow(app);SetField(app,"<Rail>k__BackingField",rail);rail.Show();RunFor(60);
            Field<DispatcherTimer>(rail,"screenTick").Stop();
            foreach(var edge in new[]{"left","right","top","bottom"})
            {
                config.DockEdge=edge;rail.ApplySettings();SetMouseOver(rail,true);RunFor(60);
                WaitUntil(()=>!Field<bool>(rail,"placementQueued"),"Progress placement did not settle.");
                Invoke(rail,"SetRailCollapsed",true);Field<RailMotionValue>(rail,"railMotion").SnapTo(0);
                ProgressDrawing(rail,.75,AppearancePalette.Quota(config,25),edge);
                var size=new Size(rail.ActualWidth,rail.ActualHeight);var position=new Point(rail.Left,rail.Top);
                Field<RailMotionValue>(rail,"railMotion").SnapTo(.5);
                Equal(size.Width,rail.ActualWidth,"Progress morph fixed width");Equal(size.Height,rail.ActualHeight,"Progress morph fixed height");
                Equal(position.X,rail.Left,"Progress morph anchor X");Equal(position.Y,rail.Top,"Progress morph anchor Y");
                Field<RailMotionValue>(rail,"railMotion").SnapTo(0);
            }
            config.ShowRemaining=false;rail.Render();ProgressDrawing(rail,.25,AppearancePalette.Quota(config,25),"bottom");
            config.ShowRemaining=true;reading.Windows[0].UsedPercent=100;rail.Render();ProgressDrawing(rail,0,AppearancePalette.Quota(config,100),"bottom");
            reading.Windows[0].UsedPercent=double.NaN;rail.Render();ProgressDrawing(rail,0,AppearancePalette.Dock(config,[reading]),"bottom");
            reading.Windows[0].UsedPercent=0;rail.Render();ProgressDrawing(rail,1,AppearancePalette.Quota(config,0),"bottom");
            reading.Windows[0].UsedPercent=25;rail.Render();
            settings=new SettingsWindow(app);settings.Navigate("appearance");
            var toggle=Descendants((DependencyObject)settings.Content).OfType<CheckBox>().SingleOrDefault(box=>Equals(box.Tag,"dock-show-progress"))
                ??throw new Exception("The live collapsed-progress appearance switch is missing.");
            toggle.IsChecked=false;
            if(app.Storage.LoadConfig().Appearance.DockShowProgress)
                throw new Exception("The legacy solid-strip switch was not persisted.");
            if(!Field<bool>(rail,"collapsed"))throw new Exception("Changing progress presentation expanded the rail.");
            var solid=DockDrawings(rail);var color=((SolidColorBrush)Ui.Brush(AppearancePalette.Quota(config,25))).Color;
            if(solid.Count(item=>item.Drawing.Brush is SolidColorBrush brush&&brush.Color==color)!=1)
                throw new Exception("Disabling progress did not restore the solid quota-colored strip.");
            toggle.IsChecked=true;ProgressDrawing(rail,.75,AppearancePalette.Quota(config,25),"bottom");
            Invoke(rail,"SetRailCollapsed",false);Field<RailMotionValue>(rail,"railMotion").SnapTo(1);
            if(DockDrawings(rail).Count!=1)throw new Exception("The progress overlay remained in the fully expanded rail.");
            Console.WriteLine("PASS: actual collapsed progress drawing on four edges, remaining/used, zero/full/unknown, live legacy switch and fixed-window morph; expanded chrome remains unchanged.");
        }
        finally
        {
            settings?.Close();rail?.Close();SetField(app,"<Rail>k__BackingField",originalRail!);
            var restored=JsonSerializer.Deserialize<AppConfig>(saved,StorageJson.Options)!;
            foreach(var property in typeof(AppConfig).GetProperties().Where(property=>property.CanRead&&property.CanWrite))property.SetValue(config,property.GetValue(restored));
            for(var index=0;index<accounts.Count;index++)accounts[index].Enabled=enabled[index];
            config.Accounts=accounts;readings.Clear();foreach(var item in cached)readings[item.Key]=item.Value;Ui.ApplyTheme(config);
        }
    }
    private static void ProgressDrawing(RailWindow rail,double fraction,string color,string edge)
    {
        var chrome=Field<FrameworkElement>(rail,"chrome");var bounds=(Rect)chrome.GetType().GetProperty("VisibleBounds")!.GetValue(chrome)!;
        var ink=((SolidColorBrush)Ui.Brush(color)).Color;
        var painted=DockDrawings(rail).Where(item=>item.Drawing.Brush is SolidColorBrush brush&&brush.Color==ink).ToArray();
        if(fraction==0){if(painted.Length!=0)throw new Exception("Empty/unknown quota painted a fabricated progress fill.");return;}
        var expected=edge is "left" or "right"?new Rect(bounds.X,bounds.Bottom-bounds.Height*fraction,bounds.Width,bounds.Height*fraction):new Rect(bounds.X,bounds.Y,bounds.Width*fraction,bounds.Height);
        if(painted.Length!=1||!NearProgressRect(painted[0].Drawing.Geometry.Bounds,expected)||!painted[0].Clipped)
            throw new Exception($"Collapsed {edge} progress is not a clipped {fraction:P0} fill: expected {expected}; observed "+string.Join("; ",painted.Select(item=>item.Drawing.Geometry.Bounds.ToString())));
    }
    private static bool NearProgressRect(Rect actual,Rect expected)=>Math.Abs(actual.X-expected.X)<.00001&&Math.Abs(actual.Y-expected.Y)<.00001&&Math.Abs(actual.Width-expected.Width)<.00001&&Math.Abs(actual.Height-expected.Height)<.00001;
    private static List<(GeometryDrawing Drawing,bool Clipped)> DockDrawings(RailWindow rail)
    {
        var chrome=Field<FrameworkElement>(rail,"chrome");var commands=new DrawingGroup();
        using(var context=commands.Open())chrome.GetType().GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(chrome,[context]);
        var result=new List<(GeometryDrawing,bool)>();
        void Walk(Drawing drawing,bool clipped)
        {
            if(drawing is GeometryDrawing geometry)result.Add((geometry,clipped));
            else if(drawing is DrawingGroup group)foreach(var child in group.Children)Walk(child,clipped||group.ClipGeometry is not null);
        }
        Walk(commands,false);return result;
    }
}
