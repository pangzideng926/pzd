# 交接：06 持久化约定定版后，07/08/09/13 各自该引什么（2026-10-30）

> 状态：06 七审通过（老登），代码零待改；下游四张票可直接开工
> 作者：老卫（写作）　日期：2026-10-30 12:00 Asia/Shanghai
> 只写票面装不下的跨票事实：下游引用索引、写路径闸门、遗留。单票口径看票面，理由看决策笔记，两处都不在此重复。

## 1. 基线

| 项 | 值 |
|---|---|
| 代码对象（七审基准） | `2096a0b` |
| 票面候选 | `2c93cde`（相对 `2096a0b` 只多票面） |
| 合并提交 | `7a412fe`（`--no-ff` 合并 `feat/06-persistence-base`，PZD-调度Bot 署名，15 文件 +2307 行；已推送，`HEAD...origin/main` 为 `0 0`） |
| 合并后复跑（张一一，在 main 上重跑非照抄） | build 0 警告 0 错误；`PersistenceTests` 33/33；集成 41 / 单元 33 / 架构 1 全 0 失败；残库 `pzd_test%` 为 0 |
| 审计结论 | 通过：四种业务态各自独立、规范化两边一致、三处变异全红；一条 low 不卡闸门 |
| 七审全文 | [.ai/验证/06/2026-10-30/证据/run-12-审计七审/结论.md](../验证/06/2026-10-30/证据/run-12-审计七审/结论.md) |
| 票面 | [06-persistence-base.md](../../docs/work/cross-cutting/tickets/06-persistence-base.md) |
| 决策理由 | [.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md](../../.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md) ——「为什么这么定」只在那里，本文件不复制 |

## 2. 四个写入口（07/08/09/13 都要认这张表）

业务代码**只允许** `db.Queryable<T>()` 读、`PersistenceWriter` 写。入口实现在 `src/Pzd.Infrastructure/PersistenceWriter.cs`：

| 入口 | 语义 | 可能的返回值 |
|---|---|---|
| `InsertAsync` | 唯一插入口；审计四列、雪花主键、`Version=1`、租户校验由背后 AOP 强制 | `DuplicateKey`（1062） |
| `UpdateAsync` | 更新；WHERE 带 `AND TenantId=@tenantId`；创建审计列永不进 SET | `VersionConflict` / `NotFound` / `RecordInTrash` / `DuplicateKey` |
| `SoftDeleteAsync` | 软删除唯一入口；写全 `IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy` 四列 | 同上四种 |
| `RestoreAsync` | 恢复；`DeletedToken` 归 0、`DeletedAt`/`DeletedBy` 清空 | 同上四种，另加恢复撞唯一键的 `DuplicateKey` |

读侧：`db.Queryable<T>()`；看回收站必须显式 `ClearFilter()`。硬删除只能给 `PersistenceOptions.HardDeleteTypes` 里登记过的类型。

理由（票面已写，这里只提一句）：`db.Updateable(entity)` 会把 `Version` 写成 0、抹掉创建审计、把整行搬到别的租户；`db.Deleteable(entity)` 会让回收站失效。两层网是「本契约 + 13 号票静态守卫」，静态那层还没织好。

## 3. 四种业务态 code（09 号票映射依据，别压平）

`PersistenceFailureKind` → 建议状态码 → `code`：

| Kind | 状态码 | code | 什么时候 |
|---|---|---|---|
| `NotFound` | 404 | `Persistence.Record.NotFound` | 本租户下没这行；**跨租户也归这里**，不泄露别家存在性 |
| `RecordInTrash` | 409 | `Persistence.Record.InTrash` | 行在回收站（`IsDeleted=1`）——这是数据状态、可恢复，所以 409 不用 410 |
| `VersionConflict` | 409 | `Persistence.Record.VersionConflict` | 版本对不上，影响 0 行；内存实体不被改写 |
| `DuplicateKey` | 409 | `Persistence.Record.DuplicateKey` | MySQL 1062 |
| `Unknown` | 500 | `Persistence.Record.Unknown` | 其它库错误，走异常不是业务态 |

`RecordInTrash` 与 `NotFound` 的区分是六审 high 的结论，别再合并回一个 409。恢复场景有个例外：`kind == Restore` 时行是删除态不是错误，版本对不上才是 `VersionConflict`——这条由 `PersistenceWriter` 内部保证，调用方按上表接即可。

### 边界：异常只留给「用错 API」，业务态一律返回值

这条是 06 全文的分界线，07/08/09/13 接的时候最容易搞混：

| 情形 | 表现 | 例子 |
|---|---|---|
| **业务态**（数据本身处于某种状态） | 走返回值 `PersistenceFailure`，调用方按 §3 映射状态码 | 行不存在、行在回收站、版本对不上、业务键撞了 |
| **用错 API**（调用方的代码就是错的） | 抛异常，不降级成返回值 | `db.Updateable(...)`/`db.Deleteable(...)` 被 AOP 无条件拒绝；写入口没拿到租户上下文（`InvalidOperationException`）；拿活跃记录去 `RestoreAsync`（`InvalidOperationException`） |
| **意外** | 抛异常，`Classify` 归 `Unknown`/500 | 其它数据库错误 |

判断口径：**这个失败是「数据就在这个状态」还是「你根本不该这么调」**。前者返回值（前端能给出重试/恢复的出口），后者抛异常（要改代码，不是要用户重来）。把用错 API 写进返回值，等于把编程错误伪装成可重试的业务失败。

## 4. 下游各引哪一段

| 票 | 引用什么 | 不要做什么 |
|---|---|---|
| **07** 多租户解析 | 租户谓词 **已在 06 内**：写入口 WHERE 自带 `AND TenantId=@tenantId`，租户上下文缺失时抛 `InvalidOperationException` 而非按 Id 更新 | 不重做谓词；07 只补跨租户的红用例与解析链路本身 |
| **08** UoW + Outbox | 提交路径必须经 `PersistenceWriter` 的四个入口 | 不自建 `Updateable`/`Insertable`，也不绕过写入口拼 SQL |
| **09** 异常映射 | §3 的五种 Kind 与 code 逐字对齐（票面 `697bcaa` 已是四种 code） | 不把 `RecordInTrash` 压成并发冲突，不把 `NotFound` 压成 409 |
| **13** 架构测试 | §2 的禁止面：`src/` 内除 `src/Pzd.Infrastructure/**` 出现 `Insertable`/`Updateable`/`Deleteable` 即红 | 这条守卫合入前，任何模块不得写写路径代码（见 §5） |

## 5. 写路径闸门（最容易漏的一条）

13 号票静态守卫合入之前，**任何模块不得编写写路径代码**。原因实测：`db.Deleteable<T>().Where(...)` 这类不带实体的形态 AOP 钩不到，仍能一路物理删（实测 `物理行数=0`）；眼下「运行期 + 静态」两层网只剩运行期一层。

07/08 若在此前开工，写路径部分必须先等 13。这是票面 `:67` 的原话「已知缺口不是许可」，不是老卫的补充要求。

## 6. 建表约定（供 17、19、20、23、32 复用）

定型的软删除唯一索引方案（票面有完整 SQL，那处与 `PersistenceTests.SchemaSql` 必须逐字同形，17 号票建表拿两份对照）：

- `DeletedToken BIGINT NOT NULL DEFAULT 0`，活记录恒 0，软删除时写自身主键 `Id`；唯一索引是 `(TenantId, <业务键>, DeletedToken)`。
- 不用可空列 + NULL：MySQL 唯一索引对 NULL 不去重，未删写 NULL 时两条活跃同名都能插入，唯一约束等于没有。
- 建表必带：雪花 `BIGINT` 主键、`Version INT`、审计四列、`IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy`、时间列 `DATETIME(6)`。
- 后两列（`DeletedAt`/`DeletedBy`）不是可选：缺了 `SoftDeleteAsync` 报 `Unknown column`，看报错的人会误以为是 ORM 映射问题。

## 7. 遗留

1. **`dotnet test` 本体仍未在普通终端跑过**：07/08 开工前，维护者补跑 `cd E:/pzd && dotnet test Pzd.slnx`，要看到 `PersistenceTests 33/33`（整仓集成 41、单元 33、架构 1）。`总计: 0` 一律按未运行。
2. **一条 low 不阻塞合并，但 07 开工前必修**：`PersistenceWriter.cs:53-58` 的 `MissKind` 声明序是 `NoRow, Recycled, Active`（0/1/2），而 `ResolveMissAsync` 的 SQL 把「已删」编成 2、「活跃」编成 1，正好反着。当前靠显式 `switch` 映射，行为正确；但有人改成 `(MissKind)code` 语义立刻反转且没有编译错误。修法两字符级：SQL 改成 `THEN 1`/`ELSE 2`，或返回字符串。张一一已定为「07 开工前必须修掉」（07 的租户解析要碰这条路径），并要求雨哥写进 06 票面遗留、补完给 SHA。
3. **票面待补一句**：`DeletedAt` 必须可空（活跃为 NULL、回收站存删除时刻）——老登建议落进 06 票面 `:98` 的建表约定正文。06 票面由原主写入者维护，我不代改。
4. 06 worktree `E:/pzd/.worktree/06`（`2c93cde`）在合入并确认后可清理，清理按[文件整理](../../.ai/规则/文件整理.md)走可恢复移动。

## 8. 接手先核对什么

```bash
cd E:/pzd
git log --oneline -2                     # 应看到 7a412fe（06 合并）
git status --porcelain                   # 应为空
dotnet build Pzd.slnx                    # 0 警告 0 错误
./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe   # Total: 41, Failed: 0
```
