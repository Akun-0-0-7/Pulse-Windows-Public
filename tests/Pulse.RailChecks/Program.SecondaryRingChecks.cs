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
    private static void SecondaryRingChecks(PulseApp app)
    {
        var account=new AccountConfig{Id="inner-ring-fixture",ProviderId="unknown-offline-fixture",Enabled=true,PrimaryWindowId="five"};
        var reading=new UsageReading{AccountId=account.Id,ProviderId=account.ProviderId,Windows=[new(){Id="five",Kind="fiveHour",UsedPercent=20},new(){Id="week",Kind="weekly",UsedPercent=25}]};
        AppConfig Options(bool remaining)=>JsonSerializer.Deserialize<AppConfig>("{\"ringSize\":36,\"showClock\":false,\"showRemaining\":true,\"secondaryRingShowRemaining\":"+(remaining?"true":"false")+"}",StorageJson.Options)!;
        var legacy=JsonSerializer.Deserialize<AppConfig>("{\"ringSize\":36,\"showClock\":false}",StorageJson.Options)!;
        SecondaryArc(RingDrawings(account,reading,legacy),.25,"Legacy settings retain used inner quota");
        var used=RingDrawings(account,reading,Options(false));var remaining=RingDrawings(account,reading,Options(true));
        SecondaryArc(remaining,.75,"Remaining weekly quota");SecondaryArc(used,.25,"Used weekly quota");
        var usedInk=used.Single(item=>item.Pen?.Thickness==1).Pen!.Brush;
        var remainingInk=remaining.Single(item=>item.Pen?.Thickness==1).Pen!.Brush;
        if(((SolidColorBrush)usedInk).Color!=((SolidColorBrush)remainingInk).Color)throw new Exception("Switching inner quota direction inverted its risk color.");
        var outerUsed=used.Where(item=>item.Pen?.Thickness==4).Select(item=>item.Geometry.ToString()).ToArray();
        var outerRemaining=remaining.Where(item=>item.Pen?.Thickness==4).Select(item=>item.Geometry.ToString()).ToArray();
        if(!outerUsed.SequenceEqual(outerRemaining))throw new Exception("Inner quota mode changed the outer ring.");
        var independent=Options(true);independent.ShowRemaining=false;
        SecondaryArc(RingDrawings(account,reading,independent),.75,"Inner remaining is independent of outer used mode");
        independent.SecondaryRingShowRemaining=false;
        SecondaryArc(RingDrawings(account,reading,independent),.25,"Inner used is independent of outer used mode");
        reading.Windows[1].UsedPercent=0;
        SecondaryArc(RingDrawings(account,reading,Options(true)),1,"Zero used is full remaining");
        SecondaryArc(RingDrawings(account,reading,Options(false)),0,"Zero used is empty used");
        reading.Windows[1].UsedPercent=100;
        SecondaryArc(RingDrawings(account,reading,Options(true)),0,"No remaining quota is empty");
        SecondaryArc(RingDrawings(account,reading,Options(false)),1,"Full usage fills the inner ring");
        reading.Windows[1].UsedPercent=25;reading.Windows[1].Exhausted=true;
        SecondaryArc(RingDrawings(account,reading,Options(true)),0,"Exhausted secondary quota has no remaining arc");
        SecondaryArc(RingDrawings(account,reading,Options(false)),1,"Exhausted secondary quota fills the used arc");
        reading.Windows[1].Exhausted=false;reading.Windows[1].UsedPercent=double.NaN;
        SecondaryArc(RingDrawings(account,reading,Options(true)),0,"Invalid quota does not invent an arc");
        reading.Windows[1].UsedPercent=25;independent.ShowSecondaryRing=false;
        SecondaryArc(RingDrawings(account,reading,independent),0,"Hiding inner quota still works");
        account.PrimaryWindowId="week";reading.Windows[0].UsedPercent=50;
        SecondaryArc(RingDrawings(account,reading,Options(true)),.5,"Selected weekly outer keeps five-hour secondary");
        account.PrimaryWindowId="five";reading.Windows[0].UsedPercent=20;
        var onlyPrimary=new UsageReading{Windows=[reading.Windows[0]]};
        SecondaryArc(RingDrawings(account,onlyPrimary,Options(true)),0,"A missing secondary window has no inner arc");

        var config=app.Engine.Config;var saved=JsonSerializer.Serialize(config,StorageJson.Options);var accounts=config.Accounts;var enabled=accounts.Select(item=>item.Enabled).ToArray();
        var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");var cached=readings.ToArray();
        RailWindow? rail=null;SettingsWindow? settings=null;var originalRail=app.Rail;
        try
        {
            foreach(var item in accounts)item.Enabled=false;
            config.OnboardingCompleted=true;config.SpendEnabled=false;config.WindowStarterEnabled=false;config.AlertsEnabled=false;config.ShowPanel=false;config.SettingsShortcut=null;config.PanelShortcut=null;
            var drained=false;app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>drained=true));
            WaitUntil(()=>app.Rail is not null&&drained,"Synthetic startup did not settle before inner ring checks.");
            foreach(var name in new[]{"refresh","activityTimer","windowStarterTimer"})Field<DispatcherTimer>(app,name).Stop();
            originalRail=app.Rail;config.Accounts=[account];readings.Clear();readings[account.Id]=reading;
            config.ShowPanel=true;config.ShowClock=false;config.RingSize=36;config.ShowSecondaryRing=true;config.AutoCollapse=false;config.FollowDisplay=false;config.HideFullscreen=false;config.PanelOpacity=.01;
            config.SecondaryRingShowRemaining=false;
            rail=new RailWindow(app);SetField(app,"<Rail>k__BackingField",rail);rail.Show();RunFor(60);SetMouseOver(rail,true);Field<DispatcherTimer>(rail,"screenTick").Stop();
            Invoke(rail,"SetRailCollapsed",true);Field<RailMotionValue>(rail,"railMotion").SnapTo(0);
            settings=new SettingsWindow(app);settings.Navigate("appearance");
            var toggle=Descendants((DependencyObject)settings.Content).OfType<CheckBox>().SingleOrDefault(item=>Equals(item.Tag,"secondary-ring-show-remaining"))??throw new Exception("Independent inner remaining-quota switch is missing.");
            var tile=Field<StackPanel>(rail,"rings").Children.OfType<RingTile>().Single();
            var outerMode=config.ShowRemaining;var dockBefore=AppearancePalette.DockState(config,[reading]);
            foreach(var value in new[]{true,false})
            {
                toggle.IsChecked=value;
                if(app.Storage.LoadConfig().SecondaryRingShowRemaining!=value)throw new Exception("Inner quota mode was not saved for restart.");
                SecondaryArc(CaptureRing(tile),value ? .75 : .25,"Live inner quota switch");
                if(config.ShowRemaining!=outerMode||AppearancePalette.DockState(config,[reading])!=dockBefore)throw new Exception("Inner quota mode changed outer/collapsed quota.");
                if(!Field<bool>(rail,"collapsed"))throw new Exception("Inner quota switch reopened the collapsed strip.");
            }
            Console.WriteLine("PASS: independent secondary-ring used/remaining drawing, legacy default, zero/full/invalid/exhausted, alternate primary, unchanged outer/color/dock, live save and preserved collapse.");
        }
        finally
        {
            settings?.Close();rail?.Close();SetField(app,"<Rail>k__BackingField",originalRail!);
            var restored=JsonSerializer.Deserialize<AppConfig>(saved,StorageJson.Options)!;
            foreach(var property in typeof(AppConfig).GetProperties().Where(item=>item.CanRead&&item.CanWrite))property.SetValue(config,property.GetValue(restored));
            for(var index=0;index<accounts.Count;index++)accounts[index].Enabled=enabled[index];
            config.Accounts=accounts;readings.Clear();foreach(var item in cached)readings[item.Key]=item.Value;Ui.ApplyTheme(config);
        }
    }
    private static List<GeometryDrawing> RingDrawings(AccountConfig account,UsageReading reading,AppConfig config)=>CaptureRing(new RingTile(account,reading,null,config,false));
    private static List<GeometryDrawing> CaptureRing(RingTile tile)
    {
        var group=new DrawingGroup();using(var context=group.Open())typeof(RingTile).GetMethod("OnRender",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(tile,[context]);
        var result=new List<GeometryDrawing>();
        void Walk(Drawing drawing){if(drawing is GeometryDrawing geometry)result.Add(geometry);else if(drawing is DrawingGroup nested)foreach(var item in nested.Children)Walk(item);}
        Walk(group);return result;
    }
    private static void SecondaryArc(List<GeometryDrawing> drawings,double fraction,string name)
    {
        var arcs=drawings.Where(item=>item.Pen?.Thickness==1).ToArray();
        if(fraction==0){if(arcs.Length!=0)throw new Exception(name+": expected no inner fill.");return;}
        if(arcs.Length!=1)throw new Exception(name+": expected exactly one inner arc.");
        if(fraction==1){if(arcs[0].Geometry is not EllipseGeometry)throw new Exception(name+": expected a complete circle.");return;}
        var geometry=PathGeometry.CreateFromGeometry(arcs[0].Geometry);var figure=geometry.Figures.Single();var arc=figure.Segments.OfType<ArcSegment>().Single();
        var expected=fraction switch{.25=>new Point(32,18),.75=>new Point(6,18),.5=>new Point(19,31),_=>throw new Exception("Unknown fixture fraction")};
        Equal(19,figure.StartPoint.X,name+" start X");Equal(5,figure.StartPoint.Y,name+" start Y");Equal(expected.X,arc.Point.X,name+" end X");Equal(expected.Y,arc.Point.Y,name+" end Y");
        if(arc.IsLargeArc!=(fraction>.5)||arc.SweepDirection!=SweepDirection.Clockwise)throw new Exception(name+": incorrect arc direction or length.");
    }
}
