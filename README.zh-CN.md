# Pulse Windows 使用说明

Pulse Windows 在桌面悬浮栏和系统托盘中显示 AI 服务额度、余额和重置倒计时。公开版本为 **0.1.5**，基于 [qunqin24/Pulse](https://github.com/qunqin24/Pulse) 1.12.2 移植。

支持 Windows 11、Windows 10 企业版长期服务版（LTSC）21H2 的 64 位 Intel/AMD 电脑。

## 安装与首次运行

1. 从[版本下载页](https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases/latest)下载 `Pulse-Windows-Public-0.1.5-win-x64.zip`。
2. 完整解压，保留同目录的组件文件。
3. 双击 `Pulse.exe` 即可运行。也可双击 `安装到当前用户.cmd`，安装到当前用户应用目录并创建开始菜单入口，无需管理员权限。
4. 在欢迎页选择需要监控的服务，点击“开始监控”。也可点击“稍后设置”，之后到“服务与账号”添加。

首次运行所有服务均未勾选，自动发送消息、开机启动和消耗统计默认关闭。每位使用者在自己的电脑上选择服务并登录。

运行包已包含 .NET 运行环境。网页登录使用 Microsoft Edge WebView2；缺少该组件时，登录窗口提供微软官方安装入口。

## 连接账号与代理

**Codex：** 可使用已经登录的本机 Codex 工具，也可在账号页点击“通过服务商登录”。需要先在页面顶部勾选“启用这个账号”。本机工具通道要求本机安装的工具能够正常工作；自动选择通道会在接口读取失败或超时后尝试本机工具。

**Claude：** 可使用本机工具登录、网页授权或桌面会话，具体入口见账号设置。账号页的“设置帮助”可打开相应服务说明。

**DeepSeek 及其他密钥服务：** 在对应账号页填写自己的 API 密钥。API 是程序访问服务的接口；API 账户余额与网页聊天订阅分别处理。需要自建网关的服务还应填写服务地址。

在“网络与刷新”中选择“跟随系统”“自定义代理”或“直连”。已在 Windows 配置代理时可用“跟随系统”；自定义代理填写实际地址，例如 `http://127.0.0.1:7890`，其中 `127.0.0.1` 表示本机，`7890` 是示例端口，需要替换为自己代理软件提供的端口。

额度读取失败时先看账号卡片和“连接诊断”的具体提示。程序保留上次有效读数并标注原读取时间，旧缓存不代表已经取得最新额度。服务商未返回的窗口或数值会保持缺失，不补成零。

## 悬浮栏与刷新

- 右键悬浮栏或托盘图标打开设置；点击“刷新额度”查询已启用账号。
- 在“位置与行为”开启“停靠时自动收起”，悬浮栏停靠屏幕边缘后，鼠标离开会收成细条，移回展开。自由悬浮时保持展开。
- 在“外观与圆环”选择已用或剩余、主额度窗口和圆环样式。
- 关闭设置窗口后，程序继续运行。完全退出请使用托盘或悬浮栏菜单的“退出”。

活跃刷新间隔可选 10 秒、30 秒、1 分钟、2 分钟、5 分钟、10 分钟、15 分钟、30 分钟。服务正在工作、设置窗口打开，或电脑最近两分钟有操作时使用所选间隔；电脑空闲达到 2、5、10 分钟后，分别放慢至至少 5、10、30 分钟。上一轮读取未完成时不会叠加新请求。

## 五小时额度重置后发送 hi

该功能默认关闭，仅供符合本机登录条件的 Codex、Claude Code 主账号开启。`hi` 是通过官方本机工具提交的一条普通消息，**会使用账号额度**。

0.1.5 按已记录的五小时重置时间倒计时执行，到点且处于允许时段时发送一次，再请求刷新额度。每周窗口不单独发送。

在账号页开启“5 小时重置后发送一次 hi 并刷新额度”；需要全天执行时，到“网络与刷新”把允许开始和结束时间都设为 `00`。同一周期只认领一次，发送失败也不会反复重发。

详细规则见[五小时自动发送说明](Docs/five-hour-reset.zh-CN.md)，失败状态见[发送失败排查](Docs/reset-send-hotfix.zh-CN.md)。

## 消耗、报告与服务状态

开启“Token 消耗”后，程序整理所选本机来源并生成日、模型和客户端统计。Token 是模型处理文本的计数单位。公开接口价格产生的金额为用量估算，与订阅账单分别展示。

“用量报告”可生成日、周、月、年图片；“服务状态”查看已选择服务的官方状态页。消耗来源及格式见[来源覆盖说明](Docs/spend-status-implementation.md)。

## 更新与卸载

手动更新：从托盘退出旧版，下载新的运行包并完整替换程序目录。更换电脑后重新配置自己的登录和代理。

也可在“关于与更新”的“Windows 更新清单地址”填写：

```text
https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates.json
```

点击“检查更新”后，程序读取该发布源。有较新版本时先下载并校验，再显示文件和安装信息；只有点击确认安装，才退出程序并替换文件。更新机制见[更新说明](Docs/updates.md)。

预览版渠道：从 [0.1.6-preview.7 版本页](https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases/tag/v0.1.6-preview.7) 手动升级一次后，在“关于与更新”选择“预览版更新源”，再点“检查更新”。固定地址为 `https://raw.githubusercontent.com/Akun-0-0-7/Pulse-Windows-Public/main/updates-preview.json`。0.1.6-preview.5 及更早版本的检查器不能正确处理预览标签和 GitHub 下载跳转。预览发布不改变稳定版清单；维护者按[发布步骤](Docs/updates.md#维护者发布步骤)先发布 ZIP，再提交对应清单。

卸载可运行安装目录内的 `uninstall.ps1`，个人设置和数据会保留。

## 个人数据与故障记录

数据默认保存在 `%LOCALAPPDATA%\PulseWindows`。`%LOCALAPPDATA%` 表示当前 Windows 用户的本机应用数据目录；按 Windows 键和 R 键，输入该路径即可打开。

| 文件或目录 | 用途 |
|---|---|
| `settings.json` | 已选择的服务、界面和网络设置。 |
| `secrets.json` | Windows 当前用户保护机制加密的密钥或会话。 |
| `usage-cache.json` | 上次读取的额度、来源和读取时间。 |
| `usage-history.json` | 本机已观察到的额度趋势。 |
| `window-starter-state.json` | 五小时重置计划和已认领周期，用于防止重复发送。 |
| `window-starter-last.json` | 最近一次自动发送的状态、完成标记、退出码和用量计数；不含登录凭据。 |
| `window-starter/` | 本机工具发送消息时使用的独立工作目录。 |
| `spend-cache.json`、`spend-file-cache-v1.json` | 消耗整理结果与文件解析缓存。 |
| `browser-sessions/` | 主动连接服务时使用的独立网页登录环境。 |
| `last-error.json`、`background-last-error.json` | 界面或后台读取最近一次的脱敏异常。 |

提交自动发送失败的问题时，请说明 Pulse 版本、Windows 版本和账号设置页提示，并附 `window-starter-last.json`。无需提供 `secrets.json`、本机工具的登录文件或完整个人数据目录。

## 源码与构建

`Pulse-Windows-Public-0.1.5-source.zip` 包含程序、离线检查和文档。源码构建需要 .NET 10 开发工具，在源码目录运行：

```powershell
.\Scripts\build.ps1
.\Scripts\verify.ps1
.\Scripts\package.ps1
```

`build.ps1` 构建程序并运行核心检查；`verify.ps1` 继续检查桌面布局、六语言、原生启动和脚本语法；`package.ps1` 生成自带运行环境的发布包。各程序文件用途见[文件职责](Docs/file-guide.zh-CN.md)，验证结果见[验证记录](Docs/verification.md)。

主项目使用 Apache-2.0 许可。原作者、改写来源及随包组件许可保留在 `LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `Licenses/`。
