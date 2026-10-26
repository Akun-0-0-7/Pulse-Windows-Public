# 本机消耗来源与格式

在“Token 消耗”中开启统计并选择来源后，Pulse 才会扫描对应客户端的本机用量记录。Token 是模型处理文本的计数单位；金额按公开接口价格估算。价格估算与订阅账单分别展示。

本页列出 54 个来源的默认位置、格式和去重规则，供“打开来源覆盖说明”按钮查看。默认不启用统计；额外日志目录由用户自己添加。本页中的离线验证采用合成文件，真实客户端列记录对应格式的验证状态。

## 54 个来源逐项状态

路径以传入的用户目录为基准。Windows 的编辑器数据还支持 Code、Code - Insiders、VSCodium 和远程 `.vscode-server` 目录。SQLite 指关系型本地数据库；Protobuf 指使用数字字段编号的二进制编码；Zstd 是压缩格式。

| 序号 | 来源编号 / 名称 | 已实现的数据与关键规则 | 离线验证 | 真实客户端 |
|---|---|---|---|---|
| 1 | `claude` / Claude Code | `.claude/projects`；助手调用四种计数、同调用累计副本逐字段最大值、1 小时缓存写入计价、逐文件持久化读取缓存。 | 专属：流式副本、缓存计价、缓存重读/删除/重新计价。 | 未执行 |
| 2 | `codex` / Codex | `.codex/sessions` 与归档；累计计数作差、缓存读属于输入子集、会话与归档去重、计数回退建立新基线；逐文件缓存。 | 专属：累计差分、跨日、缓存子集、持久化重读。 | 未执行 |
| 3 | `opencode` / OpenCode | `opencode.db`；V2 `session_message` 和旧 `message` 表按调用编号消除迁移副本；总量证明推理独立时并入输出。 | 专属：V2/旧表迁移及推理关系。 | 未执行 |
| 4 | `kilo` / Kilo CLI | `kilo.db`；共用 OpenCode 的表结构和四种独立计数。 | 同族 OpenCode 数据库用例；无 Kilo 独立数据库样本。 | 未执行 |
| 5 | `grok` / Grok Build | 会话 `updates.jsonl`；只读 `turn_completed`，每模型一条；身份含文件会话与模型。 | 专属：两个会话、每轮两个模型不互相合并。 | 未执行 |
| 6 | `kimi` / Kimi CLI | 会话 `wire.jsonl` 的 `message.payload.token_usage`；会话目录隔离，保持未命名 Kimi 模型标签。 | 专属：两个同名日志的会话均保留。 | 未执行 |
| 7 | `devin-cli` / Devin CLI | `sessions.db/message_nodes` 的助手 `metadata.metrics`；同消息编号的兄弟节点折叠；有实测 CLI 记录才压过 Desktop 镜像。 | 专属：兄弟节点、CLI/Desktop 优先关系。 | 未执行 |
| 8 | `pi` / Pi | `.pi/agent/sessions`；首个非标题事件必须是会话头；响应编号折叠分叉副本，计数独立。 | 专属：首行校验、分叉副本。 | 未执行 |
| 9 | `omp` / Oh My Pi | `.omp/agent/sessions`；共用 Pi 会话头、助手用量和响应身份规则。 | 同族 Pi 用例；无 omp 独立样本。 | 未执行 |
| 10 | `senpi` / OmO Native | `.senpi/agent/sessions`；共用 Pi 规则；读取头部记录的工作目录下 `.omo/senpi-task/children`，支持专属路径变量。 | 同族 Pi 用例和路径变量用例；子会话目录无单独样本。 | 未执行 |
| 11 | `kimchi` / Kimchi | `.config/kimchi/harness/sessions`；消息编号限定在会话内。 | 专属：两个会话同消息编号不合并；路径变量。 | 未执行 |
| 12 | `prime-agent` / Prime Agent | sessions 与 session-artifacts；只在对应子会话被实际读到时从父累计归属扣除，归属编号按谱系隔离。 | 专属：无子会话不扣、真实子会话扣一次、分叉和两条谱系。 | 未执行 |
| 13 | `gemini` / Gemini CLI | `.gemini/tmp`；会话消息和 headless stats 分模型读取；工具提示并入输入、thoughts 并入输出；缓存与总量关系判定。 | 专属：会话、headless 多模型、工具和未知种类。 | 未执行 |
| 14 | `qwen` / Qwen Code | `.qwen/projects`；thoughts 只加入输出一次；报告总量判断缓存是否已包含于 prompt；不相符的报告总量完整放入未分类。 | 专属：缓存独立/总量冲突。 | 未执行 |
| 15 | `amp` / Amp | `T-*.json`；`usageLedger.events` 为主，按消息编号或模型/计数匹配消息镜像；只知线程时间的消息标记累计。 | 专属：Ledger 优先、未匹配调用和累计时间。 | 未执行 |
| 16 | `droid` / Droid | `.factory/sessions/*.settings.json`；`tokenUsage`、`providerLockTimestamp`；检查输出/推理和输入/缓存各候选等式，保留会话累计标志。 | 专属：实际时间字段、模型规范化和四桶关系。 | 未执行 |
| 17 | `openclaw` / OpenClaw | 当前与三种旧产品目录；数据库按会话/序号读取、模型状态按会话隔离；读取包含 `.jsonl` 的档案名，排除认证镜像目录；事件编号/时间/输入输出跨库存去重。 | 专属：两个会话模型、数据库与已删除档案副本、镜像排除。 | 未执行 |
| 18 | `roocode` / Roo Code | VS Code `ui_messages.json`；只读 API 开始项的内嵌 JSON，`tokensIn/Out`、`cacheReads/Writes`；模型取 `modelInfo.modelId` 或 history 的 `<model>`。 | 专属：四桶、history 模型回退及会话目录。 | 未执行 |
| 19 | `kilocode` / Kilo Code | 对应扩展任务目录，共用 Roo 任务格式。 | 专属：与 Roo 相同样本分别运行该来源。 | 未执行 |
| 20 | `cline` / Cline | 扩展任务格式及 CLI `<session>.messages.json`；CLI 输入扣除缓存读/写，使用 manifest 模型/工作目录；支持 CLI 专属目录变量。 | 专属：扩展任务、CLI、路径变量。 | 未执行 |
| 21 | `codebuddy` / CodeBuddy | 本机 transcript 与扩展 `.log`；独立请求保留，已有 transcript 优先于扩展后备；按报告总量处理推理。 | 专属：相同调用保留、扩展模型与计数关系。 | 未执行 |
| 22 | `workbuddy` / WorkBuddy | transcript、扩展日志及 `session_usage` 数据库；只有 `used` 的累计量放入未分类；transcript 优先且标记部分覆盖。 | 专属：数据库累计与 transcript 优先。 | 未执行 |
| 23 | `cherrystudio` / Cherry Studio | V2 `Data/Agents/.claude/projects` 优先旧路径同名会话；请求编号优先于消息编号；流式四桶取最大、时间取最新。 | 专属：同 request 不同 message、副本最新时间、V2 优先。 | 未执行 |
| 24 | `commandcode` / Command Code | projects 与 config 模型后备；按 `parentId` 祖先继承每条回复的模型，保留回退后废弃分支的实测调用；模型去 provider 前缀。 | 专属：分支切模型与回退分支计数。 | 未执行 |
| 25 | `opencodereview` / OpenCodeReview | 会话 `llm_response`；总量不符合候选关系时完整保留为未分类；无总量且有缓存时跳过；时间减去 duration。 | 专属：跳过未知缓存关系、完整未分类总量、开始时间。 | 未执行 |
| 26 | `zcode` / ZCode | JSONL 按报告总量处理缓存/推理；SQLite 的 `started_at/completed_at`、缓存读/写实际列，输入确定包含缓存。 | 专属：数据库实际列、JSONL 包含/独立关系和模型编号。 | 未执行 |
| 27 | `hermes` / Hermes | `state.db` 与 profile 库；优先分模型分账提供方统计，回退会话累计；含缓存但关系未知时输入放未分类。 | 专属：累计记录的缓存不重复加入。 | 未执行 |
| 28 | `goose` / Goose | sessions 数据库；优先 accumulated 计数，报告总量多出的部分保留未分类，标记累计。 | 专属：累计优先与剩余量。 | 未执行 |
| 29 | `zed` / Zed | threads 数据库；普通 JSON 和 Zstd blob，按帧魔数识别错误标签；会话累计计数与工作目录。 | 专属：普通/压缩/错误标签、坏压缩帧。 | 未执行 |
| 30 | `kiro` / Kiro CLI | `.kiro/sessions/cli` 的显式逐轮 input/output；时间优先同 stem 对话侧文件 Prompt 时间，再取 end_timestamp。 | 专属：只有 Prompt 时间的实测轮次。 | 未执行 |
| 31 | `crush` / Crush | 与上游一致：只提供费用，Token 记录为空；输出来源说明。 | 无专属文件样本；目录注册和空来源检查。 | 未执行 |
| 32 | `unsloth` / Unsloth | `chat_messages` 的 contextUsage 和 `api_usage_events`；缓存限定在 prompt 内，线程模型后备。 | 已实现；无 Unsloth 专属数据库样本。 | 未执行 |
| 33 | `antigravity-cli` / Antigravity CLI | conversations 数据库 `gen_metadata` Protobuf；只采用已确认字段编号、明确时间与实测输出/推理关系。 | 专属：Protobuf 字段、时间、响应副本。 | 未执行 |
| 34 | `antigravity-ide` / Antigravity IDE | 两种 IDE conversations 目录，共用 Antigravity Protobuf 解码。 | 同族 CLI Protobuf 样本；无 IDE 独立数据库样本。 | 未执行 |
| 35 | `micode` / MiMo Code | mimocode/orca 数据库；内嵌消息编号跨库折叠，只有行编号时加数据库绝对路径。 | 专属：两个库相同行编号仍计两条。 | 未执行 |
| 36 | `devin-desktop` / Devin Desktop | `acp-events`；canonical `notification._meta` 输入/缓存取最后值、输出按步骤累加；旧事件后备；CLI 数据库只作会话身份查找。 | 专属：10 个三来源组用例中的 canonical、零覆盖、旧格式、歧义标题、数据库只读、CLI 优先。 | 未执行 |
| 37 | `mux` / Mux | 每会话 `session-usage.json` 按模型累计；身份含父会话目录；推理关系未知时不再次相加。 | 专属：双会话固定文件名、推理不重复。 | 未执行 |
| 38 | `codebuff` / Codebuff | projects chat 历史、runState 后备；role 或 variant 判断助手，usage 副本逐字段首个非零值，时间取 chatId 明示时间。 | 专属：分字段合并、nested details、runState 后备。 | 未执行 |
| 39 | `freebuff` / Freebuff | 与上游一致：没有持久化实测 Token，读取结果为空；不从字符数产生计数。 | 无专属文件样本；目录注册和空来源检查。 | 未执行 |
| 40 | `jcode` / JCode | session 与 journal；追加过程模型状态，消息编号/稳定语义身份去除重放；缓存关系未知时保留未分类。 | 专属：journal 重放、模型变化、匿名消息、显式缓存形状。 | 未执行 |
| 41 | `augment` / Augment | 只读已完成 turn；取最后非空 token_usage，不相加流式累计；request/sequence 编号限定在会话。 | 专属：最后累计值、尾空节点、相同序号双会话。 | 未执行 |
| 42 | `gjc` / Gajae Code | 助手消息四桶；entryId 限定在会话；无编号只去掉同 session、文件名、字节摘要的镜像；优先消息自身时间。 | 专属：跨会话编号、无编号镜像和消息时间。 | 未执行 |
| 43 | `junie` / Junie | 会话 `events.jsonl` 的 LlmResponseMetadataEvent；开始时间=结束时间减延迟，缺结束时间取 session 日期；身份含会话和计数。 | 专属：双会话同名日志、会话日期回退、推理不重复。 | 未执行 |
| 44 | `dsh` / DeepSeek Harness | 普通/压缩 transcript，按帧魔数解压；保留已完成连接帧，忽略未完成尾帧；seedLength 前副本不重计，推理已在输出内。 | 专属：Zstd、普通副本、错误扩展名、连接帧和半写尾帧。 | 未执行 |
| 45 | `fx` / FX | `usage-v2.json` snapshot；父目录/显式 session_id 作用域，时间和工作目录来自 session.json；按模型累计。 | 专属：固定文件名双会话、推理不重复。 | 未执行 |
| 46 | `lmstudio` / LM Studio | 平衡括号提取服务器日志 usage；缓存读/写限定于 prompt，输出包含推理；本机推理不生成估算金额。 | 专属：缓存读大于输入、缓存写一次、金额为空。 | 未执行 |
| 47 | `reasonix` / Reasonix | stats JSONL；total 或 requests 任一正值有效；cache_miss 优先，推理包含在 completion；存在 split 时不补 total 差值。 | 专属：total=0、缺模型跳过、不虚增总量。 | 未执行 |
| 48 | `cursor` / Cursor 导出 | JSON dashboard/CSV；按账号分组，以会话、时间、模型、四桶组成语义签名；跨文件保留单文件最大重复次数，JSON 范围优先于同账号 CSV。 | 专属：同文件双调用、跨文件费用/字段顺序变化、账号隔离、CSV 引号和范围。 | 未执行 |
| 49 | `antigravity` / Antigravity 导出 | 显式缓存/导入 JSONL 用量，独立于 CLI/IDE 数据库编号；已知用量映射，未知推理关系标记部分记录。 | 已实现导出映射；无该导出格式专属样本。 | 未执行 |
| 50 | `trae` / Trae 导出 | 原始数组的 extra_info 四桶；Auto 模型保留 trae-mode；按语义签名和单文件最大次数去掉重叠页。 | 专属：实际 extra_info 与费用变化重放。 | 未执行 |
| 51 | `warp` / Warp 导出 | 只读费用快照；ReportsTokens=false，费用不能换成 Token。 | 专属：金额 1.25 美元、Token 总量为空。 | 未执行 |
| 52 | `hindsight` / Hindsight | ledger 的 started_at、input/output/cached；缓存与输入独立；bank 作为项目，trace 作为会话。 | 专属：独立缓存桶、真实时间字段、项目。 | 未执行 |
| 53 | `mcode` / MiniMax Code | native session 与捕获流；有 native 实测记录时 native 优先；排除可读子代理副本，等待模型后按消息身份保留同值独立调用。 | 专属：native/capture 优先、子代理副本、模型后到、相同计数消息。 | 未执行 |
| 54 | `copilot` / GitHub Copilot | OpenTelemetry、Desktop SQLite、VS Code append/patch 日志；较高精度事件优先，重放补丁后才统计，会话级数据不叠加请求级副本。 | 专属：OTEL 四层优先、缓存子集、VS Code 补丁重放。 | 未执行 |

## 文件职责和调用关系

以下文件均在 `src/Pulse.Core/Spend/`：

| 文件 | 作用 |
|---|---|
| `SpendSources.cs` | 54 来源目录、Windows 默认路径、指定环境变量的路径覆盖。只解析明确路径，不扫描整个用户目录找相似文件。 |
| `SpendService.cs` | 用户启用/来源选择入口、扫描编排、全局身份合并、CLI/Desktop 优先关系、价格计算、日期/模型/来源聚合。 |
| `SpendReaders.cs` | 来源分派、公共文件遍历、原生基础格式、Codex 累计差分、Mux/FX/Junie/Grok/Kimi/LM Studio 等单文件规则和公共字段函数。 |
| `SpendReadCache.cs` | Claude/Codex 持久化逐文件缓存，保留未定价记录；源文件变更重读、删除清除、每次按当前价格表定价。 |
| `SpendSessionReaders.cs` | Qwen、Droid、Amp、Hindsight 的专属字段关系与主/镜像选择。 |
| `SpendPiReaders.cs` | Pi/omp/Senpi/Kimchi 会话头与身份；Prime 父子归属核对、谱系隔离。 |
| `SpendEditorReaders.cs` | Roo/Kilo/Cline 扩展任务、Cline CLI、CommandCode 分支、ZCode JSONL、OpenCodeReview 专属格式。 |
| `SpendNativeReaders.cs` | GJC 消息身份、Cherry V2/旧格式和流式累计、Kiro Prompt 时间、Reasonix 实测 split。 |
| `SpendExportReaders.cs` | Cursor/Trae 导出作用域、多重集合重放合并、CSV/JSON 重叠规则。 |
| `SpendImportReaders.cs` | CSV 解析，以及 Copilot 的 OpenTelemetry 优先级、VS Code append/patch 重建和来源合并。 |
| `SpendDatabaseReaders.cs` | 只读 SQLite 表映射：OpenCode/Kilo/MiMo、Devin CLI、Hermes、Goose、Unsloth、ZCode、Copilot、WorkBuddy。 |
| `SpendOpenClawReaders.cs` | OpenClaw 数据库/日志同请求身份、会话排序、模型状态隔离与历史档案。 |
| `SpendBinaryReaders.cs` | Zed 压缩数据库内容、Antigravity Protobuf 字段解码。 |
| `SpendCompression.cs` | Zstd 帧检测和解压，包括扩展名与实际压缩格式不同的情况。 |
| `SpendSpecialReaders.cs` | Gemini 会话/headless stats、MiniMax native/capture 的专属读取。 |
| `SpendTencentReaders.cs` | CodeBuddy/WorkBuddy transcript 与扩展日志、模型上下文及总量等式。 |
| `SpendMissingReaders.cs` | Devin Desktop、Codebuff、JCode 的专属实现；详细字段说明见 `Docs/spend-missing-readers.zh-CN.md`。 |
| `SpendCsv.cs` | 统一导出已归一化记录；缺失 Token 计数为空单元格，转义表格公式。 |

主调用为 `SpendService.ScanAsync` → `SpendSources.ResolvePaths` → 来源专属读取器 → `SpendRecord` → 全局身份合并 → 当前价格表 → `SpendReport`。测试文件职责：`SpendTests.cs` 为基础口径，`SpendAdvancedTests.cs` 为数据库/二进制/压缩，`SpendFamilyTests.cs` 为来源语义差异，`SpendMissingTests.cs` 为三种补写来源，`SpendCacheTests.cs` 为持久化增量行为。

## 记录字段与结果含义

`SpendRecord` 是一条经过来源语义归一化的记录。`Input` 为普通输入 Token，`Output` 为输出 Token，`CacheRead` 为缓存读取 Token，`CacheWrite` 为缓存写入 Token，`Unclassified` 为有实测总量但无法归入上述桶的 Token。Token 是模型文本计量单位。`CacheWrite1h` 是 CacheWrite 中属于 1 小时缓存写入的子集，不再次加入总数。总数按普通输入、输出、缓存读、缓存写、未分类五部分相加。

`Identity` 为去重身份；`Session` 为来源明确的会话或累计范围；`Model` 为来源记录的模型编号；`At` 为来源时间；`Project` 为来源记录的工作目录/项目；`Origin` 标记 native 或 imported。`ReportsTokens=false` 表示来源没有提供 Token 计数，此时 TotalTokens 为 null，CSV 对应单元格留空。`IsAggregate=true` 表示会话/报告累计量，不能当成该时刻的一次请求；`IsPartial=true` 表示来源已知只覆盖可确认的一部分记录或分类。`RequestContext` 为可用于长上下文价格层的单请求上下文量。

`EstimatedCost` 按当前模型价格表计算；含未分类计数或未知模型的记录保留未定价状态。`ReportedCost` 是来源明确给出的金额，两者分别保留。美元字段按每百万 Token 的价格换算；本机 LM Studio 不计算金额。

`SpendReport.Records` 是最终明细，`ByDay`、`ByModel`、`ByAgent` 分别是按本地日期、模型、来源汇总，`UnpricedTokens` 是尚未匹配到价格的 Token 总量，`Notes` 是无法读取/未完成日志等具体诊断。

生成的数据文件含义：

| 文件或结果 | 内容 |
|---|---|
| `spend-cache.json` | 桌面统计最近一次完整扫描结果，供页面展示。 |
| `spend-file-cache-v1.json` | Claude/Codex 的未定价逐文件计数缓存，带格式版本、来源、绝对路径、文件长度、UTC 修改时刻和归一化记录；不存提示词/回答正文/凭据。 |
| 用户导出的 CSV | 明细记录，显示时间、来源、模型、各 Token 桶及金额；未知 Token 保留空值。 |
| 报告图片 | 根据同一 SpendReport 绘制的统计卡片；累计记录的小时分布按界面统计规则处理。 |
| 测试终端结果 | `PASS` 是该合成场景断言通过；末行给出本次所选测试的通过/失败数量，不是已实测客户端数。 |

## 路径环境和增量读取

原调用 `new SpendService(prices, storage).ScanAsync(config, home, cancellationToken)` 保持兼容。其中 prices 是模型价格字典，storage 是可选 AppStorage 缓存存储，config 是当前配置，home 是明确用户目录，cancellationToken 是调用者取消标记。

实际用户目录扫描在启动时取得路径环境快照；显式临时 home 默认使用空环境。需要自行指定环境时，调用 `ScanAsync(config, home, environment, cancellationToken)`，其中 environment 为键值字典。这样测试不会通过进程真实环境跳到另一个用户来源目录。

已接通的路径变量包括 `LOCALAPPDATA`、`APPDATA`、`XDG_DATA_HOME`、`TOKSCALE_CONFIG_DIR`、`HERMES_HOME`、`UNSLOTH_STUDIO_HOME`、`JCODE_HOME`、`DSH_HOME`、`LM_STUDIO_HOME`、`REASONIX_HOME`、`REASONIX_STATE_HOME`、`GEMINI_CLI_HOME`、`SENPI_CODING_AGENT_DIR`、`SENPI_CODING_AGENT_SESSION_DIR`、`KIMCHI_CODING_AGENT_DIR`、三种 `PRIME_AGENT_*` 路径变量、三种 `CLINE_*` 路径变量、`CODEBUFF_DATA_DIR`、`FREEBUFF_DATA_DIR`、`HINDSIGHT_HOME`、`GOOSE_PATH_ROOT`、`GJC_CODING_AGENT_DIR`、`GJC_CONFIG_DIR` 和 `PI_CONFIG_DIR`。`~` 始终相对于传入 home 展开。用户额外设置的 SpendPaths 按来源编号子目录接入。

缓存键由来源和文件绝对路径构成；缓存命中同时要求长度和 UTC 修改时刻一致。每次扫描重新枚举现存文件，已删除文件不返回旧记录。变化文件从头重读，以保留 Codex 累计差分的正确基线；读取过程中还在增长的文件不写入稳定缓存。缓存中的记录未定价，价格表改变后即时重新计算金额。复杂多文件来源继续使用专属读取器，每次按完整来源规则读取。

离线缓存用例在第一次读取后独占锁住源文件，第二个 SpendService 实例仍返回相同 Token；换价格表使估算金额相应变化；改写文件后返回新计数；删除文件后明细为空。这验证了持久化复用、计数更新和删除失效三个行为。
