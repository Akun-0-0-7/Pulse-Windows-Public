using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void ScrollbarContrastChecks(PulseApp app)
    {
        var settings=new SettingsWindow(app){Height=580,ShowInTaskbar=false,ShowActivated=false,Opacity=.01};
        var sidebar=Field<StackPanel>(settings,"sidebar");
        var page=Field<StackPanel>(settings,"page");
        for(var index=0;index<45;index++)sidebar.Children.Add(Ui.Label("synthetic sidebar item "+index));
        page.Children.Clear();
        var wideContent=new Border{Width=1400,Height=20,Background=Ui.Bg};
        var horizontal=new ScrollViewer{Content=wideContent,Width=320,Height=70,HorizontalAlignment=HorizontalAlignment.Left,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var dropdown=Ui.Select(Enumerable.Range(0,30).Select(index=>"synthetic choice "+index),"synthetic choice 0",_=>{});
        dropdown.Width=320;dropdown.MaxDropDownHeight=180;dropdown.HorizontalAlignment=HorizontalAlignment.Left;
        var multiline=Ui.Field(string.Join(Environment.NewLine,Enumerable.Range(0,35).Select(index=>"synthetic text line "+index)));
        multiline.Height=70;multiline.Width=320;multiline.AcceptsReturn=true;multiline.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;multiline.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;multiline.HorizontalAlignment=HorizontalAlignment.Left;
        var table=new DataGrid{Width=320,Height=100,HorizontalAlignment=HorizontalAlignment.Left,AutoGenerateColumns=false,ItemsSource=Enumerable.Range(0,30).ToArray(),CanUserAddRows=false,Background=Ui.Bg,RowBackground=Ui.Bg,Foreground=Ui.Text};
        table.Columns.Add(new DataGridTextColumn{Header="synthetic value",Binding=new Binding("."),Width=120});
        page.Children.Add(horizontal);
        for(var index=0;index<65;index++)page.Children.Add(Ui.Label("synthetic page item "+index));
        var palettes=new (string Name,AppConfig Config)[]{
            ("dark",new(){Theme="dark"}),
            ("light",new(){Theme="light"}),
            ("dark page and light sidebar",new(){Theme="dark",Appearance=new(){AppBackground="#101318",AppSurface="#FFFFFF",AppAccent="#3366CC"}}),
            ("light page and dark sidebar",new(){Theme="light",Appearance=new(){AppBackground="#FFFFFF",AppSurface="#101318",AppAccent="#AA5500"}})
        };
        var bringRequests=new List<string>();
        RequestBringIntoViewEventHandler traceBring=(_,args)=>bringRequests.Add("source="+ScrollbarElement(args.OriginalSource)+", target="+ScrollbarElement(args.TargetObject)+", rect="+args.TargetRect);
        settings.AddHandler(FrameworkElement.RequestBringIntoViewEvent,traceBring,true);
        try
        {
            settings.Show();settings.UpdateLayout();RunFor(50);
            var viewers=ScrollbarVisuals(settings).OfType<ScrollViewer>().ToArray();
            var sideViewer=viewers.Single(viewer=>ReferenceEquals(viewer.Content,sidebar));
            var pageViewer=viewers.Single(viewer=>ReferenceEquals(viewer.Content,page));
            var sideBar=ScrollbarFor(sideViewer,Orientation.Vertical,"settings sidebar");
            var pageBar=ScrollbarFor(pageViewer,Orientation.Vertical,"settings page");
            var horizontalBar=ScrollbarFor(horizontal,Orientation.Horizontal,"horizontal fixture");
            // Isolate explicit viewport preservation from legal BringIntoView
            // requests made while editable controls first initialize.
            foreach(var (name,config) in palettes)
            {
                sideViewer.ScrollToVerticalOffset(40);pageViewer.ScrollToVerticalOffset(60);RunFor(20);
                string State()=>ScrollbarOffsets(sideViewer,pageViewer)+", focus="+ScrollbarElement(Keyboard.FocusedElement)+", logicalFocus="+ScrollbarElement(FocusManager.GetFocusedElement(settings))
                    +", dropdownFocus="+dropdown.IsKeyboardFocusWithin+", textFocus="+multiline.IsKeyboardFocusWithin+", gridFocus="+table.IsKeyboardFocusWithin+", activeWindow="+settings.IsActive;
                bringRequests.Clear();
                var controlBefore=State();var controlSide=sideViewer.VerticalOffset;var controlPage=pageViewer.VerticalOffset;
                settings.UpdateLayout();RunFor(30);
                var controlAfter=State();
                if(Math.Abs(sideViewer.VerticalOffset-controlSide)>.01||Math.Abs(pageViewer.VerticalOffset-controlPage)>.01)
                    throw new Exception(name+": no-theme layout control reset scroll position. before=["+controlBefore+"], after=["+controlAfter+"], bringIntoView=["+string.Join(" | ",bringRequests.Take(8))+"]");
                var sideOffset=sideViewer.VerticalOffset;var pageOffset=pageViewer.VerticalOffset;
                bringRequests.Clear();var themeBefore=State();
                Ui.ApplyTheme(config);settings.UpdateLayout();RunFor(30);
                if(!ReferenceEquals(sideBar,ScrollbarFor(sideViewer,Orientation.Vertical,name+" sidebar"))||!ReferenceEquals(pageBar,ScrollbarFor(pageViewer,Orientation.Vertical,name+" page")))
                    throw new Exception(name+": applying the palette replaced the existing scrollbar controls.");
                if(Math.Abs(sideViewer.VerticalOffset-sideOffset)>.01||Math.Abs(pageViewer.VerticalOffset-pageOffset)>.01)
                    throw new Exception(name+": applying the palette reset a scroll position. before=["+themeBefore+"], after=["+State()+"], noThemeBefore=["+controlBefore+"], noThemeAfter=["+controlAfter+"], bringIntoView=["+string.Join(" | ",bringRequests.Take(8))+"]");
                ScrollbarAssertPalette(sideBar,((SolidColorBrush)Ui.Card).Color,name+" sidebar");
                ScrollbarAssertPalette(pageBar,((SolidColorBrush)Ui.Bg).Color,name+" page");
                ScrollbarAssertPalette(horizontalBar,((SolidColorBrush)Ui.Bg).Color,name+" horizontal");
                ScrollbarAssertStates(sideBar,((SolidColorBrush)Ui.Card).Color,name+" sidebar states");
                ScrollbarAssertStates(pageBar,((SolidColorBrush)Ui.Bg).Color,name+" page states");
            }
            ScrollbarAssertCommands(sideViewer,sideBar,false);
            ScrollbarAssertCommands(pageViewer,pageBar,false);
            ScrollbarAssertCommands(horizontal,horizontalBar,true);
            ScrollbarAssertDrag(pageViewer,pageBar,false);
            ScrollbarAssertDrag(horizontal,horizontalBar,true);
            pageViewer.ScrollToVerticalOffset(0);RunFor(20);
            pageViewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120){RoutedEvent=UIElement.MouseWheelEvent});RunFor(20);
            var wheelOffset=pageViewer.VerticalOffset;
            if(wheelOffset<=0)throw new Exception("A routed mouse-wheel event did not scroll the real settings viewer.");
            var source=PresentationSource.FromVisual(pageViewer)??throw new Exception("Settings viewer has no native key event source.");
            pageViewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.PageDown){RoutedEvent=Keyboard.KeyDownEvent});RunFor(20);
            if(pageViewer.VerticalOffset<=wheelOffset)throw new Exception("A routed PageDown key did not scroll the real settings viewer.");

            // Now attach the actual input controls. They may legitimately ask
            // to reveal a caret/current cell; this phase checks inheritance and
            // existing chrome colors rather than asserting an unrelated offset.
            page.Children.Insert(0,table);page.Children.Insert(0,multiline);page.Children.Insert(0,dropdown);
            pageViewer.ScrollToVerticalOffset(0);settings.UpdateLayout();RunFor(30);
            var textViewer=ScrollbarVisuals(multiline).OfType<ScrollViewer>().FirstOrDefault()
                ??throw new Exception("The real multiline TextBox template has no ScrollViewer.");
            var tableViewer=ScrollbarVisuals(table).OfType<ScrollViewer>().FirstOrDefault()
                ??throw new Exception("The real DataGrid template has no ScrollViewer.");
            var textBar=ScrollbarFor(textViewer,Orientation.Vertical,"attached TextBox");
            var tableBar=ScrollbarFor(tableViewer,Orientation.Vertical,"attached DataGrid");
            ScrollBar? previousPopupBar=null;
            foreach(var (name,config) in palettes)
            {
                Ui.ApplyTheme(config);settings.UpdateLayout();RunFor(30);
                pageViewer.ScrollToVerticalOffset(0);RunFor(20);
                if(!ReferenceEquals(textBar,ScrollbarFor(textViewer,Orientation.Vertical,name+" TextBox"))||!ReferenceEquals(tableBar,ScrollbarFor(tableViewer,Orientation.Vertical,name+" DataGrid")))
                    throw new Exception(name+": applying the palette replaced an existing nested input scrollbar.");
                ScrollbarAssertPalette(textBar,((SolidColorBrush)Ui.Bg).Color,name+" inherited TextBox");
                ScrollbarAssertPalette(tableBar,((SolidColorBrush)Ui.Bg).Color,name+" inherited DataGrid");
                dropdown.IsDropDownOpen=true;RunFor(30);
                try
                {
                    var popup=ScrollbarVisuals(dropdown).OfType<Popup>().FirstOrDefault(candidate=>candidate.IsOpen)
                        ??dropdown.Template.FindName("PART_Popup",dropdown) as Popup;
                    if(popup?.IsOpen!=true||popup.Child is not FrameworkElement popupContent)throw new Exception("The real settings dropdown popup did not open.");
                    popupContent.UpdateLayout();
                    var popupViewer=ScrollbarVisuals(popupContent).OfType<ScrollViewer>().FirstOrDefault()
                        ??throw new Exception("The real settings dropdown popup has no ScrollViewer.");
                    var popupBar=ScrollbarFor(popupViewer,Orientation.Vertical,name+" popup");
                    if(previousPopupBar is not null&&!ReferenceEquals(previousPopupBar,popupBar))throw new Exception(name+": palette application recreated the existing popup scrollbar.");
                    previousPopupBar=popupBar;
                    ScrollbarAssertPalette(popupBar,((SolidColorBrush)Ui.Card).Color,name+" inherited dropdown popup");
                }
                finally{dropdown.IsDropDownOpen=false;}
            }
            wideContent.Width=80;horizontal.UpdateLayout();RunFor(20);
            if(horizontal.ComputedHorizontalScrollBarVisibility!=Visibility.Collapsed)
                throw new Exception("Auto scrollbar remained visible after the horizontal content stopped overflowing.");
            Console.WriteLine("PASS: live settings scrollbar tracks, thumbs, arrows and hover/drag colors; popup, TextBox and DataGrid inheritance; native commands, drag completion, wheel, PageDown and horizontal auto visibility.");
        }
        finally{settings.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent,traceBring);settings.Close();Ui.ApplyTheme(app.Engine.Config);}
    }

    private static ScrollBar ScrollbarFor(ScrollViewer viewer,Orientation orientation,string name)
    {
        viewer.ApplyTemplate();viewer.UpdateLayout();
        var bar=ScrollbarVisuals(viewer).OfType<ScrollBar>().FirstOrDefault(candidate=>ReferenceEquals(candidate.TemplatedParent,viewer)&&candidate.Orientation==orientation&&candidate.IsVisible&&candidate.ActualWidth>0&&candidate.ActualHeight>0)
            ??throw new Exception(name+": no visible overflow scrollbar was generated by the real ScrollViewer template.");
        bar.ApplyTemplate();bar.UpdateLayout();return bar;
    }

    private static void ScrollbarAssertPalette(ScrollBar bar,Color surface,string name)
    {
        // Inspect actual template painting, including Grid backgrounds. Merely
        // assigning ScrollBar.Background leaves default theme chrome untouched.
        foreach(var element in ScrollbarVisuals(bar).OfType<FrameworkElement>().Where(element=>element.IsVisible&&element.ActualWidth>=bar.ActualWidth*.75&&element.ActualHeight>=bar.ActualHeight*.75))
            foreach(var paint in ScrollbarPaint(ScrollbarBackground(element),surface))
                if(paint!=surface)throw new Exception($"{name}: actual track paints {paint} over expected surface {surface}.");
        var track=ScrollbarVisuals(bar).OfType<Track>().SingleOrDefault()
            ??throw new Exception(name+": the applied scrollbar has no functional Track.");
        var thumb=track.Thumb??throw new Exception(name+": the scrollbar Track has no thumb.");
        thumb.ApplyTemplate();thumb.UpdateLayout();
        var thumbPaint=ScrollbarVisuals(thumb).OfType<FrameworkElement>().Where(element=>element.IsVisible&&element.ActualWidth>0&&element.ActualHeight>0)
            .SelectMany(element=>ScrollbarPaint(ScrollbarBackground(element),surface)).Where(color=>color!=surface).ToArray();
        if(thumbPaint.Length==0)throw new Exception(name+": the actual thumb has no visible painted surface.");
        foreach(var paint in thumbPaint)
            if(ScrollbarContrast(paint,surface)<3)throw new Exception($"{name}: actual thumb {paint} has contrast {ScrollbarContrast(paint,surface):F2} against {surface}, below 3.");
        var arrows=ScrollbarVisuals(bar).OfType<System.Windows.Shapes.Path>().Where(path=>path.IsVisible&&path.Fill is not null&&path.Data is not null&&path.Data.Bounds.Width<=12&&path.Data.Bounds.Height<=12).ToArray();
        if(arrows.Length<2)throw new Exception(name+": visible scrollbar arrow glyphs are missing from the applied template.");
        foreach(var arrow in arrows)
            foreach(var paint in ScrollbarPaint(arrow.Fill,surface))
                if(ScrollbarContrast(paint,surface)<3)throw new Exception($"{name}: actual arrow {paint} has contrast {ScrollbarContrast(paint,surface):F2} against {surface}, below 3.");
    }

    private static void ScrollbarAssertCommands(ScrollViewer viewer,ScrollBar bar,bool horizontal)
    {
        if(horizontal)viewer.ScrollToHorizontalOffset(0);else viewer.ScrollToVerticalOffset(0);
        viewer.UpdateLayout();RunFor(20);
        var line=horizontal?ScrollBar.LineRightCommand:ScrollBar.LineDownCommand;
        var page=horizontal?ScrollBar.PageRightCommand:ScrollBar.PageDownCommand;
        if(!line.CanExecute(null,bar)||!page.CanExecute(null,bar))throw new Exception("Native scrollbar line/page commands cannot execute.");
        line.Execute(null,bar);RunFor(20);
        var lineOffset=horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
        if(lineOffset<=0)throw new Exception("Native scrollbar line command did not move its owning ScrollViewer.");
        page.Execute(null,bar);RunFor(20);
        var pageOffset=horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
        if(pageOffset<=lineOffset)throw new Exception("Native scrollbar page command did not move its owning ScrollViewer further.");
        if(Math.Abs(bar.Value-pageOffset)>.01)throw new Exception("Scrollbar range value stopped matching the actual viewer offset.");
    }

    private static void ScrollbarAssertStates(ScrollBar bar,Color surface,string name)
    {
        var thumb=ScrollbarVisuals(bar).OfType<Track>().Single().Thumb??throw new Exception(name+": native scrollbar thumb is missing.");
        var normal=ScrollbarThumbColor(thumb,surface,name);
        var priorHover=thumb.IsMouseOver;var priorDragging=thumb.IsDragging;
        try
        {
            SetMouseOver(thumb,true);thumb.UpdateLayout();
            ScrollbarAssertPalette(bar,surface,name+" hovered");
            if(ScrollbarThumbColor(thumb,surface,name)==normal)throw new Exception(name+": actual thumb surface ignored hover state.");
            thumb.SetValue(ScrollbarDraggingKey(),true);thumb.UpdateLayout();
            ScrollbarAssertPalette(bar,surface,name+" dragging");
            if(ScrollbarThumbColor(thumb,surface,name)==normal)throw new Exception(name+": actual thumb surface ignored drag state.");
        }
        finally{thumb.SetValue(ScrollbarDraggingKey(),priorDragging);SetMouseOver(thumb,priorHover);}
    }

    private static void ScrollbarAssertDrag(ScrollViewer viewer,ScrollBar bar,bool horizontal)
    {
        if(horizontal)viewer.ScrollToHorizontalOffset(0);else viewer.ScrollToVerticalOffset(0);
        viewer.UpdateLayout();RunFor(20);
        var thumb=ScrollbarVisuals(bar).OfType<Track>().Single().Thumb??throw new Exception("Native scrollbar thumb is missing for drag input.");
        var priorDragging=thumb.IsDragging;var endScroll=false;
        ScrollEventHandler observe=(_,args)=>endScroll|=args.ScrollEventType==ScrollEventType.EndScroll;
        bar.Scroll+=observe;
        try
        {
            thumb.SetValue(ScrollbarDraggingKey(),true);
            thumb.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
            thumb.RaiseEvent(new DragDeltaEventArgs(horizontal?30:0,horizontal?0:30){RoutedEvent=Thumb.DragDeltaEvent});RunFor(20);
            var moved=horizontal?viewer.HorizontalOffset:viewer.VerticalOffset;
            if(moved<=0||Math.Abs(bar.Value-moved)>.01)throw new Exception("A standard Thumb.DragDelta event did not move the owning ScrollViewer through its native range binding.");
            thumb.SetValue(ScrollbarDraggingKey(),false);
            thumb.RaiseEvent(new DragCompletedEventArgs(horizontal?30:0,horizontal?0:30,false){RoutedEvent=Thumb.DragCompletedEvent});RunFor(20);
            if(!endScroll||thumb.IsDragging)throw new Exception("Thumb drag completion did not finish the native scroll gesture.");
        }
        finally{bar.Scroll-=observe;thumb.SetValue(ScrollbarDraggingKey(),priorDragging);}
    }

    private static DependencyPropertyKey ScrollbarDraggingKey()=>typeof(Thumb).GetField("IsDraggingPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey
        ??throw new Exception("Cannot exercise the standard WPF thumb drag state.");
    private static Color ScrollbarThumbColor(Thumb thumb,Color surface,string name)
    {
        var colors=ScrollbarVisuals(thumb).OfType<FrameworkElement>()
            .Where(element=>element.IsVisible&&element.ActualWidth>0&&element.ActualHeight>0)
            .SelectMany(element=>ScrollbarPaint(ScrollbarBackground(element),surface)).Where(color=>color!=surface).ToArray();
        return colors.Length>0?colors[^1]:throw new Exception(name+": actual thumb has no state color.");
    }

    private static string ScrollbarElement(object? element)=>element is FrameworkElement framework?framework.GetType().Name+"#"+framework.Name:element?.GetType().Name??"none";
    private static string ScrollbarOffsets(ScrollViewer sidebar,ScrollViewer page)=>
        $"side(offset={sidebar.VerticalOffset:F2}, viewport={sidebar.ViewportHeight:F2}, extent={sidebar.ExtentHeight:F2}, size={sidebar.ActualWidth:F2}x{sidebar.ActualHeight:F2}, measure={sidebar.IsMeasureValid}, arrange={sidebar.IsArrangeValid}), "
        +$"page(offset={page.VerticalOffset:F2}, viewport={page.ViewportHeight:F2}, extent={page.ExtentHeight:F2}, size={page.ActualWidth:F2}x{page.ActualHeight:F2}, measure={page.IsMeasureValid}, arrange={page.IsArrangeValid})";

    private static Brush? ScrollbarBackground(FrameworkElement element)=>element switch{Border border=>border.Background,Panel panel=>panel.Background,_=>null};
    private static IEnumerable<Color> ScrollbarPaint(Brush? brush,Color surface)
    {
        IEnumerable<Color> colors=brush switch{SolidColorBrush solid=>[solid.Color],GradientBrush gradient=>gradient.GradientStops.Select(stop=>stop.Color),_=>Array.Empty<Color>()};
        foreach(var color in colors)
        {
            var amount=color.A/255d*(brush?.Opacity??1);
            byte Mix(byte back,byte front)=>(byte)Math.Round(back+(front-back)*amount);
            yield return Color.FromRgb(Mix(surface.R,color.R),Mix(surface.G,color.G),Mix(surface.B,color.B));
        }
    }
    private static IEnumerable<DependencyObject> ScrollbarVisuals(DependencyObject root)
    {
        yield return root;
        if(root is not Visual&&root is not System.Windows.Media.Media3D.Visual3D)yield break;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var child in ScrollbarVisuals(VisualTreeHelper.GetChild(root,index)))yield return child;
    }
    private static double ScrollbarContrast(Color first,Color second)
    {
        static double Linear(byte channel)=>channel/255d<=.04045?channel/255d/12.92:Math.Pow((channel/255d+.055)/1.055,2.4);
        static double Light(Color color)=>.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        var a=Light(first);var b=Light(second);return(Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
}
