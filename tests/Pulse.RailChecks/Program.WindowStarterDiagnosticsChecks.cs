using System.Windows;
using System.Windows.Controls;
using System.Threading;
using Pulse.Core;
using Pulse.Desktop;
internal static partial class Program
{
    private static void WindowStarterDiagnosticsChecks(PulseApp app,SettingsWindow settings,AccountConfig account)
    {
        var original=Field<Func<string,string?,string?,Dictionary<string,string>?,CancellationToken,Task<WindowStarterPreflightResult>>>(settings,"starterPreflight");
        var proxy=app.Engine.Config.ProxyMode;var savedModel=account.Extra.GetValueOrDefault("windowStarterModel");var enabled=account.Enabled;
        var path=System.IO.Path.Combine(app.Storage.Root,"window-starter-last.json");var history=System.IO.File.Exists(path)?System.IO.File.ReadAllText(path):null;
        try
        {
            account.Enabled=true;app.Engine.Config.ProxyMode="none";
            var pending=new TaskCompletionSource<WindowStarterPreflightResult>();CancellationToken token=default;var calls=0;
            SetField(settings,"starterPreflight",(Func<string,string?,string?,Dictionary<string,string>?,CancellationToken,Task<WindowStarterPreflightResult>>)((_,_,_,_,ct)=>{calls++;token=ct;return pending.Task;}));
            settings.Navigate("account:"+account.Id);
            var check=Descendants((DependencyObject)settings.Content).OfType<Button>().Single(b=>Equals(b.Tag,"starter-preflight-check"));
            var status=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Single(t=>Equals(t.Tag,"starter-preflight-status"));
            check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(calls!=1||check.IsEnabled)throw new Exception("Read-only check did not start once.");
            account.Extra["windowStarterModel"]="gpt-6.1-terra-updated";settings.UpdateReadings();
            if(!token.IsCancellationRequested)throw new Exception("Model change did not cancel old inspection.");
            pending.SetResult(new("fixture-tool","fixture-version","stale-model","pulseSetting",null));WaitUntil(()=>check.IsEnabled,"Inspection did not complete.");
            if(status.Text.Contains("stale-model")||!status.Text.Contains("配置已变化"))throw new Exception("Late inspection overwrote new configuration state.");
            if((System.IO.File.Exists(path)?System.IO.File.ReadAllText(path):null)!=history)throw new Exception("Read-only inspection rewrote historical send result.");
            Console.WriteLine("PASS: model changes cancel checks, late replies cannot overwrite current status, and checks preserve historical sends.");
        }
        finally
        {
            SetField(settings,"starterPreflight",original);app.Engine.Config.ProxyMode=proxy;account.Enabled=enabled;
            if(savedModel is null)account.Extra.Remove("windowStarterModel");else account.Extra["windowStarterModel"]=savedModel;
            settings.Navigate("account:"+account.Id);
        }
    }
}
