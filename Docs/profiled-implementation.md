# 52 项配置型服务的 Windows 实现

对照版本：上游 `qunqin24/Pulse` 提交 `b035a7acf1aad9c1b6f9f895197ad1a5f58ea64e`。本次复核时间：2026-10-21。

## 文件职责

| 文件 | 作用 |
|---|---|
| `src/Pulse.Core/ProviderCatalog.cs` | 提供全部 76 项服务的名称、认证类别、帮助页、用量页和显示颜色。52 项配置型服务是其中一组。 |
| `src/Pulse.Core/Providers/Profiled/ProfiledProviders.cs` | 注册这 52 项读取器，分派服务专用解析器，建立用量窗口、套餐和余额结果。 |
| `ProfiledFetch.cs` | 执行普通 API、Cookie、网关以及阿里 `bl` 命令读取，组合辅助响应，统一处理网络和本地工具错误。 |
| `ProfiledRequests.cs` | 执行 Qwen、阿里 Coding Plan、Gemini、Notion、Windsurf、Factory、LongCat、Manus、TypeSafe 等多阶段协议；发现 Windows 本地登录或配额文件；补充服务要求的请求头。 |
| `SimpleParsers.cs` | 解析直接字段、额度比例和小型结构，如 Synthetic、Kilo、Manus、V0、Neuralwatt。 |
| `StructuredParsers.cs` | 解析嵌套预算、分组额度、控制台 JSON 字符串、多个团队和实例，例如 Alibaba、Qwen、Factory、IBM Bob、LiteLLM。 |
| `WebAndLocalParsers.cs` | 解析网页、React 数据流、JetBrains XML 和 Windsurf protobuf。 |
| `tests/Pulse.Tests/ProfiledTests.cs` | 执行离线响应解析和请求协议检查，使用替代 HTTP 处理器记录实际请求。 |
| `tests/Pulse.Tests/Fixtures/Profiled/*.json` | 50 份固定响应样本；覆盖服务字段的读取规则。它们是协议样本，不是此电脑的真实账号用量。 |

上表中未带目录的文件，均位于 `src/Pulse.Core/Providers/Profiled/`。

## 接入范围

包含 ClinePass、Alibaba Coding Plan、Alibaba Token Plan、Qwen Cloud、Factory、Gemini、Kilo Code、Augment、JetBrains AI、T3 Chat、Synthetic、ElevenLabs、Warp、Windsurf、Bifrost、Chutes、LongCat、ZoomMate、Notion AI、IBM Bob、Nous Portal、Raycast AI、GitKraken AI、xKiro、Abacus、Moonshot、Hyper、Atlas Cloud、Poe、Venice、OpenAI API、Amp、Zed、Sakana AI、Mistral、Codebuff、LLM API Key Proxy、LiteLLM、Aixy、Neuralwatt、ClawRouter、ZenMux、v0、DevPass、Perplexity、Manus、Hugging Face、DeepInfra、xAI API、Replicate、TypeSafe、Vercel AI Gateway，共 52 项。

这组读取器均有实际读取路径；未启用的账号在任何本机文件或网络访问之前直接返回停用状态。服务响应缺少真实分母时，不生成虚构的零用量圆环。

## 多阶段请求

- Qwen Cloud：先从订阅页、`sec_token` Cookie 或账号信息取得安全令牌，再向控制台网关发送表单，查询用量和套餐。表单分别转义 `+`、`&`、`=`；携带控制台区域、浏览器标识和防跨站令牌。
- Alibaba Coding Plan：向国际站发送具体查询动作、商品编码和三个密钥字段；当前区域不认识密钥或套餐时尝试中国站。控制台明确要求登录时停止回退。拒绝状态先于响应内的缓存额度处理。
- Alibaba Token Plan：运行 `bl usage token-plan`，按国际/国内控制台读取。子进程仅继承运行 Windows 和寻找用户配置所需的环境变量，不把 Pulse 进程中的其他云密钥当成 CLI 登录。单次命令限时 15 秒，输出上限 262144 字节。
- Gemini：只读 `.gemini/oauth_creds.json`，尊重 `.gemini/settings.json` 中 Google、API Key 或 Vertex 登录方式。过期令牌不发送；先查询项目和套餐，再把项目带入配额请求，不刷新或改写 CLI 登录。
- Notion：先读取工作区，确认单一用户；优先选择 Business 或 Enterprise 工作区，再以其 `spaceId` 查询额度。
- Windsurf：从四项浏览器储存值构造请求头，发送真正的 protobuf 请求并按原始二进制响应解析，不把二进制当成 JSON 或 UTF-8 文本。
- Factory：先查询账号，读取新版计费标志和额度；旧计费账号即使新版接口返回 200，仍转到旧版用量接口，携带账号标识。
- LongCat：先验证会话；当前有效 Token 包读取失败时使用用量接口，燃料包查询失败不抹掉已取得的主额度。
- Manus：只取 `session_id` 的值构造 Bearer 登录请求，不发送其余浏览器 Cookie。
- TypeSafe：发现账单页引用的同源 JavaScript，定位账单读取动作，再发送 Next Action 请求。脚本不携带 Cookie；只访问同源脚本，最多检查 60 个。
- IBM Bob、ZoomMate、Replicate、LiteLLM：分别完成区域团队查询、会话换令牌、账单页发现账号、密钥归属用户/团队查询。

“JSON”表示 JavaScript 对象表示法，“XML”表示可扩展标记语言，“CLI”表示命令行工具；`spaceId` 是 Notion 工作区标识。protobuf 是以字段编号编码的二进制协议，Next Action 是网页声明的服务器动作标识。

## Windows 本地发现

- JetBrains：在用户的 `AppData/Roaming/JetBrains`、`AppData/Roaming/Google` 和 `.config/JetBrains` 下发现最新的 `options/AIAssistantQuotaManager2.xml`，也支持账号中指定文件。
- Nous Portal：只读 Hermes 的 `.hermes/auth.json` 与 `.hermes/shared/nous_auth.json`，支持提供者对象、凭据池和独立对象；过滤过期登录，仅允许 Nous 自有 HTTPS 门户。
- Codebuff：未手填密钥时读取 `.config/manicode/credentials.json`。
- Kilo Code：未手填密钥时读取 `.local/share/kilo/auth.json`，并识别 Windows 的 `AppData/Local/kilo/auth.json`。
- Hugging Face：未手填密钥时读取 `.cache/huggingface/token`。

这些文件均不由 Pulse 修改。手填密钥优先于已有本机登录。

## 用量和耗尽语义

`UsedPercent` 是服务报告或由真实已用量/总量计算的已用百分比，保留大于 100 的超额值。`Exhausted` 是是否耗尽：普通额度按百分比达到 100 判定；Synthetic 的 `limited=true` 即使百分比较低也判为耗尽；ZoomMate 根据 `is_quota_available` 和 `allow_overage` 判断。绘制圆环时的显示范围由界面层处理，解析层不截断原始比例。

`WindowSeconds` 是窗口长度，单位为秒。服务只说明月度账期而不说明固定长度时，30 天仅用于排序，`ReportsLength=false`，界面不会据此推算已过时间。`ResetsAt` 是重置时刻，`ExpiresAt` 是额度到期时刻。

## 验证结果

执行命令：

```powershell
& .\.tools\dotnet\dotnet.exe run --project tests\Pulse.Tests -p:TestGroup=Profiled
```

2026-10-21 结果为 **24 passed, 0 failed**。其中一个检查逐项运行 50 份 JSON 样本，另有 JetBrains XML 和 Sakana HTML 检查；协议检查覆盖区域回退、安全令牌、表单转义、工作区选择、本地登录、protobuf、网页动作发现、备用用量路径、Cookie 提取、虚拟密钥请求头以及拒绝状态优先级。

上述结果验证请求构造和固定响应的解析。此轮没有向这 52 项服务使用真实账号发起查询；真实账号验证结果单独记在 `Docs/verification.md`。
