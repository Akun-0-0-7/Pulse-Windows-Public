$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $taskRoot
try {
    & $taskDotnet build src\Pulse.Desktop -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw '桌面程序编译失败' }
    & $taskDotnet build src\Pulse.Cli -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw '命令行程序编译失败' }
    & $taskDotnet run --project tests\Pulse.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw '自动检查未通过' }
} finally { Pop-Location }
