# 程序文件和结果说明

主调用关系是 `Pulse.exe → App → UsageEngine → 服务适配器 → UsageReading → 浮动栏/设置/缓存`。`UsageReading` 表示一次额度读取结果；`UsageWindow` 表示其中一条限额。命令行与桌面共用 `Pulse.Core`，其中 `Core` 指不依赖桌面控件的公共逻辑。

## 构建与发布

| 文件 | 作用 |
|---|---|
| `Directory.Build.props` | 统一 .NET 版本、可空检查、编译警告和分析规则。 |
| `.gitignore` | 排除下载工具、编译输出、测试结果和本机数据。 |
| `src/Pulse.Core/Pulse.Core.csproj` | 公共逻辑项目及 Zstandard 解压依赖。 |
| `src/Pulse.Desktop/Pulse.Desktop.csproj` | Windows 桌面程序、WebView2 登录和翻译资源构建。 |
| `src/Pulse.Cli/Pulse.Cli.csproj` | 命令行程序构建。 |
| `Scripts/build.ps1` | 构建桌面、命令行，执行离线检查；失败即停止。 |
| `Scripts/verify.ps1` | 一次执行构建、核心、报告像素、圆环逻辑、六语言、原生启动、第二实例账号链接和脚本语法检查。 |
| `Scripts/package.ps1` | 生成自带运行环境的 x64 发布目录、ZIP 和摘要；收集说明与第三方许可证。 |
| `Scripts/install.ps1` | 安装到当前用户应用目录，创建开始菜单快捷方式。 |
| `Scripts/uninstall.ps1` | 移除本程序安装及所属快捷方式、启动项和协议；保留个人数据。 |
| `Scripts/安装到当前用户.cmd` | 双击启动同目录安装脚本。 |
| `updates.json` | 公开运行包的版本、下载地址和完整性摘要，供应用内检查更新读取。 |
| `Docs/five-hour-reset.zh-CN.md` | 0.1.5 的五小时重置选项、执行边界、状态文件和结果文件说明。 |
| `LICENSE`、`THIRD_PARTY_NOTICES.md`、`Licenses/*` | 主许可证、改写来源与随包组件授权。 |

## 公共逻辑：`src/Pulse.Core/`

| 文件 | 作用 |
|---|---|
| `Models.cs` | 定义服务目录、账号、设置、额度窗口、余额、指标及读取上下文。 |
| `Storage.cs` | 设置和缓存的 JSON 读写；先写临时文件再替换正式文件。JSON 是带字段名称的结构化文本格式。 |
| `JsonTools.cs` | 响应字段、数字、时间解析和诊断脱敏。 |
| `ProviderCatalog.cs` | 76 项服务的名称、账号类型、帮助地址、用量地址和颜色。 |
| `UsageProviders.cs` | 将服务编号路由到手写、配置式或扩展适配器。 |
| `ProviderUtilities.cs` | 有界 HTTP 请求、响应正文超时、通用错误和窗口转换。HTTP 是网页及服务接口的传输协议。 |
| `UsageEngine.cs` | 只刷新已启用账号、并发控制、取消、旧结果隔离、失败保留缓存、余额基准；末尾 `AlertRules` 负责额度提醒。 |
| `UsageFigures.cs` | 百分比显示和来源名称归一化；显示取整不覆盖原始数值。 |
| `UsageHistoryStore.cs` | 按账号和窗口保存本机额度历史，限制时间、点数与总条数。 |
| `LocalUsageSummary.cs` | 从消耗缓存计算今日、7 天、31 天、主要模型和缓存命中率。 |
| `PromptCacheService.cs` | 从 Codex/Claude 本机记录读取最近请求和已知提示词缓存期限。 |
| `CodexDetailsService.cs` | 通过 Codex 本机工具主动读取账号历史及重置积分，解析每日计数和到期时间。 |
| `AgentActivity.cs` | 判断本机 agent 是否工作、计算消耗速度预测、执行用户明确开启的窗口启动器。agent 指运行模型任务的客户端。 |
| `WindowResetCoordinator.cs` | 保存 5 小时重置计划、按账号和周期认领去重，并协调发送前后两次额度刷新。 |
| `WindowStarterIdentity.cs` | 核对 Codex 监控登录与工具实际登录为同一账号，拒绝身份冲突及 API 密钥。 |
| `WindowStarterReceipt.cs` | 验证 Codex 完成事件和实际计数，区分启动、超时、失败与成功。 |
| `RefreshSchedule.cs` | 根据活动状态和 Windows 空闲时间在 10 秒至 30 分钟间选择刷新频率；支持秒级活跃间隔及空闲降频。 |
| `PricingCatalog.cs` | 公开模型价格、长上下文价格层级及汇率缓存；缺少价格保持为空。 |
| `LocalPaths.cs` | 本机目录/可执行文件发现，以及有超时和输出上限的子进程运行。 |
| `WindowsCommand.cs` | 将可识别的 Node.js 命令包装器转为直接程序调用，使用独立参数传递。 |
| `ProxyEnvironment.cs` | 为本机工具传递系统、自定义或关闭代理的设置。 |
| `ChildProcesses.cs` | 跟踪本程序启动的工具，退出时取消/结束；使用 Windows 作业对象清理后代。 |
| `CredentialSlots.cs` | 同时清除一个账号的接口、控制台和桌面会话凭据槽。 |
| `SqliteReader.cs` | 使用 Windows 自带接口只读 SQLite 数据库。SQLite 是客户端常用的本机数据库格式。 |
| `CommandModes.cs` | 缓存 JSON、文字回顾、状态栏及安装/恢复状态栏命令。 |
| `StatuslineInstaller.cs` | 备份及恢复 Claude 状态栏设置，采集主动回传的限额。 |
| `UpdatePackage.cs` | 校验 Windows 更新清单、下载摘要、隔离解压及逐文件替换/恢复计划。 |

### 服务请求和解析

路径均以 `src/Pulse.Core/Providers/` 为起点；服务逐项说明见 `handwritten-implementation.md`、`profiled-implementation.md`。

| 文件 | 作用 |
|---|---|
| `Handwritten/HandwrittenProvider.cs` | 24 项手写服务请求、通道选择和失败回退。 |
| `Handwritten/HandwrittenParsing.cs` | Codex、Claude、DeepSeek 等主要限额和余额响应解析。 |
| `Handwritten/HandwrittenOtherParsing.cs` | 其余手写服务的额度、套餐和余额解析。 |
| `Handwritten/HandwrittenLogin.cs` | 设备代码登录、浏览器授权及令牌刷新。 |
| `Handwritten/HandwrittenLocalSessions.cs` | Antigravity 本机语言服务器与 Claude 桌面会话发现。 |
| `Handwritten/ClaudeWindowsCookieStore.cs` | 只读 Claude Electron Cookie 数据库，使用当前用户 Windows 保护接口解密兼容格式。Cookie 是网站维持登录的会话字段。 |
| `Handwritten/HandwrittenCookies.cs` | 服务会话 Cookie 白名单及手工输入格式处理。 |
| `Handwritten/HandwrittenRpc.cs` | Codex app-server、Kiro 的本机进程请求/答复；完成后结束子进程。 |
| `Handwritten/HandwrittenSigning.cs` | 火山引擎接口签名。 |
| `Profiled/ProfiledProviders.cs` | 52 项配置式服务的解析分派、通用值与状态转换。 |
| `Profiled/ProfiledFetch.cs` | 配置式服务的本机读取及接口流程路由。 |
| `Profiled/ProfiledRequests.cs` | 多阶段登录、工作区/区域发现、二进制请求和 Windows 本机登录发现。 |
| `Profiled/SimpleParsers.cs` | 简单余额及百分比响应解析。 |
| `Profiled/StructuredParsers.cs` | 多额度池、分模型/分层响应解析。 |
| `Profiled/WebAndLocalParsers.cs` | 网页、XML、本机及特殊结构响应解析。XML 是带标签的结构化文本格式。 |

### 消耗、状态和扩展

路径均以 `src/Pulse.Core/` 为起点。

| 文件 | 作用 |
|---|---|
| `Spend/SpendSources.cs` | 54 项来源编号、格式依据和 Windows 搜索路径。 |
| `Spend/SpendService.cs` | 所选来源扫描、跨副本去重、价格计算、时间范围与聚合。 |
| `Spend/SpendReaders.cs` | 普通日志和会话读取、各来源专属字段及累计计数差分。 |
| `Spend/SpendDatabaseReaders.cs` | 只读客户端数据库中的消息、会话和用量字段。 |
| `Spend/SpendImportReaders.cs` | 导出文件、Cursor CSV、Copilot 遥测/会话及重复对账。CSV 是按行列保存的文本表格。 |
| `Spend/SpendCompression.cs` | 有大小上限的 Zstandard 日志解压和压缩格式识别。 |
| `Spend/SpendBinaryReaders.cs` | Antigravity protobuf 和 Zed 压缩数据库记录解析。protobuf 是按字段编号编码的二进制格式。 |
| `Spend/SpendTencentReaders.cs` | WorkBuddy 等腾讯客户端的会话和汇总对账。 |
| `Spend/SpendPiReaders.cs` | Pi 家族会话、分支身份及 Prime 父子累计量对账。 |
| `Spend/SpendSpecialReaders.cs` | Qwen、Droid、Amp、Hindsight 等专属日志格式和缓存关系。 |
| `Spend/SpendSessionReaders.cs` | 固定文件名位于不同会话目录时的身份、时间和模型归属。 |
| `Spend/SpendEditorReaders.cs` | Cline CLI、Roo 系列和编辑器记录的计数及模型语义。 |
| `Spend/SpendOpenClawReaders.cs` | OpenClaw 日志/档案/数据库、分会话模型和跨副本去重。 |
| `Spend/SpendExportReaders.cs` | Cursor/Trae 等导出的语义身份、多重副本和账号作用域。 |
| `Spend/SpendNativeReaders.cs` | 原生来源特有的时间、缓存、累计量和请求身份规则。 |
| `Spend/SpendMissingReaders.cs` | Devin Desktop、Codebuff、Jcode 的累计和副本语义，详见独立来源说明。 |
| `Spend/SpendReadCache.cs` | Claude/Codex 未变文件复用已解析计数，变化重读、删除移除，按当前价格重新定价。 |
| `Spend/SpendCsv.cs` | 明细导出；来源未报告计数时留空，并转义表格公式文本。 |
| `Status/StatusPages.cs` | 44 个官方状态页的地址、格式和组件筛选规则。 |
| `Status/StatusService.cs` | 各状态页响应、历史条带和可用率读取。 |
| `Status/StatusAlertRules.cs` | 仅对已启用服务的相关组件故障变化通知并去重。 |
| `Extensions/ExtensionService.cs` | 扩展清单发现、路径校验、主动执行、输出/时间上限及协议解析。 |

## 桌面：`src/Pulse.Desktop/`

| 文件 | 作用 |
|---|---|
| `App.cs` | 程序入口、单实例、托盘、定时刷新、后台通知、生命周期和设置保存。 |
| `AppWindowStarter.cs` | 独立每秒检查五小时重置、隔离本机主账号、先保存认领再发送消息，并维护最近执行状态。 |
| `GlobalUsings.cs` | 桌面项目公共命名空间。 |
| `app.manifest` | Windows 版本和显示缩放声明。 |
| `WindowsServices.cs` | 当前用户凭据加密、开机启动、快捷键、全屏判断、空闲时间和协议注册。 |
| `FullscreenDetection.cs` | 全屏范围判断，排除桌面和任务栏，保留真正全屏和多显示器范围检查。 |
| `InstanceChannel.cs` | 当前 Windows 用户限定的命名管道，将再次启动及账号链接交给运行实例。 |
| `RailWindow.cs` | 浮动圆环、悬停详情、拖动、折叠、截图排除、全局快捷键；含 `RingTile` 圆环绘制。 |
| `RailPlacement.cs` | 屏幕局部逻辑尺寸到物理像素的停靠几何计算。 |
| `RailScreen.cs` | 显示器发现、缩放读取及原生物理像素定位。 |
| `DetailCharts.cs` | 分段进度、额度及指标趋势、Codex 主动历史卡。 |
| `LocalUsageCard.cs` | 主账号本机消耗摘要、31 天小图、窗口费用及提示词缓存会话列表。 |
| `BotRenderer.cs` | 独立绘制的 18 种身体、8 种性格及工作/读取/耗尽/安静状态。 |
| `SettingsWindow.cs` | 设置导航、入门选择、服务与账号页、总览和值区域刷新。 |
| `SettingsPreferences.cs` | 外观、位置、通用、通知、网络、诊断、集成和关于页。 |
| `SettingsTools.cs` | 来源选择、扫描、图表、筛选/排序/分页、报告、官方状态和扩展页。 |
| `StatusView.cs` | 读取并绘制官方组件、故障和历史条带。 |
| `ReportWindow.cs` | 日/周/月/年卡片预览、模板选择、图片保存/复制/分享。 |
| `ReportCanvas.cs` | 使用 Windows GDI+ 在 CPU 上绘制报告，再转换为图片；导出不依赖当前桌面合成状态。GDI+ 是 Windows 的二维绘图接口，CPU 指中央处理器。 |
| `ModelBreakdown.cs` | 所选模型的输入/输出/缓存、每日及小时分布、按客户端分布。 |
| `SpendPresentation.cs` | 根据当前币种取汇率、金额显示及按实际天数折算订阅月费。 |
| `SpendCoordinator.cs` | 手动/后台扫描共用串行入口、来源设置快照、退出取消及缓存刷新。 |
| `MessageBox.cs` | 将本程序原生消息框文本接入所选语言。 |
| `UpdateInstaller.cs` | 程序内确认后启动独立隐藏安装器，等待退出再安装；目标不可写时保留手动安装目录。 |
| `Ui.cs` | 共用控件、主题、额度名称、重置时间和大数单位。 |
| `Localization.cs` | 翻译资源读取、动态句子和语言数字格式。 |
| `Resources/Localizations/zh-CN.json`、`zh-Hant.json`、`en.json`、`ja.json`、`ko.json`、`ru.json` | 简中、繁中、英、日、韩、俄界面文本。 |
| `BrowserSignIn.cs` | 独立 WebView2 网页会话，仅读所选服务的登录字段。 |
| `DeviceSignIn.cs` | Codex/Copilot 设备授权窗口。 |
| `OAuthSignIn.cs` | Claude/Grok 浏览器授权、本机回调及一次性状态校验。OAuth 指服务商提供的授权协议。 |
| `VisualChecks.cs` | 隔离演示数据生成和离屏界面检查；不自动访问真实账号。 |
| `src/Pulse.Cli/Program.cs` | 命令行入口，调用公共命令模式。 |

## 检查程序

每个 `tests/*/*.csproj` 是同目录检查程序的构建入口。

| 文件 | 检查内容 |
|---|---|
| `tests/Pulse.Tests/Program.cs` | 自动发现 `[Check]` 标记的方法，统计通过/失败并返回退出码。 |
| `CoreTests.cs` | 数据模型、存储、目录、空值和输出契约。 |
| `RuntimeTests.cs` | 请求、进程、缓存、禁用账号和并发结果隔离。 |
| `HandwrittenTests.cs` | 手写服务、授权流程、本机会话及失败回退。 |
| `ProfiledTests.cs` | 配置式服务样例、多阶段请求和本机发现。 |
| `SpendTests.cs` | 普通消耗、状态和扩展协议。 |
| `SpendAdvancedTests.cs` | 压缩/数据库/二进制、分支汇总对账和复杂来源边界。 |
| `SpendFamilyTests.cs` | 各客户端的累计/缓存关系、会话身份、祖先模型和路径专属回归。 |
| `SpendMissingTests.cs` | Devin Desktop、Codebuff、Jcode 的独立合成记录回放。 |
| `SpendCacheTests.cs` | 未变文件缓存复用、变动重读、删除及重新定价。 |
| `PricingTests.cs` | 长上下文层级、价格优先级和模型名称匹配。 |
| `AutomationTests.cs` | 活动识别、预测、窗口启动器和 Codex 历史解析。 |
| `WindowResetTests.cs` | 合成五小时读数、重启状态、补处理、去重、取消和发送前后刷新顺序；不发送真实消息。 |
| `WindowStarterIdentityTests.cs` | 合成登录字段和令牌声明的同账号核验，以及未知、冲突和 API 密钥排除。 |
| `WindowStarterExecutionTests.cs` | 假子进程检查隔离参数与工作目录，合成完成事件及用量验证成功判定。 |
| `ProxyEnvironmentTests.cs` | 本机工具的系统代理映射、大小写、分协议及直连回归。 |
| `CommandTests.cs` | 缓存命令、状态栏安装恢复及输出。 |
| `ReviewFixTests.cs` | 正文超时、清凭据、子进程取消、刷新频率、代理和明细空值回归。 |
| `HistoryAlertTests.cs` | 历史存储边界、额度重置及状态故障通知去重。 |
| `LocalHistoryTests.cs` | 提示词缓存模型/层级/请求时间及本机统计空值。 |
| `UpdateTests.cs` | 下载、摘要、路径与解压边界、准备取消、文件篡改以及临时目录真实安装/失败恢复。 |
| `tests/Pulse.Tests/Fixtures/Handwritten/*`、`Fixtures/Profiled/*` | 按服务和响应变体命名的离线输入；`normal` 是常规，`empty` 是缺数据，`expired/refused` 是过期/拒绝，其余后缀描述字段变体。JSON/XML/HTML 均为解析输入，不是用户数据。 |
| `tests/Pulse.DesktopChecks/Program.cs` | 空报告、模板选择、币种切换和订阅费用；生成 PNG 后检查尺寸及图像内容。 |
| `tests/Pulse.RailChecks/Program.cs` | 停靠坐标、主内环选择、分段进度、详细卡和图表数；禁用账号不展示旧错误、启用立即刷新；秒级选择、保存、重开与实际刷新计时器；自动收起生命周期。 |
| `tests/Pulse.DesktopChecks/FullscreenChecks.cs` | 桌面和任务栏排除、普通窗口、真正全屏和多显示器的全屏判断回归。 |
| `tests/Pulse.Tests/CodexFallbackTests.cs` | 用合成本机工具验证 Codex 网络失败及超时回退，检查独立账号边界、取消和诊断脱敏。 |
| `tests/Pulse.Tests/RefreshQueueTests.cs` | 用阻塞的合成服务验证启用账号时等待当前刷新后补刷，以及等待取消后的锁恢复。 |
| `tests/Pulse.Tests/ShortRefreshTests.cs` | 秒级精度、旧整数配置读取、小数保存重读、活跃判断、空闲边界及非法间隔处理。 |
| `tests/Pulse.LiveChecks/Program.cs` | 显式选择服务后查询真实额度；不会发送推理消息。 |

上述 `*Tests.cs` 均位于 `tests/Pulse.Tests/`。

## 结果和个人数据

| 路径／命名模式 | 含义 |
|---|---|
| `artifacts/Pulse-Windows-Public-<版本>-win-x64/` | 可运行的完整发布目录；`<版本>` 是三个整数形式的版本号。 |
| `artifacts/Pulse-Windows-Public-<版本>-win-x64.zip`、`.zip.sha256` | 可分发压缩包和完整性摘要。 |
| `artifacts/Pulse-Windows-Public-<版本>-source.zip`、`.zip.sha256` | 可转移源码包及摘要，包含程序、检查和文档，不包含下载的 SDK 或个人数据。 |
| `**/bin/`、`**/obj/` | 编译程序和中间结果，由构建工具再生，不是个人用量。 |
| `artifacts/visual-checks*/dashboard-*.png`、`services-dark.png`、`onboarding.png`、`rail.png` | 合成账号的布局检查，不是实际登录额度；图像有效性以验证记录为准。 |
| `artifacts/visual-checks*/report-1.png` 至 `report-8.png` | 同一份合成用量的八种报告版式。 |
| `artifacts/desktop-checks*/empty-report-1.png` 至 `empty-report-8.png` | 八种模板的空数据边界检查。 |
| `artifacts/*test-data/`、检查程序指定的临时目录 | 隔离测试设置、缓存和生成样本。 |
| `settings.json`、`usage-cache.json`、`secrets.json` 等个人文件 | 默认位于 `%LOCALAPPDATA%\PulseWindows`，逐项含义见 README 的数据位置表，不打进发布包。 |
| `usage-history.json` | 成功读取的本机额度趋势，最多保留 90 天、每窗 720 点、共 256 个账号/窗口组合。 |
| `spend-file-cache-v1.json` | Codex/Claude 文件大小、修改时间和未定价计数缓存，减少重复解析；不保存对话正文或凭据。 |
| `background-last-error.json` | 后台读取遇到文件或网络异常时最近一次的脱敏信息。 |
| `status-alert-memory.json`、`recap-alert-memory.json` | 已通知的官方故障和月份，用来去重。 |
| `codex-details-<账号摘要>.json` | 用户主动读取的 Codex 历史、重置积分和过期时间缓存，文件名使用账号编号摘要。 |
| `window-starter-state.json` | 各账号未来五小时重置计划和最近认领周期；先保存认领，重启后避免同周期重复发送。不含凭据。 |
| `window-starter-last.json` | 按账号保存最近一次处理的服务、重置时间、处理时间和状态；不含凭据。 |
| `window-starter/` | 发送普通 `hi` 时供本机工具使用的独立工作目录。 |
| `startup-check.json` | 隔离启动检查是否创建浮动窗口、是否进入消息循环及检查时间。 |
| `startup-ready.json` | 原生启动检查已创建窗口，供第二实例链接检查同步等待；不包含账号凭据。 |
| `artifacts/verification.json`、`build-test.log` | 最近一次完整检查的结构化结果和构建/核心检查日志。 |
| `build-info.json` | 发布包版本、构建时间、架构、源代码提交和上游依据；不包含用户数据。 |
| `updates/` | 已校验的更新包、隔离准备文件及安装计划/日志/备份；详见 `updates.md`。 |

检查结果与实际兼容性见 `verification.md`，功能和 Windows 平台对应关系见 `parity.md`。
