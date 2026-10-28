using System.Reflection;

namespace Pulse.Desktop;

public static class AppReleaseInfo
{
    public const string StableManifestUrl="https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates.json";
    public const string PreviewManifestUrl="https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates-preview.json";
    public static string CurrentVersion
    {
        get
        {
            var assembly=typeof(PulseApp).Assembly;
            var value=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if(ReleaseVersion.TryParse(value,out var version)&&version is not null)return version.DisplayVersion;
            return assembly.GetName().Version?.ToString(3)??"0.0.0";
        }
    }
    public static string ManifestUrl(string? configured)=>string.IsNullOrWhiteSpace(configured)?StableManifestUrl:configured.Trim();
    public static bool IsNewer(string remoteVersion,string installedVersion)=>ReleaseVersion.Parse(remoteVersion).CompareTo(ReleaseVersion.Parse(installedVersion))>0;
}
