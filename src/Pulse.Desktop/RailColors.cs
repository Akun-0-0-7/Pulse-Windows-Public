namespace Pulse.Desktop;

internal static class RailColors
{
    public static Brush Background(AppConfig config)=>Ui.Brush(AppearancePalette.RailBackground(config));
    public static Brush DetailBackground(AppConfig config)=>Ui.Brush(AppearancePalette.DetailBackground(config));
    private static string Surface(AppConfig config,bool detail,bool inSettings)=>inSettings?AppearancePalette.AppSurface(config):detail?AppearancePalette.DetailBackground(config):AppearancePalette.RailBackground(config);
    public static Brush Foreground(AppConfig config,bool detail=false,bool inSettings=false)=>Ui.Brush(AppearancePalette.Foreground(Surface(config,detail,inSettings)));
    public static Brush Secondary(AppConfig config,bool detail=false,bool inSettings=false)=>Blend(config,detail,inSettings,.45);
    public static Brush Track(AppConfig config,bool detail=false,bool inSettings=false)=>Blend(config,detail,inSettings,.17);
    public static Brush DockTrack(AppConfig config)=>Blend(config,false,false,.32);
    public static Brush Quota(AppConfig config,double? used,string? accountColor=null,bool exhausted=false,bool detail=false,bool inSettings=false)=>
        Ui.Brush(AppearancePalette.Quota(config,used,exhausted,accountColor,AppearancePalette.Foreground(Surface(config,detail,inSettings))));
    private static Brush Blend(AppConfig config,bool detail,bool inSettings,double amount)
    {
        var bg=Ui.Brush(Surface(config,detail,inSettings)).Color;
        var fg=((SolidColorBrush)Foreground(config,detail,inSettings)).Color;
        byte Mix(byte from,byte to)=>(byte)Math.Round(from+(to-from)*amount);
        var result=new SolidColorBrush(Color.FromRgb(Mix(bg.R,fg.R),Mix(bg.G,fg.G),Mix(bg.B,fg.B)));result.Freeze();return result;
    }
}
