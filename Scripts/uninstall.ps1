$ErrorActionPreference = 'Stop'
$taskExpected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\PulseWindows'))
$taskActual = [IO.Path]::GetFullPath($PSScriptRoot)
if ($taskExpected.TrimEnd('\') -ine $taskActual.TrimEnd('\')) { throw '请从已安装的 PulseWindows 目录运行卸载脚本' }
if ((Get-Item -LiteralPath $taskActual -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '卸载目录是目录链接，已停止' }
$taskRunning = Get-Process -Name Pulse,Pulse-cli -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($taskActual + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($taskRunning) { throw '请先从托盘退出 Pulse，再卸载' }
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Pulse Windows.lnk'
if (Test-Path -LiteralPath $taskShortcut) { $taskShell=New-Object -ComObject WScript.Shell; $taskLink=$taskShell.CreateShortcut($taskShortcut); if ($taskLink.TargetPath -ieq (Join-Path $taskActual 'Pulse.exe')) { Remove-Item -LiteralPath $taskShortcut } }
$taskRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskStartup=(Get-ItemProperty -LiteralPath $taskRun -Name 'PulseWindows' -ErrorAction SilentlyContinue).PulseWindows
if ($taskStartup -like ('*' + (Join-Path $taskActual 'Pulse.exe') + '*')) { Remove-ItemProperty -LiteralPath $taskRun -Name 'PulseWindows' -ErrorAction SilentlyContinue }
$taskProtocol = 'HKCU:\Software\Classes\pulse'
if (Test-Path -LiteralPath $taskProtocol) { $taskCommand = (Get-ItemProperty -LiteralPath ($taskProtocol + '\shell\open\command')).'(default)'; if ($taskCommand -like ('*' + $taskActual + '*')) { Remove-Item -LiteralPath $taskProtocol -Recurse } }
Remove-Item -LiteralPath $taskActual -Recurse -Force
Write-Host '程序已卸载；账号设置与本机加密凭据仍保留在 LocalAppData\PulseWindows。'
