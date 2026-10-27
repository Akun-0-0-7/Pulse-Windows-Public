using System.IO.Compression;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulse.Core;
namespace Pulse.Tests;

public static class UpdatePublishingTests
{
    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Scripts", "package.ps1"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate publishing scripts");
    }
    private static string Temporary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pulse-publishing-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
    }
    private static Dictionary<string, string> PowerShellEnvironment()
    {
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value!, StringComparer.OrdinalIgnoreCase);
        // A dotnet process launched by pwsh can otherwise pass PowerShell 7's module paths to Windows PowerShell 5.1.
        environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
        return environment;
    }
    private static string Archive(string directory, params (string Name, string Value)[] files)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files) { using var output = new StreamWriter(zip.CreateEntry(file.Name).Open(), new UTF8Encoding(false)); output.Write(file.Value); }
        return path;
    }
    [Check] public static void UpdateTransportUsesBoundedRedirectsWithoutAmbientCredentials()
    {
        foreach (var mode in new[] { "system", "none", "custom" })
        {
            using var handler = UpdateHttpClient.CreateHandler(new AppConfig { ProxyMode = mode, ProxyAddress = "http://127.0.0.1:7897" });
            Assert.True(handler.AllowAutoRedirect, "Public release downloads need HTTPS redirect support");
            Assert.Equal(5, handler.MaxAutomaticRedirections);
            Assert.Equal(mode != "none", handler.UseProxy);
            Assert.True(!handler.UseDefaultCredentials && handler.Credentials is null && handler.DefaultProxyCredentials is null, "Updates must not use ambient credentials");
            if (mode == "custom") Assert.Equal(new Uri("http://127.0.0.1:7897"), handler.Proxy!.GetProxy(new Uri("https://github.com")));
            else Assert.True(handler.Proxy is null, "System/direct mode must retain the platform proxy choice");
        }
        using var client = UpdateHttpClient.Create(new AppConfig());
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.True(client.DefaultRequestHeaders.Authorization is null, "Updater must not attach account authorization");
        using var account = UsageEngine.CreateHttp(new AppConfig());
        Assert.Equal(TimeSpan.FromSeconds(25), account.Timeout);
    }
    private static async Task<(int ExitCode, string Text)> Manifest(string version, string archive, string url, string output)
    {
        var result = await ProcessRunner.RunAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Repository(), "Scripts", "New-UpdateManifest.ps1"), "-Version", version, "-PackagePath", archive, "-DownloadUrl", url, "-OutputPath", output], TimeSpan.FromSeconds(20), environment: PowerShellEnvironment());
        return (result.ExitCode, result.Output + " | " + result.Error);
    }
    [Check] public static async Task ManifestGeneratorHashesStableAndPreviewPackagesWithoutChangingTheArchive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Temporary(); try
        {
            foreach (var version in new[] { "0.1.6", "0.1.6-preview.6" })
            {
                var archive = Archive(directory, ("release/Pulse.exe", "synthetic desktop"), ("release/Pulse-cli.exe", "synthetic cli"), ("release/build-info.json", "{\"version\":\"" + version + "\"}"));
                var original = File.ReadAllBytes(archive); var output = Path.Combine(directory, "updates.json");
                var result = await Manifest(version, archive, "https://github.com/example/pulse/releases/download/v" + version + "/Pulse.zip", output);
                Assert.Equal(0, result.ExitCode); Assert.True(File.Exists(output), result.Text);
                using var json = JsonDocument.Parse(File.ReadAllText(output, Encoding.UTF8)); var root = json.RootElement;
                Assert.Equal(version, root.GetProperty("version").GetString());
                Assert.Equal("https://github.com/example/pulse/releases/download/v" + version + "/Pulse.zip", root.GetProperty("url").GetString());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(), root.GetProperty("sha256").GetString()!.ToLowerInvariant());
                Assert.Equal(3, root.EnumerateObject().Count()); Assert.True(original.SequenceEqual(File.ReadAllBytes(archive)), "Generating a manifest changed the archive");
            }
        } finally { Directory.Delete(directory, true); }
    }
    [Check] public static async Task ManifestGeneratorAcceptsRootPackageWithoutBuildInfo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Temporary(); try
        {
            var archive = Archive(directory, ("Pulse.exe", "synthetic desktop"), ("Pulse-cli.exe", "synthetic cli"));
            var result = await Manifest("0.1.6-preview.6", archive, "https://updates.invalid/Pulse.zip", Path.Combine(directory, "updates.json"));
            Assert.True(result.ExitCode == 0, result.Text);
        } finally { Directory.Delete(directory, true); }
    }
    [Check] public static async Task ManifestGeneratorRejectsInvalidVersionsUrlsAndBuildVersionMismatch()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Temporary(); try
        {
            var archive = Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"));
            foreach (var version in new[] { "0.1", "0.1.6.0", "0.1.6-preview", "0.1.6-rc.1", "0.1.6-preview.6+build", "0.1.6\n", "01.1.6", "0.1.6-preview.06" })
            {
                var output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"); var result = await Manifest(version, archive, "https://updates.invalid/a.zip", output);
                Assert.True(result.ExitCode != 0 && !File.Exists(output), "Invalid or mismatched version was published: " + version);
            }
            foreach (var url in new[] { "http://updates.invalid/a.zip", "https://user:secret@updates.invalid/a.zip", "https://updates.invalid/a.zip#fragment", "file:///a.zip" })
            {
                var output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"); var result = await Manifest("0.1.6-preview.6", archive, url, output);
                Assert.True(result.ExitCode != 0 && !File.Exists(output), "Unsafe URL was published: " + url);
            }
            var mismatch = Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"), ("build-info.json", "{\"version\":\"0.1.6-preview.5\"}"));
            var mismatchOutput = Path.Combine(directory, "mismatch.json"); var mismatchResult = await Manifest("0.1.6-preview.6", mismatch, "https://updates.invalid/a.zip", mismatchOutput);
            Assert.True(mismatchResult.ExitCode != 0 && !File.Exists(mismatchOutput), "Mismatched build-info version was published");
        } finally { Directory.Delete(directory, true); }
    }
    [Check] public static async Task ManifestGeneratorRejectsAmbiguousUnsafeAndIncompletePackages()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Temporary(); try
        {
            var archives = new[] {
                Archive(directory, ("Pulse.exe", "desktop")),
                Archive(directory, ("a/Pulse.exe", "desktop"), ("b/Pulse-cli.exe", "cli")),
                Archive(directory, ("a/Pulse.exe", "desktop"), ("a/Pulse-cli.exe", "cli"), ("b/Pulse.exe", "desktop"), ("b/Pulse-cli.exe", "cli")),
                Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"), ("PULSE.EXE", "duplicate")),
                Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"), ("../outside.txt", "unsafe")),
                Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"), ("build-info.json", "{}")),
                Archive(directory, ("Pulse.exe", "desktop"), ("Pulse-cli.exe", "cli"), ("build-info.json", "{\"version\":[\"0.1.6-preview.6\"]}"))
            };
            foreach (var archive in archives)
            {
                var output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"); var result = await Manifest("0.1.6-preview.6", archive, "https://updates.invalid/a.zip", output);
                Assert.True(result.ExitCode != 0 && !File.Exists(output), "Unusable package was published: " + archive);
            }
        } finally { Directory.Delete(directory, true); }
    }
    [Check] public static async Task StandardPackagerPassesPreviewVersionToBothPublishes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Temporary(); try
        {
            foreach (var child in new[] { "Scripts", "Docs", "Licenses", "src/Pulse.Desktop/obj", "runtime/Microsoft.NETCore.App.Runtime.win-x64/10.0.0", "runtime/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.0", "profile/.nuget/packages/microsoft.web.webview2/1.0.4258.31" }) Directory.CreateDirectory(Path.Combine(directory, child));
            File.WriteAllText(Path.Combine(directory, "Scripts", "package.ps1"), File.ReadAllText(Path.Combine(Repository(), "Scripts", "package.ps1")), new UTF8Encoding(true));
            foreach (var child in new[] { "LICENSE", "THIRD_PARTY_NOTICES.md", "README.md", "README.zh-CN.md", "Scripts/install.ps1", "Scripts/uninstall.ps1", "Scripts/安装到当前用户.cmd", "Licenses/ZstdSharp-MIT.txt", "Licenses/Lobe-Icons-MIT.txt", "runtime/Microsoft.NETCore.App.Runtime.win-x64/10.0.0/LICENSE.TXT", "runtime/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.0/LICENSE.TXT", "profile/.nuget/packages/microsoft.web.webview2/1.0.4258.31/LICENSE.txt", "profile/.nuget/packages/microsoft.web.webview2/1.0.4258.31/NOTICE.txt" }) File.WriteAllText(Path.Combine(directory, child), "synthetic fixture");
            File.WriteAllText(Path.Combine(directory, "src/Pulse.Desktop/obj/project.assets.json"), JsonSerializer.Serialize(new { packageFolders = new Dictionary<string, object> { [Path.Combine(directory, "runtime")] = new { } } }));
            var harness = Path.Combine(directory, "harness.ps1"); File.WriteAllText(harness, """
param([string]$Root)
$ErrorActionPreference = 'Stop'
$env:USERPROFILE = Join-Path $Root 'profile'
function global:Get-Command { param([string]$Name) if ($Name -eq 'dotnet') { [pscustomobject]@{Source='Invoke-FixtureDotnet'} } else { Microsoft.PowerShell.Core\Get-Command @PSBoundParameters } }
function global:git { $global:LASTEXITCODE=0; 'fixture-commit' }
function global:Invoke-FixtureDotnet {
    [ordered]@{arguments=@($args)} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $Root 'calls.jsonl') -Encoding UTF8
    $destination = $args[[Array]::IndexOf($args, '-o') + 1]
    Set-Content -LiteralPath (Join-Path $destination 'Pulse.exe') -Value 'synthetic desktop'
    Set-Content -LiteralPath (Join-Path $destination 'Pulse-cli.exe') -Value 'synthetic cli'
    '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}' | Set-Content -LiteralPath (Join-Path $destination 'Pulse.runtimeconfig.json') -Encoding UTF8
    $global:LASTEXITCODE=0
}
& (Join-Path $Root 'Scripts/package.ps1') -Version '0.1.6-preview.6'
""", new UTF8Encoding(true));
            var result = await ProcessRunner.RunAsync("powershell.exe", ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", harness, "-Root", directory], TimeSpan.FromSeconds(30), environment: PowerShellEnvironment());
            Assert.True(result.ExitCode == 0, result.Output + " | " + result.Error);
            var calls = File.ReadAllLines(Path.Combine(directory, "calls.jsonl")); Assert.Equal(2, calls.Length);
            foreach (var call in calls) { using var json = JsonDocument.Parse(call); Assert.True(json.RootElement.GetProperty("arguments").EnumerateArray().Any(value => value.GetString() == "-p:Version=0.1.6-preview.6"), "A published executable did not receive the requested preview version"); }
            var package = Path.Combine(directory, "artifacts/Pulse-Windows-Public-0.1.6-preview.6-win-x64.zip");
            Assert.True(File.Exists(package), "Preview archive was not produced");
            string[] entries; using (var zip = ZipFile.OpenRead(package)) entries = zip.Entries.Select(entry => entry.FullName).ToArray();
            var manifestPath = Path.Combine(directory, "updates-preview.json");
            var manifest = await Manifest("0.1.6-preview.6", package, "https://updates.invalid/Pulse.zip", manifestPath);
            Assert.True(manifest.ExitCode == 0, "The standard ZIP could not produce an update manifest: " + manifest.Text);
            Assert.True(entries.Length > 0 && entries.All(name => !name.Contains('\\') && name.StartsWith("Pulse-Windows-Public-0.1.6-preview.6-win-x64/", StringComparison.Ordinal)), "Standard ZIP entries must use safe forward-slash paths beneath one release root");
            await using var stream = File.OpenRead(package);
            var prepared = await UpdatePackage.PrepareArchiveAsync(stream, UpdatePackage.ParseManifest(File.ReadAllText(manifestPath)), Path.Combine(directory, "staging"));
            Assert.Equal("synthetic desktop" + Environment.NewLine, File.ReadAllText(Path.Combine(prepared.PackageRoot, "Pulse.exe")));
        } finally { Directory.Delete(directory, true); }
    }
}
