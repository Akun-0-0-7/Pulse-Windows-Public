# Windows 应用内更新

Pulse 从设置中填写的 Windows 更新清单地址检查版本。清单、发布压缩包均使用 HTTPS。检查版本只读取清单；下载校验完成后显示准备结果，用户点击确认安装才启动安装器。安装器等待当前 Pulse 进程退出，再逐个替换发布文件并重新打开程序。

## 发布清单

清单是 UTF-8 编码的 JSON 文本，例如：

```json
{
  "version": "0.2.0",
  "url": "https://example.com/Pulse-Windows-0.2.0-win-x64.zip",
  "sha256": "将整个ZIP文件的64位十六进制SHA256校验值放在这里"
}
```

`version` 是至少具有主版本、次版本、修订版本三个数字部分的 Windows 程序版本；`url` 是发布 ZIP 压缩包的 HTTPS 地址；`sha256` 是整个 ZIP 文件的 SHA-256 摘要，以 64 个十六进制字符表示。SHA-256 是文件校验算法；下载内容必须与清单摘要完全一致，才会进入解压步骤。示例中的地址与摘要是格式说明，不是可用发布源。

清单不包含账号信息，也不调用账号额度或推理接口。公开更新清单地址为 `https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates.json`。在“关于与更新”填写此地址后可检查版本；未配置地址时使用版本页的手动安装包。

## 准备与安装

1. 清单最多读取 64 KiB，完整读取截止时间为 30 秒。
2. 压缩包最多下载 256 MiB，完整下载截止时间为 10 分钟。
3. ZIP 必须通过 SHA-256 校验。解压总量最多 1 GiB，单文件最多 256 MiB，最多 10,000 个条目；大于 4 KiB 的文件展开比不得超过 250 倍。KiB、MiB、GiB 分别表示 1,024 字节、1,024 KiB、1,024 MiB。
4. 解压拒绝绝对路径、上级目录、Windows 保留名称、数据流路径、大小写冲突、链接、特殊文件与文件/目录冲突。准备路径及其已存在的祖先目录也不能是目录链接。
5. 文件只解压到数据根目录下 `updates/<随机编号>/content`。其中必须只有一个同时包含 `Pulse.exe` 和 `Pulse-cli.exe` 的发布根目录；安装只使用这个发布根目录的文件清单。
6. 准备结果记录版本、下载字节数、压缩包摘要、发布根目录、每个文件的长度与摘要。此时安装目录尚未替换。
7. 用户确认后，安装器先检查目标可写性。目标不可写时保留准备目录并返回手动安装说明，不请求管理员权限。
8. 隐藏的 PowerShell 安装器等待当前进程退出，最多等待 120 秒。安装时再次核对来源文件及目标路径，仅备份和复制新版本清单中的文件；不递归删除安装目录，不复制数据目录中的账号设置、凭据或用量缓存。
9. 每个复制完成的文件再次校验长度和摘要。发生复制异常时，逐个恢复已备份的旧文件，并删除本次新建的文件；其它文件保持原样。空目录可能保留。

每个安装任务的日志和备份保存在准备目录内 `install-<随机编号>/`。`install.log` 记录安装开始、成功、复制错误、恢复结果及重新启动结果。复制失败时会保留原始备份，供按日志检查。准备文件在安装前发生变化，会终止安装。

## 文件职责

| 文件 | 作用 |
|---|---|
| `src/Pulse.Core/UpdatePackage.cs` | 解析清单、有界下载、校验、检查 ZIP、隔离解压、生成逐文件安装计划和 PowerShell 安装器。 |
| `src/Pulse.Desktop/UpdateInstaller.cs` | 接收程序内的用户确认，检查安装目录可写性并隐藏启动安装器；返回安排状态、准备目录及日志路径。 |
| `tests/Pulse.Tests/UpdateTests.cs` | 用合成 ZIP、替代 HTTP 响应和临时安装目录检查校验、路径、大小、取消、逐文件替换及故障恢复。不会替换真实 Pulse 程序。 |

## 调用接口

```csharp
var manifest = await UpdatePackage.ReadManifestAsync(http, manifestUri, token);
if (manifest.Version <= currentVersion) return;
var prepared = await UpdatePackage.PrepareAsync(http, manifest, dataRoot, token);
// 在界面展示 prepared.Version、Files.Count、PreparationDirectory 等准备结果。
// 仅在用户点击确认安装后执行下方调用。
var result = UpdateInstaller.Schedule(
    prepared, AppContext.BaseDirectory, Environment.ProcessId,
    confirmedByUser: true);
if (result.Scheduled) Quit();
else ShowMessage(result.Message + "\n" + result.PreparedDirectory);
```

示例中的 `http` 是按当前代理配置建立的 HTTP 客户端；`manifestUri` 是清单地址；`token` 是取消标记；`currentVersion` 是当前程序版本；`dataRoot` 是 Pulse 数据目录；`prepared` 是已经校验的准备结果；`result` 是安装安排结果；`Quit` 和 `ShowMessage` 表示桌面程序已有的退出及提示操作。

`UpdateInstallResult.Scheduled` 表示安装器已经启动，尚不表示文件替换完成；实际结果写入 `LogPath` 指向的安装日志。`PreparedDirectory` 是供用户查看、手动安装或检查日志的准备目录。调用 `Schedule` 时若 `confirmedByUser` 为 `false`，只返回等待确认的提示，不启动安装器。

底层 `PrepareArchiveAsync` 允许传入本机压缩包流和较小的 `UpdateLimits` 边界值，供离线样例检查使用。`WriteInstaller` 只生成计划与脚本，不执行替换；正式界面通过 `UpdateInstaller.Schedule` 安排安装。

## 验证范围

更新检查使用替代 HTTP 响应验证完整读取、清单上限及拒绝非 HTTPS 最终地址。安装检查实际运行 PowerShell 对临时目录内的合成程序文件执行复制，并故意设置目录冲突，验证旧文件恢复、新文件清除、非发布文件保留和日志输出。本轮没有连接真实发布服务，也没有执行真实程序替换。
