using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private readonly record struct SettingsChromeWrite(IntPtr Handle,int Attribute,int Value,int Result);

    private static void SettingsChromeChecks(PulseApp app)
    {
        // 20/35/36 are documented setters. The isolated native probe confirms
        // Get(20) works, while Get(35/36) returns E_INVALIDARG before AND after
        // successful setters, hidden AND shown. Do not treat unsupported getters
        // as a missing feature or claim a pixel/color readback for those values.
        // https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        var supportsColors=OperatingSystem.IsWindowsVersionAtLeast(10,0,22000);
        if(!supportsColors)
            Console.WriteLine($"SKIP: requiring DWM 20/35/36 setter S_OK needs the documented Windows 11 build 22000 minimum; actual OS is {Environment.OSVersion.Version}. Native calls, styles and closed-window lifecycle checks still run.");
        var setterField=typeof(SettingsChrome).GetField("nativeSetter",BindingFlags.NonPublic|BindingFlags.Static)
            ??throw new Exception("SettingsChrome native setter boundary is missing.");
        var original=(Func<IntPtr,int,int,int>)(setterField.GetValue(null)??throw new Exception("SettingsChrome native setter is null."));
        var writes=new List<SettingsChromeWrite>();
        Func<IntPtr,int,int,int> forwarding=(handle,attribute,value)=>
        {
            var result=original(handle,attribute,value);
            writes.Add(new(handle,attribute,value,result));
            return result;
        };
        var config=app.Engine.Config;
        var onboarding=config.OnboardingCompleted;var spend=config.SpendEnabled;var starter=config.WindowStarterEnabled;var alerts=config.AlertsEnabled;
        var showPanel=config.ShowPanel;var settingsShortcut=config.SettingsShortcut;var panelShortcut=config.PanelShortcut;
        var accounts=config.Accounts.Select(account=>(Account:account,Enabled:account.Enabled)).ToArray();
        config.OnboardingCompleted=true;config.SpendEnabled=false;config.WindowStarterEnabled=false;config.AlertsEnabled=false;
        config.ShowPanel=false;config.SettingsShortcut=null;config.PanelShortcut=null;
        foreach(var account in accounts)account.Account.Enabled=false;
        try
        {
            // A newly constructed WPF Application queues OnStartup. Drain it
            // under an offline fixture before the first measured palette change,
            // otherwise it restores Engine.Config's dark palette during RunFor.
            var startupDrained=false;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>startupDrained=true));
            WaitUntil(()=>app.Rail is not null&&startupDrained,"Synthetic application startup did not finish before title-bar checks.");
            setterField.SetValue(null,forwarding);
            var priorMain=app.MainWindow;
            Ui.ApplyTheme("dark");
            var settings=new SettingsWindow(app){ShowInTaskbar=false,ShowActivated=false};
            app.MainWindow=priorMain;
            IntPtr handle=IntPtr.Zero;
            try
            {
                var windowStyle=settings.WindowStyle;
                var resizeMode=settings.ResizeMode;
                var beforeHandle=writes.Count;
                handle=new WindowInteropHelper(settings).EnsureHandle();
                if(handle==IntPtr.Zero)throw new Exception("Real SettingsWindow did not create an HWND for title-bar checks.");
                var nativeStyle=SettingsChromeNativeStyle(handle,-16);
                var extendedStyle=SettingsChromeNativeStyle(handle,-20);
                const long requiredNativeChrome=0x00CF0000; // Caption, system menu, resize frame, minimize/maximize boxes.
                if((nativeStyle&requiredNativeChrome)!=requiredNativeChrome)
                    throw new Exception($"SettingsWindow lost native caption/drag/resize/system-button styles: 0x{nativeStyle:X}.");
                SettingsChromeAssertWrites(writes,beforeHandle,handle,0x00181310,0x00FFFFFF,1,supportsColors,"SourceInitialized automatically applies initial dark caption");
                foreach(var sample in new (string Name,AppConfig Config,int Caption,int Text,int Dark)[]{
                    ("light",new(){Theme="light"},0x00FAF6F5,0x00000000,0),
                    ("dark",new(){Theme="dark"},0x00181310,0x00FFFFFF,1),
                    ("light actual background with dark theme",new(){Theme="dark",Appearance=new(){AppBackground="#E6D2B8",AppSurface="#101318"}},0x00B8D2E6,0x00000000,0),
                    ("dark actual background with light theme",new(){Theme="light",Appearance=new(){AppBackground="#112B46",AppSurface="#FFFFFF"}},0x00462B11,0x00FFFFFF,1)
                })
                {
                    var beforeTheme=writes.Count;
                    Ui.ApplyTheme(sample.Config);RunFor(10);
                    SettingsChromeAssertWrites(writes,beforeTheme,handle,sample.Caption,sample.Text,sample.Dark,supportsColors,sample.Name+" automatically follows Ui.Bg.Changed");
                    SettingsChromeAssertNativeStyle(settings,handle,nativeStyle,extendedStyle,windowStyle,resizeMode,sample.Name);
                }
                // A config/theme-only event cannot satisfy a direct Freezable update.
                var beforeBrush=writes.Count;
                ((SolidColorBrush)Ui.Bg).Color=Color.FromRgb(0x22,0x44,0x66);RunFor(10);
                SettingsChromeAssertWrites(writes,beforeBrush,handle,0x00664422,0x00FFFFFF,1,supportsColors,"direct shared background-brush change");
                SettingsChromeAssertNativeStyle(settings,handle,nativeStyle,extendedStyle,windowStyle,resizeMode,"direct background change");
            }
            finally{settings.Close();app.MainWindow=priorMain;}
            var afterClose=writes.Count;
            Ui.ApplyTheme("light");Ui.ApplyTheme(app.Engine.Config);RunFor(10);
            if(writes.Skip(afterClose).Any(write=>write.Handle==handle))
                throw new Exception("Closed SettingsWindow still receives native title-bar palette writes.");
            var beforeProbe=writes.Count;
            var closed=SettingsChromeClosedWindowReference(app);
            if(writes.Count-beforeProbe!=3)throw new Exception("Repeated Attach added duplicate title-bar palette handlers.");
            for(var attempt=0;attempt<5&&closed.IsAlive;attempt++)
            {
                GC.Collect();GC.WaitForPendingFinalizers();RunFor(15);GC.Collect();
            }
            if(closed.IsAlive)throw new Exception("A closed SettingsWindow is still retained; remove its shared Ui.Bg.Changed listener on Closed.");
            Console.WriteLine(supportsColors
                ?"PASS: real SettingsWindow SourceInitialized and Ui.Bg.Changed automatically forward correct dark/light/custom COLORREFs to DWM 20/35/36 with S_OK; Get(20) confirms dark mode; native styles remain unchanged and Closed releases the palette listener. Attributes 35/36 were verified through real setter calls, not unsupported getters or pixel capture."
                :"PASS: unsupported-OS fallback emits native palette writes, preserves native styles and releases closed windows; S_OK/Get assertions skipped with documented OS-build reason. No 35/36 getter or pixel acceptance is claimed.");
        }
        finally
        {
            setterField.SetValue(null,original);
            config.OnboardingCompleted=onboarding;config.SpendEnabled=spend;config.WindowStarterEnabled=starter;config.AlertsEnabled=alerts;
            config.ShowPanel=showPanel;config.SettingsShortcut=settingsShortcut;config.PanelShortcut=panelShortcut;
            foreach(var account in accounts)account.Account.Enabled=account.Enabled;
            Ui.ApplyTheme(config);
        }
    }

    private static void SettingsChromeAssertWrites(List<SettingsChromeWrite> writes,int start,IntPtr handle,int caption,int text,int dark,bool requireSuccess,string name)
    {
        var actual=writes.Skip(start).Where(write=>write.Handle==handle).ToArray();
        if(actual.Length!=3)
        {
            var details=string.Join("; ",actual.Select(write=>$"attr={write.Attribute},value=0x{unchecked((uint)write.Value):X8},HRESULT=0x{unchecked((uint)write.Result):X8}"));
            throw new Exception($"{name}: expected exactly three automatic DWM color calls, observed {actual.Length}. Calls: {details}");
        }
        foreach(var expected in new (int Attribute,int Value)[]{(20,dark),(35,caption),(36,text)})
        {
            var matches=actual.Where(write=>write.Attribute==expected.Attribute).ToArray();
            if(matches.Length!=1||matches[0].Value!=expected.Value)
                throw new Exception($"{name}: native attribute {expected.Attribute} did not receive expected value 0x{expected.Value:X8}.");
            if(requireSuccess&&matches[0].Result!=0)
                throw new Exception($"{name}: real DwmSetWindowAttribute({expected.Attribute}) returned HRESULT 0x{unchecked((uint)matches[0].Result):X8} on OS {Environment.OSVersion.Version}.");
        }
        if(requireSuccess)
        {
            var result=SettingsChromeGetAttribute(handle,20,out var value,sizeof(uint));
            if(result!=0||value!=(uint)dark)throw new Exception($"{name}: supported Get(20) returned HRESULT 0x{unchecked((uint)result):X8}, mode {value}; expected {dark}.");
        }
    }

    private static void SettingsChromeAssertNativeStyle(SettingsWindow settings,IntPtr handle,long style,long extended,WindowStyle windowStyle,ResizeMode resizeMode,string name)
    {
        if(settings.WindowStyle!=windowStyle||settings.ResizeMode!=resizeMode||settings.AllowsTransparency||SettingsChromeNativeStyle(handle,-16)!=style||SettingsChromeNativeStyle(handle,-20)!=extended)
            throw new Exception(name+": title-bar recoloring changed native window chrome, resize behavior or window styles.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SettingsChromeClosedWindowReference(PulseApp app)
    {
        var priorMain=app.MainWindow;
        var settings=new SettingsWindow(app){ShowInTaskbar=false,ShowActivated=false};
        app.MainWindow=priorMain;
        SettingsChrome.Attach(settings);SettingsChrome.Attach(settings);
        try{new WindowInteropHelper(settings).EnsureHandle();return new WeakReference(settings);}
        finally{settings.Close();SettingsChrome.Attach(settings);app.MainWindow=priorMain;}
    }

    private static long SettingsChromeNativeStyle(IntPtr handle,int index)=>IntPtr.Size==8
        ?SettingsChromeGetWindowLong64(handle,index).ToInt64()
        :unchecked((uint)SettingsChromeGetWindowLong32(handle,index));

    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]
    private static extern int SettingsChromeGetAttribute(IntPtr handle,uint attribute,out uint value,uint size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]
    private static extern IntPtr SettingsChromeGetWindowLong64(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]
    private static extern int SettingsChromeGetWindowLong32(IntPtr handle,int index);
}
