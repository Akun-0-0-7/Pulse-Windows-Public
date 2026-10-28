using System.Windows;
using System.Windows.Controls;
using System.IO;
using Pulse.Core;
using Pulse.Desktop;

internal static partial class Program
{
    private static void CodexSpendDirectoryChecks(PulseApp app)
    {
        var config=app.Engine.Config;var originalHome=config.CodexSpendHome;var originalEnabled=config.SpendEnabled;
        var reportPath=Path.Combine(app.Storage.Root,"spend-cache.json");var originalReport=File.Exists(reportPath)?File.ReadAllBytes(reportPath):null;
        var fixture=Path.Combine(app.Storage.Root,"codex-home-fixture-"+System.Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(fixture,"sessions"));
        config.SpendEnabled=false;config.CodexSpendHome=null;
        var settings=new SettingsWindow(app);
        try
        {
            settings.Navigate("spend");
            if(!Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Any(block=>block.Text=="Codex 数据目录（可选）"))
                throw new System.Exception("Token statistics lacks an explicit Codex data directory and scan-root display.");
            var input=Descendants((DependencyObject)settings.Content).OfType<TextBox>().Single(box=>Equals(box.Tag,"codex-spend-home"));
            var apply=Descendants((DependencyObject)settings.Content).OfType<Button>().Single(button=>Equals(button.Tag,"codex-spend-home-apply"));
            input.Text=fixture;
            if(config.CodexSpendHome is not null)throw new System.Exception("Typing a directory unexpectedly saved it.");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(app.Storage.LoadConfig().CodexSpendHome!=fixture)throw new System.Exception("Saved Codex data directory was not persisted.");
            var roots=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Single(block=>Equals(block.Tag,"codex-spend-roots"));
            if(!roots.Text.Contains(Path.Combine(fixture,"sessions"),System.StringComparison.Ordinal)||!roots.Text.Contains(Path.Combine(fixture,"archived_sessions"),System.StringComparison.Ordinal))throw new System.Exception("Displayed scan roots do not match the saved Codex home.");
            input.Text="relative-invalid-folder";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.CodexSpendHome!=fixture)throw new System.Exception("Invalid relative directory changed the saved home.");
            var status=Descendants((DependencyObject)settings.Content).OfType<TextBlock>().Single(block=>Equals(block.Tag,"codex-spend-home-status"));
            if(!status.Text.Contains("已存在的完整数据目录",System.StringComparison.Ordinal))throw new System.Exception("Invalid directory lacks actionable explanation.");
            input.Text=Path.Combine(fixture,"not-created");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.CodexSpendHome!=fixture)throw new System.Exception("Missing directory changed the saved home.");
            input.Text=Path.Combine(fixture,"sessions");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.CodexSpendHome!=fixture)throw new System.Exception("Selecting the sessions subfolder silently created an empty nested scan root.");
            Descendants((DependencyObject)settings.Content).OfType<Button>().Single(button=>Equals(button.Tag,"codex-spend-home-auto")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(config.CodexSpendHome is not null||app.Storage.LoadConfig().CodexSpendHome is not null)throw new System.Exception("Automatic detection did not clear the saved override.");
            if(Field<System.Threading.CancellationTokenSource?>(settings,"scanning") is not null||config.SpendEnabled)throw new System.Exception("Saving a scan directory launched a scan or enabled consumption tracking.");
            if(originalReport is null?File.Exists(reportPath):!File.ReadAllBytes(reportPath).SequenceEqual(originalReport))throw new System.Exception("Directory-only settings edited private spend reports.");
            System.Console.WriteLine("PASS: explicit Codex scan home validation, displayed session/archive roots, save/reset persistence, and no scan/report side effects.");
        }
        finally
        {
            settings.Close();config.CodexSpendHome=originalHome;config.SpendEnabled=originalEnabled;
            if(!Path.GetFullPath(fixture).StartsWith(Path.GetFullPath(app.Storage.Root)+Path.DirectorySeparatorChar,System.StringComparison.OrdinalIgnoreCase))throw new System.Exception("Fixture cleanup escaped its data directory.");
            Directory.Delete(fixture,true);
        }
    }
}
