namespace Pulse.Desktop;

public static class FullscreenDetection
{
    public static bool IsFullscreen(System.Drawing.Rectangle window,System.Drawing.Rectangle display,string windowClass,bool shellDesktop)
    {
        if(shellDesktop||windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return false;
        return window.Left<=display.Left&&window.Top<=display.Top&&window.Right>=display.Right&&window.Bottom>=display.Bottom;
    }
}
