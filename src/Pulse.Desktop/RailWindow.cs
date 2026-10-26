using System.Windows.Interop;
using System.Windows.Shapes;
namespace Pulse.Desktop;
public sealed class RailWindow : Window
{
    private readonly PulseApp app; private readonly StackPanel rings=new(); private readonly Border surface; private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(320)}; private readonly DispatcherTimer screenTick=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool collapsed; private bool dragging; private bool fullscreenHidden; private bool closing; private Point press; private Window? detail; private bool hotkeyRegistered; private string? detailAccountId; private bool placementQueued; private FrameworkElement? detailAnchor;private UsageReading? detailReading;private SpendReport? detailSpend;private string? detailOptions;
    private readonly List<string> shortcutErrors=[];
    public IReadOnlyList<string> ShortcutErrors=>shortcutErrors;
    private readonly DispatcherTimer animation=new(){Interval=TimeSpan.FromMilliseconds(120)};
    public RailWindow(PulseApp app)
    {
        this.app=app; Title=Localization.Translate("Pulse · AI 额度"); ShowInTaskbar=false; ShowActivated=false; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ResizeMode=ResizeMode.NoResize; SizeToContent=SizeToContent.WidthAndHeight;
        MaxHeight=SystemParameters.WorkArea.Height-24;MaxWidth=SystemParameters.WorkArea.Width-24;
        surface=new Border {Background=Ui.Bg,BorderBrush=Ui.BorderBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(12),Child=new ScrollViewer {Content=rings,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden}}; Content=surface;
        var menu=new ContextMenu(); foreach(var (text,action) in new (string,Action)[]{("用量面板",()=>app.ShowDashboard()),("刷新",()=>_=app.Engine.RefreshAsync()),("设置",()=>app.ShowSettings()),("隐藏浮动栏",app.TogglePanel),("退出",app.Quit)}) {var item=new MenuItem {Header=text}; item.Click+=(_,_)=>action(); menu.Items.Add(item);} surface.ContextMenu=menu;
        surface.MouseEnter+=(_,_)=> {idle.Stop(); if(collapsed){collapsed=false;Render();}};
        surface.MouseLeave+=(_,_)=>UpdateAutoCollapse();menu.Opened+=(_,_)=>UpdateAutoCollapse();menu.Closed+=(_,_)=>UpdateAutoCollapse();
        idle.Tick+=(_,_)=>{idle.Stop();if(CanIdle()){detail?.Close();detail=null;if(app.Engine.Config.AutoCollapse&&app.Engine.Config.DockEdge!="free"){collapsed=true;Render();}}UpdateAutoCollapse();};
        surface.PreviewMouseLeftButtonDown+=(_,e)=>press=e.GetPosition(this);
        surface.PreviewMouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&!dragging&&(e.GetPosition(this)-press).Length>6){dragging=true;idle.Stop();detail?.Close();detail=null;try{DragMove();}catch(InvalidOperationException){}finally{dragging=false;RememberPlacement();UpdateAutoCollapse();}}};
        SourceInitialized+=(_,_)=> {var handle=new WindowInteropHelper(this).Handle; var source=HwndSource.FromHwnd(handle);source.AddHook(Hook);ApplySettings();};
        Loaded+=(_,_)=>{ApplySettings();};
        screenTick.Tick+=(_,_)=> {if(!app.Engine.Config.ShowPanel)return;var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle); var hide=app.Engine.Config.HideFullscreen&&WindowsServices.IsFullscreen(screen)&&WindowsServices.GetForegroundWindow()!=new WindowInteropHelper(this).Handle; SetFullscreenHidden(hide); if(app.Engine.Config.FollowDisplay&&!dragging&&detail is null) {var active=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position); if(active.DeviceName!=screen.DeviceName){var config=app.Engine.Config;RememberDisplay(screen);config.PanelDisplay=active.DeviceName;if(config.DisplayPlacements.TryGetValue(active.DeviceName,out var saved))config.DockEdge=saved.Edge;app.Engine.Save();Place(active);}} if(!System.Windows.Forms.Screen.AllScreens.Any(s=>s.DeviceName==app.Engine.Config.PanelDisplay)&&app.Engine.Config.PanelDisplay is not null)Place();UpdateAutoCollapse(); foreach(var child in rings.Children.OfType<FrameworkElement>())child.InvalidateVisual();}; screenTick.Start();
        SizeChanged+=(_,_)=>QueuePlacement();
        DetailCharts.CodexUpdated+=CodexUpdated;
        animation.Tick+=(_,_)=>{if(app.Engine.Config.ShowPanel&&(app.Engine.Config.AnimatedMarks||app.Engine.IsRefreshing||app.Activities.Values.Any(active=>active)))foreach(var tile in rings.Children.OfType<RingTile>())tile.InvalidateVisual();};animation.Start();
        Closing+=(_,_)=>{closing=true;screenTick.Stop();idle.Stop();animation.Stop();DetailCharts.CodexUpdated-=CodexUpdated;detail?.Close();var handle=new WindowInteropHelper(this).Handle;WindowsServices.UnregisterHotKey(handle,1);WindowsServices.UnregisterHotKey(handle,2);};
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0312){if(wParam.ToInt32()==1)app.ShowSettings();else if(wParam.ToInt32()==2)app.TogglePanel();handled=true;}
        if(message is 0x007E or 0x02E0)QueuePlacement(); return IntPtr.Zero;
    }
    private void CodexUpdated(string accountId){if(detailAccountId==accountId)RefreshDetail(true);}
    public void ApplySettings()
    {
        if(!IsLoaded)return;var config=app.Engine.Config; Visibility=config.ShowPanel?Visibility.Visible:Visibility.Hidden;Opacity=config.PanelOpacity;
        var handle=new WindowInteropHelper(this).Handle;WindowsServices.SetWindowDisplayAffinity(handle,config.HideCapture?0x11u:0u);
        if(config.PanelDisplay is {} display&&config.DisplayPlacements.TryGetValue(display,out var saved)&&saved.Edge!=config.DockEdge){config.DisplayPlacements[display]=saved with{Edge=config.DockEdge};app.Storage.SaveConfig(config);}shortcutErrors.Clear();if(hotkeyRegistered){WindowsServices.UnregisterHotKey(handle,1);WindowsServices.UnregisterHotKey(handle,2);} Register(config.SettingsShortcut,1);Register(config.PanelShortcut,2);hotkeyRegistered=true;idle.Stop();collapsed=false;Render();QueuePlacement();SetFullscreenHidden(config.HideFullscreen&&fullscreenHidden);
    }
    private bool CanIdle()=>!closing&&app.Engine.Config.ShowPanel&&!fullscreenHidden&&!IsMouseOver&&detail?.IsMouseOver!=true&&surface.ContextMenu?.IsOpen!=true&&!dragging;
    private void UpdateAutoCollapse()
    {
        var config=app.Engine.Config;var canCollapse=config.AutoCollapse&&config.DockEdge!="free";
        if(!canCollapse&&collapsed){collapsed=false;Render();}
        if(!CanIdle()||(!canCollapse&&detail is null)||(collapsed&&detail is null)){idle.Stop();return;}
        if(!idle.IsEnabled)idle.Start();
    }
    private void SetFullscreenHidden(bool hidden)
    {
        fullscreenHidden=hidden;if(hidden){detail?.Close();detail=null;}Opacity=hidden?0:app.Engine.Config.PanelOpacity;UpdateAutoCollapse();
    }
    private void Register(string? text,int id)
    {
        if(string.IsNullOrWhiteSpace(text))return;try{var parts=text.Split('+',StringSplitOptions.TrimEntries);uint modifiers=0x4000;foreach(var part in parts[..^1])modifiers|=part.ToLowerInvariant() switch{"ctrl"=>2u,"alt"=>1u,"shift"=>4u,"win"=>8u,_=>throw new ArgumentException("Unknown modifier")};if(!Enum.TryParse<Key>(parts[^1],true,out var key)||key==Key.None)throw new ArgumentException("Unknown key");if(!WindowsServices.RegisterHotKey(new WindowInteropHelper(this).Handle,id,modifiers,(uint)KeyInterop.VirtualKeyFromKey(key)))shortcutErrors.Add(Localization.Format("快捷键 {0} 已被占用，未能注册。",text));}catch(ArgumentException){shortcutErrors.Add(Localization.Format("快捷键 {0} 格式无效。",text));}
    }
    public void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}var config=app.Engine.Config;rings.Children.Clear();rings.Orientation=config.Horizontal||config.DockEdge is "top" or "bottom"?Orientation.Horizontal:Orientation.Vertical;surface.Background=config.Theme=="glass"?Ui.Brush("#A0101824"):Ui.Bg;RefreshDetail();QueuePlacement();
        surface.Padding=collapsed?new Thickness(0):new Thickness(12);surface.CornerRadius=new CornerRadius(collapsed?4:22);
        if(collapsed){rings.Children.Add(new Border {Width=rings.Orientation==Orientation.Horizontal?120:6,Height=rings.Orientation==Orientation.Horizontal?6:120,Background=app.Engine.Readings.Any(r=>r.Windows.Any(w=>w.UsedPercent>=95))?Ui.Brush("#FF786C"):Ui.Accent,CornerRadius=new CornerRadius(4)});return;}
        var accounts=config.Accounts.Where(a=>a.Enabled).ToList();
        if(accounts.Count==0){var empty=Ui.Label("P",22,Ui.Accent);empty.Margin=new Thickness(6);empty.Cursor=Cursors.Hand;empty.MouseLeftButtonUp+=(_,_)=>app.ShowSettings("onboarding");rings.Children.Add(empty);return;}
        foreach(var account in accounts){var reading=app.Engine.Reading(account.Id);var tile=new RingTile(account,reading,ProviderCatalog.Find(account.ProviderId),config,app.Engine.IsRefreshing,app.Activities.GetValueOrDefault(account.Id)){Margin=new Thickness(config.RingGap/2),Cursor=Cursors.Hand};tile.MouseEnter+=(_,_)=>{idle.Stop();ShowDetail(account,tile);};tile.MouseLeftButtonUp+=async(_,_)=>{if(!dragging)await app.Engine.RefreshAsync();};rings.Children.Add(tile);if(detailAccountId==account.Id)detailAnchor=tile;}
    }
    private void ShowDetail(AccountConfig account,FrameworkElement tile)
    {
        if(dragging)return;if(detail is not null&&detailAccountId==account.Id){detailAnchor=tile;PositionDetail();return;}detail?.Close();detailAccountId=account.Id;detailAnchor=tile;var popup=new Window {Title=app.AccountName(account.Id),Owner=this,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,SizeToContent=SizeToContent.WidthAndHeight,MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40)};detail=popup;RefreshDetail(true);popup.MouseEnter+=(_,_)=>idle.Stop();popup.MouseLeave+=(_,_)=>UpdateAutoCollapse();popup.Closed+=(_,_)=>{if(ReferenceEquals(detail,popup)){detail=null;detailAccountId=null;detailAnchor=null;detailReading=null;detailSpend=null;detailOptions=null;UpdateAutoCollapse();}};popup.SizeChanged+=(_,_)=>PositionDetail();popup.Show();PositionDetail();
    }
    private void RefreshDetail(bool force=false)
    {
        if(detail is null||detailAccountId is null)return;var account=app.Engine.Config.Accounts.FirstOrDefault(a=>a.Id==detailAccountId&&a.Enabled);if(account is null){detail.Close();return;}
        var config=app.Engine.Config;var reading=app.Engine.Reading(account.Id);var options=$"{account.Detailed}|{account.Label}|{config.SegmentedBars}|{config.Theme}|{config.Language}|{config.Currency}|{config.ManualExchangeRate}|{config.HideProjectNames}|{config.ForecastEnabled}|{app.Engine.Forecast(account.Id)}";if(!force&&ReferenceEquals(reading,detailReading)&&ReferenceEquals(app.LocalSpend,detailSpend)&&options==detailOptions)return;detailReading=reading;detailSpend=app.LocalSpend;detailOptions=options;var offset=(detail.Content as ScrollViewer)?.VerticalOffset??0;
        var content=Ui.Box(DetailContent(app,account));if(config.Theme=="glass")content.Background=Ui.Brush("#E618202D");content.Margin=new Thickness(0);var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};detail.Content=scroll;Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(detail?.Content==scroll)scroll.ScrollToVerticalOffset(offset);}));
    }
    private void PositionDetail()
    {
        if(detail is null||detailAnchor is null||!detail.IsLoaded||!detailAnchor.IsLoaded)return;var point=detailAnchor.PointToScreen(new Point(0,0));var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));var area=screen.WorkingArea;var scale=RailScreen.Scale(screen);var width=detail.ActualWidth*scale;var height=detail.ActualHeight*scale;var x=point.X-width-12*scale;if(x<area.Left)x=point.X+detailAnchor.ActualWidth*scale+12*scale;x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-width));var y=Math.Clamp(point.Y,area.Top,Math.Max(area.Top,area.Bottom-height-10));RailScreen.Move(detail,new Point(x,y));
    }
    public static StackPanel DetailContent(PulseApp app,AccountConfig account)
    {
        var panel=new StackPanel {Width=310};panel.Children.Add(Ui.Label(app.AccountName(account.Id),19));
        if(!account.Enabled){panel.Children.Add(Ui.Label("账号未启用。勾选“启用这个账号”后读取额度。",13,Ui.Muted));panel.Children.Add(Ui.Button("账号设置",()=>app.ShowSettings("account:"+account.Id)));return panel;}
        var reading=app.Engine.Reading(account.Id);if(reading?.Plan is {} plan)panel.Children.Add(Ui.Label(plan,12,Ui.Muted));
        if(reading is null)panel.Children.Add(Ui.Label("等待首次读取",13,Ui.Muted));else {
            foreach(var window in reading.Windows){panel.Children.Add(Ui.Label($"{Ui.WindowName(window)}  ·  {UsageFigures.Percent(window.UsedPercent)}% 已用",13));panel.Children.Add(UsageBar.Create(window.UsedPercent,app.Engine.Config.SegmentedBars));panel.Children.Add(Ui.Label(Ui.ResetText(window.ResetsAt)+(window.Estimated?" · 按所选基准估算":""),11,Ui.Muted));}
            if(reading.Balance is {} balance)panel.Children.Add(Ui.Label($"余额  {balance.Amount:0.####} {balance.Currency}",16));
            if(reading.CreditBalance is {} credits)panel.Children.Add(Ui.Label("积分  "+credits));
            if(app.Engine.Config.ForecastEnabled&&app.Engine.Forecast(account.Id) is {} forecast)panel.Children.Add(Ui.Label($"按最近消耗速度，约 {forecast.TotalHours:0.#} 小时后用尽",12,Ui.Brush("#FFBA83")));
            foreach(var metric in reading.Metrics)panel.Children.Add(Ui.Label($"{metric.Label}  {metric.Value:0.##} {metric.Unit}"));
            DetailCharts.AddHistory(panel,app,account,reading);
            if(reading.ErrorMessage is {} error)panel.Children.Add(Ui.Label((reading.IsStale?"上次读数 · ":"")+error,12,Ui.Brush("#FFBA83")));
            if(reading.ObservedAt is {} at)panel.Children.Add(Ui.Label($"读取于 {at.ToLocalTime():MM-dd HH:mm:ss} · {reading.Source}",11,Ui.Muted));
        }
        if(account.Detailed&&LocalUsageCard.Create(app,account) is {} local)panel.Children.Add(local);DetailCharts.AddCodex(panel,app,account);panel.Children.Add(Ui.Button("账号设置",()=>app.ShowSettings("account:"+account.Id)));return panel;
    }
    private void RememberPlacement()
    {
        var config=app.Engine.Config;config.PanelX=Left;config.PanelY=Top;var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);var scale=RailScreen.Scale(screen);var area=screen.WorkingArea;var rect=RailScreen.Bounds(this);
        if(config.SnapToEdges){var distances=new Dictionary<string,double>{{"left",Math.Abs(rect.Left-area.Left)/scale},{"right",Math.Abs(rect.Right-area.Right)/scale},{"top",Math.Abs(rect.Top-area.Top)/scale},{"bottom",Math.Abs(rect.Bottom-area.Bottom)/scale}};var nearest=distances.MinBy(p=>p.Value);config.DockEdge=nearest.Value<70?nearest.Key:"free";}else config.DockEdge="free";config.PanelDisplay=screen.DeviceName;config.DisplayPlacements[screen.DeviceName]=new(config.DockEdge,(rect.Left-area.Left)/scale,(rect.Top-area.Top)/scale);app.Save();
    }
    private void RememberDisplay(System.Windows.Forms.Screen screen)
    {
        var config=app.Engine.Config;var area=screen.WorkingArea;var rect=RailScreen.Bounds(this);var scale=RailScreen.Scale(screen);config.DisplayPlacements[screen.DeviceName]=new(config.DockEdge,(rect.Left-area.Left)/scale,(rect.Top-area.Top)/scale);
    }
    private void QueuePlacement()
    {
        if(placementQueued||dragging||!IsLoaded)return;placementQueued=true;Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{placementQueued=false;if(!dragging){Place();PositionDetail();}}));
    }
    private void Place(System.Windows.Forms.Screen? target=null)
    {
        if(!IsLoaded||dragging)return;UpdateLayout();var config=app.Engine.Config;var screen=target??RailScreen.Resolve(config.PanelDisplay);var area=screen.WorkingArea;var scale=RailScreen.Scale(screen);MaxHeight=Math.Max(100,area.Height/scale-24);MaxWidth=Math.Max(100,area.Width/scale-24);var saved=config.DisplayPlacements.GetValueOrDefault(screen.DeviceName);if(config.PanelDisplay!=screen.DeviceName){config.PanelDisplay=screen.DeviceName;if(saved is not null)config.DockEdge=saved.Edge;app.Engine.Save();}var point=RailPlacement.Physical(new Rect(area.Left,area.Top,area.Width,area.Height),new Size(ActualWidth,ActualHeight),scale,config.DockEdge,saved is null?null:new Point(saved.X,saved.Y));RailScreen.Move(this,point);
    }
}
public sealed class RingTile(AccountConfig account,UsageReading? reading,ProviderDefinition? definition,AppConfig config,bool fetching,bool active=false) : FrameworkElement
{
    protected override Size MeasureOverride(Size availableSize)=>new(config.RingSize+18,config.RingSize+28);
    protected override void OnRender(DrawingContext drawing)
    {
        var size=config.RingSize;var center=new Point((size+18)/2,size/2+1);var radius=size/2-4;var window=UsageEngine.Headline(account,reading);var percent=window?.UsedPercent;var brush=account.AccentColor is {} color?Ui.Brush(color):percent>=90?Ui.Brush("#FF786C"):percent>=75?Ui.Brush("#FFCA7A"):Ui.Brush(definition?.Color??"#B5ADFF");
        drawing.DrawEllipse(null,new Pen(Ui.BorderBrush,3),center,radius,radius);
        if(percent is {} value) Arc(drawing,center,radius,Math.Clamp((config.ShowRemaining?100-value:value)/100,.001,1),new Pen(brush,3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round});
        if(config.ShowSecondaryRing&&reading?.Windows.Where(w=>w.Id!=window?.Id).OrderByDescending(w=>w.UsedPercent).FirstOrDefault() is {} second)Arc(drawing,center,radius-5,Math.Clamp(second.UsedPercent/100,.001,1),new Pen(Ui.Muted,1));
        if(config.ShowClock&&window is {ReportsLength:true,ResetsAt:{} reset,WindowSeconds:>0}&&window.WindowSeconds is {} seconds)Arc(drawing,center,radius+4,Math.Clamp(1-(reset-DateTimeOffset.Now).TotalSeconds/seconds,.001,1),new Pen(Ui.Muted,1));
        var initial=definition?.Name switch{"Codex"=>">_","Claude Code"=>"✳","DeepSeek"=>"D",_=>definition?.Name[..Math.Min(2,definition.Name.Length)]??"P"};
        if(config.AnimatedMarks){var shape=int.TryParse(account.Extra.GetValueOrDefault("botBody"),out var body)?body:0;var personality=int.TryParse(account.Extra.GetValueOrDefault("botPersonality"),out var mood)?mood:0;BotRenderer.Draw(drawing,center,brush,window?.Exhausted==true?"spent":active?"working":fetching?"fetching":"quiet",Math.Clamp(shape,0,17),Math.Clamp(personality,0,7));}else Text(drawing,initial,new Point(center.X,center.Y-9),13,brush,true);
        var label=percent is {} p?$"{UsageFigures.Percent(p,config.ShowRemaining)}%":reading?.Balance is {} money?$"{money.Amount:0.##}":fetching?"···":"—";
        Text(drawing,label,new Point(center.X,size+4),12,reading?.IsStale==true?Ui.Muted:Ui.Text,true);
        if(fetching||active){var angle=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/600d;drawing.DrawEllipse(Ui.Accent,null,new Point(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)),2.5,2.5);}
    }
    private void Text(DrawingContext drawing,string text,Point point,double size,Brush brush,bool center){var formatted=new FormattedText(text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);drawing.DrawText(formatted,new Point(point.X-(center?formatted.Width/2:0),point.Y));}
    private static void Arc(DrawingContext drawing,Point center,double radius,double fraction,Pen pen){if(fraction>=.999){drawing.DrawEllipse(null,pen,center,radius,radius);return;}var angle=fraction*2*Math.PI;var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(center.X,center.Y-radius),false,false);path.ArcTo(new Point(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle)),new Size(radius,radius),0,fraction>.5,SweepDirection.Clockwise,true,false);}drawing.DrawGeometry(null,pen,geometry);}
}
