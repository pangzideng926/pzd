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
| 老登对本文件的复核 | [交接记录复核.md](../验证/06/2026-10-30/证据/run-12-审计七审/交接记录复核.md) —— 两条 high、一条 low，已按 §3/§6 与写入口表修订 |
| 票面 | [06-persistence-base.md](../../docs/work/cross-cutting/tickets/06-persistence-base.md) |
| 决策理由 | [.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md](../../.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md) ——「为什么这么定」只在那里，本文件不复制 |

## 2. 四个写入口（07/08/09/13 都要认这张表）

业务代码**只允许** `db.Queryable<T>()` 读、`PersistenceWriter` 写。入口实现在 `src/Pzd.Infrastructure/PersistenceWriter.cs`：

| 入口 | 语义 | 可能的返回值 |
|---|---|---|
| `InsertAsync` | 唯一插入口；审计四列、雪花主键、`Version=1`、租户校验由背后 AOP 强制 | `DuplicateKey`（1062） |
| `UpdateAsync` | 更新；WHERE 带 `AND TenantId=@tenantId`；创建审计列永不进 SET | `VersionConflict` / `NotFound` / `RecordInTrash` / `DuplicateKey` |
| `SoftDeleteAsync` | 软删除唯一入口；写全 `IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy` 四列 | 见 §3（`RecordInTrash` 最常见） |
| `RestoreAsync` | 恢复；`DeletedToken` 归 0、`DeletedAt`/`DeletedBy` 清空；要求目标是删除态（`WHERE … AND IsDeleted=1`） | 见 §3；其中 `DuplicateKey` 特指业务键已被活跃记录占用 |

读侧：`db.Queryable<T>()`；看回收站必须显式 `ClearFilter()`。硬删除只能给 `PersistenceOptions.HardDeleteTypes` 里登记过的类型。

理由（票面已写，这里只提一句）：`db.Updateable(entity)` 会把 `Version` 写成 0、抹掉创建审计、把整行搬到别的租户；`db.Deleteable(entity)` 会让回收站失效。两层网是「本契约 + 13 号票静态守卫」，静态那层还没织好。

## 3. 五种业务态 code（09 号票映射依据，别压平）

> **实现状态先说清**：`7a412fe` 上只有前四种（`NotFound` / `RecordInTrash` / `VersionConflict` / `DuplicateKey`）。第五种 `NotInTrash` 是张一一 2026-10-30 的裁定（09 票 `698e4da`），**尚未落到代码**——雨哥正在 `MissKind` 那个分支里一起做。下表是**目标态**，接的时候按代码实际行为核一遍再定稿。

| Kind | 状态码 | code | 什么时候 |
|---|---|---|---|
| `NotFound` | 404 | `Persistence.Record.NotFound` | 本租户下没这行；**跨租户也归这里**，不泄露别家存在性 |
| `RecordInTrash` | 409 | `Persistence.Record.InTrash` | 行在回收站（`IsDeleted=1`），不能直接改；文案写「请先恢复」，不用 410（可恢复，410 语义是不可恢复） |
| `NotInTrash` | 409 | `Persistence.Record.NotInTrash` | **新增**：恢复的目标不在回收站（那行是活跃的）。文案「该记录不在回收站」 |
| `VersionConflict` | 409 | `Persistence.Record.VersionConflict` | 版本对不上，影响 0 行；内存实体不被改写 |
| `DuplicateKey` | 409 | `Persistence.Record.DuplicateKey` | MySQL 1062 |
| `Unknown` | 500 | `Persistence.Record.Unknown` | 其它库错误，走异常不是业务态 |

三种 409（`InTrash` / `NotInTrash` / `VersionConflict`）`code` 与文案必须互不相同——压成一个的后果是前端提示「请刷新重试」，而重载永远读不到那条记录。`RecordInTrash` 与 `NotFound` 的区分是六审 high 的结论，别再合并回一个 409。

`NotInTrash` 取代的是现在的一个 409：裁定前，恢复一条活跃记录返回 `VersionConflict`（七审 `R7`）；裁定后它变成独立的一种「恢复目标状态不对」。两者的区别对前端是实打实的——前者提示「被人改过，刷新重试」，后者提示「这条记录本来就没在回收站」。

### 边界：异常只留给「用错 API」，业务态一律返回值

这条是 06 全文的分界线，07/08/09/13 接的时候最容易搞混：

| 情形 | 表现 | 例子 |
|---|---|---|
| **业务态**（数据本身处于某种状态） | 走返回值 `PersistenceFailure`，调用方按 §3 映射状态码 | 行不存在、行在回收站、版本对不上、业务键撞了 |
| **用错 API**（调用方的代码就是错的） | 抛异常，不降级成返回值 | `db.Updateable(...)`/`db.Deleteable(...)` 被 AOP 无条件拒绝；写入口没拿到租户上下文（`InvalidOperationException`）；`RestoreAsync` 传入内存里 `IsDeleted=false` 的实体（`PersistenceWriter.cs:134-138` 直接抛） |
| **意外** | 抛异常，`Classify` 归 `Unknown`/500 | 其它数据库错误 |

判断口径：**这个失败是「数据就在这个状态」还是「你根本不该这么调」**。前者返回值（前端能给出重试/恢复的出口），后者抛异常（要改代码，不是要用户重来）。把用错 API 写进返回值，等于把编程错误伪装成可重试的业务失败。

**「活跃 / 已删」要看是谁在说** —— `7a412fe` 上，判据是**调用方实体上的标记**，不是库状态。同一件事走哪条路，取决于实体自己怎么标：

| 入口 | 实体内存标记 | 走哪条 | 结果 |
|---|---|---|---|
| `RestoreAsync` | `IsDeleted=false` | 内存守卫 `:135-139` | **抛** `InvalidOperationException` —— 用错 API |
| `RestoreAsync` | `IsDeleted=true`，但库里那行已活跃 | SQL 带 `AND IsDeleted=1` → 0 行 → `Active` → `Recycled` 例外不生效（`:282`）→ `:287` | **返回** `VersionConflict`/409 —— 业务态 |
| `UpdateAsync` | `IsDeleted=true` | 内存守卫 `:184-189` | **抛** `InvalidOperationException`，提示走 `SoftDeleteAsync`/`RestoreAsync` |
| `UpdateAsync` | `DeletedToken≠0` | 内存守卫 `:191-196` | **抛** `InvalidOperationException`，提示走 `RestoreAsync` |
| `UpdateAsync` | 干净活跃快照（HTTP 转回来的实体就是这样） | SQL 带 `AND IsDeleted=0` → 0 行 → `Recycled` | **返回** `RecordInTrash`/409 —— 业务态 |
| `SoftDeleteAsync` | 实体已删、库里也已删 | `AND IsDeleted=0` → 0 行 → `Recycled` | **返回** `RecordInTrash`/409（七审 `R5` 的幂等口径） |

三条实测对照：老登七审 `R7`（实体自称已删、库活跃 → `VersionConflict`）、`R5`（重复软删 → `InTrash`）、李总合并后复验（活跃快照更新已删记录 → `InTrash`）分别落在上面第 2、6、5 行。

**这条对端点层的实际后果（09 必读）**：从 HTTP 请求体反序列化出来的实体，`IsDeleted` 默认 `false`、`DeletedToken` 默认 `0`。所以一个「恢复」请求如果直接把 DTO 转成实体丢进 `RestoreAsync`，**走的是异常路径，拿不到 409**；`UpdateAsync` 也因为实体自称活跃而不会命中内存守卫，只能靠库里状态返回 `RecordInTrash`。换句话说：**只有实体自称删除态时，恢复才返回业务态**。端点层必须给这类 `InvalidOperationException` 一个出口——先按主键查记录确认它真的在回收站再调，或捕获后映射成 409「该记录不在回收站」/ 参数错误，**不要让它冒成 500**。07/08 写调用方时按这张表逐行留分支，别只接返回值。

**但这张表只是现状，不是目标态。** 张一一 2026-10-30 已裁定：`RestoreAsync` 去掉实体自报守卫，改由库判定（`WHERE IsDeleted=1` 本来就在 SQL 里），`affected==0` 时按三态区分，目标行活跃就给 `Persistence.Record.NotInTrash`（409）。理由跟 P8 同一个病根——**该由库判定状态的地方用了实体上的标记**。改造后上面前两行合并为一行：

| 入口 | 情形 | 改造后结果 |
|---|---|---|
| `RestoreAsync` | 目标行活跃（不在回收站） | 返回 `NotInTrash`/409，不再抛异常、也不再报 `VersionConflict` |
| `RestoreAsync` | 目标行已删但版本对不上 | 返回 `VersionConflict`/409 |
| `RestoreAsync` | DTO 形态（实体 `IsDeleted=false`、库已是删除态） | **恢复成功**，不再被守卫拦住 |

改造由雨哥在 `MissKind` 分支里一起做（两处都在 `WriteAsync` 同一段），并补两条用例：DTO 形态恢复已删记录应成功、恢复活跃记录返回 `NotInTrash` 而不是异常。**改完这张表要按实际代码重核一遍**——本文件此刻写的是裁定，不是已实现的事实。

## 4. 下游各引哪一段

| 票 | 引用什么 | 不要做什么 |
|---|---|---|
| **07** 多租户解析 | 租户谓词 **已在 06 内**：写入口 WHERE 自带 `AND TenantId=@tenantId`，租户上下文缺失时抛 `InvalidOperationException` 而非按 Id 更新 | 不重做谓词；07 只补跨租户的红用例与解析链路本身 |
| **08** UoW + Outbox | 提交路径必须经 `PersistenceWriter` 的四个入口 | 不自建 `Updateable`/`Insertable`，也不绕过写入口拼 SQL |
| **09** 异常映射 | §3 的五种 Kind 与 code 逐字对齐（票面 `698e4da` 已写五种，其中 `NotInTrash` 待 06 代码落地） | 不把 `RecordInTrash` 压成并发冲突，不把 `NotFound` 压成 409，三种 409 不许共用文案 |
| **13** 架构测试 | §2 的禁止面：`src/` 内除 `src/Pzd.Infrastructure/**` 出现 `Insertable`/`Updateable`/`Deleteable` 即红 | 这条守卫合入前，任何模块不得写写路径代码（见 §5） |

## 5. 写路径闸门（最容易漏的一条）

13 号票静态守卫合入之前，**任何模块不得编写写路径代码**。原因实测：`db.Deleteable<T>().Where(...)` 这类不带实体的形态 AOP 钩不到，仍能一路物理删（实测 `物理行数=0`）；眼下「运行期 + 静态」两层网只剩运行期一层。

07/08 若在此前开工，写路径部分必须先等 13。这是票面 `:67` 的原话「已知缺口不是许可」，不是老卫的补充要求。

## 6. 建表约定（供 17、19、20、23、32 复用）

定型的软删除唯一索引方案（票面有完整 SQL，那处与 `PersistenceTests.SchemaSql` 必须逐字同形，17 号票建表拿两份对照）：

- `DeletedToken BIGINT NOT NULL DEFAULT 0`，活记录恒 0，软删除时写自身主键 `Id`；唯一索引是 `(TenantId, <业务键>, DeletedToken)`。
- 不用可空列 + NULL：MySQL 唯一索引对 NULL 不去重，未删写 NULL 时两条活跃同名都能插入，唯一约束等于没有。
- 建表必带：雪花 `BIGINT` 主键、`Version INT`、审计四列、`IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy`、时间列 `DATETIME(6)`。
- **`DeletedAt` 必须可空**：`DeletedAt DATETIME(6) NULL`。活跃行的它就是 NULL（七审 `R8` 实证），建成 `NOT NULL` 会让活跃记录直接插入失败，或逼出 `'0001-01-01'` 这类哨兵值，回收站排序随即失真；报错还指向列约束，看的人想不到是约定漏了半句。17 号票建表照这条写。
- 后两列（`DeletedAt`/`DeletedBy`）不是可选：缺了 `SoftDeleteAsync` 报 `Unknown column`，看报错的人会误以为是 ORM 映射问题。

## 7. 遗留

1. **`RestoreAsync` 去实体自报守卫 + 新增 `NotInTrash`（张一一 2026-10-30 裁定，雨哥在做）**：改造完成后 §3 才有五种 code，§3 与边界表里「恢复」那几行要按实际代码重核。判据两条用例：DTO 形态（实体 `IsDeleted=false`、库已是删除态）恢复应成功；恢复活跃记录返回 `NotInTrash` 而不是异常。这条同时消掉「09 只接返回值会冒 500」的风险。
2. **`dotnet test` 本体仍未在普通终端跑过**：07/08 开工前，维护者补跑 `cd E:/pzd && dotnet test Pzd.slnx`，要看到 `PersistenceTests` 全绿（当前基线 33，雨哥分支加了用例后会变 34+；整仓集成 41、单元 33、架构 1）。`总计: 0` 一律按未运行。
3. **`MissKind` 数字编码反序（07 开工前必修）**：`PersistenceWriter.cs:53-58` 声明序是 `NoRow, Recycled, Active`（0/1/2），而 `ResolveMissAsync` 的 SQL 把「已删」编成 2、「活跃」编成 1，正好反着。当前靠显式 `switch` 映射，行为正确；但有人改成 `(MissKind)code` 语义立刻反转且没有编译错误。**方案已定（雨哥，与上一条同分支）**：SQL 返回自描述字符串、`Enum.TryParse` 接，不再有数字编码。纯对齐编码的写法测不出来，用例要按行为写。
4. ~~票面待补一句~~ **已办**：`DeletedAt` 必须可空已由雨哥补进 06 票面（`01fdb59`，建表约定节 `:100`，并在遗留里补了列类型），本文件 §6 也写进了正文，两处口径一致；`dotnet test` 与 `MissKind` 两条同样进了票面遗留。
5. 06 worktree `E:/pzd/.worktree/06`（`2c93cde`）在合入并确认后可清理，清理按[文件整理](../../.ai/规则/文件整理.md)走可恢复移动。

## 8. 接手先核对什么

```bash
cd E:/pzd
git log --oneline -2                     # 应看到 7a412fe（06 合并）
git status --porcelain                   # 应为空
dotnet build Pzd.slnx                    # 0 警告 0 错误
./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe   # Total: 41, Failed: 0
```
