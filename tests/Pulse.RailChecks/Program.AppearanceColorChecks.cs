using System.Windows;
using System.Windows.Controls;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void AppearanceColorChecks(PulseApp app)
    {
        UiAppearanceChecks.Run();
        var config=app.Engine.Config;var saved=JsonSerializer.Serialize(config,StorageJson.Options);var originalAccounts=config.Accounts;
        var readings=Field<ConcurrentDictionary<string,UsageReading>>(app.Engine,"readings");var originalReadings=readings.ToArray();
        var originalRail=app.Rail;
        config.Theme="dark";config.Appearance=new();config.ShowPanel=true;config.AutoCollapse=false;config.DockEdge="right";config.HideFullscreen=false;config.FollowDisplay=false;config.PanelOpacity=.01;config.SettingsShortcut=null;config.PanelShortcut=null;
        var account=new AccountConfig{Id="appearance-fixture",ProviderId="unknown-offline-fixture",Enabled=true};
        config.Accounts=[account,new(){Id="disabled-color-fixture",ProviderId=account.ProviderId,Enabled=false}];
        var reading=new UsageReading{AccountId=account.Id,ProviderId=account.ProviderId,ObservedAt=System.DateTimeOffset.Now,Windows=[new(){Id="quota",Kind="fiveHour",UsedPercent=80}]};
        readings.Clear();readings[account.Id]=reading;readings["disabled-color-fixture"]=new(){AccountId="disabled-color-fixture",ProviderId=account.ProviderId,Windows=[new(){UsedPercent=100}]};
        var settings=new SettingsWindow(app);var rail=new RailWindow(app);
        SetField(app,"<Rail>k__BackingField",rail);
        try
        {
            settings.Navigate("appearance");
            var labels=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Select(t=>t.Text).ToArray();
            foreach(var label in new[]{"收起条颜色","悬浮条背景色","详情卡背景色","软件背景色","卡片背景色","软件强调色","正常额度颜色","警告额度颜色","危险额度颜色"})
                if(!labels.Contains(label))throw new System.Exception("Missing adjustable appearance color: "+label);
            FrameworkElement Row(string key)=>Descendants((DependencyObject)settings.Content).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"color-setting:"+key));
            void Edit(string key,string value)
            {
                var row=Row(key);Descendants(row).OfType<TextBox>().Single().Text=value;
                Descendants(row).OfType<Button>().Single(button=>Equals(button.Tag,"color-apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            var field=Descendants(Row("RailBackground")).OfType<TextBox>().Single();field.Text="#123456";
            if(config.Appearance.RailBackground is not null)throw new System.Exception("Typing a partial color unexpectedly saved settings.");
            Edit("RailBackground","#123456");
            if(app.Storage.LoadConfig().Appearance.RailBackground!="#123456")throw new System.Exception("Valid color was not persisted.");
            Edit("RailBackground","invalid");
            if(config.Appearance.RailBackground!="#123456"||!Descendants(Row("RailBackground")).OfType<TextBlock>().Any(block=>Equals(block.Tag,"color-error")&&block.Visibility==Visibility.Visible))
                throw new System.Exception("Invalid color must retain the saved value and explain the error.");
            Descendants(Row("RailBackground")).OfType<Button>().Single(button=>Equals(button.Tag,"color-default")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.Appearance.RailBackground is not null)throw new System.Exception("Per-color default did not remove its override.");
            Edit("WarningPercent","95");
            Equal(75,config.Appearance.WarningPercent,"Invalid threshold ordering retains prior value");
            Edit("CriticalPercent","95");Edit("WarningPercent","70");
            Equal(70,app.Storage.LoadConfig().Appearance.WarningPercent,"Saved warning threshold");
            var mode=Descendants((DependencyObject)settings.Content).OfType<ComboBox>().Single(combo=>Equals(combo.Tag,"appearance:QuotaColorMode"));
            mode.SelectedValue="随额度渐变";
            if(config.Appearance.QuotaColorMode!="gradient")throw new System.Exception("Color mode did not save its stable value.");
            mode.SelectedValue="按阈值分档";

            rail.Show();RunFor(70);SetMouseOver(rail,true);
            if(app.Rail is {} startupRail&&!ReferenceEquals(startupRail,rail)&&!ReferenceEquals(startupRail,originalRail))startupRail.Close();
            SetField(app,"<Rail>k__BackingField",rail);
            foreach(var name in new[]{"refresh","activityTimer","windowStarterTimer"})Field<System.Windows.Threading.DispatcherTimer>(app,name).Stop();
            Invoke(rail,"SetRailCollapsed",true);RunFor(35);var before=MotionValue(rail,"railMotion");
            Edit("DockColor","#2468AC");
            var dockMode=Descendants((DependencyObject)settings.Content).OfType<ComboBox>().Single(combo=>Equals(combo.Tag,"appearance:DockColorMode"));dockMode.SelectedValue="固定颜色";
            if(config.Appearance.DockColorMode!="fixed"||config.Appearance.DockColor!="#2468AC"||!ReferenceEquals(app.Rail,rail))throw new System.Exception("Color binding mismatch: mode="+config.Appearance.DockColorMode+", color="+config.Appearance.DockColor+", rail="+ReferenceEquals(app.Rail,rail));
            if(!Field<bool>(rail,"collapsed"))throw new System.Exception("Changing colors expanded the collapsed rail.");
            Equal(before,MotionValue(rail,"railMotion"),"Color update preserves presented spring position");
            CheckColor("#2468AC",Field<Color>(rail,"closedColor"),"Fixed collapsed color");
            dockMode.SelectedValue="跟随额度";
            CheckColor(AppearancePalette.Quota(config,80),Field<Color>(rail,"closedColor"),"Disabled cached account cannot override quota color");
            var hiddenWeekly=new UsageWindow{Id="hidden-weekly",Kind="weekly",UsedPercent=100,Exhausted=true};
            reading.Windows.Add(hiddenWeekly);account.PrimaryWindowId="quota";
            try
            {
                rail.Render();
                CheckColor(AppearancePalette.Quota(config,80),Field<Color>(rail,"closedColor"),"Collapsed rail follows displayed five-hour quota despite hidden weekly exhaustion");
                account.PrimaryWindowId=hiddenWeekly.Id;rail.Render();
                CheckColor(AppearancePalette.Quota(config,100,true),Field<Color>(rail,"closedColor"),"Collapsed rail follows weekly exhaustion only when weekly is displayed");
                if(!Field<bool>(rail,"collapsed"))throw new System.Exception("Changing the displayed quota color expanded the collapsed rail.");
            }
            finally{reading.Windows.Remove(hiddenWeekly);account.PrimaryWindowId=null;rail.Render();}
            Edit("RailBackground","#112233");CheckColor("#112233",Field<Color>(rail,"expandedColor"),"Expanded background updates live");
            Invoke(rail,"SetRailCollapsed",false);RunFor(650);SetMouseOver(rail,true);
            var tile=Field<StackPanel>(rail,"rings").Children.OfType<FrameworkElement>().First();Invoke(rail,"ShowDetail",account,tile);RunFor(50);
            var bubble=Field<object>(rail,"detailBubble");var oldAnchor=(double)bubble.GetType().GetProperty("Anchor")!.GetValue(bubble)!;
            Edit("DetailBackground","#F0F0F0");
            if(!ReferenceEquals(bubble,Field<object>(rail,"detailBubble")))throw new System.Exception("Changing detail colors replaced its window/bubble.");
            CheckColor("#F0F0F0",((SolidColorBrush)bubble.GetType().GetProperty("Fill")!.GetValue(bubble)!).Color,"Detail background updates despite identical reading object");
            Equal(oldAnchor,(double)bubble.GetType().GetProperty("Anchor")!.GetValue(bubble)!,"Color update preserves detail anchor");
            var content=RailWindow.DetailContent(app,account);var header=Descendants(content).OfType<TextBlock>().First();
            CheckColor("#000000",((SolidColorBrush)header.Foreground).Color,"Light detail foreground contrasts with independent rail background");
            Edit("QuotaWarning","#AA22CC");
            content=RailWindow.DetailContent(app,account);var meter=Descendants(content).OfType<FrameworkElement>().Single(element=>Equals(element.Tag,"quota-meter"));
            CheckColor("#AA22CC",((SolidColorBrush)meter.GetType().GetProperty("FillBrush")!.GetValue(meter)!).Color,"Detail quota meter uses custom warning color");
            Edit("AppBackground","#101820");Edit("AppSurface","#FFFFFF");Edit("AppAccent","#3366CC");
            CheckColor("#101820",((SolidColorBrush)Ui.Bg).Color,"Application background");CheckColor("#FFFFFF",((SolidColorBrush)Ui.Card).Color,"Application card");CheckColor("#3366CC",((SolidColorBrush)Ui.Accent).Color,"Application accent");
            Edit("DetailBackground","");settings.Navigate("dashboard");
            var dashboardHeader=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Single(block=>block.Text.EndsWith(" 用量",System.StringComparison.Ordinal));
            CheckColor("#000000",((SolidColorBrush)dashboardHeader.Foreground).Color,"Dashboard card uses its software surface rather than floating detail background");
            settings.Navigate("appearance");
            var originalTray=Field<System.Windows.Forms.NotifyIcon?>(app,"tray");
            using(var fixtureTray=new System.Windows.Forms.NotifyIcon{Visible=false})
            {
                SetField(app,"tray",fixtureTray);config.TrayUsageMode="ring";config.TrayAccountId=account.Id;
                reading.Windows[0].UsedPercent=20;reading.Windows[0].Exhausted=true;
                try
                {
                    Invoke(app,"UpdateTray");using var bitmap=fixtureTray.Icon!.ToBitmap();var dangerPixels=0;
                    for(var y=0;y<bitmap.Height;y++)for(var x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.R>150&&pixel.G<70&&pixel.B<120)dangerPixels++;}
                    if(dangerPixels==0)throw new System.Exception("Tray icon ignored an exhausted low-percent quota window.");
                }
                finally{typeof(PulseApp).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,originalTray);reading.Windows[0].UsedPercent=80;reading.Windows[0].Exhausted=false;}
            }
            Edit("AppBackground","#FFFFFF");account.ProviderId="codex";account.Detailed=true;reading.ProviderId="codex";
            var detailsKey=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(account.Id)))[..20];
            app.Storage.Save("codex-details-"+detailsKey+".json",new CodexAccountDetails([],321,2,null));
            var detailed=RailWindow.DetailContent(app,account);
            var historyText=Descendants(detailed).OfType<TextBlock>().Single(block=>block.Text.StartsWith("累计 Token",System.StringComparison.Ordinal));
            CheckColor("#FFFFFF",((SolidColorBrush)historyText.Foreground).Color,"Nested detail history uses the floating detail surface");
            Descendants((DependencyObject)settings.Content).OfType<Button>().Single(button=>Equals(button.Content,"清新绿")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.Appearance.QuotaNormal!="#4ADE80")throw new System.Exception("Preset was not applied.");
            Descendants((DependencyObject)settings.Content).OfType<Button>().Single(button=>Equals(button.Content,"恢复默认配色")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.Appearance.RailBackground is not null||config.Appearance.AppAccent is not null||config.Appearance.WarningPercent!=75)throw new System.Exception("Reset palette retained overrides.");
            Console.WriteLine("PASS: committed color editing, validation, persistence, presets/defaults, dock quota and fixed colors, live app/detail palette, cache refresh and preserved motion/anchor.");
        }
        finally
        {
            rail.Close();typeof(PulseApp).GetProperty("Rail")!.SetValue(app,originalRail);settings.Close();
            var restored=JsonSerializer.Deserialize<AppConfig>(saved,StorageJson.Options)!;
            foreach(var property in typeof(AppConfig).GetProperties().Where(property=>property.CanRead&&property.CanWrite))property.SetValue(config,property.GetValue(restored));
            config.Accounts=originalAccounts;readings.Clear();foreach(var pair in originalReadings)readings[pair.Key]=pair.Value;Ui.ApplyTheme(config);
        }
    }
    private static void CheckColor(string expected,Color actual,string name)
    {if(((SolidColorBrush)Ui.Brush(expected)).Color!=actual)throw new System.Exception(name+": expected "+expected+", got "+actual);}
}
