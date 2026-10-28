using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Pulse.Core;

namespace Pulse.Desktop;

public static class SettingsChrome
{
    private static readonly ConditionalWeakTable<Window,Attachment> attachments=new();
    // Keep the native call boundary separate from the window/brush lifecycle.
    // Native checks decorate this delegate and forward every call to DWM.
    private static Func<IntPtr,int,int,int> nativeSetter=SetNativeAttribute;

    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Dispatcher.VerifyAccess();
        _=attachments.GetValue(window,static target=>new Attachment(target));
    }

    private static int SetNativeAttribute(IntPtr handle,int attribute,int value)
    {
        try{return DwmSetWindowAttribute(handle,attribute,ref value,sizeof(int));}
        catch(DllNotFoundException){return unchecked((int)0x80004001);}
        catch(EntryPointNotFoundException){return unchecked((int)0x80004001);}
    }

    private sealed class Attachment
    {
        private readonly Window window;
        private readonly SolidColorBrush background;
        private IntPtr handle;
        private bool closed;

        public Attachment(Window window)
        {
            this.window=window;
            background=(SolidColorBrush)Ui.Bg;
            window.SourceInitialized+=SourceInitialized;
            window.Closed+=Closed;
            background.Changed+=BackgroundChanged;
            handle=new WindowInteropHelper(window).Handle;
            if(handle!=IntPtr.Zero)Apply();
        }

        private void SourceInitialized(object? sender,EventArgs args)
        {
            handle=new WindowInteropHelper(window).Handle;
            Apply();
        }

        private void BackgroundChanged(object? sender,EventArgs args)=>Apply();

        private void Apply()
        {
            if(closed||handle==IntPtr.Zero)return;
            var color=background.Color;
            var foreground=AppearancePalette.Foreground($"#{color.R:X2}{color.G:X2}{color.B:X2}");
            var whiteText=foreground=="#FFFFFF";
            // DWM 35/36 are setters; COLORREF is 0x00bbggrr, not ARGB.
            // https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
            var caption=color.R|(color.G<<8)|(color.B<<16);
            _=nativeSetter(handle,20,whiteText?1:0);
            _=nativeSetter(handle,35,caption);
            _=nativeSetter(handle,36,whiteText?0x00FFFFFF:0);
            // Unsupported attributes return an HRESULT on older Windows. Keep
            // its native title bar and controls instead of changing system state.
        }

        private void Closed(object? sender,EventArgs args)
        {
            if(closed)return;
            closed=true;
            background.Changed-=BackgroundChanged;
            window.SourceInitialized-=SourceInitialized;
            window.Closed-=Closed;
            handle=IntPtr.Zero;
            // Retain only the weak-table entry so repeated Attach remains inert
            // after Closed. The entry cannot keep the window alive.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
}
