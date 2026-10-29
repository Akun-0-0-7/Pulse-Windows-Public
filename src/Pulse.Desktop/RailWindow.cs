using System.Windows.Interop;
using System.Windows.Shapes;
namespace Pulse.Desktop;
public sealed partial class RailWindow : Window
{
    private readonly PulseApp app; private readonly StackPanel rings=new(); private readonly Border surface; private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(320)}; private readonly DispatcherTimer screenTick=new(){Interval=TimeSpan.FromSeconds(1)};
    private bool collapsed; private bool dragging; private bool fullscreenHidden; private bool closing; private Point press; private Window? detail; private bool hotkeyRegistered; private string? detailAccountId; private bool placementQueued; private FrameworkElement? detailAnchor;private UsageReading? detailReading;private SpendReport? detailSpend;private string? detailOptions;
    private readonly List<string> shortcutErrors=[];
    public IReadOnlyList<string> ShortcutErrors=>shortcutErrors;
    private readonly DispatcherTimer animation=new(){Interval=TimeSpan.FromMilliseconds(120)};
    public RailWindow(PulseApp app)
    {
        this.app=app; Title=Localization.Translate("Pulse · AI 额度"); ShowInTaskbar=false; ShowActivated=false; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ResizeMode=ResizeMode.NoResize; SizeToContent=SizeToContent.Manual;
        MaxHeight=SystemParameters.WorkArea.Height-24;MaxWidth=SystemParameters.WorkArea.Width-24;
        surface=CreateSurface(); Content=surface;
        var menu=new ContextMenu(); foreach(var (text,action) in new (string,Action)[]{("用量面板",()=>app.ShowDashboard()),("刷新",()=>_=app.Engine.RefreshAsync()),("设置",()=>app.ShowSettings()),("隐藏浮动栏",app.TogglePanel),("退出",app.Quit)}) {var item=new MenuItem {Header=text}; item.Click+=(_,_)=>action(); menu.Items.Add(item);} surface.ContextMenu=menu;
        surface.MouseEnter+=(_,_)=> {idle.Stop();detailDismiss.Stop();SetRailCollapsed(false);};
        surface.MouseLeave+=(_,_)=>UpdateAutoCollapse();menu.Opened+=(_,_)=>UpdateAutoCollapse();menu.Closed+=(_,_)=>UpdateAutoCollapse();
        idle.Tick+=(_,_)=>{idle.Stop();if(CanIdle()){CloseDetail(true);if(app.Engine.Config.AutoCollapse&&app.Engine.Config.DockEdge!="free")SetRailCollapsed(true);}UpdateAutoCollapse();};
        surface.PreviewMouseLeftButtonDown+=(_,e)=>press=e.GetPosition(this);
        surface.PreviewMouseMove+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed&&!dragging&&(e.GetPosition(this)-press).Length>6){dragging=true;idle.Stop();detailDismiss.Stop();CloseDetail(false);railMotion.SnapTo(collapsed?0:1);try{DragMove();}catch(InvalidOperationException){}finally{dragging=false;RememberPlacement();UpdateAutoCollapse();}}};
        SourceInitialized+=(_,_)=> {var handle=new WindowInteropHelper(this).Handle; var source=HwndSource.FromHwnd(handle);source.AddHook(Hook);ApplySettings();};
        Loaded+=(_,_)=>{ApplySettings();};
        screenTick.Tick+=(_,_)=> {if(!app.Engine.Config.ShowPanel)return;var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle); var hide=app.Engine.Config.HideFullscreen&&WindowsServices.IsFullscreen(screen)&&WindowsServices.GetForegroundWindow()!=new WindowInteropHelper(this).Handle; SetFullscreenHidden(hide); if(app.Engine.Config.FollowDisplay&&!dragging&&detail is null) {var active=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position); if(active.DeviceName!=screen.DeviceName){var config=app.Engine.Config;RememberDisplay(screen);config.PanelDisplay=active.DeviceName;if(config.DisplayPlacements.TryGetValue(active.DeviceName,out var saved))config.DockEdge=saved.Edge;app.Engine.Save();Place(active);}} if(!System.Windows.Forms.Screen.AllScreens.Any(s=>s.DeviceName==app.Engine.Config.PanelDisplay)&&app.Engine.Config.PanelDisplay is not null)Place();UpdateAutoCollapse(); if(app.Engine.Config.ShowClock&&!fullscreenHidden&&railMotion.Value>0)foreach(var child in rings.Children.OfType<FrameworkElement>())child.InvalidateVisual();}; screenTick.Start();
        SizeChanged+=(_,_)=>QueuePlacement();
        DetailCharts.CodexUpdated+=CodexUpdated;
        animation.Tick+=(_,_)=>{if(app.Engine.Config.ShowPanel&&!fullscreenHidden&&railMotion.Value>0)foreach(var tile in rings.Children.OfType<RingTile>().Where(tile=>tile.NeedsAnimation))tile.InvalidateVisual();};animation.Start();
        Closing+=(_,_)=>{closing=true;screenTick.Stop();idle.Stop();animation.Stop();DetailCharts.CodexUpdated-=CodexUpdated;DisposeMotion();var handle=new WindowInteropHelper(this).Handle;WindowsServices.UnregisterHotKey(handle,1);WindowsServices.UnregisterHotKey(handle,2);};
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0312){if(wParam.ToInt32()==1)app.ShowSettings();else if(wParam.ToInt32()==2)app.TogglePanel();handled=true;}
        if(message is 0x007E or 0x02E0)QueuePlacement(); return IntPtr.Zero;
    }
    private void CodexUpdated(string accountId){if(detailAccountId==accountId)RefreshDetail(true);}
    public void ApplySettings()
    {
        if(!IsLoaded)return;var config=app.Engine.Config; if(!config.ShowPanel){idle.Stop();detailDismiss.Stop();CloseDetail(false);} Visibility=config.ShowPanel?Visibility.Visible:Visibility.Hidden;Opacity=config.PanelOpacity;
        var handle=new WindowInteropHelper(this).Handle;WindowsServices.SetWindowDisplayAffinity(handle,config.HideCapture?0x11u:0u);
        if(config.PanelDisplay is {} display&&config.DisplayPlacements.TryGetValue(display,out var saved)&&saved.Edge!=config.DockEdge){config.DisplayPlacements[display]=saved with{Edge=config.DockEdge};app.Storage.SaveConfig(config);}shortcutErrors.Clear();if(hotkeyRegistered){WindowsServices.UnregisterHotKey(handle,1);WindowsServices.UnregisterHotKey(handle,2);} Register(config.SettingsShortcut,1);Register(config.PanelShortcut,2);hotkeyRegistered=true;idle.Stop();detailDismiss.Stop();collapsed=false;ResetRailMotion();Render();Place();SetFullscreenHidden(config.HideFullscreen&&fullscreenHidden);
    }
    private bool CanIdle()=>!closing&&app.Engine.Config.ShowPanel&&!fullscreenHidden&&!IsMouseOver&&detail?.IsMouseOver!=true&&surface.ContextMenu?.IsOpen!=true&&!dragging;
    private void UpdateAutoCollapse()
    {
        var config=app.Engine.Config;var canCollapse=config.AutoCollapse&&config.DockEdge!="free";
        if(!canCollapse&&collapsed)SetRailCollapsed(false);
        if(!CanIdle()||(!canCollapse&&detail is null)||(collapsed&&detail is null)){idle.Stop();detailDismiss.Stop();return;}
        if(detail is not null&&!detailClosing&&!detailDismiss.IsEnabled)detailDismiss.Start();
        if(!idle.IsEnabled)idle.Start();
    }
    private void SetFullscreenHidden(bool hidden)
    {
        fullscreenHidden=hidden;if(hidden){detailDismiss.Stop();CloseDetail(false);railMotion.SnapTo(collapsed?0:1);}Opacity=hidden?0:app.Engine.Config.PanelOpacity;UpdateAutoCollapse();
    }
    private void Register(string? text,int id)
    {
        if(string.IsNullOrWhiteSpace(text))return;try{var parts=text.Split('+',StringSplitOptions.TrimEntries);uint modifiers=0x4000;foreach(var part in parts[..^1])modifiers|=part.ToLowerInvariant() switch{"ctrl"=>2u,"alt"=>1u,"shift"=>4u,"win"=>8u,_=>throw new ArgumentException("Unknown modifier")};if(!Enum.TryParse<Key>(parts[^1],true,out var key)||key==Key.None)throw new ArgumentException("Unknown key");if(!WindowsServices.RegisterHotKey(new WindowInteropHelper(this).Handle,id,modifiers,(uint)KeyInterop.VirtualKeyFromKey(key)))shortcutErrors.Add(Localization.Format("快捷键 {0} 已被占用，未能注册。",text));}catch(ArgumentException){shortcutErrors.Add(Localization.Format("快捷键 {0} 格式无效。",text));}
    }
    public void Render()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(Render);return;}
        RenderRail();
    }
    private void ShowDetail(AccountConfig account,FrameworkElement tile)=>OpenDetail(account,tile);
    private void RefreshDetail(bool force=false)=>UpdateDetailContent(force);
    private void PositionDetail()=>PlaceDetail();
    public static StackPanel DetailContent(PulseApp app,AccountConfig account,bool inSettings=false)=>RailCard.Content(app,account,inSettings);
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
        if(!IsLoaded||dragging)return;
        var config=app.Engine.Config;var screen=target??RailScreen.Resolve(config.PanelDisplay);var area=screen.WorkingArea;var scale=RailScreen.Scale(screen);
        var saved=config.DisplayPlacements.GetValueOrDefault(screen.DeviceName);
        if(config.PanelDisplay!=screen.DeviceName){config.PanelDisplay=screen.DeviceName;if(saved is not null)config.DockEdge=saved.Edge;app.Engine.Save();}
        UpdateRailGeometry(screen,false);UpdateLayout();
        var point=RailPlacement.Physical(new Rect(area.Left,area.Top,area.Width,area.Height),new Size(ActualWidth,ActualHeight),scale,config.DockEdge,saved is null?null:new Point(saved.X,saved.Y));
        point=config.DockEdge switch{"top"=>new Point(point.X,point.Y-6*scale),"bottom"=>new Point(point.X,point.Y+6*scale),"left"=>new Point(point.X-6*scale,point.Y),"right"=>new Point(point.X+6*scale,point.Y),_=>point};
        RailScreen.Move(this,point);
    }
}
public sealed class RingTile : FrameworkElement
{
    private readonly AccountConfig account;
    private readonly AppConfig config;
    private UsageReading? reading;
    private ProviderDefinition? definition;
    private bool fetching,active;
    internal bool NeedsAnimation=>config.AnimatedMarks||fetching||active;
    public RingTile(AccountConfig account,UsageReading? reading,ProviderDefinition? definition,AppConfig config,bool fetching,bool active=false)
    {this.account=account;this.reading=reading;this.definition=definition;this.config=config;this.fetching=fetching;this.active=active;UpdateTooltip();}
    public void Update(UsageReading? value,ProviderDefinition? provider,bool refreshing,bool working)
    {
        reading=value;definition=provider;fetching=refreshing;active=working;UpdateTooltip();InvalidateVisual();
    }
    private void UpdateTooltip()
    {
        var first=UsageEngine.Headline(account,reading);
        if(first is null){ToolTip=Localization.Translate("暂无额度数据");return;}
        string Line(string ring,UsageWindow window,bool remaining,bool secondary=false)
        {
            var known=double.IsFinite(window.UsedPercent)&&window.UsedPercent>=0;
            var percent=secondary&&window.Exhausted?100:window.UsedPercent;
            var value=known?Localization.Format(remaining?"剩余 {0}%":"已用 {0}%",UsageFigures.Percent(percent,remaining)):Localization.Translate("额度未知");
            return Localization.Format("{0}：{1} · {2}",Localization.Translate(ring),window.Label??Ui.WindowName(window),value);
        }
        var lines=new List<string>{Line("外环",first,config.ShowRemaining)};
        if(config.ShowSecondaryRing&&reading?.Windows.Where(w=>w.Id!=first.Id).OrderByDescending(w=>w.UsedPercent).FirstOrDefault() is {} second)
            lines.Add(Line("内环",second,config.SecondaryRingShowRemaining,true));
        ToolTip=string.Join(Environment.NewLine,lines);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var s=RailCard.Scale(config);return new Size(38*s,(58+(config.ShowClock?15:0))*s);
    }
    protected override void OnRender(DrawingContext drawing)
    {
        var s=RailCard.Scale(config);var size=36*s;var inset=config.ShowClock?10*s:0;var center=new Point(19*s,inset+18*s);var radius=18*s;
        var window=UsageEngine.Headline(account,reading);var percent=window?.UsedPercent;var white=RailColors.Foreground(config);
        var brush=RailColors.Quota(config,percent,account.AccentColor,window?.Exhausted==true);
        drawing.DrawEllipse(null,new Pen(RailColors.Track(config),4*s),center,radius,radius);
        if(percent is {} value&&UsageFigures.Percent(value,config.ShowRemaining)>0)Arc(drawing,center,radius,Math.Clamp((config.ShowRemaining?100-value:value)/100,0,1),new Pen(brush,4*s){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round});
        if(config.ShowSecondaryRing&&reading?.Windows.Where(w=>w.Id!=window?.Id).OrderByDescending(w=>w.UsedPercent).FirstOrDefault() is {} second&&double.IsFinite(second.UsedPercent)&&second.UsedPercent>=0)
        {
            var used=second.Exhausted?Math.Max(100,second.UsedPercent):second.UsedPercent;
            var fraction=Math.Clamp(config.SecondaryRingShowRemaining?1-used/100:used/100,0,1);
            if(fraction>0)Arc(drawing,center,radius-5*s,fraction,new Pen(RailColors.Quota(config,second.UsedPercent,account.AccentColor,second.Exhausted),s));
        }
        if(config.ShowClock&&window is {ReportsLength:true,ResetsAt:{} reset,WindowSeconds:>0}&&window.WindowSeconds is {} seconds)Arc(drawing,center,radius+4*s,Math.Clamp(1-(reset-DateTimeOffset.Now).TotalSeconds/seconds,.001,1),new Pen(RailColors.Secondary(config),s));
        var initial=definition?.Name[..Math.Min(2,definition.Name.Length)]??"P";
        if(config.AnimatedMarks){var shape=int.TryParse(account.Extra.GetValueOrDefault("botBody"),out var body)?body:0;var personality=int.TryParse(account.Extra.GetValueOrDefault("botPersonality"),out var mood)?mood:0;BotRenderer.Draw(drawing,center,brush,window?.Exhausted==true?"spent":active?"working":fetching?"fetching":"quiet",Math.Clamp(shape,0,17),Math.Clamp(personality,0,7));}
        else if(!ProviderMark.Draw(drawing,account.ProviderId,new Rect(center.X-9*s,center.Y-9*s,18*s,18*s),white))Text(drawing,initial,new Point(center.X,center.Y-8*s),12*s,white,true);
        var label=percent is {} p?$"{UsageFigures.Percent(p,config.ShowRemaining)}%":reading?.Balance is {} money?RailCard.Balance(money):fetching?"···":"—";
        Text(drawing,label,new Point(center.X,inset+42*s),13*s,reading?.IsStale==true?RailColors.Secondary(config):percent is not null?brush:white,true);
        if(fetching||active){var angle=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/600d;drawing.DrawEllipse(white,null,new Point(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)),2*s,2*s);}
    }
    private void Text(DrawingContext drawing,string text,Point point,double size,Brush brush,bool center){var formatted=new FormattedText(text,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);drawing.DrawText(formatted,new Point(point.X-(center?formatted.Width/2:0),point.Y));}
    private static void Arc(DrawingContext drawing,Point center,double radius,double fraction,Pen pen){if(fraction>=.999){drawing.DrawEllipse(null,pen,center,radius,radius);return;}var angle=fraction*2*Math.PI;var geometry=new StreamGeometry();using(var path=geometry.Open()){path.BeginFigure(new Point(center.X,center.Y-radius),false,false);path.ArcTo(new Point(center.X+radius*Math.Sin(angle),center.Y-radius*Math.Cos(angle)),new Size(radius,radius),0,fraction>.5,SweepDirection.Clockwise,true,false);}drawing.DrawGeometry(null,pen,geometry);}
}
