using System.Windows.Media.Animation;

namespace Pulse.Desktop;

public sealed partial class RailWindow
{
    private readonly Canvas stage=new(){ClipToBounds=true};
    private readonly ScrollViewer expandedHost=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden};
    private readonly Border ringBleed=new();
    private readonly RectangleGeometry expandedClip=new();
    private readonly DispatcherTimer detailDismiss=new(){Interval=TimeSpan.FromMilliseconds(150)};
    private RailChrome chrome=null!;
    private RailMotionValue railMotion=null!;
    private RailMotionValue? detailMotion;
    private Size expandedExtent=new(100,100),slotExtent=new(100,100),closedExtent=new(6,96);
    private Color expandedColor,closedColor;
    private string? ringStructure;
    private Border? detailHost;
    private RailBubble? detailBubble;
    private bool placingDetail;
    private ScrollViewer? detailScroll;
    private readonly ScaleTransform detailScale=new(1,1);
    private readonly TranslateTransform detailSlide=new();
    private Vector detailOffset;
    private bool detailClosing;
    private int ringFadeVersion;

    private Border CreateSurface()
    {
        chrome=new RailChrome{IsHitTestVisible=false};
        ringBleed.Child=rings;expandedHost.Content=ringBleed;expandedHost.Clip=expandedClip;stage.Children.Add(chrome);stage.Children.Add(expandedHost);
        railMotion=new RailMotionValue(1,.32,.86,PaintRail);
        detailDismiss.Tick+=(_,_)=>{detailDismiss.Stop();if(CanIdle())CloseDetail(true);};
        // A fixed transparent native window keeps its coordinate system stable while the visible berth morphs.
        return new Border{Background=Brushes.Transparent,Child=stage};
    }

    private void RenderRail()
    {
        var config=app.Engine.Config;
        var horizontal=config.Horizontal||config.DockEdge is "top" or "bottom";
        var accounts=config.Accounts.Where(a=>a.Enabled).ToArray();
        var structure=$"{horizontal}|{config.RingSize}|{config.RingGap}|{config.ShowClock}|"+string.Join("|",accounts.Select(a=>a.Id));
        if(structure!=ringStructure)
        {
            ringStructure=structure;rings.Children.Clear();rings.Orientation=horizontal?Orientation.Horizontal:Orientation.Vertical;
            if(accounts.Length==0)
            {
                var empty=Ui.Label("P",22,Ui.Accent);empty.Margin=new Thickness(6);empty.Cursor=Cursors.Hand;
                empty.MouseLeftButtonUp+=(_,_)=>app.ShowSettings("onboarding");rings.Children.Add(empty);
            }
            foreach(var account in accounts)
            {
                var tile=new RingTile(account,app.Engine.Reading(account.Id),ProviderCatalog.Find(account.ProviderId),config,app.Engine.IsRefreshing,app.Activities.GetValueOrDefault(account.Id)){Margin=ReferenceEquals(account,accounts[^1])?new Thickness(0):horizontal?new Thickness(0,0,30*RailCard.Scale(config)*config.RingGap/12,0):new Thickness(0,0,0,30*RailCard.Scale(config)*config.RingGap/12),Cursor=Cursors.Hand};
                tile.MouseEnter+=(_,_)=>{idle.Stop();detailDismiss.Stop();ShowDetail(account,tile);};
                tile.MouseLeftButtonUp+=async(_,_)=>{if(!dragging)await app.Engine.RefreshAsync();};
                rings.Children.Add(tile);if(detailAccountId==account.Id)detailAnchor=tile;
            }
        }
        foreach(var pair in accounts.Zip(rings.Children.OfType<RingTile>()))
            pair.Second.Update(app.Engine.Reading(pair.First.Id),ProviderCatalog.Find(pair.First.ProviderId),app.Engine.IsRefreshing,app.Activities.GetValueOrDefault(pair.First.Id));
        expandedColor=Ui.Brush(AppearancePalette.RailBackground(config)).Color;
        closedColor=Ui.Brush(AppearancePalette.Dock(config,app.Engine.Readings)).Color;
        UpdateRailGeometry(RailScreen.Resolve(config.PanelDisplay),true);RefreshDetail();
    }
    private void UpdateRailGeometry(System.Windows.Forms.Screen screen,bool queuePlacement)
    {
        var scale=RailScreen.Scale(screen);var area=screen.WorkingArea;
        MaxWidth=Math.Max(100,area.Width/scale-24);MaxHeight=Math.Max(100,area.Height/scale-24);
        var s=RailCard.Scale(app.Engine.Config);var horizontal=rings.Orientation==Orientation.Horizontal;var attached=app.Engine.Config.DockEdge!="free";var flare=attached?32*s:0;
        var paddingX=(horizontal?22:13)*s;var paddingY=(horizontal?10:22)*s;
        var bodyMaxWidth=Math.Max(1,MaxWidth-(horizontal?2*flare:0));var bodyMaxHeight=Math.Max(1,MaxHeight-(horizontal?0:2*flare));
        rings.Measure(new Size(Math.Max(1,bodyMaxWidth-2*paddingX),Math.Max(1,bodyMaxHeight-2*paddingY)));
        expandedExtent=new Size(Math.Min(bodyMaxWidth,Math.Max(horizontal?0:64*s,rings.DesiredSize.Width+2*paddingX)),Math.Min(bodyMaxHeight,rings.DesiredSize.Height+2*paddingY));
        closedExtent=horizontal?new Size(96*s,6*s):new Size(6*s,96*s);
        var slot=new Size(Math.Max(expandedExtent.Width+(horizontal?2*flare:0),closedExtent.Width),Math.Max(expandedExtent.Height+(horizontal?0:2*flare),closedExtent.Height));
        var sizeChanged=slot!=slotExtent;slotExtent=slot;
        Width=stage.Width=surface.Width=slot.Width;Height=stage.Height=surface.Height=slot.Height;
        if(sizeChanged&&queuePlacement)QueuePlacement();
        chrome.Width=slot.Width;chrome.Height=slot.Height;
        var expandedRect=Berth(expandedExtent);
        var bleed=3*s;ringBleed.Padding=new Thickness(bleed);
        expandedHost.Width=Math.Max(1,expandedExtent.Width-2*paddingX+2*bleed);expandedHost.Height=Math.Max(1,expandedExtent.Height-2*paddingY+2*bleed);
        Canvas.SetLeft(expandedHost,expandedRect.X+paddingX-bleed);Canvas.SetTop(expandedHost,expandedRect.Y+paddingY-bleed);
        expandedHost.IsHitTestVisible=!collapsed;PaintRail(railMotion.Value);
    }

    private Rect Berth(Size size)
    {
        var edge=app.Engine.Config.DockEdge;
        var x=edge=="left"?0:edge=="right"?slotExtent.Width-size.Width:(slotExtent.Width-size.Width)/2;
        var y=edge=="top"?0:edge=="bottom"?slotExtent.Height-size.Height:(slotExtent.Height-size.Height)/2;
        return new Rect(x,y,size.Width,size.Height);
    }
    private void PaintRail(double value)
    {
        var p=Math.Clamp(value,0,1);
        var rect=Berth(new Size(closedExtent.Width+(expandedExtent.Width-closedExtent.Width)*p,closedExtent.Height+(expandedExtent.Height-closedExtent.Height)*p));
        byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*p);
        chrome.Update(rect,Color.FromArgb(Mix(closedColor.A,expandedColor.A),Mix(closedColor.R,expandedColor.R),Mix(closedColor.G,expandedColor.G),Mix(closedColor.B,expandedColor.B)),p,app.Engine.Config.DockEdge,RailCard.Scale(app.Engine.Config),app.Engine.Config.Theme=="light");
        var hostX=Canvas.GetLeft(expandedHost);var hostY=Canvas.GetTop(expandedHost);
        expandedClip.Rect=new Rect(rect.X-(double.IsNaN(hostX)?0:hostX),rect.Y-(double.IsNaN(hostY)?0:hostY),rect.Width,rect.Height);
        expandedClip.RadiusX=expandedClip.RadiusY=3+19*p;
    }
    private void SetRailCollapsed(bool value)
    {
        if(collapsed==value)return;collapsed=value;expandedHost.IsHitTestVisible=!value;
        var from=expandedHost.Opacity;var target=value?0d:1d;
        var version=++ringFadeVersion;expandedHost.BeginAnimation(OpacityProperty,null);expandedHost.Opacity=from;
        if(SystemParameters.ClientAreaAnimation)
        {
            var fade=new DoubleAnimation(from,target,TimeSpan.FromSeconds(value?.10:.18)){BeginTime=TimeSpan.FromSeconds(value?0:.12),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.HoldEnd};
            fade.Completed+=(_,_)=>{if(version!=ringFadeVersion||closing)return;expandedHost.BeginAnimation(OpacityProperty,null);expandedHost.Opacity=target;};
            expandedHost.BeginAnimation(OpacityProperty,fade);
        }
        else expandedHost.Opacity=target;
        railMotion.MoveTo(value?0:1);
    }
    private void ResetRailMotion()
    {
        ++ringFadeVersion;railMotion.SnapTo(1);expandedHost.BeginAnimation(OpacityProperty,null);expandedHost.Opacity=1;expandedHost.IsHitTestVisible=true;
    }

    private void OpenDetail(AccountConfig account,FrameworkElement tile)
    {
        if(dragging||closing||fullscreenHidden)return;idle.Stop();detailDismiss.Stop();detailClosing=false;
        detailAccountId=account.Id;detailAnchor=tile;
        if(detail is not null)
        {
            UpdateDetailContent();PlaceDetail();detailMotion?.MoveTo(1);return;
        }
        var popup=new Window{Title=app.AccountName(account.Id),Owner=this,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,Opacity=0,SizeToContent=SizeToContent.WidthAndHeight,MaxHeight=Math.Max(300,SystemParameters.WorkArea.Height-40)};
        var transforms=new TransformGroup();transforms.Children.Add(detailScale);transforms.Children.Add(detailSlide);
        detailHost=new Border{Padding=new Thickness(12*RailCard.Scale(app.Engine.Config)),RenderTransform=transforms,Opacity=0};popup.Content=detailHost;detail=popup;
        detailMotion=new RailMotionValue(0,.28,.84,value=>PaintDetail(popup,value));
        UpdateDetailContent(true);
        popup.MouseEnter+=(_,_)=>{idle.Stop();detailDismiss.Stop();if(detailClosing){detailClosing=false;detailMotion?.MoveTo(1);}};
        popup.MouseLeave+=(_,_)=>UpdateAutoCollapse();
        popup.Closed+=(_,_)=>
        {
            if(!ReferenceEquals(detail,popup))return;
            detailMotion?.Dispose();detailMotion=null;detail=null;detailHost=null;detailBubble=null;detailScroll=null;detailAccountId=null;detailAnchor=null;
            detailReading=null;detailSpend=null;detailOptions=null;detailClosing=false;UpdateAutoCollapse();
        };
        popup.SizeChanged+=(_,_)=>PlaceDetail();
        // Both the native window and its host are invisible until the final monitor/DPI position is known.
        popup.Show();popup.UpdateLayout();PlaceDetail();PaintDetail(popup,0);popup.Opacity=1;detailMotion.MoveTo(1);
    }
    private void UpdateDetailContent(bool force=false)
    {
        if(detail is null||detailHost is null||detailAccountId is null)return;
        var account=app.Engine.Config.Accounts.FirstOrDefault(a=>a.Id==detailAccountId&&a.Enabled);if(account is null){CloseDetail(false);return;}
        var config=app.Engine.Config;var reading=app.Engine.Reading(account.Id);
        var options=$"{account.Id}|{account.ProviderId}|{config.RingSize}|{account.Detailed}|{account.Label}|{account.AccentColor}|{config.SegmentedBars}|{AppearancePalette.Signature(config)}|{config.Language}|{config.Currency}|{config.ManualExchangeRate}|{config.HideProjectNames}|{config.ForecastEnabled}|{app.Engine.Forecast(account.Id)}";
        if(!force&&ReferenceEquals(reading,detailReading)&&ReferenceEquals(app.LocalSpend,detailSpend)&&options==detailOptions)return;
        var sameAccount=detailOptions?.StartsWith(account.Id+"|",StringComparison.Ordinal)==true;var offset=sameAccount?detailScroll?.VerticalOffset??0:0;
        detailReading=reading;detailSpend=app.LocalSpend;detailOptions=options;
        var content=DetailContent(app,account);
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var side=config.DockEdge=="free"?detailBubble?.ArrowSide??"right":config.DockEdge;
        detailBubble??=new RailBubble();
        detailBubble.Scale=RailCard.Scale(config);detailBubble.ArrowSide=side;detailBubble.Fill=RailColors.DetailBackground(config);
        detailBubble.Child=scroll;detailBubble.InvalidateVisual();
        detailScroll=scroll;detailHost.Child=detailBubble;detail.Title=app.AccountName(account.Id);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(ReferenceEquals(detailScroll,scroll)){scroll.ScrollToVerticalOffset(offset);PlaceDetail();}}));
    }
    private void PlaceDetail()
    {
        if(placingDetail||detail is null||detailHost is null||detailBubble is null||detailAnchor is null||!detail.IsLoaded||!detailAnchor.IsLoaded)return;
        placingDetail=true;
        try
        {
            var point=detailAnchor.PointToScreen(new Point(detailAnchor.ActualWidth/2,detailAnchor.ActualHeight/2));
            var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));var area=screen.WorkingArea;var scale=RailScreen.Scale(screen);var ui=RailCard.Scale(app.Engine.Config);
            detail.MaxHeight=Math.Max(100,area.Height/scale-16);detail.MaxWidth=Math.Max(100,area.Width/scale-16);
            var edge=app.Engine.Config.DockEdge;if(edge=="free")edge=point.X-detail.ActualWidth*scale>=area.Left?"right":"left";
            if(detailBubble.ArrowSide!=edge){detailBubble.ArrowSide=edge;detail.UpdateLayout();}
            var width=detail.ActualWidth*scale;var height=detail.ActualHeight*scale;var padding=12*ui*scale;var gap=8*ui*scale;
            var origin=PointToScreen(new Point());var visible=chrome.VisibleBounds;
            var railRect=new Rect(origin.X+visible.X*scale,origin.Y+visible.Y*scale,visible.Width*scale,visible.Height*scale);
            var x=edge=="right"?railRect.Left-gap+padding-width:edge=="left"?railRect.Right+gap-padding:point.X-width/2;
            var y=edge=="bottom"?railRect.Top-gap+padding-height:edge=="top"?railRect.Bottom+gap-padding:point.Y-height/2;
            x=Math.Clamp(x,area.Left,Math.Max(area.Left,area.Right-width));y=Math.Clamp(y,area.Top,Math.Max(area.Top,area.Bottom-height));
            RailScreen.Move(detail,new Point(x,y));
            detailBubble.Anchor=(edge is "left" or "right"?(point.Y-y):(point.X-x))/scale-12*ui;
            detailHost.RenderTransformOrigin=edge switch
            {
                "left"=>new Point(padding/Math.Max(1,width),Math.Clamp((point.Y-y)/Math.Max(1,height),0,1)),
                "right"=>new Point(1-padding/Math.Max(1,width),Math.Clamp((point.Y-y)/Math.Max(1,height),0,1)),
                "top"=>new Point(Math.Clamp((point.X-x)/Math.Max(1,width),0,1),padding/Math.Max(1,height)),
                _=>new Point(Math.Clamp((point.X-x)/Math.Max(1,width),0,1),1-padding/Math.Max(1,height))
            };
            detailOffset=edge switch{"left"=>new Vector(-10,0),"right"=>new Vector(10,0),"top"=>new Vector(0,-10),_=>new Vector(0,10)};
            if(detailMotion is not null)PaintDetail(detail,detailMotion.Value);
        }
        finally{placingDetail=false;}
    }
    private void PaintDetail(Window popup,double value)
    {
        if(!ReferenceEquals(detail,popup)||detailHost is null)return;
        detailHost.Opacity=Math.Clamp(value,0,1);detailScale.ScaleX=detailScale.ScaleY=.88+.12*value;
        detailSlide.X=detailOffset.X*(1-value);detailSlide.Y=detailOffset.Y*(1-value);
        if(detailClosing&&detailMotion is {IsRunning:false,Target:0})CloseDetail(false);
    }
    private void CloseDetail(bool animate)
    {
        detailDismiss.Stop();if(detail is null)return;
        if(animate&&SystemParameters.ClientAreaAnimation&&detailMotion is not null){detailClosing=true;detailMotion.MoveTo(0);return;}
        detailClosing=false;detailMotion?.Dispose();detail.Close();
    }
    private void DisposeMotion()
    {
        detailDismiss.Stop();++ringFadeVersion;expandedHost.BeginAnimation(OpacityProperty,null);railMotion.Dispose();CloseDetail(false);
    }
}
