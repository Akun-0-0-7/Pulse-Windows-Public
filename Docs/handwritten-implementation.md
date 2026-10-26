# 手写服务商实现与 Windows 通道

日期：2026 年 10 月 20 日。对照上游 Pulse 1.12.2，提交 `b035a7acf1aad9c1b6f9f895197ad1a5f58ea64e`。本页记录实际代码与离线验证结果。

## 文件职责

| 文件 | 作用 |
|---|---|
| `src/Pulse.Core/Providers/Handwritten/HandwrittenProvider.cs` | 24 个服务商的请求编排、来源选择和失败回退；新接入 Antigravity 语言服务器及 Claude 桌面会话路由。 |
| `HandwrittenParsing.cs`、`HandwrittenOtherParsing.cs` | 将各服务的响应映射为额度窗口、套餐和余额。Antigravity 的 `remainingFraction` 表示剩余比例，转换后 `UsedPercent = (1 - remainingFraction) × 100`。 |
| `HandwrittenLogin.cs` | Codex、Copilot 设备授权，Claude、Grok 浏览器 OAuth 授权，以及过期令牌刷新。浏览器授权采用 PKCE：`CodeVerifier` 是一次性随机证明串，`code_challenge` 是它的 SHA-256 摘要，`State` 是校验回调的一次性随机值。 |
| `HandwrittenLocalSessions.cs` | 发现 Antigravity 的 Windows 语言服务器进程及其动态端口、查询本机 HTTPS 接口；筛选 Claude 会话 Cookie 并读取组织用量。CSRF 令牌是语言服务器每次启动时传入的请求校验值。 |
| `ClaudeWindowsCookieStore.cs` | 只读当前用户 `%APPDATA%\Claude` 下 Electron 的 Cookies SQLite 数据库和 Local State；通过 Windows DPAPI 解开密钥，再对 Chromium `v10`/`v11` Cookie 作 AES-GCM 解密。DPAPI 是 Windows 当前用户的数据保护接口，AES-GCM 是带完整性校验的加密方式。 |
| `HandwrittenCookies.cs` | 其他网页会话的 Cookie 名称白名单和格式检查。 |
| `HandwrittenRpc.cs` | Codex app-server 与 Kiro ACP 的本机进程通信。 |
| `HandwrittenSigning.cs` | 火山引擎请求签名。 |
| `tests/Pulse.Tests/HandwrittenTests.cs` | 离线响应、登录、来源与失败回退测试。 |
| `tests/Pulse.Tests/Fixtures/Handwritten/` | 按服务名命名的合成或脱敏响应样例；`normal` 是有效用量，`empty`/`no-plan` 是无额度，`expired`/`refused` 是登录失效或拒绝，其他后缀指明响应变体。它们只用于测试，不作为账号缓存。 |

## Desktop 可调用接口

- `HandwrittenLogin.BeginBrowser(providerId, redirectUri)` 返回 `BrowserLoginPrompt`。`providerId` 为 `claudeCode` 或 `grok`，`redirectUri` 必须是随机端口的 `http://127.0.0.1:<端口>/callback` 或 `http://localhost:<端口>/callback`。先绑定本机回调监听器，再调用此方法并打开 `AuthorizationUrl`。
- `HandwrittenLogin.CompleteBrowserAsync(prompt, callbackUri, http, ct)` 校验完整回调地址和 `State`，交换令牌，返回应写入该账号 `ISecretStore` 的 JSON 凭据。`http` 是 Desktop 提供的 HTTP 客户端，`ct` 是取消令牌。每个账号的密钥名为 `account.Id`，额外账号互不覆盖。
- `HandwrittenLogin.BeginDeviceAsync` 与 `PollDeviceAsync` 继续供 Codex、Copilot 使用。轮询间隔由 `DeviceLoginPrompt.IntervalSeconds` 和 `DeviceLoginPoll.RetryAfterSeconds` 给出。
- Claude 桌面路由选 `AccountConfig.Route = "desktopApp"`，优先从 `ISecretStore.Get("desktop:" + account.Id)` 获取桌面会话；也接受该账号手工填入的 Cookie。两者没有可用会话时，尝试读取已安装 Claude Electron 的本机 Cookies 数据库。只发送 `sessionKey`、`sessionKeyV3`，`lastActiveOrg` 仅在本地决定查询哪个组织，不发往网络。
- Antigravity 的 `desktopApp` 路由只查本机语言服务器，`tooling`/`cli` 只运行 `agy`；`automatic` 先问语言服务器，再尝试 CLI。语言服务器用 `RetrieveUserQuotaSummary` 查窗口，用 `GetUserStatus` 查套餐；所有候选进程和端口依次尝试。

## 实际结果与剩余差距

运行 `.tools/dotnet/dotnet.exe run --project tests/Pulse.Tests -p:TestGroup=Handwritten -- HandwrittenTests`：29 项通过、0 项失败。新增测试涵盖 Claude 与 Grok 的 PKCE 回调状态校验、Claude 当前组织选择及 Cookie 白名单、合成 AES-GCM Cookie、临时 SQLite Cookie 数据库、Antigravity 多进程拒绝后继续寻找。HTTP 响应来自假处理器；本次没有读取真实用户凭据或日志，也没有访问真实服务。

Windows 本机 Claude 读取支持传统 DPAPI Cookie 以及 `v10`/`v11` AES-GCM Cookie。Chromium `v20` 应用绑定密钥没有通用的当前用户直接解密路径；此时由 Desktop 的 WebView2 主动登录窗保存 `desktop:<account.Id>` 会话，或者使用手工会话。Antigravity 进程发现已按 Windows CIM 和 TCP 监听端口实现，尚未在真实运行中的 Antigravity 上验收；本机自签名证书只对 `127.0.0.1` 查询放行。Claude 与 Grok 的授权请求也只通过离线交换测试，真实账号授权待用户在 Desktop 中主动执行。
