# 审计报告：票 07（UoW + Outbox + 领域事件）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec）+ 变异复核，合并李总 [`test-report-07.md`](test-report-07.md)
> 被测：`d9919ab`（`feat(persistence): 票 07 UoW + Outbox + 领域事件`）；审计在独立 worktree 的干净副本上执行，07 代码在后续提交中未再变动
> 依据：[`architecture.md`](../architecture.md) §6.3、[`foundation.md`](../specs/foundation.md) §4.5、[`tickets.md`](../specs/tickets.md) 票 07（`d9919ab` 回填版）
> **结论（首轮 `d9919ab`）：需修改。** 主路径（提交顺序、失败补偿、事件清除、`TenantId` + 消费前 `Set`、停用/过期拒绝、消息级幂等、失败重试、优雅停止）经我实跑与读码成立；但 **F-07-1**（F-05-8 代码检查可被改名 token 绕过）与 **F-07-2**（`DomainEventDispatcher` 零覆盖）必修，另 4 条 low。
>
> **终审：通过（`7f699c5`，185 条）。** F-07-1/2/3 在 `40f016a`、F-07-4 在 `0c29633`、F-07-5 在 `7f699c5`，我逐条实跑 + 独立变异复核：基线 `33 + 30 + 86 + 1 + 35 = 185` 全绿、build 0 警告 0 错误；F-07-4 四条变异全红、F-07-5 四条变异全红。剩 1 条明确遗留（多处理器部分重放 → 步骤 3）+ 2 条 low 观察项，均不阻塞。详见 §6。

---

## 1. 基线（我实跑，独立 worktree）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 35+71+26+19+1 = **152 通过，0 失败**，exit 0 |
| 提交范围 | `git show --stat d9919ab` | 14 文件（实现 + 测试 + ArchRules），只含 `src/` + `tests/` |
| 工作树 | `git status --porcelain` | 干净（审计时点） |

## 2. 票面验收逐条核对

| # | 验收项 | 核对结果 |
|---|---|---|
| 1 | 提交顺序：领域事件分发 → 写 Outbox → 事务提交 | **通过**：`Commit_dispatches_events_then_writes_outbox_then_commits_the_transaction` 断言 `["dispatch:FakeEvent", "outbox-add:FakeEvent", "commit"]` |
| 2 | 处理器抛异常 → UoW 不提交、Outbox 无残留 | **通过**：`Handler_exception_prevents_commit_and_leaves_no_outbox_message`（`CommitCalls=0`、`RollbackCalls=1`、`Added` 空、聚合事件保留） |
| 3 | 事件分发后清除、不重复分发 | **通过**：`Events_are_cleared_after_commit_and_not_dispatched_again` |
| 4 | Outbox 带 `TenantId`；消费前 `ITenantContext.Set` | **通过**：消息 `TenantId=7` 用例 + 消费者读到 `SeenTenantId=3` |
| 5 | F-05-8：显式重载 + 停用/过期拒绝 + 禁止无参裸取（代码检查 + 用例守护） | **部分通过**：运行期行为有守护（`ExplicitTenantIds=[5]`、`ParameterlessCalls=0`、Disabled/Expired 拒绝并记原因）；**代码检查可绕过**（F-07-1），"用例守护"不成立 |
| 6 | 投递幂等：同一消息重复投递无重复副作用 | **通过**：`The_same_message_id_is_delivered_only_once`（同 Id 入队两次只消费一次）；多处理器部分失败的语义见 F-07-6 |
| 7 | 失败重试并记 Warning/Error，不阻塞主流程；优雅停止 | **通过**：`Delivery_failure_keeps_the_message_pending_for_retry_and_logs_a_warning`、`One_failing_message_does_not_block_the_rest_of_the_batch`、`Hosted_service_stops_gracefully_when_cancelled` |
| 8 | 无跨库事务；内存实现不引入 MySQL 依赖 | **通过**：全内存 store + 空操作事务，未新增 MySQL 依赖 |

## 3. 缺陷复核（独立 worktree，复现后已删除）

李总的两条 medium 我全部独立复现，另外新增 4 条 low。

### F-07-1 · medium · F-05-8 代码检查按 token 变量名白名单，改个名字就能绕过

- **位置**：[`ArchRules.cs:189-191`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L189-L191)、[`TenantConnectionRuleTests.cs`](../../tests/Pzd.ArchitectureTests/TenantConnectionRuleTests.cs)
- **实测**（worktree `d9919ab`）：在**真实源码**加 `src/Pzd.Infrastructure/Persistence/BypassProbe.cs`：
  ```csharp
  var ct2 = cancellationToken;
  await db.GetConnectionScopeAsync(ct2);
  ```
  重新构建后 `Pzd.ArchitectureTests` **19 条全绿**，违规未被发现。
- **为什么错**：正则只匹配 `GetConnectionScopeAsync(` 后紧跟 `)` 或 `CancellationToken|cancellationToken|ct|token|stoppingToken|default` 这几种写法。F-05-8 是安全约束（无参重载只信 `ITenantContext`，手工 `Set(停用租户)` 也能拿连接），这条规则是唯一自动化守护；`ct2` 这种最普通的重命名就能让调用静默进主干，而反向用例只喂白名单名字，看起来"规则有效"。
- **必须修**：
  1. 首选把无参重载改名（如 `GetCurrentConnectionScopeAsync`），规则只认这个 API 形状——不再依赖实参名字；
  2. 或反转匹配：`GetConnectionScopeAsync(` 的第一个实参不是 `*.TenantId` / `tenantId` / 显式租户变量形态就报违规（注意排除注释与换行）；
  3. 反向用例补一条「改名 token（`ct2`）也必须红」，否则修完规则仍无法证明它守得住。
  4. 同步更新 `ITenantDbProvider` 的 XML 注释：无参重载仅限请求路径，后台路径禁用。

### F-07-2 · medium · `DomainEventDispatcher` 零测试覆盖，泛型解析改坏后 152 条仍全绿

- **位置**：[`DomainEventDispatcher.cs:26-41`](../../src/Pzd.Infrastructure/Persistence/DomainEventDispatcher.cs#L26-L41)；`UnitOfWorkTests` 用的是 `RecordingDomainEventDispatcher` / `ThrowingDomainEventDispatcher` 假实现，测试里没有构造过真实分发器
- **实测**（worktree `d9919ab`）：把 `typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType())` 改成 `typeof(IDomainEvent)` → build 0 警告 0 错误，五个工程 **152 条全绿**（若真调用会抛 `GetMethod` 返回 null 的 `InvalidOperationException`）。
- **为什么错**：反射 + 泛型解析是易错点（事件类型/处理器泛型、零处理器、多处理器顺序、异常包装），又是 UoW 的必需路径；没有任何行为级守护，重构改名/改签名不会被发现。李总独立得到同一结论。
- **必须修**：补 3 条（李总建议）——同一事件实例传给处理器；两个处理器按 DI 注册顺序执行；无处理器视为成功。再补 F-07-5 的同步抛出用例。

### F-07-3 · low · 零处理器时消息被静默标记为已处理

- **位置**：[`OutboxDispatcher.cs:73-78`](../../src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs#L73-L78)；[`Program.cs:33-39`](../../src/Pzd.Api/Program.cs#L33-L39) 本轮未注册任何 `IOutboxMessageHandler`
- **问题**：`_handlers` 为空时，`foreach` 不执行，随后 `MarkProcessedAsync` 把消息移出队列并记为已处理——"成功投递"但没有任何消费者，也没有日志。
- **为什么错**：本轮不注册消费者是已记录偏差，但代码没有把"零消费者"当异常态；步骤 3 若先接 UoW、后接消费者，事件会静默丢失，且因为已标记 processed，重放也找不回来。
- **修法**：`_handlers.Count == 0` 时记 Warning 并跳过 `MarkProcessedAsync`（消息保留待消费者上线），或启动期对"有消息但零消费者"fail fast；补一条用例。

### F-07-4 · low · 失败原因未落存储；永久失败消息会占满批次、饿死后续消息

- **位置**：[`InMemoryOutboxStore.cs:80-96`](../../src/Pzd.Infrastructure/Persistence/InMemoryOutboxStore.cs#L80-L96)（`RecordFailureAsync` 忽略 `reason`）、[`InMemoryOutboxStore.cs:46-59`](../../src/Pzd.Infrastructure/Persistence/InMemoryOutboxStore.cs#L46-L59)（按入队顺序 `Take`）
- **问题**：`RecordFailureAsync` 只 `Attempts++`，`reason` 丢弃；没有最大尝试次数/死信；前 `DefaultBatchSize=50` 条永久失败消息会每轮占满批次，后面的消息永远轮不到。
- **为什么错**：验收要求"失败重试并记 Warning/Error"（日志已有），但存储层没有失败原因字段，步骤 3 落库时无字段可写；"饿死"是多租户下的真实投递正确性问题（某租户的坏消息会阻塞其他租户）。
- **修法**：`OutboxMessage` 加 `LastError`/`LastAttemptAt` 并由 `RecordFailureAsync` 写入；设 `MaxAttempts` 超限转 `Reject`/死信；`DequeueBatchAsync` 本轮跳过已失败消息或用轮转把失败消息移队尾；补一条"超限后不再重试"用例。

### F-07-5 · low · 反射 `Invoke` 把同步抛出的异常包成 `TargetInvocationException`

- **位置**：[`DomainEventDispatcher.cs:37-40`](../../src/Pzd.Infrastructure/Persistence/DomainEventDispatcher.cs#L37-L40)
- **问题**：处理器在返回 `Task` 之前同步抛出（参数校验、直接 `throw new OperationCanceledException()`）时，`MethodInfo.Invoke` 抛 `TargetInvocationException`，原始类型丢失；异步路径（异常在 Task 内）则不包装，两条路径行为不一致。UoW 只关心"失败"所以能回滚，但上层按异常类型的日志/取消判断会误判。
- **修法**：包住 `Invoke`，`catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }`；配合 F-07-2 补一条"同步抛出"用例。

### F-07-6 · low · 多处理器部分失败会重放已成功的处理器（消息级幂等 ≠ 处理器级幂等）

- **位置**：[`OutboxDispatcher.cs:73-78`](../../src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs#L73-L78)
- **问题**：处理器 A 成功、B 抛异常 → 消息保留，下轮 A 再执行一次；当前没有按处理器记录完成度。
- **为什么错**：票面"重复投递不产生重复副作用"只在消息级成立；多处理器场景下部分副作用会重复。
- **修法**：在契约/文档里明确"处理器必须幂等"（推荐），或消息记录已完成处理器集合（与步骤 3 落库一起考虑）；补一条"两处理器、第二个失败"的用例把语义钉住。

## 4. 实现偏差核对（老何 `598fc79` 已回填）

| 偏差 | 审计意见 |
|---|---|
| 事务工厂/消费者抽象内联 | 不影响契约，接受 |
| F-05-8 检查落 `ArchRules` + 合成源码反向用例 | 接受，但规则本身有 F-07-1，需修 |
| 本轮不注册任何消费者 | 接受范围，但代码要按 F-07-3 处理"零消费者" |
| **事务不真正回滚，只用补偿删除** | **本票不能算"失败整体回滚"已验证**。内存实现可以接受，但步骤 3 必须把补偿删除换成真实事务并删掉补偿代码；建议在步骤 3 移交清单里单列一条，别让它悄悄留在生产代码里 |
| 全内存、无 MySQL 依赖 | 符合票面 |

## 5. 结论与返工范围

- **票 07 主路径：通过**（顺序、补偿、清除、TenantId/Set、拒绝、消息级幂等、重试、优雅停止）。
- **关票前必修**：**F-07-1**（F-05-8 规则改认 API 形状 + 改名 token 反例）、**F-07-2**（`DomainEventDispatcher` 3 条行为用例）。
- **建议本轮顺手**：F-07-3（零消费者不静默消费）、F-07-4（失败原因 + 最大尝试次数/防饿死）；F-07-5、F-07-6 可以落步骤 3 但要在票面写明。
- **依赖**：票 07 blocked by 05、06；**06 已终审通过（`abde749`，156 条）**，07 的关票不再被 06 阻塞。
- **口径**：事务真回滚属步骤 3，已在偏差里记录；本票不因此判失败，但报告与交接不得写成"事务回滚已验证"。

修完 F-07-1、F-07-2（及顺手项）后给我 hash，我按 hash 复核并更新本报告的终审结论。

---

## 6. 返工终审（`40f016a` + `837117f`）

> 复跑：2026-10-27｜审计对象：`40f016a`（票 07 返工）+ `837117f`（票 06 残余 low，顺手带）；在独立 worktree `837117f` 的干净副本上执行，`git status` 仅剩我自己的脚本，审计后删除。
> **结论（本轮返工时点）：需修改。** 三条原必修（F-07-1/2/3）全部闭环且我独立变异复现；但按老何 `b9795e9` 的规则边界，三处规则漏检（F-07-7/8/9 → 票面 F-07-4）属**必修**（真实可写、无需伪造 `ITenantContext`），当时不能关票。**后续已在 `0c29633`（F-07-4）、`7f699c5`（F-07-5）闭合，终审结论见文首「终审：通过」。**

### 6.1 基线（我实跑）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 35 + 75 + 33 + 25 + 1 = **169 通过，0 失败** |
| 提交范围 | `git show --stat 40f016a` / `837117f` | 9 文件（实现 + 测试 + 规则）、2 文件（`ProblemDetailsWriter` + 用例），只含 `src/` + `tests/` |

与旺总自述一致（`40f016a` 166 条 → `837117f` 补 3 条 = 169 条）。

补跑（审计中新落地的移交清单第 6 项提交 `001a731`，独立 worktree）：build 0/0，**35 + 79 + 33 + 25 + 1 = 173 条全绿**，含 `A_poison_message_is_rejected_after_the_maximum_attempts`、`A_dispatcher_without_handlers_logs_a_warning` 两条新用例。该提交当时不并入 07 的验收 hash（后随 `7f699c5` 成为基线的一部分）。

**终审基线（`7f699c5`，我实跑）**：build 0 警告 0 错误，`33 + 30 + 86 + 1 + 35 = 185 条全绿`；`git grep "ProbeWorker\|false &&"` 无残留；`src`/`tests` 干净。

### 6.2 三条必修的闭环核对

| 项 | 修法 | 我的独立验证 | 判定 |
|---|---|---|---|
| **F-07-1** 改名 token 绕过 | 请求路径 API 更名 `GetCurrentConnectionScopeAsync`，`GetConnectionScopeAsync` 只剩 `(long tenantId, CancellationToken)`；规则改为「后台路径不得出现 `.GetCurrentConnectionScopeAsync(`」 | 在 `OutboxDispatcher` 加 `_ = _dbProvider.GetCurrentConnectionScopeAsync();` → **架构测试 2 红**（产物扫描 + dispatcher 自身用例）；带 `tenantId` 的显式重载在后台路径仍判绿；反向用例含 `var ct2 = cancellationToken;` 样本 | **主修法通过**（类型系统层强保证成立）；文本规则三处漏检见 §6.5 F-07-4 |
| **F-07-2** `DomainEventDispatcher` 零覆盖 | 新增 4 条（运行时类型、DI 顺序、无处理器成功、一批两个事件） | `MakeGenericType(domainEvent.GetType())` → `typeof(IDomainEvent)` → **Infrastructure 3 红**（原 152 条全绿的漏洞已封） | **通过** |
| **F-07-3** 补偿顺序 | 先 `Rollback` 再 `outbox-remove`，`UnitOfWorkTests` 断言全序列 | 换回「先删后回滚」→ **1 红**（`dispatch → outbox-add → commit → rollback → outbox-remove` 精确匹配） | **通过** |
| 移交清单第 6 项（零消费者/失败原因） | 票面已移出本票 | 本票代码未动，符合 `tickets.md:309`、`:527` 口径 | **符合** |

### 6.3 票 06 残余 low（`837117f`）

- 实现改为非泛型 `IDictionary` 重建 + `StringComparer.Ordinal` + camelCase 后撞键「后写覆盖」，非字典值/非字符串键原样返回；`ReadOnlyDictionary<string,string[]>`、`Dictionary<string,List<string>>` 均有用例并断言**转换后的形态**（`IDictionary<string,object?>`），不是只断言"没抛"。
- 我的变异：`converted[k] = v` → `converted.Add(k, v)` → **Api 1 红**，撞键去重有真守护。
- 残留 low：去重用例依赖 `Dictionary` 枚举顺序（当前实现是插入序，非契约保证）。影响仅"撞键时留哪个值"，且撞键本身是上游错误；记录不修。

### 6.4 我的变异汇总（4/4 被抓）

| 变异 | 结果 |
|---|---|
| 后台代码改用请求路径 API | 架构 2 红 |
| `MakeGenericType` 取 `typeof(IDomainEvent)` | Infrastructure 3 红 |
| 补偿顺序改回先删后回滚 | Infrastructure 1 红 |
| 撞键去重改成 `Add`（06 low） | Api 1 红 |

### 6.5 F-07-4（medium，必修）：规则仍有三处漏检（票面 `84ea33b` 已定为必修）

老何在 `b9795e9` 写死了判定口径：**审计/测试若在后台路径找到「不需要先伪造 `ITenantContext` 的真实可写绕过」→ 必修、票 07 不开**。规则从"实参名白名单"改成"文本 + 路径判定"后，换名这一维堵住了，但文本层留了三个可真实写出的漏点——我都能实测绕过（架构测试 25 条全绿），且都不需要伪造上下文。老何 `84ea33b` 把这三条并成票面 **F-07-4（medium，必修）**，要求「后台路径出现 `GetCurrentConnectionScopeAsync` 标识符即违规」并并入 `Jobs/**`、`*Job.cs`，与我的判定一致。

- **① 空白断行即漏（我首轮记 F-07-7）**：[`ArchRules.cs:207`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L207) 的 `RequestPathConnectionApi` 是字面量 `".GetCurrentConnectionScopeAsync("`，方法名与 `(` 之间只要有一个空格或换行就不匹配：
  ```csharp
  internal static Task<ISqlSugarClient> Probe(Tenancy.ITenantDbProvider db) =>
      db.GetCurrentConnectionScopeAsync (
          CancellationToken.None);   // 放在 Persistence/** 里，架构测试 25 条全绿
  ```
  旧规则用的是 `\s*`，这是换实现时丢的容忍度，属**修法引入的回归**。票面要求的「标识符级禁用」天然覆盖它。
- **② 方法组引用不带括号（我首轮记 F-07-9）**：[`ArchRules.cs:191`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L191) 用 `Contains(".GetCurrentConnectionScopeAsync(")`，方法组引用没有 `(`：
  ```csharp
  internal static Func<CancellationToken, Task<ISqlSugarClient>> Probe(Tenancy.ITenantDbProvider db) =>
      db.GetCurrentConnectionScopeAsync;   // 再 f(ct) 即完成裸取；架构测试 25 条全绿
  ```
  一行委托即可绕过、不需要伪造上下文 → 必修。**注**：改为标识符级禁用后，`nameof(...)`、XML doc 提及也会误报，按票面「宁可误报注释，也不漏」接受。
- **③ 后台路径漏掉未来的 Quartz Job（我首轮记 F-07-8）**：[`ArchRules.cs:198-205`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L198-L205) 只认 `Persistence/**`、`*HostedService.cs`、`*Dispatcher.cs`。新建 `src/Pzd.Infrastructure/Jobs/TenantCleanupJob.cs` 调 `db.GetCurrentConnectionScopeAsync()` → **架构测试 25 条全绿**。步骤 3 上 Quartz 后 Job 类通常叫 `*Job.cs`，正好落在判定外 → 票面已要求并入 `Jobs/**`、`*Job.cs`。

**验收（按票面）**：后台路径文件里出现该标识符即违规（不看括号、不看空白、方法组同样算）；三条反向用例（断行、方法组、`Jobs/` 路径）各断言**恰好 1 条违规**。

**复核结果（`0c29633`，我实跑）：F-07-4 通过。** 独立 worktree，build 0/0、`35 + 79 + 33 + 30 + 1 = 178` 条全绿；四条变异全部被抓：

| 变异 | 结果 |
|---|---|
| 后台文件写 `db.GetCurrentConnectionScopeAsync ();`（空格断行） | 架构 2 红 |
| 后台文件写方法组 `var f = db.GetCurrentConnectionScopeAsync;` | 架构 2 红 |
| 新建 `src/Pzd.Infrastructure/Jobs/NightlyCleanupJob.cs` 调该 API | 架构 1 红 |
| 规则退回字面串 `.GetCurrentConnectionScopeAsync(` | 架构 2 红（断行、方法组两条反向用例） |

三条反向用例都断言 `Assert.Single(violations)` + 精确路径；`IsBackgroundPath` Theory 补了 `Jobs/NightlyCleanupJob.cs`、`Jobs/Helpers/RetryPolicy.cs`；`/Jobs/` 与 `*Job.cs` 都在谓词里。规则现为**标识符级**，故 `nameof`、XML doc 提及也会误报——按票面「宁可误报注释，也不漏」接受；`Contains` 对 `XXXGetCurrentConnectionScopeAsync` 这类前缀也会误报，同样只朝安全方向偏。

### 6.5b 重试上限配置化 + 终态 `LastError`（票面 **F-07-5**，我首轮记 F-07-10/11）

老何在 `75d3d19` 把重试口径写进票面验收：**重试次数有限且可配置（默认 5）、不得焊死为常量判断依据**；达上限进终态「已拒绝」并记 Error 日志、**不删除**消息、同批后续消息不得饿死；「拒绝」≠「死信队列」。`0c28f5e` 把我提的上限问题定为票面 **F-07-5（low → 本轮必修）**，`6c481d0` 又把终态 `LastError` 一并并入 F-07-5，并给出编号对照：票面 F-07-1/2/3/4/5 为准，我首轮报告的 F-07-4/5/6 对应票面「low 四条（`001a731`）」，F-07-7/8/9 全部并入票面 F-07-4。

- **票面 F-07-5 · 必修 · `MaxAttempts` 焊死成 `public const int 5`**：[`OutboxDispatcher.cs:27`](../../src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs#L27)，判断 `message.Attempts + 1 >= MaxAttempts`（`:109`），用例也直接拿常量当预期（`for (... < OutboxDispatcher.MaxAttempts ...)`、`Assert.Equal(OutboxDispatcher.MaxAttempts, consumer.Calls)`）。**为什么错**：判定依据正是那个常量，与「可配置、不得焊死」直接冲突；步骤 3 落真实 Outbox 表时上限要随配置走，测试锚在常量上也换不动。**票面要求**：上限从配置读取（默认 5，键 `Outbox:MaxAttempts`），代码只此一处来源、无第二处硬编码；补一条「配置成 1 时第一条失败即拒绝、同批后续消息仍被处理」的用例，测试不依赖常量值。
- **票面 F-07-5 附带 · 终态拒绝的 `LastError` 是上一轮的值**：[`OutboxDispatcher.cs:109-121`](../../src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs#L109-L121) 在第 5 次失败时直接 `RejectAsync`，不再调 `RecordFailureAsync`，所以 `LastError` 停留在第 4 次失败的信息，终态原因只在 `RejectionReason`。**要求**：拒绝路径同时写 `LastError`（或让 `RejectAsync` 同步写），并在注释里写清两字段职责（`LastError`=最近一次投递失败原因，`RejectionReason`=终态原因）。
- **已符合**：拒绝后 `DequeueBatchAsync` 过滤 `RejectionReason != null`，消息不删除、不再投递（[`InMemoryOutboxStore.cs:46-59`](../../src/Pzd.Infrastructure/Persistence/InMemoryOutboxStore.cs#L46-L59)）；同批内用 `continue` 不中断；Error 级日志有；`RejectAsync` 只打终态标记、没有假装成死信队列。

**复核结果（`7f699c5`，我实跑）：F-07-5 通过。** 独立 worktree，build 0/0、185 条全绿；四条变异全部被抓：

| 变异 | 结果 |
|---|---|
| 判定改回硬编码 `>= 5` | Infrastructure 2 红（配置 1、配置 2 两条用例） |
| 拒绝时不写 `LastError` | Infrastructure 1 红（上限配 1 那条——它才是真正钉住终态 `LastError` 的用例） |
| 加回 `public const int MaxAttempts = 5;` | Infrastructure 1 红（反射用例） |
| `Load` 把 `< 1` 放宽成 `< 0` | Infrastructure 1 红（`0` 非法值用例） |

我另外核过：`src` 里已无第二处硬编码（判定只用 `_options.MaxAttempts`，默认值只在 `OutboxOptions.DefaultMaxAttempts`；`Program.cs:35` 启动期 `Load`、非法值直接抛）；`OutboxOptionsTests` 覆盖「读到 2」「缺键回默认 5」「`0`/`-1`/`abc` 抛」；`git grep "ProbeWorker\|false &&"` 无残留；`OutboxTests.cs:186`（配 2 断言 `Calls == 2` + `RejectionReason` 含 `RetryLimitExceeded` + `LastError` 含 `poison message`）与 `:220`（配 1 断言毒消息终态且同批健康消息仍被消费、`processed == 1`）都在，且**都不引用任何常量当预期**。

- **low 观察（不阻塞）**：`appsettings.json:14` 里显式写了 `"MaxAttempts": 5`，与 `DefaultMaxAttempts` 是两处字面值——改默认值时容易只改一处、实际生效的仍是 appsettings 里的 5。建议删掉该键让默认值生效，或在旁边注明「运行时覆盖值，改默认值以 `OutboxOptions.DefaultMaxAttempts` 为准」。

### 6.6 流程提醒（不属本票判定）

- 审计时工作区那批 low 已由旺总单独提交为 **`001a731`**（零处理器告警、`LastError` + `MaxAttempts = 5` 超限拒绝、`TargetInvocationException` 解包），173 条全绿，**与 07 验收 hash 分离**，处置正确。
- 老何已在 `75d3d19` 定完重试口径（可配置、默认 5、拒绝≠死信、不删除消息、不得饿死后续消息），并在 `0c28f5e` 定为票面 **F-07-5**；`001a731` 与它的差额见 §6.5b，步骤 3 落库时按票面把「死信/失败表 + 人工重放」一并做。
- 老何的票面措辞（F-05-8）已由 `05c9343` 同步为改名后的 API 名，规则边界在 `b9795e9`、`84ea33b`，与我的判定一致。

### 6.7 最终判定

**票 07：通过（终审 `7f699c5`，185 条）。**
- **已闭环**：F-07-1/2/3（`40f016a`）、low 四条（`001a731`）、F-07-4（`0c29633`，我四条变异全红）、F-07-5（`7f699c5`，我四条变异全红）。票面 8 条验收 + 3 条返工项全部有行为或用例守护。
- **明确遗留（交接必须写成遗留）**：F-07-6 多处理器部分失败重放——需按处理器维度记进度，与步骤 3 Outbox 落库同批；契约写明"处理器必须幂等"。
- **口径不变**：**事务真回滚属步骤 3**，本票只有补偿删除，报告与交接不得写成"事务已回滚验证"；**「拒绝」≠「死信队列」**，不得写成已实现死信。
- **low 观察（不阻塞）**：`appsettings.json` 显式 `5` 与 `DefaultMaxAttempts` 两处字面值（见 §6.5b）。
- **规则边界**：按 §6.8 写「类型系统是唯一强保证、字符串规则拦误用不防蓄意规避」，不得写「不可能绕过」。
- 票 06 的残余 low 已由 `837117f` 闭环，`review-06.md` 的 low 清单可结。
- **可选加固（我认可，旺总工作区已在做）**：全仓另加一条「零实参 `GetConnectionScopeAsync()`」检测，守住"将来有人把无参重载加回来"的回归；不属 F-07-4 验收项，保留时应同时保留其反向样本。
- 口径不变：**事务真回滚属步骤 3，本票只有补偿删除，报告与交接不得写成"事务已回滚验证"**；「拒绝」≠「死信队列」，不得写成已实现死信。
- 票 06 的残余 low 已由 `837117f` 闭环，`review-06.md` 的 low 清单可结。

### 6.8 规则定位与边界声明（F-07-4 落地后生效）

按老何 `b9795e9` 的要求，报告不得把这条检查描述成「不可能绕过」。F-07-4 已在 `0c29633` 落地（我复核通过），本规则的定位固定为：

- **唯一强保证是类型系统那层**：`GetConnectionScopeAsync(CancellationToken)` 的签名已不存在（`40f016a`），后台代码想裸取"当前租户连接"在**编译期**就过不去；请求路径专用的 `GetCurrentConnectionScopeAsync` 只信任 `ITenantContext`，也没有 `tenantId` 入参可"顺手传错"。
- **架构规则是补充，拦误用、不防蓄意规避**：F-07-4 把它收紧到"后台路径出现标识符即违规"，覆盖断行/空格、方法组引用、`Jobs/` 路径这三类真实可写的误用；但刻意用反射、动态调用、`ITenantContext` 手工 `Set` 伪造上下文等方式绕过，不在字符串规则的防守范围内——这类绕过的前提是先把租户上下文造出来，成本与动机都远高于误用。交接与 README 写这条定位时照抄本节，**不写「不可能绕过」**。
- **范围随步骤 3 扩展**：`Jobs/**`、`*Job.cs` 已并入清单；后续再出现新的后台入口形态（如独立 worker 工程、`*Consumer.cs`），由本规则的所有者同步扩展，不靠"记得改"。

> 状态：**已生效**（F-07-4 = `0c29633`，票 07 终审通过 `7f699c5`）。
