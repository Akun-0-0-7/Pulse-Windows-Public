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

`version` 是程序的实际发布版本，例如 `0.2.0` 或 `0.2.0-preview.1`；保留旧的四段数字版本兼容性。`url` 是发布 ZIP 压缩包的 HTTPS 地址；`sha256` 是整个 ZIP 文件的 SHA-256 摘要，以 64 个十六进制字符表示。下载内容必须与清单摘要完全一致，才会进入解压步骤。示例中的地址与摘要是格式说明，不是可用发布源。

清单不包含账号信息，也不调用账号额度或推理接口。在“关于与更新”选择更新源，再点击“检查更新”：

| 渠道 | 固定清单地址 | 内容 |
|---|---|---|
| 稳定版 | `https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates.json` | 仅正式版本。留空也使用此地址。 |
| 预览版 | `https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates-preview.json` | 体验版本，可包含正式发布后承接预览版的正式版本。 |

填写的是 JSON 清单地址；GitHub 仓库首页和 Releases 页面不能用作清单。选择渠道只保存地址，点击“检查更新”才开始检查。程序从当前程序集读取实际版本，不再写死为 0.1.5。相同版本与更旧版本不会下载安装；预览号按数字排序（preview.10 大于 preview.9），同一基础版本的正式版高于预览版，`+` 后的构建标识不影响比较。切换到稳定源不会自动降级。

**旧版首次升级：** 0.1.6-preview.5 及更早版本的检查逻辑不能正确处理预览标签和 GitHub 下载跳转。请从 [版本页](https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases) 手动下载新版，完整解压到新目录，从托盘退出旧版后运行新目录中的 Pulse.exe。无需删除个人数据。升级到修复后的版本，才可使用以上预览更新源。

## 维护者发布步骤

1. 提交并验证源码，以实际版本号打包，例如 `powershell -NoProfile -File Scripts/package.ps1 -Version 0.1.6-preview.6`。脚本会将版本同时写入桌面程序、命令行程序和 build-info.json。稳定版用 `0.1.6`，体验版用 `0.1.6-preview.N`。
2. 为该源码提交创建 GitHub Release，先上传完整 `win-x64.zip` 和 `.sha256` 文件，核对上传资产的大小与 SHA-256。体验版勾选 Pre-release；源码归档和许可证一起提供。
3. 用下面的脚本读取最终 ZIP、校验发布目录与版本并生成清单。`DownloadUrl` 必须是已上传资产的实际下载地址。脚本只生成文件，不上传、不提交、不安装。

```powershell
powershell -NoProfile -File Scripts/New-UpdateManifest.ps1 `
  -Version 0.1.6-preview.6 `
  -PackagePath artifacts/Pulse-Windows-Public-0.1.6-preview.6-win-x64.zip `
  -DownloadUrl https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases/download/v0.1.6-preview.6/Pulse-Windows-Public-0.1.6-preview.6-win-x64.zip `
  -OutputPath artifacts/updates-preview.json
```

4. **最后**把生成的 JSON 提交到公开仓库 `main` 的 `updates-preview.json`；正式发布则更新 `updates.json`。先发布并验证 ZIP，后更新清单，避免客户端读到不存在的包。预览版发布不改稳定源。
5. 以匿名方式重新读取公开清单，核对版本、下载地址、ZIP 摘要，并用旧版本与同版本各检查一次。只在需要替换已安装文件时，由用户在客户端确认安装。

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
| `src/Pulse.Core/ReleaseVersion.cs` | 比较数字版本、预览标签和构建标识。 |
| `src/Pulse.Core/UpdateHttpClient.cs` | 独立的公开更新下载连接；沿用代理设置，最多跟随 5 次 HTTPS 跳转。账号 API 连接保持独立。 |
| `Scripts/package.ps1` / `Scripts/New-UpdateManifest.ps1` | 构建带实际版本的运行包；从最终 ZIP 生成清单。 |
| `src/Pulse.Desktop/UpdateInstaller.cs` | 接收程序内的用户确认，检查安装目录可写性并隐藏启动安装器；返回安排状态、准备目录及日志路径。 |
| `tests/Pulse.Tests/UpdateTests.cs` | 用合成 ZIP、替代 HTTP 响应和临时安装目录检查校验、路径、大小、取消、逐文件替换及故障恢复。不会替换真实 Pulse 程序。 |

## 调用接口

```csharp
using var http = UpdateHttpClient.Create(config);
var manifest = await UpdatePackage.ReadManifestAsync(http, manifestUri, token);
if (ReleaseVersion.Parse(manifest.VersionText).CompareTo(ReleaseVersion.Parse(currentVersion)) <= 0) return;
var prepared = await UpdatePackage.PrepareAsync(http, manifest, dataRoot, token);
// 在界面展示 prepared.Version、Files.Count、PreparationDirectory 等准备结果。
// 仅在用户点击确认安装后执行下方调用。
var result = UpdateInstaller.Schedule(
    prepared, AppContext.BaseDirectory, Environment.ProcessId,
    confirmedByUser: true);
if (result.Scheduled) Quit();
else ShowMessage(result.Message + "\n" + result.PreparedDirectory);
```

示例中的 `config` 是当前配置；`http` 是按代理配置建立的更新专用 HTTP 客户端，允许 GitHub 所需的下载跳转且不携带账号凭据；`manifestUri` 是清单地址；`token` 是取消标记；`currentVersion` 是从程序集读取的实际发布版本字符串；`dataRoot` 是 Pulse 数据目录；`prepared` 是已经校验的准备结果；`result` 是安装安排结果；`Quit` 和 `ShowMessage` 表示桌面程序已有的退出及提示操作。

`UpdateInstallResult.Scheduled` 表示安装器已经启动，尚不表示文件替换完成；实际结果写入 `LogPath` 指向的安装日志。`PreparedDirectory` 是供用户查看、手动安装或检查日志的准备目录。调用 `Schedule` 时若 `confirmedByUser` 为 `false`，只返回等待确认的提示，不启动安装器。

底层 `PrepareArchiveAsync` 允许传入本机压缩包流和较小的 `UpdateLimits` 边界值，供离线样例检查使用。`WriteInstaller` 只生成计划与脚本，不执行替换；正式界面通过 `UpdateInstaller.Schedule` 安排安装。

## 验证范围

自动检查使用替代 HTTP 响应验证完整读取、清单上限及拒绝非 HTTPS 最终地址；版本用稳定、预览、四段旧格式和构建标识样例验证。安装检查实际运行 PowerShell 对临时目录内的合成程序文件执行复制，并故意设置目录冲突，验证旧文件恢复、新文件清除、非发布文件保留和日志输出。发布脚本检查使用合成 ZIP 和临时构建工具，验证版本传递、摘要与错误包拒绝。真实发布下载验证只读取公开清单、下载 ZIP、校验并隔离解压，不执行真实程序替换。
