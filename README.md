# Pulse Windows

<img src="src/Pulse.Desktop/Assets/Pulse.png" alt="Pulse 应用图标" width="96" height="96">

在 Windows 桌面浮动栏和系统托盘查看 AI 服务额度。基于 [qunqin24/Pulse](https://github.com/qunqin24/Pulse) 移植，支持 Windows 11、Windows 10 企业版长期服务版（LTSC）21H2 的 64 位 Intel/AMD 电脑。

当前公开版本：[**0.1.6**](https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases/tag/v0.1.6)。

0.1.6 汇总此前预览版的悬浮条动效、可调配色、设置控件可读性、Token 目录选择、hi 失败恢复和更新器修复。贴边收起条可显示当前额度进度，内圈可独立切换剩余或已用额度；Codex 升级后保存的旧工具路径失效时，会重新查找当前工具。源码已整合到 `main`，正式运行包和源码包见上方版本页。更新渠道及发布步骤见[更新说明](Docs/updates.md)。

## 下载与使用

1. 打开 [版本下载页](https://github.com/Akun-0-0-7/Pulse-Windows-Public/releases/latest)。
2. 下载 `Pulse-Windows-Public-0.1.6-win-x64.zip`，完整解压。
3. 双击 `Pulse.exe`，或运行 `安装到当前用户.cmd` 创建开始菜单入口。
4. 在欢迎页选择需要监控的服务，点击“开始监控”，再使用自己的账号登录。

首次运行没有已启用账号，自动发送 `hi` 默认关闭。Codex、Claude 可读取本机工具登录；DeepSeek 等服务可在账号页填写自己的密钥。网络代理在“网络与刷新”中设置。

运行包包含 .NET 运行环境。网页登录需要 Microsoft Edge WebView2 组件；缺少时，登录窗口提供微软官方安装入口。

更新时先从托盘退出旧程序，再完整替换程序目录。个人设置保存在当前 Windows 用户的 `%LOCALAPPDATA%\PulseWindows`；`%LOCALAPPDATA%` 表示本机应用数据目录。关闭设置窗口后程序仍在托盘运行。

## 文档与源码

- [完整使用说明](README.zh-CN.md)：账号、代理、悬浮栏、刷新、更新和数据位置。
- [五小时发送与失败恢复](Docs/window-starter-recovery.zh-CN.md)：开关、允许时段、自动补发和手动重试。
- [外观与配色](Docs/appearance-colors.zh-CN.md)、[Token 目录选择](Docs/codex-token-scan.zh-CN.md)：新选项与数据来源。
- [程序文件职责](Docs/file-guide.zh-CN.md)：源码各部分的用途。
- [功能对应关系](Docs/parity.md)与[验证记录](Docs/verification.md)。

`win-x64.zip` 是可直接运行的程序；`Pulse-Windows-Public-0.1.6-source.zip` 是对应版本的源码和文档。附带的 `.sha256` 文件保存 SHA-256 校验值，SHA-256 是用于核对文件完整性的摘要算法。

源码使用 [Apache-2.0](LICENSE) 许可，原作者及第三方组件说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
