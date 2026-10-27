# 交接记录：票 01–07、12、02、03 全部通过（03 基线 `f778a32`，204 条）→ 下一轮

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 文件名沿用首轮，内容已覆盖票 01–12；下一轮请从本文件第 2、3 节接着走。
> 状态快照：**已收口代码基线 `f778a32`**（票 03 终审点；`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误，五个测试工程 35+30+86+43+10 = **204 条全绿、0 Skip**，老登于 2026-10-27 在 `f778a32` 干净 worktree 实跑）。此后 HEAD 已到 `e8f2e2d`（票 08 提交，待复测），`204` 只对 `f778a32` 成立，别拿它给 08 背书。
> 结论：**票 01、04、05、06、07、12、02、03、10 全部收口**（[01/04 终审](review-01-04-final.md)、[05 终审](review-05.md)、[06 终审](review-06.md)、**07 终审 [`review-07.md`](review-07.md)（`7f699c5`，185 条）**、[12 审计](review-12.md)、**02 终审 [`review-02.md`](review-02.md)（`57c226e`/复核 `1588559`）**、**03 终审 [`review-03.md`](review-03.md) §9（`f778a32`）**、**10 终审 [`review-10.md`](review-10.md)（锚 `204f93e`，11/11）**）。在途：08 可观测性/审计/安全横切（雨哥，`e8f2e2d` 待复测）、09 Quartz（旺总，`Jobs/**` 开发中）、11 验收脚本（最后，门禁只剩 F-03-A11）。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / **已收口基线 `f778a32`**（票 03 收尾，含脚本 A1/A2/A9）；其后 `e8f2e2d` 是票 08 的在测提交 |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>+<sha 前 7 位>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`） |
| 构建 | 0 警告 0 错误（`-warnaserror`，`f778a32` 上老登实跑） |
| 测试 | **204 条、0 失败 0 Skip**（SharedKernel 35 / 架构 30 / Infrastructure 86 / Api 43 / 集成 10；`f778a32` 快照；此前的 185 是 `7f699c5`、173 是 `0da4a18`，已作废） |
| 生产代码 | 05：`Infrastructure/Tenancy/*`；06：`Api/Errors/*` + `Infrastructure/Errors/ProblemDetailsWriter.cs`；07：`Infrastructure/Persistence/*`（含 `OutboxOptions`） |
| 进行中 | 08 已提交 `e8f2e2d`（待李总复测）；09 旺总在做 `Jobs/**`（包已就位 `49a6b43`，`appsettings.json` 不改、配置键用默认值）；**10 已审计通过**；11 门禁只剩 A11 |
| 下一张票 | 08 复测 → 09 Quartz（旺总）→ 11 验收脚本（唯一门禁 **F-03-A11**） |

## 2. 未完成项 / 待办与已落票约束

> 票 06 已于 `71529fd` 回填 ✅（`abde749`，156 条），`Auth.*` 文案已对齐；票 05、12 的票面均已收口。下面按「下一轮必做」与「已落票约束」分开。

### 票 07 ✅ 通过（`7f699c5`，185 条；终审 [`review-07.md`](review-07.md)）

> **编号提示（三套并存，引用时以描述为准）**：
> - 票面现行：**F-07-4** = 架构规则收紧（`84ea33b`，含 `Jobs/**`、`*Job.cs`）；**F-07-5** = 重试上限可配置（`75d3d19`）。
> - 老登首轮报告：F-07-3 零处理器静默、F-07-4 失败原因/防饿死、F-07-5 反射包装、F-07-6 多处理器重放、F-07-7/8/9 三处规则漏检（已并入票面 F-07-4）。
> - 老登终审报告：F-07-10/11 = 重试上限可配置与终态写 `LastError`（即票面 F-07-5 的两半）。
> 同一个号在不同报告里指不同事，**别只看号**。

- **F-07-1** 已按「认 API 形状」落地：请求路径专用的无参重载改名为 `GetCurrentConnectionScopeAsync`，`GetConnectionScopeAsync` 只剩带 `long tenantId` 的重载——类型系统本身阻止「裸取」，改名 token 传 `CancellationToken` 也无法编译；架构规则改为「后台路径（`Persistence/**`、`*HostedService.cs`、`*Dispatcher.cs`）不得出现 `.GetCurrentConnectionScopeAsync(`」，反向用例补了改名 token 样本与 6 条 `IsBackgroundPath` Theory。
  - **规则适用边界**（`b9795e9` 已登记）：这条是源码字符串规则，不是语义分析；别名、动态调用、反射理论上都能绕过它。但要在后台绕过得先自行构造一个没有 `tenantId` 的上下文，成本远高于误用。文档写「后台路径禁用该调用、由架构规则守护」，**不写「不可能绕过」**。
  - **第二层（`0da4a18`）**：任何源码出现零实参 `.GetConnectionScopeAsync()` 调用同样报（方法声明无点号，不算），守住「将来重新加回无参重载」；老登把这条列为可选加固，不作为 F-07-4 的验收项。
- **F-07-2** 已补 `DomainEventDispatcherTests.cs`：同一事件实例、按 DI 注册顺序、零处理器视为成功、同步抛出、一批两个不同事件各走各的处理器。
- **补偿顺序（票面 F-07-3）**：`40f016a` 改为先回滚、再补偿删除，`UnitOfWorkTests` 用调用序列钉住 `dispatch → outbox-add → commit → rollback → outbox-remove`；零处理器/零消费者记 Warning（老登首轮 F-07-3）随 `001a731` 落地，不再静默消费。
- **失败原因与防饿死（老登首轮 F-07-4）**：`001a731` — `OutboxMessage` 增 `LastError`，`RecordFailureAsync` 记原因并累加 `Attempts`；超限记 Error 并以 `RetryLimitExceeded` 拒绝，坏消息不再占满批次。**「拒绝」只是终态标记，不是死信队列**：本轮无死信表，步骤 3 落库时才做「死信/失败表 + 人工重放」，`MaxAttempts` 与拒绝原因字段随同对齐；**任何报告或交接写成「已实现死信队列」都要退回**。
- **反射异常解包（老登首轮 F-07-5）**：`001a731` — 捕获 `TargetInvocationException` 并用 `ExceptionDispatchInfo` 解包，同步抛出保留原始异常。
- **票面 F-07-4（已修，`0c29633`）**：规则从匹配字面量 `".GetCurrentConnectionScopeAsync("` 改为匹配**标识符** `GetCurrentConnectionScopeAsync`——容忍任意空白、不要求括号（方法组引用同样算）；`IsBackgroundPath` 并入 `Jobs/**` 与 `*Job.cs`；三条反向用例拆成独立 Fact，各断言**恰好 1 条违规**（断行/空格、方法组、`Jobs/` 路径）。旺总用三处真实源码探针（非合成源码）各验 1 红后删除探针。**误报说明**：改标识符级后，后台文件里 `nameof(...)`、XML doc `<see cref=...>` 提及也会报——按「宁可误报注释，也不漏」的口径接受。
- **票面 F-07-5（已修，`7f699c5`；老登报告记作 F-07-10/11）**：新增 `OutboxOptions`（配置节 `Outbox`、键 `MaxAttempts`、默认值只在 `OutboxOptions.DefaultMaxAttempts` 一处），启动期读取并校验，非法值（`0`/`-1`/`abc`）直接抛；`OutboxDispatcher` 的 `MaxAttempts` 常量已删除，判断改走 `_options.MaxAttempts`；`RejectAsync` 进终态时同时写 `RejectionReason` 与 `LastError`。用例：注入 2 验证「第 2 次失败即拒」、上限 1 验证「毒消息不饿死同批后面的健康消息」、反射断言类内无 `MaxAttempts` 常量字段。变异：硬编码回 5 → 2 红、不写 `LastError` → 1 红、加回常量 → 1 红。
- **测试侧已收口（李总，`7f699c5`）**：build 0 警告 0 错误、185 条全绿；F-07-4 用四条**真实源码**探针验证（空格断行、换行断行、方法组引用、`Jobs/*Job.cs`）各恰好 1 条违规，删探针后架构 30 条回绿、`git status` 无残留；F-07-5 三个变异全抓到（硬编码回 5 → 2 红、终态不写 `LastError` → 2 红、加回 `MaxAttempts` 常量 → 1 红），另有真实启动冒烟（`Outbox__MaxAttempts=0` 启动即抛 `InvalidOperationException`，`=2` 正常 `Now listening`）。明细见 [`test-report-07.md`](test-report-07.md) §9。
- **终审（老登，`7f699c5`）**：独立 worktree 复跑 build 0/0 与 185 条全绿；F-07-5 四条变异全部被抓（硬编码 `>= 5` → 2 红、不写 `LastError` → 1 红、加回 `MaxAttempts` 常量 → 1 红、`Load` 放宽 `< 1` → `< 0` → 1 红）；「单处来源」成立（判定只用 `_options.MaxAttempts`，默认值只在 `OutboxOptions.DefaultMaxAttempts`，`Program.cs:35` 启动期 `Load`，非法值直接抛）。剩 2 条 low 观察项不阻塞：`appsettings.json:14` 显式写了 `"MaxAttempts": 5`（与 `DefaultMaxAttempts` 两处字面值）——**已在 `2f48e7d` 删键收口**，见本文件「下一步顺序」第 4 条；规则边界见 §6.8——**类型系统是唯一强保证，字符串规则拦误用、不防蓄意规避**。
- **明确遗留（不是已解决）**：多处理器部分失败会重放已成功的处理器（老登首轮 F-07-6，消息级幂等 ≠ 处理器级幂等）。要按处理器维度记进度，**与 Outbox 落库同批做**；在那之前契约写「处理器必须幂等」。**本票只用补偿删除，不等于「事务回滚已验证」**，步骤 3 换真实事务并删补偿代码。

### 票 06 遗留 low（已修，`837117f`）

- `ToCamelCaseKeys` 原只认 `IDictionary<string, object?>` / `IDictionary<string, string[]>`，`ReadOnlyDictionary` 等形态静默不转换；camelCase 撞键会抛。`6922421` 定的口径已落地：**落点在出口、不用全局 `DictionaryKeyPolicy`，改非泛型 `IDictionary` 重建 + Ordinal 去重（后写覆盖先写）**，并补了只读字典、`List` 值字典用例。`837117f` 上 169 条全绿。

### 已落票 · F-05-4（票 03 开工硬前置，`73f02e4`）

`/health/live`、`/health/ready`、`/api/version` 免租户已写入 foundation §4.3 与票 03/09；票 03 的 `Blocked by` 为 `01, 02, 05`，票 09 为 `03, 04`。实现用 `AllowAnonymousTenant` metadata，验收：无 claim / `X-Tenant` / 子域访问三端点均 200，其余端点仍 400。**票 03 实现时按此做，不要再新造豁免机制。**

### 已落票 · F-05-8（票 07 验收约束，`73f02e4`）

后台任务/消费者必须用带 `tenantId` 的显式重载 `GetConnectionScopeAsync(tenantId)`（先校验停用/到期），禁止无参裸取；停用/过期租户的消息拒绝处理并记录理由。自动化守护已由 `40f016a`（改名 + 认 API 形状）、`0da4a18`（零实参旧名）、`0c29633`（标识符级 + `Jobs/**`、`*Job.cs`）三层建齐；**它是源码字符串规则，不是语义分析**——别名、动态调用、反射仍可绕过，文档写「后台路径禁用该调用、由架构规则守护」，不写「不可能绕过」。

### 已落票 · `Auth.*` 错误码口径（`a2982ca` + `71529fd`）

- `Http.*` = HTTP 语义（404/405 等）、`Auth.*` = 认证与授权横切、`Tenant.*` = 租户解析与状态、`System.*` = 意外异常；**`Auth.*` 保留，不合并成 `Http.*`**。
- `Auth.Unauthorized` 只表示框架兜底「未认证」（无 token / token 无效）；**步骤 3 登录时账号密码错误是 Identity 业务错误，不得复用此码**。
- 步骤 3 的授权中间件与 `.RequirePermission` 产生的 401/403 **必须复用** `Auth.Unauthorized` / `Auth.Forbidden`，不得另造前缀；文案以 `CommonErrors` 为单一来源（已与票面对齐）。

### 未落票 · `*.Contracts` 工程不受规则④约束

`DiscoverModules` 排除 `*.Contracts`，老登实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。后续补一条「Contracts 只能引用 SharedKernel / 其他 Contracts」，否则 Contracts 会成为绕过模块隔离的后门。

### 步骤 3 移交清单（`tickets.md` 内，别丢）

已登记：FluentValidation 管道 + Application 层校验（票 06 只验证 `errors` 透传 + camelCase，**不得写成「FluentValidation 已验证」**）、真实事务替换补偿删除（**当前只有补偿删除，不等于「事务回滚已验证」**）、`Auth.*` 复用、登录类端点与匿名租户解析策略、10 张票之外的前端登录外壳；票 07 追加：**多处理器部分失败重放与 Outbox 落库同批**、**死信/失败表 + 人工重放（「拒绝」≠ 死信队列，`RejectAsync` 目前只是终态标记）**、`Outbox:MaxAttempts` 与拒绝原因字段随落库对齐。

## 3. 下一步顺序

1. **票 02 已完成**（✅ 终审 [`review-02.md`](review-02.md)，F-02-1 于 `57c226e` 闭环；测试 [`test-report-02.md`](test-report-02.md)）。两条**登记项**要带进票 11：**F-02-2** 每轮 build 有 5 条 React Router v8 future-flag `⚠️`，一键脚本**必须用退出码判定**，不许按输出里出现 `WARNING`/`⚠️` 判失败（要不要落 `future` 标记由老何定）；**F-02-5** `lint` 是 echo 占位、恒 0，不得当质量证据。另有两条没写进验收、但别再走回头路：dev 端口只在 `vite.config.ts` 一处定义（`1588559`），占位页真实 `import` 了三个 workspace 包（`0615059`）。
2. **票 03 已完成**（✅ 终审 [`review-03.md`](review-03.md) §9，被测 `f778a32`，204 条 0 失败 0 Skip；A1 血缘核对正反两面、A2 自举构建、A9 构建失败归 1 均闭环；记录 [`test-report-03.md`](test-report-03.md)）。**范围外仍欠一次证据**：`-Mode aspire` 全链路（含 5199 前端腿）在装好 `node_modules` 的**正常副本**上跑一次并贴 `web` 资源日志——老登两次审计副本都没走完这条腿（一次目录联结被 pnpm 拒绝、一次为避端口冲突用 `-SkipWeb`），所以**不得写成已全链路通过**，也是 `.CMD` 悬案唯一的结案办法。之后：08 可观测性 + 审计 + 安全横切（雨哥）→ 10 租户隔离集成测试（锋哥，本机 MySQL/Redis 真实例，无实例则 Skip）→ 11 验收脚本（前端 `127.0.0.1:5199`、后端 `127.0.0.1:5080`，dev 冷启动要**轮询**最多 60 秒，别 `sleep 5` 后一次性 curl；**开工前必修 F-03-A10**，见 §5「断言规矩」）。
3. **老何**：票面进度行要改——07 已 ✅（`7f699c5`，185 条，终审 `review-07.md`）、02 已开工；03 开工前确认 F-05-4 已生效；维护「步骤 3 移交清单」。
4. **收尾小项已做（`2f48e7d`，不重开票）**：`appsettings.json` 里那 3 行 `"Outbox": { "MaxAttempts": 5 }` 已删，缺键走 `OutboxOptions.DefaultMaxAttempts`（=5）成为唯一路径，默认值只剩一处来源；删后 `dotnet build -warnaserror` 0/0、`Pzd.Infrastructure.UnitTests` 86 条全绿，其中 `Max_attempts_defaults_to_five_when_the_key_is_absent` 正守着这条路径。
5. **别把 `pnpm -w lint` 当质量证据**：票 02 的四个包 lint 全是 echo 占位（`web: no linter yet` 之类），`lint --force` 4/4 只说明脚本存在；真正的 lint 规则随前端外壳在步骤 4 落地，票 11 的一键脚本不要把这条当门禁。
5. 依赖关系：02 是 03/11 的前置；08 依赖 06；09 依赖 03/04；10 依赖 05；11 最后。

## 4. 关键文件

| 用途 | 路径 |
|---|---|
| 架构（已确认） | [`docs/architecture.md`](../architecture.md) §2/§3.2/§6.3/§8/§18 |
| 规格 | [`docs/specs/foundation.md`](../specs/foundation.md) §4.3/§4.4/§4.5 |
| 票（含状态、验收与步骤 3 移交清单） | [`docs/specs/tickets.md`](../specs/tickets.md) |
| 集成测试环境约定 | [`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md) |
| 票 01/04 测试报告、审计终稿 | [`test-report-01-04.md`](test-report-01-04.md)、[`review-01-04-final.md`](review-01-04-final.md) |
| 票 05 测试报告、审计终稿 | [`test-report-05.md`](test-report-05.md)、[`review-05.md`](review-05.md) |
| 票 06 测试报告、审计终稿 | [`test-report-06.md`](test-report-06.md)、[`review-06.md`](review-06.md) |
| 票 07 测试报告、审计终审 | [`test-report-07.md`](test-report-07.md)、[`review-07.md`](review-07.md) |
| 票 02 测试报告、审计终审 | [`test-report-02.md`](test-report-02.md)、[`review-02.md`](review-02.md) |
| 票 10 审计报告（锚 `204f93e`） | [`review-10.md`](review-10.md) |
| 票 12 测试记录、审计报告 | [`test-report-12.md`](test-report-12.md)、[`review-12.md`](review-12.md) |
| 多租户实现 | [`src/Pzd.Infrastructure/Tenancy/`](../../src/Pzd.Infrastructure/Tenancy/) |
| ProblemDetails 唯一实现 | [`src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs`](../../src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs) |
| 持久化 / Outbox（返工中） | [`src/Pzd.Infrastructure/Persistence/`](../../src/Pzd.Infrastructure/Persistence/) |
| 架构规则实现 | [`tests/Pzd.ArchitectureTests/ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs) |

## 5. 环境约束（踩过的坑，别再踩）

- **测试运行方式**：`E:\pzd` 是 Low Mandatory Level，`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。
- **干净副本验证**：`git archive` 出来的目录没有 `.git`，MinVer 会因 `MINVER1001`（不是 Git 仓库）报错导致 `-warnaserror` 构建失败，但测试仍可跑。要验证 build 0/0，用 `git worktree add --detach` 而不是 `git archive`。
- **版本查询**：必须 `-t:Build`，否则得 `1.0.0`；期望值以 tag 为基准，不要硬编码 `0.1.0`。
- **MySQL 时区**：`@@time_zone=SYSTEM`（东八区），不是 UTC。`MySqlConnector 2.6.2` 不支持 `ServerTimeZone` / `ConnectionTimeZone`（未知关键字直接抛 `ArgumentException`），`DateTimeKind=Utc` 也不改会话时区；正确做法是**每次取连接后** `SET time_zone = '+00:00'` 并断言 `@@session.time_zone`（连接池重置会话，不能只在 fixture 里 SET 一次），或在查询里显式 `CONVERT_TZ`。禁止「写 `UtcNow` 读回相等」这种吃服务器默认值的断言。
- **表名大小写**：Windows 上 `lower_case_table_names=1`，隔离证据只认数据行与库名，不认表名大小写差异。
- **凭据**：只走环境变量或 `local.env`（gitignored）；仓库、日志、报告零密码。
- **租户配置**：`Tenants` 的非法值（`Status` 拼错/数字、非法 `ExpiresAt`、重复 Id/Code）会在**启动期**抛 `InvalidOperationException`，不会等首个请求；`Tenants` 为空允许启动但请求 400（不做 fail-fast）；`ExpiresAt` 缺省或 `null` 为永不过期，空字符串为配置错误。
- **后台任务取连接**：请求路径专用的是 `GetCurrentConnectionScopeAsync(ct)`，后台路径禁止出现该调用；后台任务/消费者必须用带 `long tenantId` 的显式重载 `GetConnectionScopeAsync(tenantId, ct)`（先校验停用/到期）。无参 `GetConnectionScopeAsync()` 已不存在，编译期即挡。架构规则守的是「后台路径不出现 `.GetCurrentConnectionScopeAsync(`」，属字符串规则，别把它当沙箱（边界见 §2「规则适用边界」）。
- **07 内存实现**：Outbox/UoW 的事务仍是**补偿删除**，不是真实回滚（步骤 3 换真实事务并删补偿）；失败原因、零处理器/零消费者告警随 `001a731` 补上；重试上限随 `7f699c5` 改为只从 `Outbox:MaxAttempts` 读（缺省 5，默认值只在 `OutboxOptions.DefaultMaxAttempts`，`appsettings.json` 不写该键），终态同时写 `RejectionReason` 与 `LastError`；处理器级幂等（F-07-6）留步骤 3。报告与文档不得写成「事务回滚已验证」「已实现死信队列」。
- **前端 dev 端口与探测地址（票 02 `c3d69f1` 定，票 11 沿用）**：dev 固定 `http://127.0.0.1:5199/`——`vite.config.ts` 里 `server.port = 5199`、`strictPort: true`，并且**显式** `server.host = "127.0.0.1"`，与后端 `127.0.0.1:5080` 统一成 IPv4。**`server.host` 留空时 Node 只绑 `::1`**，此时 `curl 127.0.0.1:5199` 返回 `000` 是假红、不是服务故障；票面早先「一律用 localhost」的写法已作废。前端 dev 期 `/api`、`/hubs` 代理到 5080。
- **生成物不入库**：`node_modules/`、`apps/web/build/`、`apps/web/.react-router/`（typegen）、`*.tsbuildinfo`；`pnpm-lock.yaml` 必须入库。已误提交的用 `git rm -r --cached` 清出。
- **MinVer**：浅克隆丢 tag 会让版本掉成 `0.0.0-alpha…`。
- **审计/复测副本不要用目录联结复用 `node_modules`**（老登原话）：pnpm 会以 `ERR_PNPM_UNSAFE_TASK_RUN_STATE_PATH` 拒绝运行，aspire 的 `web` 资源因此秒失败，会被误读成代码缺陷；需要复测前端腿时用 `pnpm install --frozen-lockfile` 装真依赖，或直接在已装依赖的正常副本上验。
- **降级日志的匹配口径（票 03 ④ 的证据链，票 11 直接依赖）**：08 把 Serilog 换成 JSON formatter 后，`Degraded mode:` 仍渲染成**可 grep 的文本**、不是模板占位（老登在 `299421d` 实跑确认，票 03 的通过结论在 08 之上继续成立）。但**行尾现在多了 ` | {"SourceContext": "Pzd.Api"}`**，所以票 11 断言必须用**子串**「`Degraded mode:` + 资源名（`MySQL`/`Redis`）」匹配，**不得整行相等**；另外框架自身每次探测还会打一条 `Health check redis with status Degraded completed…`（不是我们的打点），计数时别把它算进来，也别把它的存在当缺陷。**这个前缀会随资源增加而变多**：票 09 的 Quartz 也用它（`Degraded mode: Quartz persistent store unavailable（…）`），现在是 MySQL / Redis / Quartz **三条**，所以判据**按资源名精确匹配，绝对不要写「恰好两条」**——以后加资源就会红，而且红得莫名其妙。
- **断言规矩（票 03 血泪，票 11 必须遵守）**：**任何「HTTP 200 就算通过」的断言都不许再写进验收脚本**。必须证明这个 200 是**本次启动的进程**给的：API 腿核对 5080 监听者属于本次宿主进程树，前端腿核对 5199 监听者属于本次 `$webProcess` 子树，并检查 `verify-web.log.err` 里没有 `Port 5199 is already in use` / `ERR_PNPM`；取不到 pid 或拿不到父链时**判失败**，不许「无法判定就跳过」。已登记的必修项：**F-03-A10（=李总报告 F-03-A3，medium，票 11 开工前必修）**——`7c5f411` 上实测过完整假绿：自己的 vite 报 `Port 5199 is already in use` 挂掉，脚本仍打 `[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳` 并 `exit 0`，探到的是别人的实例。**验收按票面 `299421d` 四条走，别只做前三条**：① 正向要有 pid 与归属证据；② 反向要**先占 5199 再跑**，必须非零且**不许杀掉外来进程**；③ 取不到 pid / 父链不可读 / 不属于本次树 → 判 `1` 并打印原因，不许跳过；④ **再补一个「预检通过之后才出现外来监听」的受控反例**——不加这条，测试实际打到的只是端口预检，归属断言根本没被走到。另注意 api 模式以本次 web 根进程为准、aspire 模式以本次编排资源进程树为准，**不能机械假定两模式同一个根 PID**。
  - **A10 状态：四条已闭环（脚本交付 hash `79bbe36`；`398b6e8`/`b4b5d5d` 是过程提交，老登实测）**。① 正向 `[ok] … 监听者 pid … 属本次进程树`；② 反向（外来 node 占 5199、返回带 `lang="zh-CN"` 的 200）→ `exit 1` 并报 pid；③ 取不到 pid / 血脉不符（`398b6e8` 改三段式）：同一变异在 `7ccbbcf` 上 `[ok]`+`EXIT=0`，在 `398b6e8` 上 `前端 … 取不到监听者 pid，无法确认 200 是否来自本次实例`+`EXIT=1`；④ 竞态反向（预检之后才出现外来监听）由旺总的延迟插入副本实测到 `exit 1`。附加实测：判红退出时外来监听进程仍在 `LISTENING`（脚本只收拾自己的进程树，**不误伤别人的实例**）。web 腿另扫 `verify-web.log.err` 的 `already in use`/`EADDRINUSE`/`ERR_PNPM`，并检查 `$webProcess.HasExited`。
  - **F-03-A12（high，**已闭环 `562fd88`**）**：`398b6e8` 只改了 web 腿，API 腿当时仍是 `if ($listenerPid -and -not (Test-ProcessDescendsFrom …))` → `else [ok]`，pid 取不到时照样宣告通过（老登实测 `[ok] … 由本次启动的进程树监听（pid ）` + `REAL_EXIT=0`）。`562fd88` 把 web 腿的三段式搬到 API 腿：`-not $listenerPid` → 失败并写明「取不到监听者 pid，无法确认 200 是否来自本次实例」／不属于本次树 → 失败／否则 `[ok]`。张一一实测正常路径 `exit 0`、变异（函数直接 `return $null`）`REAL_EXIT=1` 两侧都有。**这条比 A10 ③ 更要紧**：API 腿是每轮 `-Mode api`/`-Mode aspire` 的主判据，也是票 11 唯一的 200 归属证据；改动只动 api 分支，未碰 aspire。
  - **F-03-A11（候选修复进行中，未判通过，票 11 剩余门禁）**：aspire 模式下 **5080 由 `dcp.exe` 代理持有**，父链 `dcp.exe → dcp.exe → 已退出` 第二跳就断——**不是深度问题**，调层数或加 job 关系都救不了，属「证据取错了对象」而非「取不到证据」。**老何的口径**：必须证明应答来自**本次运行**；`buildId = 本轮 hash` 排除不了同 hash 残留下实例，**目录/路径相同也一样不够**（老登补：同一个 `E:\pzd` 里上一轮没杀干净的 vite，路径字符串一模一样，按路径匹配照样放行）。**正确方向＝前后端复用同一个 runId**：脚本生成 `PZD_RUN_ID` 注入 → AppHost 透传给 api 与 vite 资源 → API 回 `X-Pzd-Run-Id` 响应头、`vite.config.ts` 的 `server.headers` 也从 `process.env` 回同一个头 → 脚本拿同一个 `$runId` 比对两条腿，一套判据、一个期望值。
    - **API 腿：新契约已入库（`1259466`，原子提交，老登核过五处齐全）**。`ApiVersionInfo` 回到三字段（`Version/BuildId/BuildTime`，**`RunId` 字段整个删掉**，不是 `[JsonIgnore]` 盖住）、新增 `ApiRunId.cs`（`Normalize` 空白视为未注入）、`Program.cs` 只在有值时才写 `X-Pzd-Run-Id` 响应头、脚本 `[:397-402](../../scripts/verify-apphost.ps1#L397-L402)` 改为读头并加**反向锁**（`/api/version` 响应体里一旦出现 `runId` 就判失败，防契约被回退扩张）、测试相应替换。**正向证据已锚定**：老登在 `1259466` 干净 worktree 实跑 `REAL_EXIT=0`，`buildId = 0.1.1-alpha.0.151+1259466`，`[ok] X-Pzd-Run-Id 与本次运行一致（b1c0018755404e14be7c1362f74e4814）`。机制上它证明「本次运行」而不是「版本相同」——同 hash 的残留实例 `buildId` 一样，但拿不到本轮的 `runId`。**受控反例也是真的，不是变异**：把 `5586f5c` 编译起在 5097（无 `X-Pzd-Run-Id`、载荷带 `"runId":null`），新脚本只跳过预检指向它 → `REAL_EXIT=1`，**三条判据各自独立命中**：① 监听者 pid 不属于本次进程树；② 响应体出现 `runId`，反向锁咬住；③ 缺 `X-Pzd-Run-Id` 头。**hash 别记串**：代码锚点是 `1259466`，其后的脚本提交（如 `20f9df0` 时间判据）与文档提交都在它之上。
    - **aspire 的 web 腿（已入库 `20f9df0`，但仍是过渡判据）**：现在是**三条同时成立**——5199 监听 pid 取到、监听进程命令行含本次副本 `apps\web` 绝对路径（`$expectedWebDir = Join-Path $repoRoot "apps\web"`，`[:441](../../scripts/verify-apphost.ps1#L441)`）、且**进程创建时间晚于本次脚本启动**（`$runStartedAt`，`[:456](../../scripts/verify-apphost.ps1#L456)`，张一一加的时间判据，专门堵「同一个 `E:\pzd` 里上一轮残留的 vite」——路径字符串一样，但启动时间早于本轮）。取不到 pid／读不到命令行／路径不符／启动更早，**一律判失败**。两点注意：① **分隔符没归一化**，`Join-Path` 出反斜杠，而 node 命令行可能给正斜杠，直接 `IndexOf` 会误红，建议先两边归一化成 `/`；② **web 腿仍没有 runId 证据**——`apps/web/vite.config.ts` 无 `server.headers`、AppHost 也没给 vite 资源 `WithEnvironment`，与 API 腿「同一套判据、一个期望值」的目标还没对齐。
    - **A11 审计结论（老登）：不通过**。理由两条——① **aspire 模式的 API 腿没实跑**（判据已改成两模式共用的响应头，代码对，但 A11 最初的现场就是 aspire 下 `dcp.exe` 持 5080，api 模式的绿替代不了它）；② **aspire 的 web 腿还是「副本路径」判据**（老何明确否过），`server.headers` 与 `WithEnvironment` 一行未写。所以「api 模式下 API 归属判据已立足」成立，「A11 通过」不成立。
    - **静态读取的限制仍在（别以为换了头就消失）**：`ApiRunId.Current` 也是静态初始化器（`Environment.GetEnvironmentVariable` 只读一次），**一个测试进程只可能覆盖一种环境变量状态**，`WebApplicationFactory` 的 `UseSetting`/内存配置碰不到它。切法：HTTP 级只测默认进程那条（未注入 → 三字段 + 无头），`Normalize` 的 `null`/空白/有效三种走单元测试；「注入后有头且相等」要另起进程，**不建议**把 `Current` 改成每次读（运行时环境变量本就不变，改了只为测试造出一个半死状态）。**复验权在老登，A11 不因代码已改就视为闭环。** **验收三条（老何）**：正常实例通过、**同 hash 的外来实例必须失败**、归属证据取不到时失败。
    - **写测试的环境坑（`PZD_RUN_ID` 是进程级变量）**：脚本自己会 `[Environment]::SetEnvironmentVariable("PZD_RUN_ID", …, "Process")`，所以「未注入时响应头不存在」这条断言**对运行环境敏感**——谁在该 shell 里跑测试，`Current` 就有值、头就存在，断言直接假红且看不出原因。用例开头必须显式断言前置条件：`Assert.Null(ApiRunId.Current);  // 本用例要求进程未注入 PZD_RUN_ID`，不符就明确报这个原因，别让下一个人以为实现坏了（雨哥建议的另一写法 `Assert.SkipUnless(ApiRunId.Current is null, "…已注入 PZD_RUN_ID…")` 也行，与票 10 的 Skip 口径一致；**关键是写明「环境不符」，不是「实现坏了」**）。同理**别用 `Environment.SetEnvironmentVariable` 去凑「注入态」**，那会污染并行跑的其它用例。
    - **A11 必要性的现场实证（老登、雨哥）**：5080 被上一轮 aspire 遗留的 `dcp.exe` 顶着时，`/health/live` 照样 **200**、看起来更像「服务正常」，但响应里**没有安全头**——旧实例在同一端口应答，这就是「200 不等于本次实例」的真实一击。以前关于 A11 的论据都是推理（`dcp.exe` 父链断、`buildId` 分不出同 hash 残留），这条是现场打到的；也提醒 **08 的复测**：5080 被占时拿到的 200 是旧二进制，不能当 08 的证据。
  - **F-03-A13（medium，必现，先于 A5 修）**：`Stop-ProcessTree`（`scripts/verify-apphost.ps1` 里只做递归 kill）**杀完不等端口释放**，`finally` 只有 `Start-Sleep -Milliseconds 500`（`[:489-493](../../scripts/verify-apphost.ps1#L489-L493)`）。所以「5199 退出后 >4s 仍 LISTENING（pid 88740，随后自行消失）、紧接的第二次运行撞预检 `exit 2`」**不是偶发，是脚本结构决定的必现行为**。修法：`finally` 里杀完**轮询 `Get-PortListenerPid` 直到端口不再 `LISTENING`**（上限 15s）——判据是**端口状态**、不是「进程消失」（`taskkill /T /F` 返回时子进程可能刚收到终止信号，socket 仍在 LISTEN 或 TIME_WAIT）；**超时就判环境问题 `exit 2`，不许静默继续**。别指望调用方隔开。
  - **F-03-A5（间歇性，未在 HEAD 复跑，不得据此放宽口径）**：李总在 `d13e58d` 上 3 次 2 过 1 挂（`exit 1`，预检通过、端口空、依赖已装、无 dcp 残留，前端 240s 连拒）；`f2d49b8` 2/2 过。**但 `d13e58d` 落后 `691a73b` 37 个提交**（含 runId、Quartz、脚本 +219/−32，`Program.cs` 差 73 行），那 1/3 不能作为当前 HEAD 的稳定性结论，要登记必须在新 HEAD 复跑。两条排除结论可用：**DCP 经 `cmd.exe` 执行 `pnpm.CMD` 是成功的，`cmd /c pnpm run dev` 不需要做**；**「连续 3 次里有一次过」不是改口径，是把「每次必须通过」降级**，须用户明确批准——根因未知时放宽频次等于用统计掩盖未知。
  - **端口 LISTENING ≠ HTTP 200**：前端起来后有约 **6 秒 `http=500`** 窗口（17:55:55、17:55:58 采样），之后才 200。`Wait-ForUrl` 只认 200，判定不受影响，但「端口监听」与「就绪判定」**不能混在同一个循环里**，否则一个会掩盖另一个；票 11 的就绪条件仍是「200 且 body 含 `lang="zh-CN"`」。
  - **清理清单（复测完自己收拾，别留给下一个人）**：① 强制中断会留孤儿 vite 占 5199（链根已死才可清）；② 实验用的假 SPA 服务器（`node -e` 返回 `lang="zh-CN"` 的单行 HTTP 服务）用完立刻撤，否则任何人跑前端断言都假绿；③ 删过 worktree 后检查是否留下 `apps`/`packages`/`node_modules` 空壳目录（`E:\pzd-a11` 就留过三个）；④ 临时变异副本（如 `scripts/.race2.ps1`）不要留在仓库里，更别误提交。
- **会留下孤儿 vite**：脚本/验收进程被强杀或中断时，`Stop-ProcessTree` 来不及跑，`pnpm --filter @pzd/web dev` 的子进程会继续占着 5199（进程链根已死）。下一次验收要么被预检拦成 `exit 2`，要么（A10 未修前）假绿。清理前先确认**链根 pid 已不存在**，别把别人正在跑的实例当孤儿杀掉；正常退出后应断言 5080/5199 均无 `LISTENING`。
- **`scripts/verify-apphost.ps1` 退出码三档（票 03 起，票 11 必须沿用；已于 `f778a32` 定稿）**：票面 `756c1fc` 定的是 `0` = 指定模式全部通过 / `1` = 构建、启动或验收失败 / `2` = 环境预检受阻（**非零、未完成，CI 不得放绿**），票 11 汇总优先级 `1 > 2 > 0`，保留原始退出码与失败阶段；退出码只分类结果，不单独裁定根因。
  - **代码与票面已一致（`f778a32` 起；前缀在 `931d700` 调整）**：`scripts/verify-apphost.ps1:281-283` 构建失败走 **`[build] 构建失败（…）：先修编译再验票`** + **`exit 1`**（`f778a32` 把原 `exit 2` 与「环境/占用问题，不是本票缺陷」的误导前缀一并改掉，老登 F-03-A9 闭环；`931d700` 再把前缀从 `[fail]` 调整为 `[build]`，退出码仍为 1，**别回退成 `[fail]`**）；`exit 2` 只剩端口占用、依赖缺失等环境预检分支。因此**编译不过 = 1 = 本票缺陷**，环境问题 = 2 = 非零但不算产品缺陷，两者不得混用，也不许有人拿 2 当「机器问题」分诊掉红。

## 6. 可直接复制的验证命令

```powershell
dotnet build Pzd.slnx -warnaserror --no-incremental
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version
```

## 7. Suggested skills（给下一个 agent）

- `test-driven-development`：07 返工先从「改名 token 必须红」「分发器变坏必须红」写起。
- `verification-before-completion`：声称完成前跑第 6 节整套命令并贴输出；干净副本用 worktree，别用 `git archive` 验 build。
- `requesting-code-review` / `receiving-code-review`：交给老登终审前自检；收到 findings 先复现再改。
- `systematic-debugging`：遇到 MTP 握手、MinVer、循环依赖、时区断言这类环境型失败时，先分清「环境失败 / 测试失败 / 产品缺陷」。
