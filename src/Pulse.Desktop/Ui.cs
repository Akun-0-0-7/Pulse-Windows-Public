namespace Pulse.Desktop;
public static partial class Ui
{
    private static readonly SolidColorBrush background=new(),card=new(),border=new(),text=new(),muted=new(),accent=new(),selection=new(),accentText=new();
    static Ui()=>ApplyTheme("dark");
    public static Brush Bg=>background; public static Brush Card=>card; public static Brush BorderBrush=>border; public static Brush Text=>text; public static Brush Muted=>muted; public static Brush Accent=>accent; public static Brush Selection=>selection;
    public static void ApplyTheme(string theme)=>ApplyTheme(new AppConfig{Theme=theme});
    public static void ApplyTheme(AppConfig config)
    {
        var light=config.Theme=="light";
        var customSurfaces=AppearancePalette.TryNormalizeHex(config.Appearance?.AppBackground,out _)||AppearancePalette.TryNormalizeHex(config.Appearance?.AppSurface,out _);
        var customAccent=AppearancePalette.TryNormalizeHex(config.Appearance?.AppAccent,out _);
        background.Color=Brush(AppearancePalette.AppBackground(config)).Color;
        card.Color=Brush(AppearancePalette.AppSurface(config)).Color;
        accent.Color=Brush(AppearancePalette.AppAccent(config)).Color;
        if(customSurfaces)
        {
            // Labels are shared by the page and its cards. Optimize their worst
            // contrast across both, including deliberately opposite surfaces.
            text.Color=CommonForeground(background.Color,card.Color,false);
            muted.Color=CommonForeground(background.Color,card.Color,true);
            border.Color=Blend(card.Color,text.Color,.2);
        }
        else
        {
            border.Color=Brush(light?"#DADDE8":"#303743").Color;
            text.Color=Brush(light?"#202335":"#F1F2F6").Color;
            muted.Color=Brush(light?"#626B80":"#9CA5B7").Color;
        }
        selection.Color=customSurfaces||customAccent?SelectionSurface(card.Color,accent.Color,text.Color):Brush("#343146").Color;
        accentText.Color=customSurfaces||customAccent?Brush(AppearancePalette.Foreground(AppearancePalette.AppAccent(config))).Color:background.Color;
        selectorText.Color=Brush(AppearancePalette.Foreground(AppearancePalette.AppSurface(config))).Color;
        selectorHighlight.Color=SelectionSurface(card.Color,accent.Color,selectorText.Color);
    }
    private static Color CommonForeground(Color page,Color surface,bool secondary)
    {
        var best=Colors.Black;var bestContrast=0d;
        var soft=Colors.Black;var softContrast=double.MaxValue;
        for(var level=0;level<=255;level++)
        {
            var candidate=Color.FromRgb((byte)level,(byte)level,(byte)level);
            var contrast=Math.Min(Contrast(candidate,page),Contrast(candidate,surface));
            if(contrast>bestContrast){best=candidate;bestContrast=contrast;}
            if(contrast>=4.5&&contrast<softContrast){soft=candidate;softContrast=contrast;}
        }
        return secondary&&softContrast<double.MaxValue?soft:best;
    }
    private static Color SelectionSurface(Color surface,Color highlight,Color foreground)
    {
        var required=Math.Min(4.5,Contrast(foreground,surface));
        for(var amount=.22;amount>0;amount-=.02)
        {
            var candidate=Blend(surface,highlight,amount);
            if(Contrast(foreground,candidate)>=required)return candidate;
        }
        return surface;
    }
    private static Color Blend(Color from,Color to,double amount)=>Color.FromRgb(
        (byte)Math.Round(from.R+(to.R-from.R)*amount),
        (byte)Math.Round(from.G+(to.G-from.G)*amount),
        (byte)Math.Round(from.B+(to.B-from.B)*amount));
    private static double Contrast(Color first,Color second)
    {
        static double Linear(byte channel)=>channel/255d<=.04045?channel/255d/12.92:Math.Pow((channel/255d+.055)/1.055,2.4);
        static double Light(Color color)=>.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        var a=Light(first);var b=Light(second);
        return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    public static SolidColorBrush Brush(string hex) { try { var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; } catch(FormatException) {return new SolidColorBrush(Colors.SlateBlue);} }
    public static TextBlock Label(string text,double size=14,Brush? color=null) => new() {Text=Localization.Translate(text),FontSize=size,Foreground=color??Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
    public static TextBlock Title(string text)=>new() {Text=Localization.Translate(text),FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=Text,Margin=new Thickness(0,0,0,18)};
    public static Button Button(string text,Action action,bool primary=false)
    {
        var button=new Button {Content=Localization.Translate(text),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,8,8),Background=primary?Accent:Card,Foreground=primary?accentText:Text,BorderBrush=BorderBrush,Cursor=Cursors.Hand}; button.Click+=(_,_)=>action(); return button;
    }
    public static CheckBox Check(string text,bool value,Action<bool> changed) {var box=new CheckBox {Content=Localization.Translate(text),IsChecked=value,Foreground=Text,Margin=new Thickness(0,5,0,12),FontSize=14}; box.Checked+=(_,_)=>changed(true); box.Unchecked+=(_,_)=>changed(false); return box;}
    public static TextBox Field(string value,Action<string>? changed=null) {var box=new TextBox {Text=value,Background=Bg,Foreground=Text,BorderBrush=BorderBrush,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,12),FontSize=14}; if(changed is not null) box.TextChanged+=(_,_)=>changed(box.Text); return box;}
    public static string Option(string value)=>Localization.Translate(value switch {"desktop"=>"桌面会话","desktopApp"=>"桌面客户端","status-line"=>"Claude 状态栏","desktop-cache"=>"桌面用量缓存","number"=>"数字","ring"=>"圆环","dual"=>"5 小时与每周并排","automatic"=>"自动选择","endpoint"=>"直接读取服务","tooling"=>"使用本机工具","dark"=>"深色","light"=>"浅色","glass"=>"透明表面","left"=>"左侧","right"=>"右侧","top"=>"顶部","bottom"=>"底部","free"=>"自由悬浮","system"=>"跟随系统","custom"=>"自定义代理","none"=>"不使用代理","sinceTopUp"=>"按已观察充值额度","balanceOnly"=>"只显示余额","budget"=>"按我的预算","day"=>"日报","week"=>"周报","month"=>"月报","year"=>"年报","zh-CN"=>"简体中文","zh-Hant"=>"繁體中文","en"=>"English","ja"=>"日本語","ko"=>"한국어","ru"=>"Русский",_=>value});
    public static Border Box(UIElement child)=>new(){Child=child,Background=Card,BorderBrush=BorderBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(18),Margin=new Thickness(0,0,0,16)};
    public static StackPanel Row(params UIElement[] items) {var panel=new StackPanel {Orientation=Orientation.Horizontal}; foreach(var item in items) panel.Children.Add(item); return panel;}
    public static string WindowName(UsageWindow window)=>Localization.Translate(window.Label??(window.Kind switch {"fiveHour"=>"5 小时额度","weekly"=>"每周额度","daily"=>"每日额度","monthly"=>"每月额度","balance"=>"余额",_=>window.Scope??window.Id}));
    public static string ResetText(DateTimeOffset? time) { if(time is not {} at) return ""; var left=at-DateTimeOffset.Now; return left<=TimeSpan.Zero?Localization.Translate("等待服务商更新"):left.TotalDays>=1?Localization.Format("{0} 天 {1} 小时后重置",(int)left.TotalDays,left.Hours):left.TotalHours>=1?Localization.Format("{0} 小时 {1} 分钟后重置",(int)left.TotalHours,left.Minutes):Localization.Format("{0} 分钟后重置",Math.Max(1,(int)left.TotalMinutes)); }
    public static string Compact(long? number)
    {
        if (number is null) return "—";
        var value = number.Value;
        var culture = Localization.Culture;
        string Scaled(long divisor,string unit) => (value/(double)divisor).ToString("0.##",culture)+unit;
        return Localization.Language switch
        {
            "en" => value>=1_000_000_000?Scaled(1_000_000_000,"B"):value>=1_000_000?Scaled(1_000_000,"M"):value>=1_000?Scaled(1_000,"K"):value.ToString("N0",culture),
            "ru" => value>=1_000_000_000?Scaled(1_000_000_000," млрд"):value>=1_000_000?Scaled(1_000_000," млн"):value>=1_000?Scaled(1_000," тыс."):value.ToString("N0",culture),
            "ja" => value>=1_000_000_000_000?Scaled(1_000_000_000_000,"兆"):value>=100_000_000?Scaled(100_000_000,"億"):value>=10_000?Scaled(10_000,"万"):value.ToString("N0",culture),
            "ko" => value>=1_000_000_000_000?Scaled(1_000_000_000_000,"조"):value>=100_000_000?Scaled(100_000_000,"억"):value>=10_000?Scaled(10_000,"만"):value.ToString("N0",culture),
            "zh-Hant" => value>=1_000_000_000_000?Scaled(1_000_000_000_000,"兆"):value>=100_000_000?Scaled(100_000_000,"億"):value>=10_000?Scaled(10_000,"萬"):value.ToString("N0",culture),
            _ => value>=1_000_000_000_000?Scaled(1_000_000_000_000,"万亿"):value>=100_000_000?Scaled(100_000_000,"亿"):value>=10_000?Scaled(10_000,"万"):value.ToString("N0",culture)
        };
    }
}
