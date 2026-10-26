param([string]$PackagePath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$taskArtifacts = Join-Path $taskRoot 'artifacts'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
$taskPreviousData = $env:PULSE_DATA_DIR
$taskPreviousDotnet = $env:DOTNET_ROOT
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'build.ps1') | Tee-Object -FilePath (Join-Path $taskArtifacts 'build-test.log')
    & $taskDotnet run --project tests\Pulse.DesktopChecks -c Release -- (Join-Path $taskArtifacts 'desktop-checks')
    if ($LASTEXITCODE -ne 0) { throw '报告图片像素检查失败' }
    & $taskDotnet run --project tests\Pulse.RailChecks -c Release -- (Join-Path $taskArtifacts 'rail-checks')
    if ($LASTEXITCODE -ne 0) { throw '浮动栏逻辑检查失败' }
    $taskCatalogs = @{}
    foreach ($taskLanguage in @('zh-CN','zh-Hant','en','ja','ko','ru')) {
        $taskResource = Get-Content -LiteralPath (Join-Path $taskRoot "src\Pulse.Desktop\Resources\Localizations\$taskLanguage.json") -Raw -Encoding UTF8 | ConvertFrom-Json
        $taskCatalogs[$taskLanguage] = $taskResource
    }
    $taskBaseKeys = @($taskCatalogs['zh-CN'].PSObject.Properties.Name | Sort-Object)
    foreach ($taskLanguage in $taskCatalogs.Keys) {
        if (Compare-Object $taskBaseKeys @($taskCatalogs[$taskLanguage].PSObject.Properties.Name | Sort-Object)) { throw "翻译键集合不一致：$taskLanguage" }
        foreach ($taskKey in $taskBaseKeys) {
            $taskExpected = @([regex]::Matches($taskKey,'\{\d+\}') | ForEach-Object { $_.Value } | Sort-Object) -join ','
            $taskActual = @([regex]::Matches([string]$taskCatalogs[$taskLanguage].$taskKey,'\{\d+\}') | ForEach-Object { $_.Value } | Sort-Object) -join ','
            if ($taskExpected -ne $taskActual) { throw "翻译参数不一致：$taskLanguage / $taskKey" }
        }
    }
    $env:PULSE_DATA_DIR = Join-Path $taskArtifacts ('startup-test-data-' + [Guid]::NewGuid().ToString('N'))
    $env:DOTNET_ROOT = Split-Path -Parent $taskDotnet
    New-Item -ItemType Directory -Path $env:PULSE_DATA_DIR -Force | Out-Null
    '{"schemaVersion":1,"onboardingCompleted":true,"accounts":[{"id":"link-check","providerId":"codex","enabled":false}]}' | Set-Content -LiteralPath (Join-Path $env:PULSE_DATA_DIR 'settings.json') -Encoding UTF8
    if ($PackagePath) {
        $taskExecutable = Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'Pulse.exe'
        $taskPrefix = ''
    } else {
        $taskExecutable = $taskDotnet
        $taskPrefix = '"' + (Join-Path $taskRoot 'src\Pulse.Desktop\bin\Release\net10.0-windows\Pulse.dll') + '" '
    }
    $taskProcess = Start-Process -FilePath $taskExecutable -ArgumentList ($taskPrefix + '--startup-check') -WindowStyle Hidden -PassThru
    try {
        $taskDeadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath (Join-Path $env:PULSE_DATA_DIR 'startup-ready.json')) -and [DateTime]::UtcNow -lt $taskDeadline -and -not $taskProcess.HasExited) { Start-Sleep -Milliseconds 100 }
        if (-not (Test-Path -LiteralPath (Join-Path $env:PULSE_DATA_DIR 'startup-ready.json'))) { throw '原生启动未进入检查消息循环' }
        $taskSecond = Start-Process -FilePath $taskExecutable -ArgumentList ($taskPrefix + 'pulse://account/link-check') -WindowStyle Hidden -PassThru
        if (-not $taskSecond.WaitForExit(10000)) { $taskSecond.Kill(); throw '第二实例转发超时' }
        if (-not $taskProcess.WaitForExit(20000)) { throw '原生启动检查未正常退出' }
        $taskStartup = Get-Content -LiteralPath (Join-Path $env:PULSE_DATA_DIR 'startup-check.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($taskProcess.ExitCode -ne 0 -or -not $taskStartup.railLoaded -or -not $taskStartup.dispatcherRunning -or $taskStartup.navigation -ne 'account:link-check') { throw '原生启动或常驻账号链接转发检查失败' }
    } finally { if (-not $taskProcess.HasExited) { $taskProcess.Kill() }; $taskProcess.Dispose() }
    foreach ($taskScript in Get-ChildItem -LiteralPath $PSScriptRoot -Filter *.ps1) {
        $taskTokens = $null; $taskParseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($taskScript.FullName,[ref]$taskTokens,[ref]$taskParseErrors)
        if ($taskParseErrors.Count) { throw ('脚本语法错误：' + $taskScript.Name) }
    }
    [ordered]@{passed=$true;at=[DateTimeOffset]::UtcNow.ToString('o');resourceKeys=$taskBaseKeys.Count;nativeStartup=$true;secondInstanceAccountLink=$true;realAccountRequests=0;realPointerInput=$false;startupData=$env:PULSE_DATA_DIR} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskArtifacts 'verification.json') -Encoding UTF8
    Write-Output 'PASS: build, core checks, report pixels, rail logic, six languages, native startup, account-link forwarding and script syntax.'
} finally { $env:PULSE_DATA_DIR=$taskPreviousData; $env:DOTNET_ROOT=$taskPreviousDotnet; Pop-Location }
