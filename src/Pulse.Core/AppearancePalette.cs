using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulse.Core;

public static class AppearancePalette
{
    private static readonly AppearanceSettings defaults=new();
    private static readonly JsonSerializerOptions signatureJson=new(StorageJson.Options){NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};

    public static bool TryNormalizeHex(string? input,out string normalized)
    {
        normalized="";
        if(input is null)return false;
        var value=input.AsSpan().Trim();
        if(value.Length>0&&value[0]=='#')value=value[1..];
        if(value.Length!=6)return false;
        foreach(var digit in value)
            if(digit is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F'))return false;
        normalized="#"+value.ToString().ToUpperInvariant();return true;
    }

    public static string Resolve(string? overrideValue,string fallback)
        =>TryNormalizeHex(overrideValue,out var selected)?selected:TryNormalizeHex(fallback,out var validFallback)?validFallback:"#000000";

    private static AppearanceSettings Settings(AppConfig config)=>config.Appearance??defaults;
    private static bool Light(AppConfig config)=>config.Theme=="light";
    private static string ThemeForeground(AppConfig config)=>Light(config)?"#202335":"#FFFFFF";
    public static string AppBackground(AppConfig config)=>Resolve(Settings(config).AppBackground,Light(config)?"#F5F6FA":"#101318");
    public static string AppSurface(AppConfig config)=>Resolve(Settings(config).AppSurface,Light(config)?"#FFFFFF":"#1B2029");
    public static string AppAccent(AppConfig config)=>Resolve(Settings(config).AppAccent,Light(config)?"#6552C9":"#B5ADFF");
    public static string RailBackground(AppConfig config)=>Resolve(Settings(config).RailBackground,Light(config)?"#F5F6FA":"#000000");
    public static string DetailBackground(AppConfig config)=>Resolve(Settings(config).DetailBackground,Light(config)?"#FFFFFF":"#000000");

    public static string Foreground(string background)
    {
        var color=Resolve(background,"#000000");
        double Channel(int offset)
        {
            var value=Convert.ToByte(color.Substring(offset,2),16)/255d;
            return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);
        }
        var luminance=.2126*Channel(1)+.7152*Channel(3)+.0722*Channel(5);
        var blackContrast=(luminance+.05)/.05;var whiteContrast=1.05/(luminance+.05);
        return blackContrast>=whiteContrast?"#000000":"#FFFFFF";
    }

    public static (double Warning,double Critical) Thresholds(AppConfig config)
    {
        var appearance=Settings(config);var warning=appearance.WarningPercent;var critical=appearance.CriticalPercent;
        return double.IsFinite(warning)&&double.IsFinite(critical)&&warning>0&&warning<critical&&critical<=100?(warning,critical):(75,90);
    }

    public static string Quota(AppConfig config,double? usedPercent,bool exhausted=false,string? accountColor=null,string? normalFallback=null)
    {
        if(TryNormalizeHex(accountColor,out var account))return account;
        var appearance=Settings(config);
        var normal=Resolve(appearance.QuotaNormal,Resolve(normalFallback,ThemeForeground(config)));
        if(appearance.QuotaColorMode=="fixed")return normal;
        var warning=Resolve(appearance.QuotaWarning,"#FFB339");var critical=Resolve(appearance.QuotaCritical,"#EF1630");
        if(exhausted)return critical;
        var used=usedPercent is {} percent&&double.IsFinite(percent)?Math.Clamp(percent,0,100):0;
        var thresholds=Thresholds(config);
        if(used>=thresholds.Critical)return critical;
        if(appearance.QuotaColorMode=="gradient")
            return used<=thresholds.Warning?Mix(normal,warning,used/thresholds.Warning):Mix(warning,critical,(used-thresholds.Warning)/(thresholds.Critical-thresholds.Warning));
        return used>=thresholds.Warning?warning:normal;
    }

    public static string Dock(AppConfig config,IEnumerable<UsageReading> readings)
    {
        var neutral=Resolve(Settings(config).DockColor,ThemeForeground(config));
        if(Settings(config).DockColorMode=="fixed")return neutral;
        var enabled=(config.Accounts??[]).Where(account=>account is not null&&account.Enabled).ToLookup(account=>(account.Id,account.ProviderId));
        double? highest=null;var exhausted=false;
        foreach(var reading in readings??[])
        {
            if(reading is null||!enabled.Contains((reading.AccountId,reading.ProviderId))||reading.IsStale||!string.IsNullOrWhiteSpace(reading.ErrorCode)||!string.IsNullOrWhiteSpace(reading.ErrorMessage)||reading.Windows is null)continue;
            // Match the ring's existing Headline choice. Only corrupt null list
            // entries are removed; invalid percentages remain selected and are
            // rejected below rather than falling back to an undisplayed window.
            var displayReading=reading.Windows.Any(window=>window is null)
                ?new UsageReading{Windows=reading.Windows.Where(window=>window is not null).ToList()}
                :reading;
            foreach(var account in enabled[(reading.AccountId,reading.ProviderId)])
            {
                var window=UsageEngine.Headline(account,displayReading);
                if(window is null||!double.IsFinite(window.UsedPercent)||window.UsedPercent<0)continue;
                var effectiveUsed=window.Exhausted?Math.Max(100,window.UsedPercent):window.UsedPercent;
                if(highest is null||effectiveUsed>highest||(effectiveUsed==highest&&window.Exhausted))
                {highest=effectiveUsed;exhausted=window.Exhausted;}
            }
        }
        return highest is {} used?Quota(config,used,exhausted):neutral;
    }

    public static string Signature(AppConfig config)=>JsonSerializer.Serialize(new{config.Theme,Appearance=Settings(config)},signatureJson);

    private static string Mix(string from,string to,double fraction)
    {
        int Channel(int offset)
        {
            var first=Convert.ToByte(from.Substring(offset,2),16);var second=Convert.ToByte(to.Substring(offset,2),16);
            return (int)Math.Round(first+(second-first)*fraction,MidpointRounding.AwayFromZero);
        }
        return $"#{Channel(1):X2}{Channel(3):X2}{Channel(5):X2}";
    }
}
