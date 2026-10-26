using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Pulse.Core;

public sealed record UpdateManifest(Version Version,Uri DownloadUri,string Sha256);
public sealed record UpdateFile(string RelativePath,long Length,string Sha256);
public sealed record PreparedUpdate(string Version,string PreparationDirectory,string PackageRoot,IReadOnlyList<UpdateFile> Files,long DownloadBytes,string Sha256);
public sealed record UpdateLimits(long MaxDownloadBytes=268435456,long MaxExtractedBytes=1073741824,long MaxEntryBytes=268435456,int MaxEntries=10000,int MaxCompressionRatio=250);
public sealed record UpdateInstallation(string ScriptPath,string PlanPath,string LogPath);

public static class UpdatePackage
{
    public static UpdateManifest ParseManifest(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!Version.TryParse(JsonTools.String(root,"version"),out var version)||version.Build<0||!Uri.TryCreate(JsonTools.String(root,"url"),UriKind.Absolute,out var url))throw new InvalidDataException("更新清单需要有效的 version、url 和 sha256。");
            var result=new UpdateManifest(version,url,JsonTools.String(root,"sha256")??"");ValidateManifest(result);return result;
        }
        catch(JsonException exception){throw new InvalidDataException("更新清单不是有效 JSON。",exception);}
    }
    private static void ValidateManifest(UpdateManifest manifest)
    {
        RequireHttps(manifest.DownloadUri);
        if(manifest.Version.Build<0||!Regex.IsMatch(manifest.Sha256,"\\A[0-9a-fA-F]{64}\\z"))throw new InvalidDataException("更新清单的版本或 SHA256 校验值无效。");
    }
    private static void RequireHttps(Uri uri)
    {
        if(!uri.IsAbsoluteUri||uri.Scheme!=Uri.UriSchemeHttps||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Fragment))throw new InvalidDataException("更新地址必须是没有嵌入登录信息的 HTTPS 地址。");
    }
    public static async Task<UpdateManifest> ReadManifestAsync(HttpClient http,Uri manifestUri,CancellationToken cancellationToken=default)
    {
        RequireHttps(manifestUri);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response=await http.GetAsync(manifestUri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();RequireHttps(response.RequestMessage?.RequestUri??manifestUri);
        await using var body=await response.Content.ReadAsStreamAsync(timeout.Token);using var bytes=new MemoryStream();await CopyBounded(body,bytes,65536,timeout.Token);return ParseManifest(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
    }
    public static async Task<PreparedUpdate> PrepareAsync(HttpClient http,UpdateManifest manifest,string dataRoot,CancellationToken cancellationToken=default)
    {
        ValidateManifest(manifest);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response=await http.GetAsync(manifest.DownloadUri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();RequireHttps(response.RequestMessage?.RequestUri??manifest.DownloadUri);
        if(response.Content.Headers.ContentLength>new UpdateLimits().MaxDownloadBytes)throw new InvalidDataException("更新包超过下载大小上限。");
        await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);return await PrepareArchiveAsync(stream,manifest,dataRoot,timeout.Token);
    }
    public static async Task<PreparedUpdate> PrepareArchiveAsync(Stream archive,UpdateManifest manifest,string dataRoot,CancellationToken cancellationToken=default,UpdateLimits? limits=null)
    {
        ValidateManifest(manifest);limits??=new();if(limits.MaxDownloadBytes<=0||limits.MaxExtractedBytes<=0||limits.MaxEntryBytes<=0||limits.MaxEntries<=0||limits.MaxCompressionRatio<=0)throw new ArgumentOutOfRangeException(nameof(limits));
        var root=Path.GetFullPath(dataRoot);EnsureOrdinaryPath(root);var updates=Path.Combine(root,"updates");EnsureOrdinaryPath(updates);Directory.CreateDirectory(updates);
        var preparation=Path.Combine(updates,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(preparation);
        try
        {
            var zipPath=Path.Combine(preparation,"package.zip");long downloaded;
            await using(var output=new FileStream(zipPath,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))downloaded=await CopyBounded(archive,output,limits.MaxDownloadBytes,cancellationToken);
            string checksum;await using(var input=File.OpenRead(zipPath))checksum=Convert.ToHexString(await SHA256.HashDataAsync(input,cancellationToken));
            if(!checksum.Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新包的 SHA256 校验不一致，未解压或安装。");
            var content=Path.Combine(preparation,"content");Directory.CreateDirectory(content);var files=await Extract(zipPath,content,limits,cancellationToken);
            var roots=files.Where(f=>Path.GetFileName(f).Equals("Pulse.exe",StringComparison.OrdinalIgnoreCase)).Select(f=>Path.GetDirectoryName(f)!).Where(d=>files.Any(f=>Path.GetDirectoryName(f)!.Equals(d,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(f).Equals("Pulse-cli.exe",StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(roots.Count!=1)throw new InvalidDataException("更新包必须包含且只包含一个同时具有 Pulse.exe 和 Pulse-cli.exe 的发布目录。");
            var package=roots[0];var entries=new List<UpdateFile>();foreach(var file in files.Where(f=>Within(f,package)).Order(StringComparer.OrdinalIgnoreCase))
            {
                await using var input=File.OpenRead(file);entries.Add(new(Path.GetRelativePath(package,file).Replace('\\','/'),input.Length,Convert.ToHexString(await SHA256.HashDataAsync(input,cancellationToken))));
            }
            var prepared=new PreparedUpdate(manifest.Version.ToString(),preparation,package,entries,downloaded,checksum);File.WriteAllText(Path.Combine(preparation,"prepared-update.json"),JsonSerializer.Serialize(prepared,StorageJson.Options));return prepared;
        }
        catch
        {
            // Delete only this operation's freshly created, ordinary staging directory.
            if(Within(preparation,updates)&&Directory.Exists(preparation)){EnsureOrdinaryPath(preparation);Directory.Delete(preparation,true);}throw;
        }
    }
    private static async Task<long> CopyBounded(Stream source,Stream destination,long limit,CancellationToken token)
    {
        long total=0;var buffer=new byte[81920];int count;while((count=await source.ReadAsync(buffer,token))>0){if(count>limit-total)throw new InvalidDataException("更新数据超过大小上限。");await destination.WriteAsync(buffer.AsMemory(0,count),token);total+=count;}return total;
    }
    private static async Task<List<string>> Extract(string zipPath,string destination,UpdateLimits limits,CancellationToken token)
    {
        using var zip=ZipFile.OpenRead(zipPath);if(zip.Entries.Count>limits.MaxEntries)throw new InvalidDataException("更新包的文件数量超过上限。");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var fileNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long expanded=0;
        foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();var directory=entry.FullName.EndsWith('/');var relative=SafeRelative(entry.FullName.TrimEnd('/'));
            var kind=(entry.ExternalAttributes>>16)&0xF000;if(kind is not(0 or 0x8000 or 0x4000)||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("更新包包含链接或特殊文件。");
            if(!paths.Add(relative)||!directory&&directories.Contains(relative))throw new InvalidDataException("更新包包含重复或冲突路径。");
            var segments=relative.Split('/');var parent="";foreach(var segment in segments[..^1]){parent=parent.Length==0?segment:parent+"/"+segment;if(fileNames.Contains(parent))throw new InvalidDataException("更新包的文件与目录路径冲突。");directories.Add(parent);}
            if(directory){directories.Add(relative);continue;}fileNames.Add(relative);
            if(entry.Length>limits.MaxEntryBytes||entry.Length>limits.MaxExtractedBytes-expanded||entry.Length>4096&&entry.Length/(double)Math.Max(1,entry.CompressedLength)>limits.MaxCompressionRatio)throw new InvalidDataException("更新包展开大小或压缩比超过上限。");expanded+=entry.Length;
        }
        var files=new List<string>();foreach(var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();var path=Path.Combine(destination,SafeRelative(entry.FullName.TrimEnd('/')).Replace('/',Path.DirectorySeparatorChar));if(!Within(path,destination))throw new InvalidDataException("更新路径越过准备目录。");
            if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using var input=entry.Open();await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);var copied=await CopyBounded(input,output,entry.Length,token);if(copied!=entry.Length)throw new InvalidDataException("更新文件长度与清单不符。");files.Add(path);
        }
        return files;
    }
    private static string SafeRelative(string relative)
    {
        if(string.IsNullOrEmpty(relative)||relative.Length>1024||relative.Contains('\\')||relative.StartsWith('/')||relative.Any(ch=>ch<' '||"<>:\"|?*".Contains(ch)))throw new InvalidDataException("更新包包含无效路径。");
        foreach(var segment in relative.Split('/'))if(string.IsNullOrEmpty(segment)||segment is "." or ".."||segment.Length>255||segment.EndsWith(' ')||segment.EndsWith('.')||Regex.IsMatch(segment,"\\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|\\z)",RegexOptions.IgnoreCase))throw new InvalidDataException("更新包包含不安全的 Windows 文件名。");return relative;
    }
    private static bool Within(string path,string root)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void EnsureOrdinaryPath(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("更新路径包含目录链接，已停止。");
    }
    public static UpdateInstallation WriteInstaller(PreparedUpdate update,string targetDirectory,int processId,bool restart=true)
    {
        var target=Path.GetFullPath(targetDirectory);var preparation=Path.GetFullPath(update.PreparationDirectory);var package=Path.GetFullPath(update.PackageRoot);EnsureOrdinaryPath(target);EnsureOrdinaryPath(package);
        if(!Directory.Exists(target)||target.TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!Within(package,preparation)||Within(target,preparation)||target.Equals(preparation,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(target,"Pulse.exe"))||!File.Exists(Path.Combine(target,"Pulse-cli.exe")))throw new InvalidDataException("更新目标必须是现有 Pulse 发布目录。");
        if(update.Files.Count==0||update.Files.Select(f=>f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=update.Files.Count)throw new InvalidDataException("更新文件清单无效。");
        foreach(var file in update.Files){var relative=SafeRelative(file.RelativePath);var source=Path.Combine(package,relative.Replace('/',Path.DirectorySeparatorChar));EnsureOrdinaryPath(source);if(!Within(source,package)||!File.Exists(source)||new FileInfo(source).Length!=file.Length)throw new InvalidDataException("准备好的更新文件已经改变，请重新下载。");using var input=File.OpenRead(source);if(!Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("准备好的更新文件已经改变，请重新下载。");EnsureOrdinaryPath(Path.Combine(target,relative.Replace('/',Path.DirectorySeparatorChar)));}
        var installation=Path.Combine(preparation,"install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(installation);var log=Path.Combine(installation,"install.log");var plan=Path.Combine(installation,"plan.json");var script=Path.Combine(installation,"install.ps1");
        File.WriteAllText(plan,JsonSerializer.Serialize(new{update.Version,TargetDirectory=target,PackageRoot=package,Files=update.Files,ProcessId=processId,Restart=restart,BackupDirectory=Path.Combine(installation,"backup"),LogPath=log},StorageJson.Options));File.WriteAllText(script,InstallerScript,new System.Text.UTF8Encoding(true));return new(script,plan,log);
    }
    private const string InstallerScript="""
param([Parameter(Mandatory=$true)][string]$PlanFile)
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanFile -Raw -Encoding UTF8 | ConvertFrom-Json
$changed = New-Object 'System.Collections.Generic.List[object]'
function Log([string]$text) { Add-Content -LiteralPath $plan.LogPath -Value ((Get-Date -Format o) + ' ' + $text) -Encoding UTF8 }
function FileHash([string]$path) { $stream = [IO.File]::OpenRead($path); $algorithm = [Security.Cryptography.SHA256]::Create(); try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') } finally { $algorithm.Dispose(); $stream.Dispose() } }
function Ordinary([string]$path) {
    $cursor = [IO.Path]::GetFullPath($path)
    while ($cursor) { if (Test-Path -LiteralPath $cursor) { if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A target or source path contains a link' } }; $cursor = [IO.Path]::GetDirectoryName($cursor) }
}
function ChildPath([string]$root,[string]$relative) {
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Contains('\') -or $relative -match '(^/|[<>:"|?*\x00-\x1f])') { throw 'Invalid relative path' }
    foreach ($part in $relative.Split('/')) { if ($part -in @('', '.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ')) { throw 'Invalid path component' } }
    $absolute = [IO.Path]::GetFullPath((Join-Path $root $relative.Replace('/', '\')))
    if (-not $absolute.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped installation root' }
    Ordinary $absolute
    return $absolute
}
try {
    $target = [IO.Path]::GetFullPath($plan.TargetDirectory); $sourceRoot = [IO.Path]::GetFullPath($plan.PackageRoot)
    Ordinary $target; Ordinary $sourceRoot; Ordinary $plan.BackupDirectory
    if ([int]$plan.ProcessId -gt 0) { $parent = Get-Process -Id ([int]$plan.ProcessId) -ErrorAction SilentlyContinue; if ($parent -and -not $parent.WaitForExit(120000)) { throw 'Pulse did not exit within 120 seconds' } }
    $hash = [Security.Cryptography.SHA256]::Create(); try { $identity = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($target.ToLowerInvariant()))).Replace('-', '') } finally { $hash.Dispose() }
    $mutex = New-Object Threading.Mutex($false, ('Local\PulseWindowsUpdate-' + $identity)); $owned = $mutex.WaitOne(0)
    if (-not $owned) { throw 'Another update is already installing' }
    foreach ($file in @($plan.Files)) {
        $source = ChildPath $sourceRoot $file.RelativePath; $destination = ChildPath $target $file.RelativePath
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -ne [long]$file.Length -or (FileHash $source) -ine $file.Sha256) { throw 'Prepared file checksum changed' }
        if (Test-Path -LiteralPath $destination -PathType Container) { throw 'A package file conflicts with an existing directory' }
    }
    Log ('START ' + $plan.Version)
    foreach ($file in @($plan.Files)) {
        $source = ChildPath $sourceRoot $file.RelativePath; $destination = ChildPath $target $file.RelativePath; $backup = ChildPath $plan.BackupDirectory $file.RelativePath
        $existed = Test-Path -LiteralPath $destination -PathType Leaf
        if ($existed) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null; Copy-Item -LiteralPath $destination -Destination $backup -Force }
        $changed.Add([pscustomobject]@{Destination=$destination;Backup=$backup;Existed=$existed})
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-Item -LiteralPath $destination).Length -ne [long]$file.Length -or (FileHash $destination) -ine $file.Sha256) { throw 'Installed file checksum did not match the prepared package' }
    }
    Log ('SUCCESS ' + $plan.Version)
    if ($plan.Restart) { try { Start-Process -FilePath (Join-Path $target 'Pulse.exe') -WorkingDirectory $target -WindowStyle Hidden } catch { Log ('RESTART FAILURE ' + $_.Exception.Message) } }
    exit 0
} catch {
    Log ('FAILURE ' + $_.Exception.Message)
    $rollbackFailed = $false
    for ($index = $changed.Count - 1; $index -ge 0; $index--) {
        $item = $changed[$index]
        try { Ordinary $item.Destination; if ($item.Existed) { Copy-Item -LiteralPath $item.Backup -Destination $item.Destination -Force } elseif (Test-Path -LiteralPath $item.Destination -PathType Leaf) { Remove-Item -LiteralPath $item.Destination -Force } }
        catch { $rollbackFailed = $true; Log ('ROLLBACK FAILURE ' + $_.Exception.Message) }
    }
    Log 'ROLLBACK completed'
    if ($rollbackFailed) { exit 2 }; exit 1
} finally { if ($owned) { $mutex.ReleaseMutex() }; if ($mutex) { $mutex.Dispose() } }
""";
}
