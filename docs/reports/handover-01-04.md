# 交接记录：票 01 / 04 / 05 / 06 / 07 / 12 全部通过（基线 `7f699c5`，185 条）→ 下一轮

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 文件名沿用首轮，内容已覆盖票 01–12；下一轮请从本文件第 2、3 节接着走。
> 状态快照：代码基线 `7f699c5`（票 07 返工提交完成）；HEAD 为最新文档提交。`dotnet build Pzd.slnx -warnaserror --no-incremental` 0 警告 0 错误；五个测试工程 35+30+86+33+1 = **185 条全绿**（我于 2026-10-27 在 `7f699c5` 实跑确认；此前的 173 是 `0da4a18` 快照，已作废）。
> 结论：**票 01、04、05、06、07、12 全部收口**（[01/04 终审](review-01-04-final.md)、[05 终审](review-05.md)、[06 终审](review-06.md)、**07 终审 [`review-07.md`](review-07.md)（`7f699c5`，185 条）**、[12 审计](review-12.md)）。票 02（前端 monorepo 骨架）已开工，它是 03 Aspire 与 11 验收脚本的唯一前置；之后 08 → 10。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / `7f699c5`（07 终审通过；其后仅文档提交，02 的前端文件尚未入库） |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`） |
| 构建 | 0 警告 0 错误（`-warnaserror --no-incremental`，我在 `7f699c5` 跑过） |
| 测试 | 185 条、0 失败（架构 30 / SharedKernel 35 / Infrastructure 86 / Api 33 / 集成 1；见各票测试报告） |
| 生产代码 | 05：`Infrastructure/Tenancy/*`；06：`Api/Errors/*` + `Infrastructure/Errors/ProblemDetailsWriter.cs`；07：`Infrastructure/Persistence/*`（含 `OutboxOptions`） |
| 进行中 | 票 02 前端 monorepo 骨架（`apps/web` + `packages/*` + turbo/pnpm，工作区未提交） |
| 下一张票 | 02 → 03 Aspire（受 F-05-4 约束）→ 08 → 10 → 11 |

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
- **终审（老登，`7f699c5`）**：独立 worktree 复跑 build 0/0 与 185 条全绿；F-07-5 四条变异全部被抓（硬编码 `>= 5` → 2 红、不写 `LastError` → 1 红、加回 `MaxAttempts` 常量 → 1 红、`Load` 放宽 `< 1` → `< 0` → 1 红）；「单处来源」成立（判定只用 `_options.MaxAttempts`，默认值只在 `OutboxOptions.DefaultMaxAttempts`，`Program.cs:35` 启动期 `Load`，非法值直接抛）。剩 2 条 low 观察项不阻塞：`appsettings.json:14` 显式写了 `"MaxAttempts": 5`，与 `DefaultMaxAttempts` 是两处字面值（改默认值时容易漏改，建议删键让默认生效或注明为运行时覆盖）；规则边界见 §6.8——**类型系统是唯一强保证，字符串规则拦误用、不防蓄意规避**。
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

1. **旺总 / Chief**：票 **02 前端 monorepo 骨架**（`apps/web` + `packages/*` + pnpm/turbo，React Router v7、`ssr: false`、dev proxy）已在工作区，按票面四条验收命令跑通后提交——它是 03 Aspire 与 11 的唯一前置。
2. **之后**：03 Aspire（受 F-05-4 约束，`/health`、`/api/version` 用 `AllowAnonymousTenant`）→ 08 可观测性 + 审计 + 安全横切 → 10 租户隔离集成测试（用本机 MySQL/Redis 真实例，无实例则 Skip）→ 11 验收脚本。
3. **老何**：票面进度行要改——07 已 ✅（`7f699c5`，185 条，终审 `review-07.md`）、02 已开工；03 开工前确认 F-05-4 已生效；维护「步骤 3 移交清单」。
4. **待处理 low（不阻塞）**：`appsettings.json` 的 `Outbox:MaxAttempts` 与 `DefaultMaxAttempts` 是两处字面值，改默认值时删键或注明为运行时覆盖。
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
- **07 内存实现**：Outbox/UoW 的事务仍是**补偿删除**，不是真实回滚（步骤 3 换真实事务并删补偿）；失败原因、`MaxAttempts=5`、零消费者告警已随 `001a731` 补上；处理器级幂等（F-07-6）留步骤 3。报告与文档不得写成「事务回滚已验证」。
- **MinVer**：浅克隆丢 tag 会让版本掉成 `0.0.0-alpha…`。

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
