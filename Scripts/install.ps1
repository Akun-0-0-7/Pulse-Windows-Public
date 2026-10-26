$ErrorActionPreference = 'Stop'
$taskSource = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $taskSource 'Pulse.exe'))) { throw '请从解压后的发布包运行安装脚本' }
$taskPrograms = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$taskTarget = [IO.Path]::GetFullPath((Join-Path $taskPrograms 'PulseWindows'))
if (-not $taskTarget.StartsWith($taskPrograms + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '安装目录校验失败' }
if ($taskSource.TrimEnd('\') -ieq $taskTarget.TrimEnd('\')) { throw '当前已经位于安装目录，可直接运行 Pulse.exe' }
if ((Test-Path -LiteralPath $taskTarget) -and ((Get-Item -LiteralPath $taskTarget -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目录是目录链接，请选择普通安装目录' }
$taskRunning = Get-Process -Name Pulse,Pulse-cli -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($taskTarget + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($taskRunning) { throw '请先从托盘退出 Pulse，再更新安装文件' }
New-Item -ItemType Directory -Path $taskTarget -Force | Out-Null
Get-ChildItem -LiteralPath $taskSource -Force | Copy-Item -Destination $taskTarget -Recurse -Force
$taskShell = New-Object -ComObject WScript.Shell
$taskMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Pulse Windows.lnk'
$taskShortcut = $taskShell.CreateShortcut($taskMenu)
$taskShortcut.TargetPath = Join-Path $taskTarget 'Pulse.exe'
$taskShortcut.WorkingDirectory = $taskTarget
$taskShortcut.Description = 'Pulse Windows · AI 额度监视器'
$taskShortcut.Save()
Write-Host "已安装到 $taskTarget。请在开始菜单打开 Pulse Windows。"
