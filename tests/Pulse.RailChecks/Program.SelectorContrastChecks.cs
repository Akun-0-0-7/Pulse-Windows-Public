using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void SelectorContrastChecks(PulseApp app)
    {
        var callbackCount=0;
        string? callbackValue=null;
        var choice=Ui.Select(["first-choice","second-choice"],"first-choice",value=>{callbackCount++;callbackValue=value;});
        choice.Width=320;
        choice.HorizontalAlignment=HorizontalAlignment.Left;
        var panel=new StackPanel{Margin=new Thickness(12)};
        panel.Children.Add(choice);
        var window=new Window{Content=panel,Width=370,Height=180,ShowInTaskbar=false,ShowActivated=false,Opacity=.01,WindowStartupLocation=WindowStartupLocation.Manual,Left=20,Top=20};
        try
        {
            window.Show();
            foreach(var (name,config) in new (string,AppConfig)[]{
                ("dark",new(){Theme="dark"}),
                ("light",new(){Theme="light"}),
                ("light card in dark page",new(){Theme="dark",Appearance=new(){AppBackground="#101318",AppSurface="#FFFFFF",AppAccent="#3366CC"}}),
                ("dark card in light page",new(){Theme="light",Appearance=new(){AppBackground="#FFFFFF",AppSurface="#101318",AppAccent="#AA5500"}})
            })
            {
                // Reuse the same controls to catch templates that take a brush snapshot.
                Ui.ApplyTheme(config);window.Background=Ui.Bg;
                choice.IsDropDownOpen=false;
                choice.ApplyTemplate();window.UpdateLayout();RunFor(40);
                SelectorAssertTextSurface(choice,"first-choice",name+" closed");
                SelectorAssertArrow(choice,name+" enabled arrow");
                choice.IsDropDownOpen=true;RunFor(40);window.UpdateLayout();
                var popup=SelectorVisuals(choice).OfType<Popup>().FirstOrDefault(item=>item.IsOpen)
                    ??choice.Template.FindName("PART_Popup",choice) as Popup;
                if(popup?.IsOpen!=true||popup.Child is not FrameworkElement popupContent)
                    throw new Exception(name+": the real selector popup did not open.");
                popupContent.UpdateLayout();
                var first=(ComboBoxItem)choice.Items[0];var second=(ComboBoxItem)choice.Items[1];
                first.ApplyTemplate();second.ApplyTemplate();first.UpdateLayout();second.UpdateLayout();
                if(!first.IsSelected)throw new Exception(name+": the expected selected popup item was lost.");
                SelectorAssertTextSurface(first,"first-choice",name+" expanded selected item");
                SelectorAssertTextSurface(second,"second-choice",name+" expanded normal item");
                var highlightedKey=typeof(ComboBoxItem).GetField("IsHighlightedPropertyKey",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey
                    ??throw new Exception("Cannot exercise the WPF selector highlight state.");
                var wasHighlighted=second.IsHighlighted;
                try
                {
                    second.SetValue(highlightedKey,true);second.UpdateLayout();RunFor(20);
                    if(!second.IsHighlighted)throw new Exception(name+": popup highlight fixture was not applied.");
                    SelectorAssertTextSurface(second,"second-choice",name+" expanded highlighted item");
                }
                finally{second.SetValue(highlightedKey,wasHighlighted);}
                choice.IsDropDownOpen=false;
                choice.IsEnabled=false;choice.UpdateLayout();RunFor(20);
                SelectorAssertTextSurface(choice,"first-choice",name+" disabled text");
                choice.IsEnabled=true;
                if(callbackCount!=0||choice.SelectedValue as string!="first-choice"||(string?)first.Tag!="first-choice"||(string?)second.Tag!="second-choice")
                    throw new Exception(name+": changing palette or opening the popup changed a persistent value or invoked its callback.");
            }
            SelectorKey(choice,Key.Down);RunFor(20);
            if(callbackCount!=1||callbackValue!="second-choice"||choice.SelectedValue as string!="second-choice")
                throw new Exception("Selector Down key did not preserve its stable Tag value and single callback.");
            var toggle=SelectorVisuals(choice).OfType<ToggleButton>().Single();
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty,true);RunFor(20);
            if(!choice.IsDropDownOpen)throw new Exception("Selector toggle did not open the popup through its two-way binding.");
            SelectorKey(choice,Key.Escape);RunFor(20);
            if(choice.IsDropDownOpen||toggle.IsChecked==true||callbackCount!=1||choice.SelectedValue as string!="second-choice")
                throw new Exception("Selector Escape did not close the popup and reset the toggle without changing its saved value.");
            Console.WriteLine("PASS: actual selector text and arrow contrast, live themes, disabled text, popup selection/highlight, Down/Escape navigation and stable callbacks.");
        }
        finally{choice.IsDropDownOpen=false;window.Close();Ui.ApplyTheme(app.Engine.Config);}
    }

    private static void SelectorAssertTextSurface(FrameworkElement root,string expectedText,string name)
    {
        var text=SelectorVisuals(root).OfType<TextBlock>().FirstOrDefault(block=>block.IsVisible&&block.Text==expectedText)
            ??throw new Exception(name+": no visible text was produced by the applied selector template.");
        var surface=SelectorSurface(root,name);
        var foreground=SelectorPaintColors(text.Foreground);
        if(foreground.Count==0)throw new Exception(name+": actual template text has no opaque foreground.");
        foreach(var ink in foreground)
            foreach(var paper in SelectorPaintColors(surface.Background))
                if(SelectorContrast(ink,paper)<4.5)
                    throw new Exception($"{name}: actual template text {ink} against painted {paper} has contrast {SelectorContrast(ink,paper):F2}, below 4.5.");
    }

    private static Border SelectorSurface(FrameworkElement root,string name)=>SelectorVisuals(root).OfType<Border>()
            .Where(border=>border.IsVisible&&border.ActualWidth>=root.ActualWidth*.75&&border.ActualHeight>=root.ActualHeight*.6&&SelectorPaintColors(border.Background).Count>0)
            .LastOrDefault()
            ??throw new Exception(name+": no painted full-width selector surface was produced by the applied template.");

    private static void SelectorAssertArrow(ComboBox choice,string name)
    {
        var arrow=SelectorVisuals(choice).OfType<System.Windows.Shapes.Path>().SingleOrDefault(path=>path.IsVisible&&path.Fill is not null)
            ??throw new Exception(name+": no visible arrow was produced by the selector template.");
        var foreground=SelectorPaintColors(arrow.Fill);
        if(foreground.Count==0||arrow.Opacity<1)throw new Exception(name+": enabled arrow has no fully visible foreground.");
        foreach(var ink in foreground)
            foreach(var paper in SelectorPaintColors(SelectorSurface(choice,name).Background))
                if(SelectorContrast(ink,paper)<4.5)
                    throw new Exception($"{name}: actual arrow {ink} against painted {paper} has contrast {SelectorContrast(ink,paper):F2}, below 4.5.");
    }

    private static void SelectorKey(ComboBox choice,Key key)
    {
        var source=PresentationSource.FromVisual(choice)??throw new Exception("The selector has no native presentation source for routed key input.");
        choice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,key){RoutedEvent=Keyboard.KeyDownEvent});
    }

    private static IReadOnlyList<Color> SelectorPaintColors(Brush? brush)=>brush switch
    {
        SolidColorBrush solid when solid.Color.A==255&&solid.Opacity>0=>[solid.Color],
        GradientBrush gradient when gradient.Opacity>0=>gradient.GradientStops.Where(stop=>stop.Color.A==255).Select(stop=>stop.Color).ToArray(),
        _=>Array.Empty<Color>()
    };
    private static IEnumerable<DependencyObject> SelectorVisuals(DependencyObject root)
    {
        yield return root;
        if(root is not Visual&&root is not System.Windows.Media.Media3D.Visual3D)yield break;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var child in SelectorVisuals(VisualTreeHelper.GetChild(root,index)))yield return child;
    }
    private static double SelectorContrast(Color first,Color second)
    {
        static double Linear(byte channel)=>channel/255d<=.04045?channel/255d/12.92:Math.Pow((channel/255d+.055)/1.055,2.4);
        static double Light(Color color)=>.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        var a=Light(first);var b=Light(second);return(Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
}
