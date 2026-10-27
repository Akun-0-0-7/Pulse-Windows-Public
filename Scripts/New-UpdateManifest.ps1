param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$DownloadUrl,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-preview\.(?:0|[1-9][0-9]*))?\z') { throw '版本号应为三个整数，可附加 -preview.N，例如 0.1.6-preview.6' }
$taskUrl = $null
if (-not [Uri]::TryCreate($DownloadUrl, [UriKind]::Absolute, [ref]$taskUrl) -or $taskUrl.Scheme -ne 'https' -or $taskUrl.UserInfo -or $taskUrl.Fragment) { throw '下载地址必须是不含登录信息或片段的 HTTPS 地址' }
$taskPackage = [IO.Path]::GetFullPath($PackagePath)
$taskOutput = [IO.Path]::GetFullPath($OutputPath)
if ($taskPackage.Equals($taskOutput, [StringComparison]::OrdinalIgnoreCase)) { throw '清单不能覆盖更新包' }
if (-not (Test-Path -LiteralPath $taskPackage -PathType Leaf)) { throw '找不到更新 ZIP 文件' }
if ([IO.Path]::GetExtension($taskPackage) -ine '.zip') { throw '更新包必须是 ZIP 文件' }
if ((Get-Item -LiteralPath $taskPackage -Force).Length -gt 268435456) { throw '更新包超过更新器的下载大小上限' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackage)
try {
    if ($taskArchive.Entries.Count -gt 10000) { throw '更新包的文件数量超过更新器上限' }
    $taskFiles = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    $taskPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $taskDirectories = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [long]$taskExpanded = 0
    foreach ($taskEntry in $taskArchive.Entries) {
        $taskIsDirectory = $taskEntry.FullName.EndsWith('/')
        $taskName = $taskEntry.FullName.TrimEnd('/')
        if (-not $taskName -or $taskName.Length -gt 1024 -or $taskName.Contains('\') -or $taskName -match '(^/|[<>:"|?*\x00-\x1f])') { throw '更新包包含无效路径' }
        foreach ($taskPart in $taskName.Split('/')) {
            if ($taskPart -in @('', '.', '..') -or $taskPart.Length -gt 255 -or $taskPart.EndsWith('.') -or $taskPart.EndsWith(' ') -or $taskPart -match '\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)') { throw '更新包包含不安全的 Windows 路径' }
        }
        $taskKind = ($taskEntry.ExternalAttributes -shr 16) -band 0xf000
        if ($taskKind -notin @(0, 0x8000, 0x4000) -or ($taskEntry.ExternalAttributes -band 0x400)) { throw '更新包包含链接或特殊文件' }
        if (-not $taskPaths.Add($taskName) -or (-not $taskIsDirectory -and $taskDirectories.Contains($taskName))) { throw '更新包包含重复或冲突路径' }
        $taskParent = ''
        $taskParts = $taskName.Split('/')
        for ($taskIndex = 0; $taskIndex -lt $taskParts.Length - 1; $taskIndex++) {
            if ($taskParent) { $taskParent += '/' }
            $taskParent += $taskParts[$taskIndex]
            if ($taskFiles.ContainsKey($taskParent)) { throw '更新包中的文件与目录路径冲突' }
            [void]$taskDirectories.Add($taskParent)
        }
        if ($taskIsDirectory) { [void]$taskDirectories.Add($taskName); continue }
        if ($taskEntry.Length -gt 268435456 -or $taskEntry.Length -gt 1073741824 - $taskExpanded -or ($taskEntry.Length -gt 4096 -and $taskEntry.Length / [double][Math]::Max(1, $taskEntry.CompressedLength) -gt 250)) { throw '更新包超过更新器的解压大小或压缩比上限' }
        $taskExpanded += $taskEntry.Length
        $taskFiles.Add($taskName, $taskEntry)
    }
    $taskRoots = New-Object 'System.Collections.Generic.List[string]'
    foreach ($taskName in $taskFiles.Keys) {
        if (($taskName -split '/')[-1] -ine 'Pulse.exe') { continue }
        $taskSlash = $taskName.LastIndexOf('/')
        $taskPrefix = if ($taskSlash -lt 0) { '' } else { $taskName.Substring(0, $taskSlash + 1) }
        if ($taskFiles.ContainsKey($taskPrefix + 'Pulse-cli.exe')) { $taskRoots.Add($taskPrefix) }
    }
    if ($taskRoots.Count -ne 1) { throw '更新包必须只有一个同时包含 Pulse.exe 和 Pulse-cli.exe 的发布目录' }
    $taskBuildInfo = $taskRoots[0] + 'build-info.json'
    if ($taskFiles.ContainsKey($taskBuildInfo)) {
        $taskEntry = $taskFiles[$taskBuildInfo]
        if ($taskEntry.Length -gt 65536) { throw '更新包的 build-info.json 过大' }
        $taskReader = New-Object IO.StreamReader($taskEntry.Open(), [Text.Encoding]::UTF8, $true)
        try { $taskBuild = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
        if ($taskBuild.version -isnot [string] -or $taskBuild.version -cne $Version) { throw '更新包 build-info.json 中的版本与请求版本不一致' }
    }
} finally { $taskArchive.Dispose() }
$taskHash = (Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash.ToLowerInvariant()
$taskJson = [ordered]@{version=$Version;url=$taskUrl.AbsoluteUri;sha256=$taskHash} | ConvertTo-Json
$taskParent = [IO.Path]::GetDirectoryName($taskOutput)
if (-not (Test-Path -LiteralPath $taskParent -PathType Container)) { throw '清单输出目录不存在' }
[IO.File]::WriteAllText($taskOutput, $taskJson + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
Write-Output $taskOutput
