# 审计报告：票 07（UoW + Outbox + 领域事件）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec）+ 变异复核，合并李总 [`test-report-07.md`](test-report-07.md)
> 被测：`d9919ab`（`feat(persistence): 票 07 UoW + Outbox + 领域事件`）；审计在独立 worktree 的干净副本上执行，07 代码在后续提交中未再变动
> 依据：[`architecture.md`](../architecture.md) §6.3、[`foundation.md`](../specs/foundation.md) §4.5、[`tickets.md`](../specs/tickets.md) 票 07（`d9919ab` 回填版）
> **结论：需修改。** 主路径（提交顺序、失败补偿、事件清除、`TenantId` + 消费前 `Set`、停用/过期拒绝、消息级幂等、失败重试、优雅停止）经我实跑与读码成立；但 **F-07-1**（F-05-8 代码检查可被改名 token 绕过）与 **F-07-2**（`DomainEventDispatcher` 零覆盖）必修，另 4 条 low。

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
