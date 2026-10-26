namespace Pulse.Desktop;
/// <summary>Apply the selected UI language to the application's native dialogs.</summary>
public static class MessageBox
{
    public static MessageBoxResult Show(string text,string title)=>System.Windows.MessageBox.Show(Localization.Translate(text),Localization.Translate(title));
    public static MessageBoxResult Show(string text,string title,MessageBoxButton buttons)=>System.Windows.MessageBox.Show(Localization.Translate(text),Localization.Translate(title),buttons);
}
