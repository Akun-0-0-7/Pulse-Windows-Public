using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Pulse.Desktop;

internal static partial class Program
{
    private static void UpdateSettingsChecks(PulseApp app)
    {
        var original=app.Engine.Config.UpdateManifestUrl;app.Engine.Config.UpdateManifestUrl=null;
        var settings=new SettingsWindow(app);
        try
        {
            settings.Navigate("about");
            var actual=typeof(PulseApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            if(!Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Any(block=>block.Text.Contains("Windows 移植版本 "+actual+" ·",System.StringComparison.Ordinal)))
                throw new System.Exception("About does not show the actual installed assembly release version: "+actual);
            var input=Descendants((DependencyObject)settings.Content).OfType<TextBox>().Single(box=>Equals(box.Tag,"update-manifest-url"));
            const string stable="https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates.json";
            const string preview="https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates-preview.json";
            if(input.Text!=stable)throw new System.Exception("Missing/blank update source does not display the stable default.");
            var buttons=Descendants((DependencyObject)settings.Content).OfType<Button>().ToList();
            buttons.Single(button=>Equals(button.Content,"预览版更新源")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(input.Text!=preview||app.Storage.LoadConfig().UpdateManifestUrl!=preview)throw new System.Exception("Preview channel was not persisted.");
            buttons.Single(button=>Equals(button.Content,"稳定版更新源")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(input.Text!=stable||app.Storage.LoadConfig().UpdateManifestUrl!=stable)throw new System.Exception("Stable channel was not persisted.");
            if(Field<bool>(app,"checkingUpdate"))throw new System.Exception("Choosing an update channel started a download or install.");
            if(AppReleaseInfo.IsNewer("0.1.6-preview.6+other","0.1.6-preview.6+installed")||AppReleaseInfo.IsNewer("0.1.5","0.1.6-preview.6"))throw new System.Exception("Same or older release offered an update.");
            if(!AppReleaseInfo.IsNewer("0.1.6-preview.7","0.1.6-preview.6")||!AppReleaseInfo.IsNewer("0.1.6","0.1.6-preview.6"))throw new System.Exception("A newer preview or stable promotion was not offered.");
            System.Console.WriteLine("PASS: actual installed About version, default stable source, saved stable/preview channels and no update side effects.");
        }
        finally{settings.Close();app.Engine.Config.UpdateManifestUrl=original;}
    }
}
