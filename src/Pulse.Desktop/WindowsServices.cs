using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
namespace Pulse.Desktop;
public sealed class WindowsSecrets(string root) : ISecretStore
{
    private readonly object gate=new();
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes,bool encrypt)
    {
        var input=new Blob {Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)}; Blob output=default;
        try {Marshal.Copy(bytes,0,input.Data,bytes.Length); var ok=encrypt?CryptProtectData(ref input,"Pulse Windows",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output); if(!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); var result=new byte[output.Length]; Marshal.Copy(output.Data,result,0,result.Length); return result;}
        finally {Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero) LocalFree(output.Data);}
    }
    public string? Get(string key) {lock(gate) {var values=new AppStorage(root).Load<Dictionary<string,string>>("secrets.json"); if(values?.GetValueOrDefault(key) is not {} encrypted) return null; try{return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(encrypted),false));}catch(Exception exception) when(exception is FormatException or System.ComponentModel.Win32Exception) {return null;}}}
    public void Set(string key,string? value) {lock(gate) {var store=new AppStorage(root); var values=store.Load<Dictionary<string,string>>("secrets.json")??[]; if(string.IsNullOrWhiteSpace(value)) values.Remove(key); else values[key]=Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value),true)); store.Save("secrets.json",values);}}
}
public static class WindowsServices
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput {public uint Size,Tick;}
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput data);
    public static TimeSpan IdleTime(){var input=new LastInput{Size=(uint)Marshal.SizeOf<LastInput>()};return GetLastInputInfo(ref input)?TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount-input.Tick)):TimeSpan.Zero;}
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd,uint affinity);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("kernel32.dll")] public static extern bool AttachConsole(int processId);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetClassNameW")] private static extern int GetClassName(IntPtr window,StringBuilder name,int capacity);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out RectNative rect);
    [StructLayout(LayoutKind.Sequential)] public struct RectNative {public int Left,Top,Right,Bottom;}
    public static void Startup(bool enabled)
    {
        using var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled) run.SetValue("PulseWindows",$"\"{Environment.ProcessPath}\""); else run.DeleteValue("PulseWindows",false);
    }
    public static void Open(string path) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {UseShellExecute=true}); } catch(System.ComponentModel.Win32Exception) {MessageBox.Show("无法打开此地址。","Pulse");} }
    public static bool IsFullscreen(System.Windows.Forms.Screen screen)
    {
        var window=GetForegroundWindow(); if(window==IntPtr.Zero||!GetWindowRect(window,out var r)) return false;
        var name=new StringBuilder(256);GetClassName(window,name,name.Capacity);
        return FullscreenDetection.IsFullscreen(System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),screen.Bounds,name.ToString(),window==GetShellWindow()||window==GetDesktopWindow());
    }
    public static void RegisterProtocol()
    {
        using var protocol=Registry.CurrentUser.CreateSubKey(@"Software\Classes\pulse"); protocol.SetValue("","URL:Pulse Windows"); protocol.SetValue("URL Protocol","");
        using var command=protocol.CreateSubKey(@"shell\open\command"); command.SetValue("",$"\"{Environment.ProcessPath}\" \"%1\"");
    }
}
