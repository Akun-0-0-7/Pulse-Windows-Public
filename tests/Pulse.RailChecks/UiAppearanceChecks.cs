using System;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using Pulse.Core;
using Pulse.Desktop;

internal static class UiAppearanceChecks
{
    public static void Run()
    {
        // Omitting the config overload, replacing live brushes, ignoring an
        // override or selecting text from only one surface must fail here.
        var apply=typeof(Ui).GetMethod(nameof(Ui.ApplyTheme),[typeof(AppConfig)])
            ??throw new Exception("Ui.ApplyTheme(AppConfig) is missing.");
        var selection=typeof(Ui).GetProperty("Selection",BindingFlags.Public|BindingFlags.Static)
            ??throw new Exception("Ui.Selection is missing.");
        Brush Selection()=>(Brush)(selection.GetValue(null)??throw new Exception("Ui.Selection returned no brush."));
        void Apply(AppConfig config)=>apply.Invoke(null,[config]);

        Ui.ApplyTheme("dark");
        var brushes=new[]{Ui.Bg,Ui.Card,Ui.BorderBrush,Ui.Text,Ui.Muted,Ui.Accent,Selection()};
        var oldSelection=ColorOf(Selection());
        var field=Ui.Field("kept value");
        var label=Ui.Label("kept label");
        var choice=Ui.Select(["dark","light"],"dark",_=>{});
        var primary=Ui.Button("kept button",()=>{},true);

        var custom=Config("dark","#f8FaFc","#FFFFFF","#0055aa");
        Apply(custom);
        ColorEquals("#F8FAFC",Ui.Bg,"Custom application background");
        ColorEquals("#FFFFFF",Ui.Card,"Custom card surface");
        ColorEquals("#0055AA",Ui.Accent,"Custom accent");
        SameBrushes(brushes,Selection());
        if(!ReferenceEquals(field.Background,Ui.Bg)||!ReferenceEquals(label.Foreground,Ui.Text))
            throw new Exception("Existing fields and labels stopped using the live palette.");
        if(field.Text!="kept value"||choice.SelectedValue as string!="dark")
            throw new Exception("Applying a palette changed editor content or selection.");
        if(!ReferenceEquals(choice.Background,Ui.Card)||!ReferenceEquals(choice.BorderBrush,Ui.BorderBrush))
            throw new Exception("ComboBox does not follow the application palette.");
        ContrastAtLeast(choice.Foreground,Ui.Card,4.5,"ComboBox text on its own surface");
        foreach(ComboBoxItem option in choice.Items)
        {
            if(!ReferenceEquals(option.Background,Ui.Card)||!ReferenceEquals(option.BorderBrush,Ui.BorderBrush))
                throw new Exception("ComboBox option does not follow the application palette.");
            ContrastAtLeast(option.Foreground,Ui.Card,4.5,"ComboBox item text on its own surface");
        }
        ContrastAtLeast(Ui.Text,Ui.Bg,7,"Custom light application text");
        ContrastAtLeast(Ui.Text,Ui.Card,7,"Custom light card text");
        ContrastAtLeast(Ui.Muted,Ui.Bg,4.5,"Custom light muted text");
        ContrastAtLeast(primary.Foreground,Ui.Accent,4.5,"Custom accent button text");
        if(ColorOf(Selection())==oldSelection)throw new Exception("Sidebar selection ignored a custom accent.");
        var blueSelection=ColorOf(Selection());
        Apply(Config("dark","#F8FAFC","#FFFFFF","#AA5500"));
        if(ColorOf(Selection())==blueSelection)throw new Exception("Existing sidebar selection did not update with the accent.");
        SameBrushes(brushes,Selection());

        Apply(Config("dark","#000000","#FFFFFF",null));
        ContrastAtLeast(Ui.Text,Ui.Bg,4.5,"Opposite surfaces application text");
        ContrastAtLeast(Ui.Text,Ui.Card,4.5,"Opposite surfaces card text");
        ContrastAtLeast(Ui.Muted,Ui.Bg,4.5,"Opposite surfaces muted application text");
        ContrastAtLeast(Ui.Muted,Ui.Card,4.5,"Opposite surfaces muted card text");

        Apply(Config("dark","#GGGGGG","red","#1234"));
        Defaults(false,Selection(),"Invalid override fallback");
        Apply(new AppConfig{Theme="light"});
        Defaults(true,Selection(),"Missing appearance defaults");
        var legacy=new AppConfig{Theme="dark"};
        var appearance=typeof(AppConfig).GetProperty("Appearance")
            ??throw new Exception("AppConfig.Appearance is missing.");
        appearance.SetValue(legacy,null);
        Apply(legacy);
        Defaults(false,Selection(),"Null legacy appearance");

        Apply(Config("dark","#112233","#223344","#FEDCBA"));
        foreach(var theme in new[]{"light","dark","glass"})
        {
            Ui.ApplyTheme(theme);
            Defaults(theme=="light",Selection(),"String overload "+theme);
            SameBrushes(brushes,Selection());
        }
        Console.WriteLine("PASS: live application palette, control brushes, contrast, fallback and legacy theme compatibility.");
    }

    private static AppConfig Config(string theme,string? background,string? surface,string? accent)
    {
        var config=new AppConfig{Theme=theme};
        var property=typeof(AppConfig).GetProperty("Appearance")
            ??throw new Exception("AppConfig.Appearance is missing.");
        var appearance=Activator.CreateInstance(property.PropertyType)
            ??throw new Exception("Appearance settings could not be created.");
        foreach(var (name,value) in new[]{("AppBackground",background),("AppSurface",surface),("AppAccent",accent)})
        {
            var color=property.PropertyType.GetProperty(name)
                ??throw new Exception("Appearance."+name+" is missing.");
            color.SetValue(appearance,value);
        }
        property.SetValue(config,appearance);
        return config;
    }

    private static void SameBrushes(Brush[] before,Brush selection)
    {
        var after=new[]{Ui.Bg,Ui.Card,Ui.BorderBrush,Ui.Text,Ui.Muted,Ui.Accent,selection};
        for(var i=0;i<before.Length;i++)
            if(!ReferenceEquals(before[i],after[i]))throw new Exception("Palette replaced live brush "+i+".");
    }

    private static void Defaults(bool light,Brush selection,string name)
    {
        ColorEquals(light?"#F5F6FA":"#101318",Ui.Bg,name+" background");
        ColorEquals(light?"#FFFFFF":"#1B2029",Ui.Card,name+" surface");
        ColorEquals(light?"#DADDE8":"#303743",Ui.BorderBrush,name+" border");
        ColorEquals(light?"#202335":"#F1F2F6",Ui.Text,name+" text");
        ColorEquals(light?"#626B80":"#9CA5B7",Ui.Muted,name+" muted text");
        ColorEquals(light?"#6552C9":"#B5ADFF",Ui.Accent,name+" accent");
        ColorEquals("#343146",selection,name+" sidebar selection");
    }

    private static Color ColorOf(Brush brush)=>((SolidColorBrush)brush).Color;
    private static void ColorEquals(string expected,Brush actual,string name)
    {
        var color=ColorOf(actual);
        var hex=$"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if(color.A!=255||hex!=expected)throw new Exception(name+": expected "+expected+", actual "+color+".");
    }
    private static void ContrastAtLeast(Brush foreground,Brush background,double minimum,string name)
    {
        static double Linear(byte channel)=>channel/255d<=.04045?channel/255d/12.92:Math.Pow((channel/255d+.055)/1.055,2.4);
        static double Light(Color value)=>.2126*Linear(value.R)+.7152*Linear(value.G)+.0722*Linear(value.B);
        var a=Light(ColorOf(foreground));var b=Light(ColorOf(background));
        var contrast=(Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
        if(contrast<minimum)throw new Exception(name+": contrast "+contrast.ToString("F2")+" is below "+minimum+".");
    }
}
