# Devin Desktop、Codebuff 与 Jcode 的用量读取

这三个读取器按 `Pulse-upstream/Sources/Pulse/Usage/Readers` 中对应 Swift 读取器的字段含义移植，离线测试只创建临时目录、日志和数据库。

## 文件职责与调用

| 文件 | 作用 |
| --- | --- |
| `src/Pulse.Core/Spend/SpendMissingReaders.cs` | 独立实现三个来源的原生日志读取、字段合并、时间恢复与语义去重。统一入口 `Read` 接收来源名称、显式来源路径、说明列表和取消信号。 |
| `tests/Pulse.Tests/SpendMissingTests.cs` | 使用临时目录中的合成记录验证累计规则、重复副本、时间、模型、缓存归属、数据库身份查询和跨来源去重。 |
| `src/Pulse.Core/Spend/SpendReaders.cs` | 将 `devin-desktop`、`codebuff`、`jcode` 调度到独立读取器。 |
| `src/Pulse.Core/Spend/SpendSources.cs` | 给出相对于传入用户目录的日志路径；Devin Desktop 同时接收 CLI 会话数据库，数据库在此路线只提供身份信息。 |
| `src/Pulse.Core/Spend/SpendService.cs` | 汇总全部来源；当 Devin CLI 已读到某会话的实测记录时，排除同会话的 Desktop 镜像。 |

读取结果使用 `SpendRecord`：`Input` 为新输入，`Output` 为输出，`CacheRead` 为读取缓存，`CacheWrite` 为创建缓存，`Unclassified` 为已报告但归属尚未明确的用量。`TotalTokens` 为这五项之和；`IsAggregate` 表示会话累计记录，`IsPartial` 表示日志中还有关系未明确的计数。`Session` 是会话标识，`Project` 保存工作目录，`Identity` 是去重标识。这里的 Token 指模型处理的文本计数单位。

## Codebuff

只读取 `chat-messages.json` 数组中的助手消息，助手由 `variant` 或 `role` 的 `ai`、`agent`、`assistant` 标记识别。

同一次响应的用量在多处保存副本。每一个字段分别按以下顺序选取第一个正数：`metadata.usage`、`metadata.codebuff.usage`、从新到旧的运行历史 `providerOptions.usage` 与 `providerOptions.codebuff.usage`。高优先级的零不会遮住后续副本的真实计数，也不会将同一字段的副本相加。缓存读取还支持 `promptTokensDetails` 和 `prompt_tokens_details` 中的缓存字段。

Codebuff 的输入字段已包含缓存读取，因此新输入等于输入字段减去缓存读取，下限为零；缓存创建独立保存。`credits` 是产品费用单位，不进入 Token 总数。

模型依次来自 `metadata.model`、最新历史记录的 `providerOptions.codebuff.model`、用量对象自己的 `model`。时间依次来自消息时间、消息创建时间、元数据时间以及聊天目录名。目录名 `2026-10-20T01-02-03.000Z` 恢复为 `2026-10-20T01:02:03.000Z`。会话标识组合渠道、项目和聊天目录，例如 `manicode/project/2026-10-20T01-02-03.000Z`。

## Jcode

读取 `session_*.json`，并回放同目录、同文件主体名称的 `.journal.jsonl`。基础会话中的消息使用会话模型，日志新增消息使用当行 `meta.model` 更新后的模型。记录保留会话工作目录。消息有原生标识时按会话与消息标识去重；没有标识时根据时间、模型和各项用量建立稳定标识，JSON 属性顺序不影响身份。

缓存归属由明确字段区分：存在 `cache_creation_input_tokens` 时，新输入与缓存分别相加；存在原生输入详情字段时，缓存读取包含在输入中，先从输入中扣除。只有缓存读取数、没有这两种字段时，把报告的输入放入 `Unclassified`，缓存读取不重复加入总数，并设置 `IsPartial`。推理输出与普通输出的包含关系没有声明时，保留报告的普通输出，推理数不再次加入总数，并设置 `IsPartial`。缺少消息时间、模型或实测用量的消息不生成记录。

## Devin Desktop

标准 ACP（Agent Client Protocol，代理客户端协议）`usage_update` 使用 `notification._meta` 中带 `cognition.ai/` 前缀的字段。完整输入、缓存读取和缓存创建使用最后一次报告的值，输出按各步骤累加。最终新输入等于最后完整输入减去最后缓存读取，一个文件生成一条累计记录。字段名中的点和斜杠属于真实键名，直接按完整键名查找。

旧日志从通知的 `content.metadata.metrics`、`metadata.metrics`、`metrics` 或元数据对象读取下划线格式的计数，每个含计数且有事件时间的通知生成一条记录。时间来自日志事件；`adaptive` 表示路由模式，保存为 `unknown`，不作为具体模型定价。

CLI 数据库按会话标题提供会话标识、模型和工作目录，查询使用只读连接。一个标题匹配多个会话时不选择其中任何一个。仅启用 Desktop 时，数据库不会额外贡献 Token。启用 CLI 且确实读到对应会话用量后，CLI 记录拥有统计优先权。

## 验证输出

在仓库目录运行 `.tools/dotnet/dotnet.exe run --project tests/Pulse.Tests -- SpendMissingTests`。每行 `PASS` 对应一个离线场景通过，末尾 `Results` 汇总通过与失败数。测试结束会删除自己创建的临时目录，不生成用户用量报告，也不读取真实用户凭据。

这组测试涵盖：Codebuff 逐字段副本合并、模型优先级、嵌套缓存、聊天时间恢复；Jcode 日志回放、按追加时间切换模型、匿名消息语义去重、明确与未分类缓存；Devin 标准累计、零值覆盖、旧事件时间、标题歧义、只读数据库完整性及 CLI 镜像优先级。
