using System.Text.Json;
using Pulse.Core;

namespace Pulse.Tests;

public static class AppearancePaletteTests
{
    [Check] public static void AppearanceSettingsArePresentInNewAndLegacyConfiguration()
    {
        foreach(var config in new[]{new AppConfig(),JsonSerializer.Deserialize<AppConfig>("{}",StorageJson.Options)!})
        {
            using var saved=JsonDocument.Parse(JsonSerializer.Serialize(config,StorageJson.Options));
            Assert.True(saved.RootElement.TryGetProperty("appearance",out var appearance),"AppConfig must persist the new appearance settings while preserving legacy-config defaults.");
            Assert.Equal("usage",appearance.GetProperty("dockColorMode").GetString());
            Assert.Equal("thresholds",appearance.GetProperty("quotaColorMode").GetString());
            Assert.Near(75,appearance.GetProperty("warningPercent").GetDouble());
            Assert.Near(90,appearance.GetProperty("criticalPercent").GetDouble());
        }
    }

    [Check] public static void StrictRgbAcceptsTrimmedSixDigitsAndNormalizesCase()
    {
        foreach(var input in new[]{"#aBcDeF","abcdef","  #ABCDEF  ","\tabcdef\r\n"})
        {
            Assert.True(AppearancePalette.TryNormalizeHex(input,out var color));
            Assert.Equal("#ABCDEF",color);
        }
        foreach(var input in new string?[]{null,""," ","red","#ABC","1234567","#12345678","##123456","#12 456","#GG0000","１２３４５６","0x123456"})
        {
            Assert.True(!AppearancePalette.TryNormalizeHex(input,out var color),"Invalid RGB accepted: "+input);
            Assert.Equal("",color);
        }
        Assert.Equal("#123ABC",AppearancePalette.Resolve(" 123abc ","#000000"));
        Assert.Equal("#AABBCC",AppearancePalette.Resolve("bad","aabbcc"));
        Assert.Equal("#000000",AppearancePalette.Resolve(null,"invalid"));
    }

    [Check] public static void BackgroundDefaultsAndOverridesKeepAllThemesCompatible()
    {
        var config=new AppConfig();
        Assert.Equal("#101318",AppearancePalette.AppBackground(config));
        Assert.Equal("#1B2029",AppearancePalette.AppSurface(config));
        Assert.Equal("#B5ADFF",AppearancePalette.AppAccent(config));
        Assert.Equal("#000000",AppearancePalette.RailBackground(config));
        Assert.Equal("#000000",AppearancePalette.DetailBackground(config));
        config.Theme="light";
        Assert.Equal("#F5F6FA",AppearancePalette.AppBackground(config));
        Assert.Equal("#FFFFFF",AppearancePalette.AppSurface(config));
        Assert.Equal("#6552C9",AppearancePalette.AppAccent(config));
        Assert.Equal("#F5F6FA",AppearancePalette.RailBackground(config));
        Assert.Equal("#FFFFFF",AppearancePalette.DetailBackground(config));
        config.Appearance=new(){AppBackground="112233",AppSurface="223344",AppAccent="#334455",RailBackground="445566",DetailBackground="#556677"};
        Assert.Equal("#112233",AppearancePalette.AppBackground(config));
        Assert.Equal("#223344",AppearancePalette.AppSurface(config));
        Assert.Equal("#334455",AppearancePalette.AppAccent(config));
        Assert.Equal("#445566",AppearancePalette.RailBackground(config));
        Assert.Equal("#556677",AppearancePalette.DetailBackground(config));
        config.Appearance=new(){AppBackground="red",AppSurface="#ABC",AppAccent="#1B2029FF",RailBackground="",DetailBackground="bad"};
        Assert.Equal("#F5F6FA",AppearancePalette.AppBackground(config));
        Assert.Equal("#FFFFFF",AppearancePalette.AppSurface(config));
        Assert.Equal("#6552C9",AppearancePalette.AppAccent(config));
        Assert.Equal("#F5F6FA",AppearancePalette.RailBackground(config));
        Assert.Equal("#FFFFFF",AppearancePalette.DetailBackground(config));
        config.Theme="glass";config.Appearance=null!;
        Assert.Equal("#101318",AppearancePalette.AppBackground(config));
        Assert.Equal("#1B2029",AppearancePalette.AppSurface(config));
        Assert.Equal("#B5ADFF",AppearancePalette.AppAccent(config));
        Assert.Equal("#000000",AppearancePalette.RailBackground(config));
        Assert.Equal("#000000",AppearancePalette.DetailBackground(config));
    }

    [Check] public static void ForegroundUsesTheHigherContrastBlackOrWhite()
    {
        Assert.Equal("#FFFFFF",AppearancePalette.Foreground("#000000"));
        Assert.Equal("#000000",AppearancePalette.Foreground("#FFFFFF"));
        Assert.Equal("#000000",AppearancePalette.Foreground("#808080"));
        Assert.Equal("#FFFFFF",AppearancePalette.Foreground("#101318"));
        Assert.Equal("#FFFFFF",AppearancePalette.Foreground("invalid"));
    }

    [Check] public static void DefaultThresholdPaletteUsesRawUsedPercentageAndAccountOverride()
    {
        var config=new AppConfig();
        foreach(var used in new double?[]{null,double.NaN,double.PositiveInfinity,double.NegativeInfinity,-20,0,74.99})Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,used));
        Assert.Equal("#FFB339",AppearancePalette.Quota(config,75));
        Assert.Equal("#FFB339",AppearancePalette.Quota(config,89.99));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,90));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,140));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,null,true));
        Assert.Equal("#123ABC",AppearancePalette.Quota(config,99,true," 123abc "));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,99,false,"red"));
        config.ShowRemaining=true;var remaining=AppearancePalette.Quota(config,80);
        config.ShowRemaining=false;Assert.Equal(remaining,AppearancePalette.Quota(config,80));
        config.Theme="light";Assert.Equal("#202335",AppearancePalette.Quota(config,10));
        config.Appearance=null!;Assert.Equal("#EF1630",AppearancePalette.Quota(config,99));
    }

    [Check] public static void FixedAndUnknownModesAndInvalidOverridesFallBackSafely()
    {
        var config=new AppConfig{Appearance=new(){QuotaColorMode="fixed",QuotaNormal="123456",QuotaWarning="aabbcc",QuotaCritical="ffffff"}};
        foreach(var used in new double?[]{null,0,80,100,double.NaN})Assert.Equal("#123456",AppearancePalette.Quota(config,used,true));
        Assert.Equal("#654321",AppearancePalette.Quota(config,100,true,"654321"));
        config.Appearance.QuotaColorMode="invalid";
        Assert.Equal("#AABBCC",AppearancePalette.Quota(config,80));
        config.Appearance.QuotaWarning="bad";config.Appearance.QuotaCritical="#ABC";
        Assert.Equal("#FFB339",AppearancePalette.Quota(config,80));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,95));
    }

    [Check] public static void SurfaceNormalFallbackOnlyAppliesWithoutAValidGlobalNormalOverride()
    {
        var config=new AppConfig();
        Assert.Equal("#123456",AppearancePalette.Quota(config,10,normalFallback:"123456"));
        Assert.Equal("#FFB339",AppearancePalette.Quota(config,80,normalFallback:"123456"));
        Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,10,normalFallback:"invalid"));
        config.Appearance.QuotaNormal="#ABCDEF";
        Assert.Equal("#ABCDEF",AppearancePalette.Quota(config,10,normalFallback:"#123456"));
        Assert.Equal("#654321",AppearancePalette.Quota(config,10,accountColor:"654321",normalFallback:"#123456"));
        config.Appearance.QuotaNormal="invalid";config.Appearance.QuotaColorMode="fixed";
        Assert.Equal("#123456",AppearancePalette.Quota(config,100,true,normalFallback:"#123456"));
        config.Appearance.QuotaColorMode="gradient";
        Assert.Equal("#123456",AppearancePalette.Quota(config,0,normalFallback:"#123456"));
    }

    [Check] public static void GradientInterpolatesAcrossConfiguredUsedPercentAnchors()
    {
        var config=new AppConfig{Appearance=new(){QuotaColorMode="gradient",QuotaNormal="#000000",QuotaWarning="#808080",QuotaCritical="#FFFFFF",WarningPercent=40,CriticalPercent=80}};
        Assert.Equal("#000000",AppearancePalette.Quota(config,-5));
        Assert.Equal("#000000",AppearancePalette.Quota(config,null));
        Assert.Equal("#404040",AppearancePalette.Quota(config,20));
        Assert.Equal("#808080",AppearancePalette.Quota(config,40));
        Assert.Equal("#C0C0C0",AppearancePalette.Quota(config,60));
        Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,80));
        Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,120));
        Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,0,true));
        config.ShowRemaining=true;Assert.Equal("#C0C0C0",AppearancePalette.Quota(config,60));
    }

    [Check] public static void InvalidThresholdsFallBackAsAPairAndHundredPercentIsValid()
    {
        var config=new AppConfig();
        foreach(var (warning,critical) in new[]{(double.NaN,90d),(75d,double.PositiveInfinity),(-1d,90d),(0d,90d),(90d,75d),(75d,75d),(101d,110d),(75d,100.1)})
        {
            config.Appearance.WarningPercent=warning;config.Appearance.CriticalPercent=critical;
            Assert.Equal((75d,90d),AppearancePalette.Thresholds(config));
            Assert.Equal("#FFB339",AppearancePalette.Quota(config,80));
        }
        config.Appearance.WarningPercent=50;config.Appearance.CriticalPercent=100;
        Assert.Equal((50d,100d),AppearancePalette.Thresholds(config));
        Assert.Equal("#FFB339",AppearancePalette.Quota(config,99));
        Assert.Equal("#EF1630",AppearancePalette.Quota(config,100));
        config.Appearance=null!;Assert.Equal((75d,90d),AppearancePalette.Thresholds(config));
    }

    [Check] public static void DockUsesHighestEnabledDisplayedWindowAndIgnoresAccountAndRemainingColors()
    {
        var config=new AppConfig{Accounts=[new(){Id="one",ProviderId="codex",Enabled=true,AccentColor="#123456"},new(){Id="two",ProviderId="claudeCode",Enabled=true},new(){Id="disabled",ProviderId="codex",Enabled=false}]};
        var usable=Reading("one","codex",10);usable.Windows.Add(new(){UsedPercent=80});
        var stale=Reading("one","codex",100);stale.IsStale=true;
        var failed=Reading("two","claudeCode",100);failed.ErrorCode="network";
        var errorText=Reading("two","claudeCode",100);errorText.ErrorMessage="unreachable";
        var readings=new[]{usable,Reading("disabled","codex",100),Reading("one","claudeCode",100),Reading("unknown","codex",100),stale,failed,errorText,Reading("two","claudeCode",double.NaN),Reading("two","claudeCode",double.PositiveInfinity)};
        Assert.Equal("#FFB339",AppearancePalette.Dock(config,readings));
        config.ShowRemaining=false;Assert.Equal("#FFB339",AppearancePalette.Dock(config,readings));
        usable.Windows.Add(new(){UsedPercent=95});Assert.Equal("#EF1630",AppearancePalette.Dock(config,readings));
        config.Appearance.QuotaColorMode="fixed";config.Appearance.QuotaNormal="#ABCDEF";
        Assert.Equal("#ABCDEF",AppearancePalette.Dock(config,readings));
    }

    [Check] public static void UnknownAndFixedDockColorsUseNeutralConfiguredOrThemeFallback()
    {
        var config=new AppConfig();
        Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[]));
        config.Theme="light";Assert.Equal("#202335",AppearancePalette.Dock(config,[]));
        config.Appearance.DockColor="123456";Assert.Equal("#123456",AppearancePalette.Dock(config,[]));
        config.Accounts=[new(){Id="one",ProviderId="codex",Enabled=true}];
        config.Appearance.DockColorMode="fixed";
        Assert.Equal("#123456",AppearancePalette.Dock(config,[Reading("one","codex",100)]));
        config.Appearance.DockColor="invalid";Assert.Equal("#202335",AppearancePalette.Dock(config,[Reading("one","codex",100)]));
        config.Appearance.DockColorMode="unknown";Assert.Equal("#EF1630",AppearancePalette.Dock(config,[Reading("one","codex",100)]));
        config.Appearance=null!;Assert.Equal("#202335",AppearancePalette.Dock(config,[]));
    }

    [Check] public static void DockRejectsNegativeWindowsWithoutChangingSingleQuotaClamping()
    {
        var config=new AppConfig{Accounts=[new(){Id="one",ProviderId="codex",Enabled=true}],Appearance=new(){DockColor="#123456"}};
        Assert.Equal("#123456",AppearancePalette.Dock(config,[Reading("one","codex",-1)]));
        Assert.Equal("#FFFFFF",AppearancePalette.Quota(config,-1));
        var invalidExhausted=Reading("one","codex",-1);invalidExhausted.Windows[0].Exhausted=true;
        Assert.Equal("#123456",AppearancePalette.Dock(config,[invalidExhausted]));
        Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[Reading("one","codex",0),invalidExhausted]));
    }

    [Check] public static void DockPromotesDisplayedExhaustedWindowAboveHigherUnexhaustedUsage()
    {
        var config=new AppConfig{Accounts=[new(){Id="one",ProviderId="codex",Enabled=true},new(){Id="two",ProviderId="claudeCode",Enabled=true}]};
        var exhausted=Reading("one","codex",10);exhausted.Windows[0].Exhausted=true;
        var warning=Reading("two","claudeCode",80);
        Assert.Equal("#EF1630",AppearancePalette.Dock(config,[exhausted,warning]));
        Assert.Equal("#EF1630",AppearancePalette.Dock(config,[warning,exhausted]));
        exhausted.Windows[0].UsedPercent=double.NaN;Assert.Equal("#FFB339",AppearancePalette.Dock(config,[exhausted,warning]));
        exhausted.Windows[0].UsedPercent=10;exhausted.IsStale=true;Assert.Equal("#FFB339",AppearancePalette.Dock(config,[exhausted,warning]));
        exhausted.IsStale=false;config.Accounts[0].Enabled=false;Assert.Equal("#FFB339",AppearancePalette.Dock(config,[exhausted,warning]));
        config.Accounts[0].Enabled=true;config.Appearance.QuotaColorMode="fixed";config.Appearance.QuotaNormal="#123456";
        Assert.Equal("#123456",AppearancePalette.Dock(config,[exhausted,warning]));
    }

    [Check] public static void DockFollowsExplicitPrimaryWindowInsteadOfHiddenWeeklyExhaustion()
    {
        var account=new AccountConfig{Id="one",ProviderId="codex",Enabled=true,PrimaryWindowId="five"};
        var config=new AppConfig{Accounts=[account]};
        var five=new UsageWindow{Id="five",Kind="fiveHour",UsedPercent=20};
        var weekly=new UsageWindow{Id="weekly",Kind="weekly",UsedPercent=100,Exhausted=true};
        var reading=new UsageReading{AccountId="one",ProviderId="codex",Windows=[five,weekly]};
        Assert.True(ReferenceEquals(five,UsageEngine.Headline(account,reading)));
        Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[reading]));
        config.ShowRemaining=false;Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[reading]));
        five.UsedPercent=80;Assert.Equal("#FFB339",AppearancePalette.Dock(config,[reading]));
        account.PrimaryWindowId="weekly";Assert.Equal("#EF1630",AppearancePalette.Dock(config,[reading]));
        account.PrimaryWindowId="five";five.UsedPercent=20;
        config.Appearance.QuotaColorMode="gradient";
        Assert.Equal(AppearancePalette.Quota(config,20),AppearancePalette.Dock(config,[reading]));
    }

    [Check] public static void DockAutomaticAndMissingPrimaryKeepTheExistingHeadlineChoice()
    {
        var account=new AccountConfig{Id="one",ProviderId="codex",Enabled=true};
        var config=new AppConfig{Accounts=[account]};
        var five=new UsageWindow{Id="five",Kind="fiveHour",UsedPercent=80};
        var weekly=new UsageWindow{Id="weekly",Kind="weekly",UsedPercent=5,Exhausted=true};
        var reading=new UsageReading{AccountId="one",ProviderId="codex",Windows=[five,weekly]};
        foreach(var primary in new string?[]{null,"missing-window"})
        {
            account.PrimaryWindowId=primary;
            Assert.True(ReferenceEquals(five,UsageEngine.Headline(account,reading)));
            Assert.Equal("#FFB339",AppearancePalette.Dock(config,[reading]));
            weekly.UsedPercent=95;
            Assert.True(ReferenceEquals(weekly,UsageEngine.Headline(account,reading)));
            Assert.Equal("#EF1630",AppearancePalette.Dock(config,[reading]));
            weekly.UsedPercent=5;
        }
    }

    [Check] public static void DockAggregatesOnlyDisplayedWindowsAcrossEnabledMatchingAccounts()
    {
        var one=new AccountConfig{Id="one",ProviderId="codex",Enabled=true,PrimaryWindowId="five"};
        var two=new AccountConfig{Id="two",ProviderId="claudeCode",Enabled=true,PrimaryWindowId="session"};
        var config=new AppConfig{Accounts=[one,two]};
        var first=new UsageReading{AccountId="one",ProviderId="codex",Windows=[new(){Id="five",UsedPercent=20},new(){Id="weekly",UsedPercent=100,Exhausted=true}]};
        var second=new UsageReading{AccountId="two",ProviderId="claudeCode",Windows=[new(){Id="session",UsedPercent=80},new(){Id="weekly",UsedPercent=100,Exhausted=true}]};
        Assert.Equal("#FFB339",AppearancePalette.Dock(config,[first,second]));
        Assert.Equal("#FFB339",AppearancePalette.Dock(config,[second,first]));
        two.Enabled=false;Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[first,second]));
        two.Enabled=true;two.PrimaryWindowId="weekly";Assert.Equal("#EF1630",AppearancePalette.Dock(config,[first,second]));
        second.ProviderId="codex";Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[first,second]));
        config.Appearance.DockColorMode="fixed";config.Appearance.DockColor="#123456";
        Assert.Equal("#123456",AppearancePalette.Dock(config,[first,second]));
    }

    [Check] public static void DockRejectsAnInvalidDisplayedWindowWithoutBorrowingHiddenUsage()
    {
        var account=new AccountConfig{Id="one",ProviderId="codex",Enabled=true,PrimaryWindowId="five"};
        var config=new AppConfig{Accounts=[account],Appearance=new(){DockColor="#123456"}};
        var five=new UsageWindow{Id="five",UsedPercent=20};
        var weekly=new UsageWindow{Id="weekly",UsedPercent=95,Exhausted=true};
        var reading=new UsageReading{AccountId="one",ProviderId="codex",Windows=[five,weekly]};
        foreach(var invalid in new[]{-1d,double.NaN,double.NegativeInfinity,double.PositiveInfinity})
        {
            five.UsedPercent=invalid;
            Assert.Equal("#123456",AppearancePalette.Dock(config,[reading]));
            five.Exhausted=true;Assert.Equal("#123456",AppearancePalette.Dock(config,[reading]));five.Exhausted=false;
        }
        five.UsedPercent=double.PositiveInfinity;account.PrimaryWindowId=null;
        Assert.True(ReferenceEquals(five,UsageEngine.Headline(account,reading)));
        Assert.Equal("#123456",AppearancePalette.Dock(config,[reading]));
        account.PrimaryWindowId="five";five.UsedPercent=20;
        reading.Windows=[null!,five,weekly];Assert.Equal("#FFFFFF",AppearancePalette.Dock(config,[reading]));
        reading.Windows=[null!];Assert.Equal("#123456",AppearancePalette.Dock(config,[reading]));
        reading.Windows=null!;Assert.Equal("#123456",AppearancePalette.Dock(config,[reading]));
    }

    [Check] public static void AppearanceOverridesRoundTripAndSignatureTracksOnlyAppearanceAndTheme()
    {
        var config=new AppConfig{Appearance=new(){AppBackground="#123456",AppSurface="#234567",AppAccent="#345678",RailBackground="#456789",DetailBackground="#56789A",DockColor="#6789AB",DockColorMode="fixed",QuotaColorMode="gradient",QuotaNormal="#789ABC",QuotaWarning="#89ABCD",QuotaCritical="#9ABCDE",WarningPercent=30,CriticalPercent=60}};
        var reloaded=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config,StorageJson.Options),StorageJson.Options)!;
        var signature=AppearancePalette.Signature(config);
        Assert.Equal(signature,AppearancePalette.Signature(reloaded));
        Assert.Equal("#123456",AppearancePalette.AppBackground(reloaded));
        Assert.Equal("#6789AB",AppearancePalette.Dock(reloaded,[]));
        Assert.Equal("#89ABCD",AppearancePalette.Quota(reloaded,30));
        config.ShowRemaining=!config.ShowRemaining;config.RefreshMinutes=5;
        Assert.Equal(signature,AppearancePalette.Signature(config));
        config.Appearance.DetailBackground="#000000";Assert.True(signature!=AppearancePalette.Signature(config));
        signature=AppearancePalette.Signature(config);config.Theme="light";Assert.True(signature!=AppearancePalette.Signature(config));
        config.Appearance.WarningPercent=double.NaN;Assert.Equal(AppearancePalette.Signature(config),AppearancePalette.Signature(config));
        config.Appearance=null!;Assert.True(AppearancePalette.Signature(config).Length>0);
    }

    private static UsageReading Reading(string id,string provider,double percent)=>new(){AccountId=id,ProviderId=provider,Windows=[new(){UsedPercent=percent}]};

}
