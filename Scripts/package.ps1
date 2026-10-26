param([string]$Version = '0.1.5')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号应为三个整数，例如 0.1.5' }
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts'))
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskArtifacts "Pulse-Windows-Public-$Version-win-x64"))
if (-not $taskOutput.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '输出目录超出构建结果目录' }
if ((Test-Path -LiteralPath $taskArtifacts) -and ((Get-Item -LiteralPath $taskArtifacts -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '构建结果根目录是目录链接，已停止' }
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (Test-Path -LiteralPath $taskOutput) {
    if ((Get-Item -LiteralPath $taskOutput -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '发布输出目录是目录链接，已停止' }
    Remove-Item -LiteralPath $taskOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location $taskRoot
try {
    $taskPathMap = '-p:PathMap=' + $taskRoot + '=/_/Pulse-Windows'
    & $taskDotnet publish src\Pulse.Desktop -c Release -r win-x64 --self-contained true -o $taskOutput --nologo -p:DebugType=None -p:DebugSymbols=false $taskPathMap
    if ($LASTEXITCODE -ne 0) { throw '桌面程序发布失败' }
    & $taskDotnet publish src\Pulse.Cli -c Release -r win-x64 --self-contained true -o $taskOutput --nologo -p:DebugType=None -p:DebugSymbols=false $taskPathMap
    if ($LASTEXITCODE -ne 0) { throw '命令行程序发布失败' }
    foreach ($taskName in @('LICENSE','THIRD_PARTY_NOTICES.md','README.md','README.zh-CN.md','Docs')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskName) -Destination $taskOutput -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $taskOutput
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $taskOutput
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '安装到当前用户.cmd') -Destination $taskOutput
    $taskThirdParty = Join-Path $taskOutput 'Licenses'
    New-Item -ItemType Directory -Path $taskThirdParty -Force | Out-Null
    $taskWebView = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2\1.0.4258.31'
    Copy-Item -LiteralPath (Join-Path $taskWebView 'LICENSE.txt') -Destination (Join-Path $taskThirdParty 'WebView2-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $taskWebView 'NOTICE.txt') -Destination (Join-Path $taskThirdParty 'WebView2-NOTICE.txt')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Licenses\ZstdSharp-MIT.txt') -Destination $taskThirdParty
    $taskRuntimeRoot = Split-Path -Parent $taskDotnet
    foreach ($taskLicense in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        $taskLicensePath = Join-Path $taskRuntimeRoot $taskLicense
        if (Test-Path -LiteralPath $taskLicensePath) { Copy-Item -LiteralPath $taskLicensePath -Destination (Join-Path $taskThirdParty ('.NET-' + $taskLicense)) }
    }
    $taskCommit = & git -C $taskRoot rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { $taskCommit = $null }
    [ordered]@{version=$Version;edition='public';builtAt=[DateTimeOffset]::UtcNow.ToString('o');architecture='win-x64';sourceCommit=$taskCommit;sourceRepository='https://github.com/Akun-0-0-7/Pulse-Windows-Public';upstreamVersion='1.12.2';upstreamCommit='b035a7acf1aad9c1b6f9f895197ad1a5f58ea64e'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'build-info.json') -Encoding UTF8
    $taskZip = Join-Path $taskArtifacts "Pulse-Windows-Public-$Version-win-x64.zip"
    Compress-Archive -LiteralPath $taskOutput -DestinationPath $taskZip -Force
    Get-FileHash -LiteralPath $taskZip -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content -LiteralPath ($taskZip + '.sha256')
    Write-Output $taskZip
} finally { Pop-Location }
