namespace Pulse.Core;
public static class UsageFigures
{
    public static int Percent(double usedPercent,bool remaining=false){if(!double.IsFinite(usedPercent))return 0;var value=Math.Clamp(remaining?100-usedPercent:usedPercent,0,100);return value<=0?0:value>=100?100:(int)Math.Clamp(Math.Round(value,MidpointRounding.AwayFromZero),1,99);}
    public static string? Source(string? value)=>value switch {"app-server"=>"appServer","status-line"=>"statusLine","desktop-session"=>"desktopSession","language-server"=>"languageServer",_=>value};
}
