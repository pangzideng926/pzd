# 交接记录：票 01–07、12、02、03 全部通过（03 基线 `f778a32`，204 条）→ 下一轮

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 文件名沿用首轮，内容已覆盖票 01–12；下一轮请从本文件第 2、3 节接着走。
> 状态快照：**当前 HEAD `db17a9b`**（`main`，工作区只有 `docs/reports/` 下的在途文档）。已收口代码基线仍是 `f778a32`（票 03 终审点；`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误，五个测试工程 35+30+86+43+10 = **204 条全绿、0 Skip**，老登于 2026-10-27 在 `f778a32` 干净 worktree 实跑），`204` 只对 `f778a32` 成立。此后实测条数：08 在 `e8f2e2d` 上 **229 条**（35+30+86+68+10，0 失败 0 Skip，报告 `test-report-08.md`）；09 的一键脚本实跑锚 `6b67c2d`（报告 `test-report-09.md`）。
> 结论：**票 01、04、05、06、07、12、02、03、10 全部收口**（[01/04 终审](review-01-04-final.md)、[05 终审](review-05.md)、[06 终审](review-06.md)、**07 终审 [`review-07.md`](review-07.md)（`7f699c5`，185 条）**、[12 审计](review-12.md)、**02 终审 [`review-02.md`](review-02.md)（`57c226e`/复核 `1588559`）**、**03 终审 [`review-03.md`](review-03.md) §9（`f778a32`）**、**10 终审 [`review-10.md`](review-10.md)（锚 `204f93e`，11/11）**）。在途：**08、09 都已测试通过、待老登审计**（08 锚 `e8f2e2d`/[报告](test-report-08.md)；09 锚 `6b67c2d`/[报告](test-report-09.md)）；11 验收脚本最后收口，门禁只剩 **F-03-A11** 的 aspire 全链路实跑；它此前被 **A5（前端 `abort` TDZ 掩盖真错误，已改判为代码缺陷）**挡着，而 **A5 的修复已交付**（`bf08bd4` 自定义 entry + `4485ca0` 失败路径清定时器、超时显式失败），只差 aspire 现场确认 DCP 是否注入 `NODE_ENV=production`。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / **HEAD `db17a9b`**；已收口基线 `f778a32`（票 03 收尾，含脚本 A1/A2/A9）；票 08 代码在 `e8f2e2d`（对 08 路径而言 `1259466`/`20f9df0` 与它等价，雨哥核过 diff 为空） |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>+<sha 前 7 位>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`） |
| 构建 | 0 警告 0 错误（`-warnaserror`，`f778a32` 上老登实跑；`6b67c2d` 上张一一实跑 0 警告 0 错误） |
| 测试 | **204 条、0 失败 0 Skip**（SharedKernel 35 / 架构 30 / Infrastructure 86 / Api 43 / 集成 10；`f778a32` 快照；此前的 185 是 `7f699c5`、173 是 `0da4a18`，已作废）。票 08 在 `e8f2e2d` 上实测 **229 条**（Api 68）；`1259466` 后 Api 73，`beb83f3` 后 **Api 78**（张一一实测全绿），别把三个数混用 |
| 生产代码 | 05：`Infrastructure/Tenancy/*`；06：`Api/Errors/*` + `Infrastructure/Errors/ProblemDetailsWriter.cs`；07：`Infrastructure/Persistence/*`（含 `OutboxOptions`）；08：`Infrastructure/{Observability,Audit,Security}`；09：`Infrastructure/Scheduling/*` |
| 进行中 | **08 测试通过待审计**（`e8f2e2d`/`test-report-08.md`，229 条）；**09 测试通过待审计**（`6b67c2d`/`test-report-09.md`）；**10 已审计通过**；11 门禁只剩 A11 的 aspire 全链路实跑，被 A5 挡着 |
| 下一张票 | 08/09 送审计 → A5 修复（`apps/web` 自定义 `entry.server`）→ aspire 全链路实跑 → A11 复审 → 11 收口（`CLAUDE.md` + 一键脚本文档） |

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
2. **票 03 已完成**（✅ 终审 [`review-03.md`](review-03.md) §9，被测 `f778a32`，204 条 0 失败 0 Skip；A1 血缘核对正反两面、A2 自举构建、A9 构建失败归 1 均闭环；记录 [`test-report-03.md`](test-report-03.md)）。**范围外仍欠一次证据**：`-Mode aspire` 全链路（含 5199 前端腿）**在 A5 修复之后**跑一次并贴 `web` 资源状态——老登两次审计副本都没走完这条腿（一次目录联结被 pnpm 拒绝、一次为避端口冲突用 `-SkipWeb`），所以**不得写成已全链路通过**。`.CMD` 悬案**已按「不是根因」结案**（DCP 经 `cmd.exe` 执行 `pnpm.CMD` 成功，`cmd /c pnpm run dev` 不必做）：真正的原因是前端 dev server 自己崩（A5，见 §5）。之后：08/09 送审计 → 11 验收脚本收口（前端 `127.0.0.1:5199`、后端 `127.0.0.1:5080`，dev 冷启动要**轮询**最多 60 秒，别 `sleep 5` 后一次性 curl；A10/A12/A13 均已闭环，剩下的门禁是 A11 的 aspire 实跑）。
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

- **测试运行方式**：`E:\pzd` 当前是 Low Mandatory Level（**注意：这个标签是下面那条 ACL 污染的产物，不是「环境天生如此」**），`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。**ACL 修好后应重验一次 `dotnet test` 是否恢复**——若恢复，这条就只是污染期现象，别长期当成规矩。
- **主仓 aspire 跑不起来＝仓库根目录被 Aspire 加固污染（自伤，不是环境天生不行）**：`scripts/verify-apphost.ps1` `:221` 把 `TEMP`/`TMP` 指到**仓库内**的 `E:\pzd\.aspire-tmp`，Aspire 的 `SocketPermissionHelper` 给「临时目录」做加固时**把 ACL 加到了仓库根上**。实测 `icacls E:\pzd`（2026-10-27 23:5x）：

  ```
  Everyone:(CI)(DENY)(DC)                         ← 继承来的 Deny
  S-1-4-679037864-1046299354:(OI)(CI)(W,D,DC)     ← AppContainer SID
  SJ\q1887:(WO,S)                                 ← 我的完全控制被换成只剩「改所有者+同步」
  Mandatory Label\Low Mandatory Level:(OI)(CI)(NW) ← 整个仓库被打低完整性标签
  ```

  对比能跑通的副本（`E:\pzd-webcheck`）：`Authenticated Users:(I)(M)`、**无 Deny、无低完整性标签**。所以症状链是：DCP 想在仓库下建日志套接字 → `SetSecurityInfo` 被拒（`UnauthorizedAccessException`）→ 两分钟 Polly 超时、资源一个都没创建。**删 `.aspire-tmp` 修不好**：那两条受限 ACE 是 `(I)` 继承自根目录，重建后原样回来（张一一实测过这一点，别重复踩）。
- **修 ACL 要用户点头**：改的是共享仓库权限，且属于「改完必须复验」的操作。两条路——① 用 DSH 自带的 ACL 修复脚本（只对 `E:\pzd` 补回 `q1887` 完全控制，不动 Deny、不递归改子项、逐条打印回滚命令）；② 用户/管理员手工恢复继承并去掉低完整性标签。**在修好之前，主仓只能用 `-Mode api`（不需要 DCP）；要跑 aspire 全链路就开干净 worktree**（李总那次 `EXIT=0` 就是这么来的）。
- **修好后应顺手改脚本**：`:221` 的 `TEMP` 不要再指向仓库内，改到仓库**同级**目录（如 `E:\pzd-aspire-tmp`），否则下一次加固还会给仓库根打标签——这是同一类自伤的唯一根治点（属于票 11 脚本改动，改前先在群里说）。
- **干净副本验证**：`git archive` 出来的目录没有 `.git`，MinVer 会因 `MINVER1001`（不是 Git 仓库）报错导致 `-warnaserror` 构建失败，但测试仍可跑。要验证 build 0/0，用 `git worktree add --detach` 而不是 `git archive`。
- **aspire 的启动方式决定成败（本机实测）**：从 **bash 工具**启动 AppHost 会立刻失败——默认 `%TEMP%` 下 `Directory.CreateTempSubdirectoryCore` → `Dcp.Locations.GetOrCreateDcpSessionDir` 抛 `System.UnauthorizedAccessException: Access to the path 'C:\Users\…\Temp\aspire-dcp<rand>.<rand>' is denied.`，外层就是 `Hosting failed to start`。换成**从 pwsh 启动**、并让 `TEMP`/`TMP` 都指向仓库内临时目录（`scripts/verify-apphost.ps1` `:221` 已这么做）后，DCP 才真的起来（`Starting DCP with arguments: start-apiserver --monitor … --detach --kubeconfig …`）。**别把「起不来」当成代码缺陷**，先看是谁在跑、用哪个 shell 跑的。
- **DCP 启动成功 ≠ 编排成功（这才是大多数「卡住」的真因）**：DCP 起来之后仍可能死在 `CreateDcpObjectsAsync` → `KubernetesService.EnsureKubernetesAsync` 的 **Polly 两分钟超时**（`TimeoutRejectedException: The operation didn't complete within the allowed timeout of '00:02:00'`，最终 `AggregateException` 冒到 `Program.Main` 的 `app.Run()`）。典型现场：`state=(unknown)`、**一条资源事件都没有**、`环境变量共 0 项`——因为**资源根本没被创建**，不是事件被吞、也不是环境变量没传。证据：[wz-aspire2.log](../../artifacts/wz-aspire2.log)（57KB，含完整调用栈）。所以「DCP 日志里什么都没有」有两种完全不同的成因：① web 崩在监听前（A5，旧形态 `Running` 后立刻 `Finished exitCode=1`）；② 编排层压根没走到资源创建（本节这条）。
- **`[pzd-diag] env` 出现 `0 项` 时先分两种成因再下结论**：正常启动中 `(unknown)`/`Starting`/`Waiting` 阶段本来就是 0 项（实测 5 次 0 项、`Running` 后才 103 项）——这是**时序没到**；而如果整轮日志里状态始终 `(unknown)`、没有任何迁移、最后以 2 分钟 Polly 超时收尾，那是**资源没创建**。两种都会打「0 项」，别用同一个结论套。**输出形态从 `db17a9b` 起变了**：空快照不再打印白名单值，只打一行说明（「环境变量快照为 0 项：这不代表 web 进程没有环境变量，只是该状态的快照还没填充」），所以**别再拿「环境变量共 0 项」当证据行**——那是旧 hash 的产物；新 hash 里看到这行说明，就等于「此刻还不是下结论的时机」。
- **探活用过 HEAD 就必须只看 GET 的状态码**：`entry.server.tsx` 的 `:57` 在渲染**之前**就为 HEAD 短路返回（`return new Response(null, { status: responseStatusCode })`），那时 `responseStatusCode` 还是初始 `200`——所以**HEAD 恒 200，哪怕渲染会失败**。不严谨的探活（先 `curl -sSI` 看 200 就以为好了）会直接读出假绿；脚本里没有 HEAD 用法，判据都走 GET。
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
- **降级日志的匹配口径（票 03 ④ 的证据链，票 11 直接依赖）**：08 把 Serilog 换成 JSON formatter 后，`Degraded mode:` 仍渲染成**可 grep 的文本**、不是模板占位（老登在 `299421d` 实跑确认，票 03 的通过结论在 08 之上继续成立）。但**行尾现在多了 ` | {"SourceContext": "Pzd.Api"}`**，所以票 11 断言必须用**子串**「`Degraded mode:` + 资源名（`MySQL`/`Redis`）」匹配，**不得整行相等**；另外框架自身每次探测还会打一条 `Health check redis with status Degraded completed…`（不是我们的打点），计数时别把它算进来，也别把它的存在当缺陷。**这个前缀会随资源增加而变多**：票 09 的 Quartz 也用它（`Degraded mode: Quartz persistent store unavailable（…）`），现在是 MySQL / Redis / Quartz **三条**，所以判据**按资源名精确匹配**（如 `Degraded mode: MySQL unavailable（`）**，绝对不要写「恰好两条」或数前缀个数**——以后加资源就会红，而且红得莫名其妙。
- **断言规矩（票 03 血泪，票 11 必须遵守）**：**任何「HTTP 200 就算通过」的断言都不许再写进验收脚本**。必须证明这个 200 是**本次启动的进程**给的：API 腿核对 5080 监听者属于本次宿主进程树，前端腿核对 5199 监听者属于本次 `$webProcess` 子树，并检查 `verify-web.log.err` 里没有 `Port 5199 is already in use` / `ERR_PNPM`；取不到 pid 或拿不到父链时**判失败**，不许「无法判定就跳过」。已登记的必修项：**F-03-A10（=李总报告 F-03-A3，medium，票 11 开工前必修）**——`7c5f411` 上实测过完整假绿：自己的 vite 报 `Port 5199 is already in use` 挂掉，脚本仍打 `[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳` 并 `exit 0`，探到的是别人的实例。**验收按票面 `299421d` 四条走，别只做前三条**：① 正向要有 pid 与归属证据；② 反向要**先占 5199 再跑**，必须非零且**不许杀掉外来进程**；③ 取不到 pid / 父链不可读 / 不属于本次树 → 判 `1` 并打印原因，不许跳过；④ **再补一个「预检通过之后才出现外来监听」的受控反例**——不加这条，测试实际打到的只是端口预检，归属断言根本没被走到。另注意 api 模式以本次 web 根进程为准、aspire 模式以本次编排资源进程树为准，**不能机械假定两模式同一个根 PID**。
  - **A10 状态：四条已闭环（脚本交付 hash `79bbe36`；`398b6e8`/`b4b5d5d` 是过程提交，老登实测）**。① 正向 `[ok] … 监听者 pid … 属本次进程树`；② 反向（外来 node 占 5199、返回带 `lang="zh-CN"` 的 200）→ `exit 1` 并报 pid；③ 取不到 pid / 血脉不符（`398b6e8` 改三段式）：同一变异在 `7ccbbcf` 上 `[ok]`+`EXIT=0`，在 `398b6e8` 上 `前端 … 取不到监听者 pid，无法确认 200 是否来自本次实例`+`EXIT=1`；④ 竞态反向（预检之后才出现外来监听）由旺总的延迟插入副本实测到 `exit 1`。附加实测：判红退出时外来监听进程仍在 `LISTENING`（脚本只收拾自己的进程树，**不误伤别人的实例**）。web 腿另扫 `verify-web.log.err` 的 `already in use`/`EADDRINUSE`/`ERR_PNPM`，并检查 `$webProcess.HasExited`。
  - **F-03-A12（high，**已闭环 `562fd88`**）**：`398b6e8` 只改了 web 腿，API 腿当时仍是 `if ($listenerPid -and -not (Test-ProcessDescendsFrom …))` → `else [ok]`，pid 取不到时照样宣告通过（老登实测 `[ok] … 由本次启动的进程树监听（pid ）` + `REAL_EXIT=0`）。`562fd88` 把 web 腿的三段式搬到 API 腿：`-not $listenerPid` → 失败并写明「取不到监听者 pid，无法确认 200 是否来自本次实例」／不属于本次树 → 失败／否则 `[ok]`。张一一实测正常路径 `exit 0`、变异（函数直接 `return $null`）`REAL_EXIT=1` 两侧都有。**这条比 A10 ③ 更要紧**：API 腿是每轮 `-Mode api`/`-Mode aspire` 的主判据，也是票 11 唯一的 200 归属证据；改动只动 api 分支，未碰 aspire。
  - **F-03-A11（代码面已落地 `564ae6d`/`728536a`，仍未判通过，票 11 剩余门禁）**：aspire 模式下 **5080 由 `dcp.exe` 代理持有**，父链 `dcp.exe → dcp.exe → 已退出` 第二跳就断——**不是深度问题**，调层数或加 job 关系都救不了，属「证据取错了对象」而非「取不到证据」。**老何的口径**：必须证明应答来自**本次运行**；`buildId = 本轮 hash` 排除不了同 hash 残留下实例，**目录/路径相同也一样不够**（老登补：同一个 `E:\pzd` 里上一轮没杀干净的 vite，路径字符串一模一样，按路径匹配照样放行）。**正确方向＝前后端复用同一个 runId**：脚本生成 `PZD_RUN_ID` 注入 → AppHost 透传给 api 与 vite 资源 → API 回 `X-Pzd-Run-Id` 响应头、`vite.config.ts` 也从 `process.env.PZD_RUN_ID` 回同一个头（**实现上不能用 `server.headers`**，原因见下条） → 脚本拿同一个 `$runId` 比对两条腿，一套判据、一个期望值。
    - **API 腿：新契约已入库（`1259466`，原子提交，老登核过五处齐全）**。`ApiVersionInfo` 回到三字段（`Version/BuildId/BuildTime`，**`RunId` 字段整个删掉**，不是 `[JsonIgnore]` 盖住）、新增 `ApiRunId.cs`（`Normalize` 空白视为未注入）、`Program.cs` 只在有值时才写 `X-Pzd-Run-Id` 响应头、脚本 `[:397-402](../../scripts/verify-apphost.ps1#L397-L402)` 改为读头并加**反向锁**（`/api/version` 响应体里一旦出现 `runId` 就判失败，防契约被回退扩张）、测试相应替换。**正向证据已锚定**：老登在 `1259466` 干净 worktree 实跑 `REAL_EXIT=0`，`buildId = 0.1.1-alpha.0.151+1259466`，`[ok] X-Pzd-Run-Id 与本次运行一致（b1c0018755404e14be7c1362f74e4814）`。机制上它证明「本次运行」而不是「版本相同」——同 hash 的残留实例 `buildId` 一样，但拿不到本轮的 `runId`。**受控反例也是真的，不是变异**：把 **`5586f5c`（反例靶子——旧契约二进制，曾被 `1259466` 取代，别再把它当当前实现读）** 编译起在 5097（无 `X-Pzd-Run-Id`、载荷带 `"runId":null`），新脚本只跳过预检指向它 → `REAL_EXIT=1`，**三条判据各自独立命中**：① 监听者 pid 不属于本次进程树；② 响应体出现 `runId`，反向锁咬住；③ 缺 `X-Pzd-Run-Id` 头。**hash 别记串**：代码锚点是 `1259466`，其后的脚本提交（如 `20f9df0` 时间判据）与文档提交都在它之上。
    - **aspire 的 web 腿：已改用同一个 runId（`564ae6d`），路径/启动时间那套过渡判据作废**。落地三点：① `apps/web/vite.config.ts` 在 `configureServer` 里注册**最前面的中间件**直接 `setHeader("X-Pzd-Run-Id", runId)`，未注入则不加任何头（`const runId = process.env.PZD_RUN_ID?.trim() ?? ""`）；② `src/Pzd.AppHost/Program.cs` 的 `WithEnvironment("PZD_RUN_ID", runId)` 加在 **vite 资源**上（不是 AppHost 自己——`vite.config.ts` 读的是自己的 `process.env`）；③ 脚本 web 腿删除路径/启动时间判据，改为与 API 腿比对**同一个 `$runId`**，缺失或不符均判失败，比对用 `-cne` 锁大小写（runId 是小写 hex，`-ne` 大小写不敏感；`[:414](../../scripts/verify-apphost.ps1#L414)` 那行旁的注释就是在说这件事，**有意保留，别改回去**）。**关键坑：不能用 `server.headers`**——它只作用于 Vite 自身那层中间件，而 `/`（SPA 文档）由 React Router 的文档中间件应答，实测 `/@vite/client` 带头而 `/` 不带头，而脚本要验的正是 `/`。**取不到监听 pid 也不放过（`728536a`）**：原 aspire 分支只在 runId 头匹配时打 `[ok]`，`$webListenerPid` 为 `$null` 也照过——那正是 A10 ③ 要禁的「查不到就算过」（runId 头证明「应答来自本次运行」，pid 证明「监听者是谁」，两者不能互相顶替）。变异证据：把 `Get-PortListenerPid` 直接 `return $null`，同一轮里 `[ok] X-Pzd-Run-Id 与本次运行一致` 照样出现、两条腿仍判红 `exit 1`，说明这条判据独立生效。**已被取代、别再引用**：`20f9df0` 的「副本路径 + `CreationDate` 晚于 `$runStartedAt`」三条过渡判据，以及 `[:456](../../scripts/verify-apphost.ps1#L456)`「取不到 `CreationDate` 仍打 `[ok]`」那个缺口——整段判据在 `564ae6d` 里被删掉了，不是给它补了一个分支。
    - **A11 正向证据已拿到（李总，2026-10-27，aspire 全链路 `EXIT=0`）**：`[ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 58668），且是 SPA 外壳：由本次运行拉起`——这是 A11 从「只有代码」到「有运行证据」的分界点，也同时证明 `WithEnvironment` 真的落到了 vite 进程（`[pzd-diag] env PZD_RUN_ID=9ef57153c7174b0481330bd0a187e302` 与脚本本轮 runId 逐字符一致，且 `/` 的响应头是同一个进程回的，两条独立证据互证）。**但 A11 仍不算通过**，见下条——缺的是反例与主仓复跑，不是正向。
    - **A11 审计结论（老登）：仍不通过；正向拿到了，缺的是反例、主仓复跑与复审**。上一轮「不通过」的两条——① aspire 的 API 腿没实跑、② web 腿是副本路径判据——现在 ② 已随 `564ae6d` 落地，① 已在那次 aspire 全链路（锚 `5682cd0`，副本）拿到正向证据。**剩下的三件事，缺一条都不能记通过**：① **反例没做**——老何验收里的「**同 hash 的外来实例必须失败**」目前只在 api 模式用 `5586f5c`（反例靶子）验过，aspire 形态下还没有；② **那次绿跑在干净副本里**，主仓复跑目前被 DCP **编排层**挡住——`CreateDcpObjectsAsync` → `EnsureKubernetesAsync` 的 **Polly 两分钟超时**（现场 `state=(unknown)`、零资源事件、env 0 项，资源根本没创建），根因是**仓库根目录 `E:\pzd` 被 Aspire 的临时目录加固污染**（低完整性标签 + AppContainer SID + `q1887` 只剩 `(WO,S)`；`.aspire-tmp` 只是继承，**删它无效**），另有「bash 工具启动时 `%TEMP%` 不可写」一层（见 §5）；都**不是 A5、也不是脚本缺陷**。要在主仓复跑一次，才能把结论从「副本绿」写成「主仓绿」；③ **老登尚未对本轮证据出复审结论**。三条齐了，A11 才记通过、票 11 才解除门禁。
    - **静态读取的限制仍在；三层保护不等于「注入态 HTTP 自动化测试」（措辞别写错）**：`ApiRunId.Current` 是静态初始化器（环境变量只读一次），**一个测试进程只可能覆盖一种环境变量状态**，`UseSetting`/内存配置碰不到它——这个事实没变，也**不要**把 `Current` 改成每次读（运行时环境变量本就不变，改了只为测试造出一个半死状态）。现在的分工是三件事、各管一段：**① `Apply` 单测**（`beb83f3`，`ApiRunIdTests` 7 个用例：`Normalize` 三态、头名固定、`Apply` 正反、载荷键集合恰好 `["buildId","buildTime","version"]`）管「给定 runId 与 headers，写不写」；**② 源码守卫**（`0a2c998` → `d674420`）管「端点调用点存在且唯一」；**③ 实跑**管「真进程里头真出来」（老登验过 `[ok] X-Pzd-Run-Id 与本次运行一致`）。**仍然没有任何自动化能在注入态下打一次 `/api/version` 并断言头**——老何那句「序列化单测不是 HTTP 接线测试」依然成立，守卫只堵「漏调」这条最可能的退化，**不是把缺口填了**。交接可以写「**接线由源码守卫保护、写头由实跑覆盖**」，**不能写「已有 HTTP 级自动化」**。HTTP 级用例只是补充证据，被 Skip 也不影响取值与写头受验。**验收三条（老何）**：正常实例通过、**同 hash 的外来实例必须失败**、归属证据取不到时失败。
    - **源码级接线守卫的边界（`d674420`，老登审过接受，两条 low 保留）**：两条独立断言——全文**恰好一处** `ApiRunId.Apply(`，**且**这一处必须落在 `MapGet("/api/version")` 到下一个 `app.Map` 之间的片段里；`EndpointBody` 找不到起点时 `Assert.True` **明确报错**（守卫自身失效要坏得很响，不许静默通过——这是文本断言最容易漏的一环）。载荷反向锁改 `OrdinalIgnoreCase`（小写 `runId` 也抓），只剔 `//` 与 `///` 行。**标准假绿路径（复核变异效力时照这个复现）**：把调用从 version 端点**挪到** `/health/live`、version 端点那行删掉——全文仍恰好一处，**旧守卫逻辑上必然放行**（它只断言全文 `Contains`，字符串只是挪了位置，这条不需要再实跑），新守卫在 `Assert.Contains` 处红；还原后 32/0、`git status src/` 干净。**两个已知 low，别当成新 bug**：① `EndpointBody` 靠 `IndexOf("app.Map")` 截断，端点若改成 `endpoints.MapGet(...)` 或用 `app.MapGroup(...)` 包一层，截断就失效——失效方式是报错而非假绿，但那时要人改守卫；② `IsComment` 只剔 `//` 与 `///`，**块注释 `/* */` 没剔**，在块注释里写 `runId` 会假红。格式化改动（换行、对齐、空格）也会误红，这是**刻意的取舍：误报好过误放行**。
    - **写测试的环境坑（`PZD_RUN_ID` 是进程级变量）**：脚本自己会 `[Environment]::SetEnvironmentVariable("PZD_RUN_ID", …, "Process")`，所以「未注入时响应头不存在」这条断言**对运行环境敏感**。**准确写法（雨哥定稿，照这个抄）**：「未注入时无归属头」这条用例要求进程**未**注入 `PZD_RUN_ID`——测试进程默认满足（CI、新开的 shell 都算），只有「同一条 shell 先跑过 `verify-apphost.ps1` 再跑测试」才会命中，此时用 `Assert.SkipUnless(ApiRunId.Current is null, "当前进程已注入 PZD_RUN_ID…")` **Skip 并报环境原因**，别判红让人以为实现坏了。而「注入时头等于注入值」由 `ApiRunIdTests` 的 `Normalize` 单测覆盖，**不受该前提影响**——所以即使 HTTP 那条被 Skip，取值与序列化逻辑仍然受验。**别用 `Environment.SetEnvironmentVariable` 去凑「注入态」**，那会污染并行跑的其它用例。彻底去掉这个 Skip 的办法（`IRunIdProvider` 走 DI 单例、启动期读一次）讨论过但**本轮不做**：`Program.cs` 已有三家改动，代价不值，留票 11 若有余量再说。
    - **A11 必要性的现场实证（老登、雨哥）**：5080 被上一轮 aspire 遗留的 `dcp.exe` 顶着时，`/health/live` 照样 **200**、看起来更像「服务正常」，但响应里**没有安全头**——旧实例在同一端口应答，这就是「200 不等于本次实例」的真实一击。以前关于 A11 的论据都是推理（`dcp.exe` 父链断、`buildId` 分不出同 hash 残留），这条是现场打到的；也提醒 **08 的复测**：5080 被占时拿到的 200 是旧二进制，不能当 08 的证据。
  - **F-03-A13（medium，已闭环 `16bd0f4`，原为「必现、先于 A5 修」）**：`Stop-ProcessTree`（`scripts/verify-apphost.ps1` 里只做递归 kill）**杀完不等端口释放**，`finally` 原先是固定 `Start-Sleep -Milliseconds 500`。所以「5199 退出后 >4s 仍 LISTENING（pid 88740，随后自行消失）、紧接的第二次运行撞预检 `exit 2`」**不是偶发，是脚本结构决定的必现行为**。实现：新增 `Wait-PortReleased`（`[:147](../../scripts/verify-apphost.ps1#L147)`），`finally` 里杀完**轮询 `Get-PortListenerPid` 直到端口不再 `LISTENING`**，上限 15s、间隔 250ms——判据是**端口状态**，不是「进程消失」（`taskkill /T /F` 返回时子进程可能刚收到终止信号，socket 仍在 LISTEN 或 TIME_WAIT）。别指望调用方隔开。
    - **判决语义与原计划不同，这是有意的，别当漏改**：原定「超时就归环境问题 `exit 2`」，实现改成**只打 `[warn]`、不 `exit 2`**——那一轮的应用结论已经出了，清理滞后不该把整轮判成环境问题（否则一个已经通过或已经失败的判决会被清理阶段改写）。但 `[warn]` 里写明了「紧接着的下一轮可能撞预检 `exit 2`」，不是静默。**若要把判决语义改回 `exit 2`，需老何裁定**，因为那会改变退出码对调用方的含义。
  - **F-03-A5（high；已改判为前端代码缺陷；修复件 `bf08bd4`/`4485ca0`/`331fac3` 已过老登代码审计，且**运行侧已通过**：aspire 全链路 `EXIT=0`、44 行日志里 `web` 的 `Finished` 出现 **0 次**、`exitCode` 非 `-` 出现 **0 次**、`state=Running` 稳定收尾，§17 那个 `Running ×4 → Finished exitCode=1` 形态消失）**：**先撤回一条错结论**——「web 资源压根没被 DCP 拉起」**不成立**（李总已撤回 §17，老登也认领自己把「观测到没起来」写成了「本机验不了」，那是过度推断）。实测是 DCP 拉起来了、**dev server 自己崩了**：
    - 现场：`[pzd-diag] resource=web state=Running ×4 → Finished exitCode=1`；`[wrap] cwd=…\apps\web args=run dev --port 5199`；随后 `ReferenceError: Cannot access 'abort' before initialization`（`…@react-router/dev/dist/config/defaults/entry.server.node.tsx:30`），进程 `exit 1`，5199 自始至终无人监听。**对照**：同一副本手动 `pnpm run dev --port 5199` **5/5 起得来**，再发 3 个请求 **3/3 200**。
    - **默认 entry 的代码形状（这是「为什么会被掩盖」，行号指 `@react-router/dev` 7.18.4 的默认文件）**：`export const streamTimeout = 5_000`（:10）、`setTimeout(() => abort(), streamTimeout + 1000)`（:42-45）、而 `const { pipe, abort } = renderToPipeableStream(...)` 在 **:47** —— **引用在前、声明在后**。所以一次文档请求进来时只要 `renderToPipeableStream` **同步抛错**（Promise executor 里抛 → 直接变 rejected promise），`const` 那行就永远执行不完、`abort` 停在 TDZ；6 秒后定时器回调访问 `abort` → 未捕获 `ReferenceError` → `exit 1`。**6000ms = `streamTimeout(5000) + 1000`，这个数与实测完全对上，不是巧合。** 注意：能抛出这个 `ReferenceError`，本身就是「同步抛出过」的证据（否则 `abort` 早已初始化）。
    - **根因的因果链（锋哥本机复现，不需要 aspire）**：`NODE_ENV=production` 撞上 `react-router dev` —— Vite 的 SSR 模块解析拿到 production 的 `react/jsx-dev-runtime`（没有 `jsxDEV`），而 dev 的变换仍生成 `jsxDEV` 调用，于是在渲染调用点同步抛 `TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function`。**A/B 对照（同一环境 `NODE_ENV=production`）**：默认 entry → `/` 得 500 → 6 秒后 TDZ `exit 1` → 端口再没人监听（`HTTP=000`）；锋哥的自定义 entry → **0.16 秒**就打出真错误原文、`/` 500、8 秒后仍能应答、**进程存活**。另已排除 `NODE_OPTIONS="--conditions=browser"`（Vite 下无影响，200 无错）。**但根因仍标「待定位」**：还没确认 DCP 是否真给 `web` 注入了 `NODE_ENV=production`（旺总从 DCP 环境变量白名单日志或 `jsxDEV` 那行签名确认）。**反向判定很关键**：若 aspire 日志里**没有** `[pzd/entry.server]` 那行、web 仍按 6 秒退出，则要么跑的是旧树、要么异常发生在 `try/catch` 之外（模块加载阶段）——这两种指向完全不同的排查方向，别当成同一件事。**AppHost 侧的对应修复是 `37bd872`**：`web.WithEnvironment("NODE_ENV", "development")`——这个资源跑的是 `package.json` 的 `dev` 脚本，语义上本就该是 development；**没被注入时是无害的显式声明，被注入 `production` 时它就是修复**。全仓（`scripts/`、`src/`、`apps/web/*.ts|json`）除这段与其白名单外没有任何地方设置 `NODE_ENV`，两个 shell 里也没有，所以真有注入则来源在 supervisor 那一层。
    - **判别纪律（钉住之后就看不出原值了，别拿新跑当证据）**：`37bd872` 把 web 的 `NODE_ENV` 钉成 `development`，之后任何新跑的诊断行都只会显示 `development`，**因此「DCP 到底有没有注入 `production`」只能在「没有钉住」的树上跑一次才知道**——注意 `37bd872` 之前根本没有锚点（锚点是 `331fac3` 才加的），所以不存在「翻旧日志」这条路，必须做一个只删钉住那一行的对照件（锋哥的做法：基座取 `331fac3`，工作区删 `WithEnvironment("NODE_ENV", …)` 一行，diff 恰好 1 行）。**进程真实值现在有锚点了（`331fac3`）**：`apps/web/app/entry.server.tsx:45` 的 `console.log('[pzd-web] entry loaded（NODE_ENV=…）')` 在模块加载时打印 vite 进程自己的 `NODE_ENV`，这是**进程真实值**，不受快照语义影响；`:80` 再打一次「首次文档渲染：即将调用 renderToPipeableStream」。**三分法只对本机 `pnpm dev` 成立，别搬到 aspire 上**：aspire 下 web 的 stdout 被 DCP 接管，`console.log` 类锚点（含这两行、含张一一那个 `[pzd-web] NODE_ENV`）**一条都进不了 AppHost 日志**（旺总实测；他最后是靠 `writeFileSync` 写文件才拿到 vite 进程真实环境的，张一一在 `cd17520` 里加的 `artifacts/pzd-web-env.log` 就是这个路子）。所以：**aspire 下看不到 `[pzd-web]`/`[pzd/entry.server]`，只说明这条通道不通，不等于修复没生效、更不等于「失败在 import/模块图阶段」**——把它当模块图失败的证据就会判反。要看 aspire 下的真实值，只能读文件探针；`console.log` 的三分行只在**本机 dev** 上算数：两行都没有 → 失败在 import/模块图（加载器、导出条件、外部化），只有第一行 → 抛点在 `handleRequest` 内、调用之前，两行都有 → 调用本身同步抛出（`jsxDEV is not a function` 属这类）。
    - **A5 这次变绿，同时变了三件事，不能归因给 `NODE_ENV`**：① 自定义 entry（`bf08bd4`/`4485ca0` 让真错误可见、失败路径清定时器）；② `37bd872` 把 `NODE_ENV` 钉成 `development`；③ 那一轮跑在**全新的干净副本**里（主仓的历史 ACL 问题不在其中）。所以「A5 跑了 `EXIT=0`」**不能**写成「根因是 DCP 注入了 `NODE_ENV=production`」。定因果只有一条路：**同一个干净副本、只去掉钉住那一行**跑一次，看 `[pzd-web] entry loaded（NODE_ENV=…）`——打出 `production` → 根因确认；打出 `<未设置>`/`development` 且不再崩 → 是环境/ACL 那条修好的，钉住只是防御性声明。**在此之前根因一律记「待定位」**。
    - **`development` 在钉住之后是恒真的（零信息量）**：`5682cd0` 已在 `37bd872` 之后，被注入 `production` 会被 `WithEnvironment("NODE_ENV","development")` 覆盖 → 打 `development`；没被注入时 Vite 自己 `process.env.NODE_ENV ??= "development"` → 也打 `development`。两种假设**同一个输出**，所以别写「已排除 `NODE_ENV` 注入」——要排除必须在**没有钉住的树**上跑。
    - **判断 `NODE_ENV` 只看 `pnpm run dev` 那次的 DCP 资源日志，别看 typecheck/build**：`react-router typegen` 也会加载 `vite.config.ts`，在 typecheck/build 上下文里 `NODE_ENV=production` 是**正常现象**（张一一实测：`pnpm run dev` 打 `development`，`typecheck` 打 `production` 两次）。拿 typecheck 的输出当「被注入」的证据正好读反。**另一条同理成立**：拿 env 判「有没有传进进程」，必须先看到 `state=Running`（`(unknown)`/`Starting`/`Waiting` 全是 0 项，那是时序不是缺值）。
    - **三处方法论边界（写下来防止后来人把结论读高）**：① **资源快照 ≠ 进程真实环境，而且要看状态时序**——`ResourceNotifications` 里 `snapshot.EnvironmentVariables` 是**资源声明**，supervisor 运行时塞进去的变量未必出现；更关键的是实测分布（`5682cd0` 那份 aspire 日志，44 行）：`环境变量共 0 项` 出现 **5 次**、全在 `(unknown)`/`Starting`/`Waiting` 阶段，`103 项` 只出现 **1 次**、就在 `state=Running` 那行之后。**所以「还没进过 `Running` 时的 0 项」是时序没到，不是没传**——拿 0 项判「环境变量没进进程」会判反。这也解释了修 A5 之前为什么这份诊断什么都看不到：旧形态是 `Running` 后紧接着 `Finished exitCode=1`，快照赶不上。② **日志编码别写死「GBK」**（我上一版写错了，已核）：`artifacts/verify-apphost.log`、`artifacts/verify-api.log` 实测都是 **UTF-8**，`grep -a` 能命中 `[pzd-diag]`（4 条）；先前「grep 不到」的假阴性**不是编码造成**，别据此把读法写成 GBK。若用 PowerShell `>` 重定向新生成的日志，那可能是 UTF-16LE，按需加 `-Encoding`；**结论：先 `file -bi` 看编码，再决定读法，不要凭印象**。③ **`AuxiliaryBackchannelService` 的 `SetSecurityInfo` / `UnauthorizedAccessException` 是「非致命」**——它报完之后 DCP 照样 `Starting DCP with arguments: start-apiserver …`，所以「SACL 被拒 = DCP 起不来」这条因果**不能写死**。
    - **交付形状（`bf08bd4` 自定义 entry + `4485ca0` 失败路径清定时器 + `331fac3` 锚点；行号按 `331fac3` 核过，别按旧副本读）**：`:73` `let timeoutId … | undefined`、`:74` `let abort: (() => void) | undefined`（先占位）、`:83` `try { renderToPipeableStream(...) }`、`:126` `catch` 里**立即**打印 `[pzd/entry.server] … 同步抛出` 并 `reject`（`:129` 兜底 `clearTimeout`）、`:140` `abort = stream.abort`、**`:144` 定时器挪到渲染成功之后才挂**、`:91`（成功路径 `final`）与 `:111`（`onShellError`）各一处 `clearTimeout`；到点若外壳仍未就绪就**显式 `reject`**，把「悄悄 abort 后请求挂死」变成可观测失败。`ssr: false` 时 `:70-71` 走 `onAllReady`，与 `react-router.config.ts` 的配置一致。**读行号前先看是哪个文件**：工作区曾出现「默认 entry 的逐行副本」（`abort` 在 `:42` 引用、`:50` 才声明），那是 A/B 实验件、**不是交付物**，别拿它的行号当当前实现。
    - **一条已知 low，结论是「不必改」**：`:144` 的定时器只在 `final()` 里清。外壳已发出、之后流**错误终止**时 `PassThrough.final` 不触发，定时器会活到 6.2 秒点，但那时 `abort?.()` 是 no-op、`shellRendered` 已为 true 所以不 `reject`——良性。**别顺手在 `onError` 里加 `clearTimeout`**：流式输出中途报错不代表流结束，清了就丢掉「卡住也不放」的保护。
    - **修法与禁区**：修复点是 `apps/web`，两件事必须同时成立（见下面的 ②③ 反作用力）。**不许靠删 `setTimeout` 来「修」**——那只是拿掉掩盖层，SSR 那次同步抛错照样存在，而且超时保护一起丢；也**别把 `ssr: false` 当成没这条路径**：项目就是 SPA 模式（`react-router.config.ts` 里 `ssr: false`），dev server 生成文档时照样走这份 `entry.server`（也因此这份 entry 决定 index.html 何时可写）。**版本没动**：`apps/web/package.json` 只加了 `@types/node`（entry 要 `node:stream` 类型），`react-router`/`@react-router/*` 仍是 `^7.8.0`、锁文件仍解析到 7.18.4；要动版本是另一件事。
    - **可见性已补 `6b67c2d`**：AppHost 订阅资源状态流（`ResourceNotifications`）打印 `web` 的 state 与 `exitCode`，所以这类「资源崩了但日志里什么都没有」的情况以后自己会现形；DCP 日志通道被 ACL 挡（`Failed to enable orchestration logging`）这个问题仍在，但不再是唯一通道。另 `90ee1b2`：订阅中断不再静默吞异常、建立订阅时打一行锚点——否则订阅自己失败，又会退化成「日志里什么都没有」。
    - **对票 03 的结论不变**：aspire 下前端腿**不能标通过**——不是断定功能坏，而是这条路径现在既不稳也看不见。**A11 的 aspire 实跑排在 A5 修复之后**，别把两件事并行写成「都差不多了」。
    - **超时用例的验收口径（老何，2026-10-27 定）**：本轮 dev 超时用例按**「15 秒内结束并返回失败」**验收——**6 秒是单次 entry 的保护窗口，不是整条请求的 SLA**，此前也从没规定 HTTP 总耗时要 ≤6 秒；12.16 秒是实测值，**不直接当硬上限**（15 秒是给「双调用是否稳定发生」留的调度余量）。该口径**不外推到生产或 aspire**，也不把「双调用固定发生」当长期保证。**资源释放**按已提供的证据记「请求结束、后续请求正常、无未处理异常」，**不写成句柄级验证完成**（`process._getActiveHandles` 未测）。
    - **验收四条（老何口径；李总落成可执行判据，见 [test-report-03.md](test-report-03.md) §19）**：① **真错误原文可见且脱敏**（不打印整包 env，只按白名单打关键变量）；② **失败路径不遗留定时器**（出错后进程不许 6 秒后再崩一次）；③ **6 秒 SSR 超时保护仍在**（卡死的渲染必须在限定时间内被 abort，不许改成「永不 abort」）；④ **脱敏的机械判据**：日志里不得出现 `Password=`/`Pwd=`/`sk-`/`Bearer ` 等模式，且用 `local.env` 里 `PZD_MYSQL_PASSWORD` 的值 `grep -F -c` 必须为 0（**只输出计数或通过/失败，不输出匹配行，也不把真实密码展开进命令行参数**）。
    - **②与③是一对反作用力，单独满足哪条都会把缺陷换个位置**：只满足 ②（失败索性不再 abort）→ 进程不崩了，但卡死的 SSR 流一直挂到进程被杀，超时保护丢了；只满足 ③（定时器照挂）→ 就是现在的 TDZ。**同时满足的写法**：定时器挪到 `renderToPipeableStream` **之后**挂、`abort` 声明成 `| undefined` 调用前判空（同步抛出时定时器压根没挂上 → ②；成功路径定时器照旧 → ③）；**并且 `onShellError` 里也要 `clearTimeout`**——但**分级是 low（资源卫生），不是“不补就崩”**：李总 §20.4 实测过不补的后果是良性的（进到 `onShellError` 时 `abort` 已初始化，6 秒后 `abort?.()` 对已失败的流是静默 no-op；连续观察 14 秒进程存活、日志零新增、服务可用），补它只是让「失败路径不留定时器」无条件成立。交接里别写成 high。**判红形态**：③ 的红是「卡死渲染既不响应也不退出」，② 的红是「错误日志已经有了、进程仍在 ~6 秒后 exit 1」。
    - **两条边界（老何，防过度解读）**：① **显式 HTTP 500 ＋ 脱敏错误日志就是正确的失败行为**——禁止的是伪装成成功或无诊断吞错，**不要求用进程崩溃表示请求失败**（所以 `try/catch` 抓到真错后**不许返回正常响应**，那会把看得见的崩溃变成静默的 500）；② **超时要验「请求在限定时间内结束、资源释放」，不能拿进程退出当通过**——这条正好和当前 TDZ 那个 6 秒退出形态对上，修完别把「进程没崩」当成验收信号。**根因仍标待定位**：现有证据只到机制与时间常数（6000ms = `streamTimeout + 1000`），李总的反向控制已排除「普通渲染错误能炸出 TDZ」，所以根因只能落在调用点本身或它之前（模块入口解析、版本/导出条件、`renderToPipeableStream` 缺失等），**环境变量是候选之一、不是答案**。
    - **老登的代码审计：修复件通过（2026-10-27，`331fac3` 逐行核对，不采信自述）**。核对项：`onShellError` 补 `clearTimeout`（`:111-112`，`4485ca0` 起）、`pzd-probe` 计数 **0**、定时器在 `abort = stream.abort`（`:140`）之后才挂（`:144`）、两个 `| undefined` 提前声明（`:73-74`）、`ssr: false` 与 `:70-71` 的 `onAllReady` 注释一致。**但代码正确 ≠ 拿到证据**：aspire 实跑里「两腿都 `X-Pzd-Run-Id` 与本次运行一致」这条正向判据**还没出现**，在那之前**票 11 保持门禁、A11 未放行**。这两件事不许写混。
    - **超时保护实跑的证据口径（别读高）**：现有实跑是**缩短超时值**的变异——`streamTimeout` 临时改 200ms、`readyOption` 回调直接 `return`，结果 `GET /` → 500、耗时 2.58s（限制 1.2s + 开销）、日志 `SSR 流在 1200ms 内未就绪，已中止渲染（流超时保护）`、8 秒后再请求仍 500 且进程存活、`ReferenceError` 命中 0。它证明的是「到点会显式中止，而不是挂死」，**不能单独写成「已按真实 6.2 秒验证超时保护」**。**真实值那版随后已补（锋哥，独立 worktree `E:/pzd-tmo`，跑完即删）**：真实 `streamTimeout=5000` 下一次卡死请求返回 500 的实测总耗时是 **12.16 秒**，不是 6.2 秒——原因是**本次观测到失败路径会两次调用 `handleRequest`**（`15:53:24.265 #1` → 6.02s 后 `SSR 流在 6000ms 内未就绪` reject → `15:53:30.283 #2` 紧接进来再 6.02s）；正常路径对照**只调用 1 次**（200 / 0.155s）。**这段只能按「观测」记，不能写成规律**：① 只覆盖「流永久 suspend 到超时」这一种失败形态，同步 `catch`（`:126`）与 `onShellError`（`:111`）会不会也两次调用**没测**；② 两次调用的**发起方未定**（要定因得比两次的 `request.url`/`method`/重试相关头是否逐字节相同，这条没做）。所以**不能写「dev SPA 模式下超时固定 12.2 秒」**，也别让后人拿 12.2 当常数去调超时值；这是本机 `pnpm dev` 证据，**不外推到 aspire 或生产**。另：变异日志里 `There was still abortable task at the root when we closed. This is a bug in React.` 是这次变异构造（永久 suspend 后再 abort）的副作用，**不是 entry 的行为，不计缺陷**。
  - **端口 LISTENING ≠ HTTP 200**：前端起来后有约 **6 秒 `http=500`** 窗口（17:55:55、17:55:58 采样），之后才 200。`Wait-ForUrl` 只认 200，判定不受影响，但「端口监听」与「就绪判定」**不能混在同一个循环里**，否则一个会掩盖另一个；票 11 的就绪条件仍是「200 且 body 含 `lang="zh-CN"`」。
  - **清理清单（复测完自己收拾，别留给下一个人）**：① 强制中断会留孤儿 vite 占 5199（链根已死才可清）；② 实验用的假 SPA 服务器（`node -e` 返回 `lang="zh-CN"` 的单行 HTTP 服务）用完立刻撤，否则任何人跑前端断言都假绿；③ 删过 worktree 后检查是否留下 `apps`/`packages`/`node_modules` 空壳目录（`E:\pzd-a11` 就留过三个）；④ 临时变异副本（如 `scripts/.race2.ps1`）不要留在仓库里，更别误提交；⑤ aspire 跑到一半被中断时可能留下 `Pzd.AppHost.exe`（DCP 已死、宿主没退，5080/5199 都不监听），下一轮开始前先收掉自己起的那个——别杀别人正在用的。
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
