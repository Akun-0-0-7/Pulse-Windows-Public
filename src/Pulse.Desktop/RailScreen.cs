using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pulse.Desktop;

internal static class RailScreen
{
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromPoint(NativePoint point,uint flags);
    [DllImport("shcore.dll")]private static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    public static Rect Bounds(Window window)
    {
        if(GetWindowRect(new WindowInteropHelper(window).Handle,out var rect))return new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
        var scale=VisualTreeHelper.GetDpi(window);return new Rect(window.Left*scale.DpiScaleX,window.Top*scale.DpiScaleY,window.ActualWidth*scale.DpiScaleX,window.ActualHeight*scale.DpiScaleY);
    }
    public static double Scale(System.Windows.Forms.Screen screen)
    {
        var area=screen.WorkingArea;var monitor=MonitorFromPoint(new NativePoint{X=area.Left+area.Width/2,Y=area.Top+area.Height/2},2);
        return GetDpiForMonitor(monitor,0,out var x,out _)==0&&x>0?x/96d:1;
    }
    public static void Move(Window window,Point physical)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle!=IntPtr.Zero)SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(physical.X),(int)Math.Round(physical.Y),0,0,0x0001|0x0004|0x0010);
    }
    public static System.Windows.Forms.Screen Resolve(string? deviceName)=>System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==deviceName)??System.Windows.Forms.Screen.PrimaryScreen??System.Windows.Forms.Screen.AllScreens[0];
}
