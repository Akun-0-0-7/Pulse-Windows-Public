using System;
using System.Drawing;
using Pulse.Desktop;

internal static class FullscreenChecks
{
    public static void Run()
    {
        var display=new Rectangle(-1920,0,1920,1080);
        foreach(var shellClass in new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"})
            Expect(false,display,display,shellClass,false,"Desktop and taskbar surfaces must not hide the rail");
        Expect(false,display,display,"custom-shell",true,"The Windows shell desktop handle must not hide the rail");
        Expect(true,display,display,"Chrome_WidgetWin_1",false,"A real fullscreen application must still hide the rail");
        Expect(false,new(-1910,10,1900,1000),display,"Chrome_WidgetWin_1",false,"An ordinary window must keep the rail visible");
        Expect(false,new(0,0,1920,1080),display,"Chrome_WidgetWin_1",false,"A fullscreen app on another monitor must keep this rail visible");
        Expect(true,new(-1928,-8,1936,1096),display,"ApplicationWindow",false,"A fullscreen application can extend past display bounds");
        Console.WriteLine("PASS: desktop and taskbar exclusions, genuine fullscreen, ordinary window and per-monitor fullscreen classification.");
    }
    private static void Expect(bool expected,Rectangle window,Rectangle display,string windowClass,bool shell,string message)
    {
        if(FullscreenDetection.IsFullscreen(window,display,windowClass,shell)!=expected)throw new Exception(message+": "+windowClass);
    }
}
