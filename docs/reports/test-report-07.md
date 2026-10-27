# 测试报告：票 07（UoW + Outbox + 领域事件）

> 测试：李总｜日期：2026-10-27｜票面：[`docs/specs/tickets.md`](../specs/tickets.md) `# 07:` 起
> 被测提交：`d9919ab`（`feat(persistence): 票 07 UoW + Outbox + 领域事件（152 条全绿）`，14 文件 / +1368）
> 结论：**票面 7 条验收全部通过**（顺序、失败回滚、事件清除、TenantId+Set、F-05-8 运行时路径、幂等、重试与优雅停止）；另有 **2 条 medium 覆盖/守护缺口**（F-05-8 代码检查可绕过、`DomainEventDispatcher` 零覆盖）与 3 条 low 观察项，不阻塞功能验收，但建议本轮顺手补

## 1. 基线（`git worktree` 干净副本 @ `d9919ab`）

- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 逐工程 `dotnet run`：35（SharedKernel）+ 71（Infrastructure）+ 26（Api）+ 19（Architecture）+ 1（Integration）= **152 条全绿**，Failed 0，与旺总自述一致。
- 提交范围核对：`git show --stat d9919ab` 只含票 07 的 14 个文件（`Persistence/` 7 个、`Program.cs`、`IUnitOfWork`/`IDomainEventDispatcher`、`ArchRules` + `TenantConnectionRuleTests`、两个测试文件），**没有混入票 06 返工的文件**；`src/`、`tests/` 干净。

## 2. 票面验收项 → 证据

| # | 验收项 | 用例 / 证据 | 结果 |
|---|---|---|---|
| 1 | 提交顺序：分发 → 写 Outbox → 提交（调用序列断言） | `UnitOfWorkTests.Commit_dispatches_events_then_writes_outbox_then_commits_the_transaction` 断言 `["dispatch:FakeEvent","outbox-add:FakeEvent","commit"]`；无事件时 `["commit"]` | 通过 |
| 2 | 处理器抛异常 → 不提交、Outbox 无残留 | `Handler_exception_prevents_commit_and_leaves_no_outbox_message`（commit 0 / rollback 1 / `Added` 空 / 聚合事件保留） | 通过 |
| 3 | 分发成功后清空聚合事件，不重复分发 | `Events_are_cleared_after_commit_and_not_dispatched_again`（第二次提交 `DispatchedTypes` 仍只有 1 条） | 通过 |
| 4 | 消息带 `TenantId`；消费前 `ITenantContext.Set` | `Outbox_message_carries_the_current_tenant_and_the_event_payload`（TenantId=7、EventType、OccurredOn、payload 非空）+ `Dispatcher_sets_the_tenant_context_before_consuming`（消费者读到 3） | 通过 |
| 5 | F-05-8：显式重载（先校验停用/到期）；停用/过期拒绝并记理由；不得裸取 | `Dispatcher_uses_the_explicit_tenant_overload…`（`ExplicitTenantIds=[5]`、`ParameterlessCalls=0`）+ 停用/过期两条（`RejectionReason` 含 `Tenant.Disabled`/`Tenant.Expired`、不再出队）+ `TenantConnectionRuleTests` 3 条 | 运行时路径通过；**代码检查可绕过，见 F-07-1** |
| 6 | 投递幂等（消息 Id + 已处理集合） | `The_same_message_id_is_delivered_only_once`（同一实例入队两次，消费者 1 次） | 通过 |
| 7 | 失败重试 + Warning，不阻塞同批；后台优雅停止 | `Delivery_failure_keeps_the_message_pending_for_retry_and_logs_a_warning`（Attempts=1 → 次轮成功）、`One_failing_message_does_not_block_the_rest_of_the_batch`、`Hosted_service_stops_gracefully_when_cancelled` | 通过 |
| — | 无跨库事务 / 不引入 MySQL 依赖 | 新增文件无 DB 访问；`git show` 未改任何 csproj（`SqlSugarCore` 是票 05 既有依赖），事务工厂是内存空实现 | 通过（代码审查） |

## 3. 变异复测（干净副本内做，逐个还原；每次 `git status` 确认只回滚了变异）

| # | 变异 | 期望 | 实际 |
|---|---|---|---|
| M1 | 在分发前提前 `CommitAsync`（顺序破坏） | 顺序用例红 | **4 failed**（顺序、异常不提交、提交失败回滚、无事件仍提交），exit 1 |
| M2 | 去掉 `aggregate.ClearEvents()` | 重复分发用例红 | `Events_are_cleared…` **1 failed** |
| M3 | 去掉 `IsProcessedAsync` 幂等判断（改为永不命中的条件，避免不可达代码告警） | 幂等用例红 | `The_same_message_id…` **1 failed** |
| M4 | 拒绝分支改为永不命中（`when (false)` 等价写法） | 停用/过期用例红 | 停用 + 过期 **2 failed** |
| M5 | 投递器改回无参 `GetConnectionScopeAsync(cancellationToken)` | 单测红 + 架构红 | Infrastructure **8 failed**；Architecture **2 failed**（`Product_sources…` 与 `The_outbox_dispatcher_itself…`） |
| M6 | 去掉提交失败的补偿删除 | 提交失败回滚用例红 | `Commit_failure_rolls_back_and_removes…` **1 failed** |
| M7 | `DomainEventDispatcher` 把 `MakeGenericType(domainEvent.GetType())` 改成 `typeof(IDomainEvent)` | 应有 dispatcher 用例红 | **71 条全绿**，exit 0 → 见 F-07-2 |

M1–M6 与旺总自述的变异结论一致（M1 我多红 1 条：无事件用例也会因双提交失败，不影响结论）；M7 是本次新发现。

## 4. 缺陷

### F-07-1 · medium · F-05-8 代码检查按「token 变量名白名单」匹配，改名即可绕过

**期望**：后台路径出现无参 `GetConnectionScopeAsync(...)` 裸取时，架构测试必须报红（票面验收第 5 条的「代码检查」）。

**实际**：`ArchRules.ParameterlessTenantConnectionPattern` 只匹配 `()` 或参数名恰好是 `CancellationToken|cancellationToken|ct|token|stoppingToken|default` 的调用（[`ArchRules.cs:189-191`](E:/pzd/tests/Pzd.ArchitectureTests/ArchRules.cs)）。把 token 赋给别的名字，规则就看不见：

```csharp
var ct2 = cancellationToken;
await db.GetConnectionScopeAsync(ct2);   // 裸取连接，绕过停用/到期校验
```

**复现**（已实测）：在 `src/Pzd.Infrastructure/Persistence/` 放一个可编译的探针类（含上面两行），跑

```powershell
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
```

→ `Total: 19, Failed: 0`，**规则未报出**。用纯正则复核：`.GetConnectionScopeAsync(t|ct2|myToken|cancellationToken2)` 全部漏报，`()`、`(ct)`、`(CancellationToken.None)`、`(default)`、`(stoppingToken)` 命中。

**影响**：F-05-8 是「后台禁止裸取连接」的硬约束，当前守护只防住了标准命名的写法；换个变量名即可把停用/过期租户的连接绕回来，与票 05 的 F-05-2（扫描目录硬编码）同一类问题——规则被构造得只能抓住自己写过的形式。

**建议**（按成本从低到高）：
1. 把无参重载改名（如 `GetCurrentConnectionScopeAsync()`），规则改为「`src/Pzd.*` 里出现 `.GetConnectionScopeAsync(` 即违规」——名字本身就表达「后台不可用」，代码审查也一眼能看出；
2. 或反转匹配：只允许第一实参是 `xxx.TenantId`/`tenantId` 形式的调用，其余一律报告；
3. 长期用 Roslyn 分析器做语义检查。
4. 无论选哪种，反向用例里补一条「改名 token」的输入（`"await db.GetConnectionScopeAsync(t)"` 应命中），把这次的绕过钉死。

### F-07-2 · medium · `DomainEventDispatcher` 零测试覆盖，反射写错不会被任何用例发现

**期望**：真实分发器（按事件运行时类型解析 `IDomainEventHandler<T>`、多处理器按 DI 顺序、无处理器算成功）至少有一条用例。

**实际**：`UnitOfWorkTests` 全部用假的 `IDomainEventDispatcher`（`RecordingDomainEventDispatcher`/`ThrowingDomainEventDispatcher`），生产实现 `DomainEventDispatcher` 没有任何测试。把 `typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType())` 改成 `MakeGenericType(typeof(IDomainEvent))`（M7）后 **152 条仍全绿**。

**影响**：反射是易错点（泛型实参、`GetMethod`、DI 顺序、处理器异常传播）；当前写错只会在步骤 3 真接处理器时才炸，且没有用例能定位。

**建议**：加 3 条 Infrastructure 单测——① 注册 `IDomainEventHandler<FakeEvent>`，断言 `HandleAsync` 收到同一事件实例；② 注册两个处理器，断言调用顺序与 DI 注册顺序一致；③ 不注册处理器，`DispatchAsync` 正常完成。用例直接 new `DomainEventDispatcher(provider)`，不需要宿主。

## 5. 观察项（low，不阻塞）

- `InMemoryOutboxStore.RecordFailureAsync` 接收 `reason` 但没存（只累加 `Attempts`），接口注释写「记录原因」。原因只在 `OutboxDispatcher` 的 Warning 日志里；步骤 3 落库时补列即可。
- 无处理器时 `OutboxDispatcher` 仍会 `MarkProcessedAsync`（消息被静默消费、删除）。建议「零处理器」时记 Warning 或按失败处理，避免漏注册处理器导致事件静默丢失。
- 被拒绝的消息与 `_processed` 集合都永久留在内存（`_messages` 只增不减的部分、`_processed` 无清理）。纯内存实现可接受，步骤 3 落库时由表结构与清理任务解决。
- `UnitOfWork` 的补偿删除若 `RemoveAsync` 抛异常，`RollbackAsync` 不会执行（catch 里先删后回滚）。内存实现风险低，换真实事务时建议用 `try/finally` 或先回滚。
- `DomainEventDispatcher` 用 `MethodInfo.Invoke`：处理器**同步**抛异常会包成 `TargetInvocationException`（异步 `Task` 失败则原样抛出）。若要统一异常语义，可捕获并解包。

## 6. 结论

- 功能面：7 条验收按 `d9919ab` 独立复核全部通过，M1–M6 变异全部被抓，旺总自述的验证可复现；票 06 返工文件未混入本提交。
- F-07-1、F-07-2 建议本轮补（都是小改动 + 几条用例）：前者把守护从「认名字」改成「认 API 形状」，后者给真实反射分发器补 3 条单测。补完后我按新 hash 冒烟；不阻塞票序。
- 若审计按现状收口，建议把这两条作为 low/medium 记进「步骤 3 必须处理」清单，尤其是 F-07-1——它直接关联 F-05-8 的租户状态校验约束。

## 7. 返工冒烟：`40f016a`（F-07-1/F-07-2/F-07-3）

> `git worktree` 干净副本 @ `40f016a`。

- 基线：build 0 警告 0 错误；35 + 75 + 30 + 25 + 1 = **166 条全绿**（Infrastructure 71 → 75，新增 4 条 dispatcher 用例；Architecture 19 → 25，新增 6 条路径 Theory）。
- **F-07-1（改名 token 绕过）修复有效**：
  - 类型系统层：无参重载已改名 `GetCurrentConnectionScopeAsync`，`GetConnectionScopeAsync` 只剩 `long tenantId` 重载。探针在 `Persistence/` 里写 `db.GetConnectionScopeAsync(cancellationToken)` → **编译失败 CS1503**（`CancellationToken` 不能转 `long`），裸取在编译期就被堵住。
  - 规则层：改为「后台路径（`Persistence/**`、`*HostedService.cs`、`*Dispatcher.cs`）不得出现 `.GetCurrentConnectionScopeAsync(`」；反向用例含改名 token 样本（`var ct2 = cancellationToken;`)`，只报两个后台文件、放行 `Tenancy/` 声明与 `Api/Endpoints` 调用；`IsBackgroundPath` 6 条 Theory。
  - 变异：`OutboxDispatcher` 改调 `GetCurrentConnectionScopeAsync(cancellationToken)` → Architecture **2 failed**（产品扫描 + dispatcher 自身断言）、Infrastructure **8 failed**，exit 1。
- **F-07-2（dispatcher 零覆盖）修复有效**：新增 4 条用例（同一事件实例按运行时类型解析、两个处理器按 DI 注册顺序、无处理器成功、一批两种事件各走各的处理器）；变异 `MakeGenericType(GetType())` → `typeof(IDomainEvent)` → **3 failed**。
- **F-07-3（补偿顺序）修复有效**：改为先 `RollbackAsync` 再补偿删除，`UnitOfWorkTests` 断言调用序列 `dispatch → outbox-add → commit → rollback → outbox-remove`；换回先删后回滚 → **1 failed**。
- **结论：F-07-1/F-07-2/F-07-3 全部修复，票 07 功能面与守护均可收口。**

### low（新观察，建议收紧规则，不阻塞）

1. **规则按字符串精确匹配，方法名与括号间加空格即绕过**：`Content.Contains(".GetCurrentConnectionScopeAsync(")`；探针在 `Persistence/` 写 `await db.GetCurrentConnectionScopeAsync (cancellationToken);`（名字与括号间一个空格）→ Architecture **25 条全绿**。建议改为正则 `\.GetCurrentConnectionScopeAsync\s*\(`，或反向只允许 `Pzd.Api` 出现该 API。
2. **后台路径靠命名约定**：`IsBackgroundPath` 只认 `Persistence/` 目录与 `*HostedService.cs` / `*Dispatcher.cs` 文件名；探针把同样的调用放进 `src/Pzd.Infrastructure/Jobs/ProbeWorker.cs` → Architecture **25 条全绿**。当前仓库的后台代码都在约定路径内，风险低；建议后续把「Infrastructure 里除 `Tenancy/` 实现外不得调用请求路径 API」作为更强的边界。

## 8. 收尾冒烟：`001a731`（07 低优先级三条）

> 干净副本 @ `001a731`。

- 基线：build 0 警告 0 错误；35 + 79 + 33 + 25 + 1 = **173 条全绿**（Infrastructure 75 → 79，新增 4 条 low 守护）。
- 修复项与守护（每条变异各 1 红，均已还原）：
  | low 项 | 变异 | 结果 |
  |---|---|---|
  | 零消费者静默消费（`OutboxDispatcher` 告警） | 告警条件改为永不命中 | `A_dispatcher_without_handlers_logs_a_warning` 1 红 |
  | 零处理器静默消费（`DomainEventDispatcher` 告警） | 告警条件反转 | `An_event_without_handlers_logs_a_warning` 1 红 |
  | 失败原因未落存储（`OutboxMessage.LastError`） | 不写 `LastError` | `Delivery_failure_keeps_the_message_pending…` 1 红 |
  | 无最大重试会饿死后续消息（`MaxAttempts = 5`） | 判定改为 `> MaxAttempts` | `A_poison_message_is_rejected_after_the_maximum_attempts` 1 红 |
  | 反射把同步异常包成 `TargetInvocationException` | 保留包装异常、不解包 | `A_synchronously_throwing_handler_surfaces_the_original_exception` 1 红 |
- **F-07-4 尚未进入本提交**：规则仍是 `Content.Contains(".GetCurrentConnectionScopeAsync(")`，后台路径仍是 `Persistence/**` + `*HostedService.cs`/`*Dispatcher.cs`，未并入 `Jobs/`。探针（`001a731` 上实测）：
  - 方法组引用 `Func<CancellationToken, Task<ISqlSugarClient>> f = db.GetCurrentConnectionScopeAsync;`（Persistence 目录）→ Architecture **25 绿**，漏报；
  - `src/Pzd.Infrastructure/Jobs/ProbeWorker.cs` 里 `GetCurrentConnectionScopeAsync (cancellationToken)`（Jobs 路径 + 空格）→ Architecture **25 绿**，漏报。
- **`0da4a18` 补了第二层但仍未覆盖 F-07-4**：新增「任何源码出现零实参 `.GetConnectionScopeAsync()`（含空白）」的正则判定，方向正确（将来重新加回无参重载也能报）；但实测仍漏两类：
  - 方法组引用（`db.GetCurrentConnectionScopeAsync` 不带括号，Persistence 目录）→ Architecture **25 绿**；
  - `Jobs/` 路径的后台文件调用请求路径 API → Architecture **25 绿**。
  - 说明：真实代码已无零参重载，零实参探针无法编译，第二层只能靠合成源码反向用例验证（用例已覆盖）。
- **结论：低优先级三条与告警修复全部通过；票 07 待 F-07-4（标识符级禁用 + Jobs 路径）落地后的 hash 再终审。**

## 9. 终审冒烟：`7f699c5`（F-07-4 规则收紧 + F-07-5 重试配置化）

> 干净副本 `git worktree` @ `7f699c5`（HEAD）；`F-07-4` 在 `0c29633`、`F-07-5` 在 `7f699c5`。
> 工作区 `E:\pzd` 同一 hash 复跑：`src`/`tests` 干净，`git grep "false &&" -- '*.cs'` 无命中（HEAD 与工作区都无）。

### 9.1 基线

- `dotnet build Pzd.slnx -warnaserror --no-incremental` → **0 警告 0 错误**。
- 五工程全量：35 + 30 + 86 + 33 + 1 = **185 条全绿**（Infrastructure 79 → 86，Architecture 25 → 30）。

### 9.2 F-07-4：三条（+1）真实源码探针，各恰好 1 条违规

探针写在 `src/Pzd.Infrastructure/` 的真实文件里（不是合成源码用例），跑完即删；每次失败信息只含探针文件本身。

| 形状 | 探针 | 结果 |
|---|---|---|
| 标识符与 `(` 之间加空格 | `Persistence/ProbeSpacing.cs`：`provider.GetCurrentConnectionScopeAsync (ct)` | 1 条违规：`src\Pzd.Infrastructure\Persistence\ProbeSpacing.cs` |
| 标识符与 `(` 之间换行 | `Persistence/ProbeNewline.cs`：`…GetCurrentConnectionScopeAsync` 换行后 `(ct)` | 1 条违规：`…\Persistence\ProbeNewline.cs` |
| 方法组引用（无括号、无点号） | `Persistence/ProbeMethodGroup.cs`：`Func<CancellationToken, Task<ISqlSugarClient>> = provider.GetCurrentConnectionScopeAsync;` | 1 条违规：`…\Persistence\ProbeMethodGroup.cs` |
| `Jobs/` 路径 | `Jobs/ProbeCleanupJob.cs`：正常写法调用请求路径 API | 1 条违规：`…\Jobs\ProbeCleanupJob.cs` |

- 四个探针逐条删除后同一工程 **30 条全绿**，`git status` 无残留。
- 结论：规则已是**标识符级**（不看括号、容忍空白、方法组算违规），路径清单含 `Persistence/**`、`Jobs/**`、`*Job.cs`、`*HostedService.cs`、`*Dispatcher.cs`；对应 §7 low 第 1/2 条（`40f016a` 时代的漏报）**已闭环**。
- 口径仍照 foundation §9：这是字符串级规则、**不是沙箱**，报告不使用「不可能绕过」；唯一强保证是类型系统层（`GetConnectionScopeAsync(ct)` 编译不过）。

### 9.3 F-07-5：上限可配置 + 终态写 `LastError` + 不饿死同批

先读实现确认与票面同义：判断依据 `message.Attempts + 1 >= _options.MaxAttempts`；达上限 `LogError` + `RejectAsync(id, "RetryLimitExceeded: …", exception.Message, ct)`，**消息保留**；`catch` 内 `continue` → 同批后续消息继续。默认值只在 `OutboxOptions.DefaultMaxAttempts = 5`，`OutboxDispatcher` 无 `MaxAttempts` 常量字段。

变异（干净副本，逐个还原，每次跑 Infrastructure 全量）：

| 变异 | 期望 | 实测 |
|---|---|---|
| 判定硬编码回 `>= 5`（忽略配置） | 配置相关用例红 | **2 failed**：`A_poison_message_is_rejected_after_the_configured_maximum_attempts`、`A_rejected_message_does_not_starve_the_healthy_message_behind_it` |
| 终态拒绝改传 `string.Empty`（不写 `LastError`） | 断言 `LastError` 的用例红 | **2 failed**：同上两条 |
| 给 `OutboxDispatcher` 加回 `public const int MaxAttempts = 5` | 反射守卫红 | **1 failed**：`The_dispatcher_exposes_no_hardcoded_attempt_limit` |
| 还原全部变异 | 回绿 | 86 条全绿，`git status` 干净 |

端到端配置冒烟（真实启动 `Pzd.Api`）：

- `Outbox__MaxAttempts=0` → 启动即失败：`Unhandled exception. System.InvalidOperationException: 配置 Outbox:MaxAttempts 必须是大于 0 的整数，实际为「0」。`（不静默退回默认值）
- `Outbox__MaxAttempts=2` → `Now listening on: http://127.0.0.1:5199` / `Application started.`，配置合法时不被误拦。
- `appsettings.json` 有 `"Outbox": { "MaxAttempts": 5 }`，`Program.cs` 用 `OutboxOptions.Load` 读并注册。

老登要求的两条用例我按 `7f699c5` 定位并核过断言：`OutboxTests.cs:186`（注入 `maxAttempts: 2`、断言 `Calls == 2`、`RejectionReason` 含 `RetryLimitExceeded`、`LastError` 含 `poison message`）、`OutboxTests.cs:220`（上限 1、毒消息终态、`processed == 1`、`HandledEventTypes == ["Healthy"]`），与实现行为一致，不是只测了个常量。

### 9.4 明确遗留（不得写成已解决）

- **多处理器部分失败重放**：未实现，需按处理器维度记进度，与步骤 3 的 Outbox 落库同批。
- **「拒绝」≠ 死信队列**：本轮只有终态标记（`RejectionReason` + `LastError`，消息保留）；死信/失败表 + 人工重放属步骤 3，`MaxAttempts` 与拒绝原因字段届时对齐。

### 9.5 结论

- F-07-4、F-07-5 测试侧**通过**：规则四条形状各恰好 1 条违规、上限只从配置读、终态同时写 `LastError`、毒消息不饿死同批，三个变异全部抓到。
- 票 07 建议按 `7f699c5` 终审；测试侧无新增缺陷，遗留两项已按上节标注。
