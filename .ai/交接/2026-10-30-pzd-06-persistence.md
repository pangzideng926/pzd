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
| 本次改造（分支 `fix/06-misskind-order`，待合入） | `RestoreAsync` 去掉实体自报守卫、改由库判定；新增第五种 `NotInTrash`；`MissKind` 去掉数字编码改自描述字符串。实测 `PersistenceTests` 36/36、集成 44、单元 33、架构 1 全 0 失败、残库 `pzd_test%` 为 0 |
| 七审全文 | [.ai/验证/06/2026-10-30/证据/run-12-审计七审/结论.md](../验证/06/2026-10-30/证据/run-12-审计七审/结论.md) |
| 老登对本文件的复核 | [交接记录复核.md](../验证/06/2026-10-30/证据/run-12-审计七审/交接记录复核.md) —— 两条 high、一条 low，已按 §3/§6 与六行矩阵修订 |
| 六行矩阵实测（李总，8 条探针全绿） | [矩阵逐行验证](../验证/06/2026-10-30/证据/run-10-矩阵逐行验证/结论.md)，探针 `WriteEntryMatrixProbes.cs` 同目录；行号与本文件矩阵同号 |
| 本文件入库 | `01de48e`（张一一，+43/−12，`src`/`tests` 零差异）；此后 §3 与矩阵的修订以最新 SHA 为准 |
| 票面 | [06-persistence-base.md](../../docs/work/cross-cutting/tickets/06-persistence-base.md) |
| 决策理由 | [.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md](../../.agents/notes/implemented/architecture/2026-10-30-persistence-conventions.md) ——「为什么这么定」只在那里，本文件不复制 |

## 2. 四个写入口（07/08/09/13 都要认这张表）

业务代码**只允许** `db.Queryable<T>()` 读、`PersistenceWriter` 写。入口实现在 `src/Pzd.Infrastructure/PersistenceWriter.cs`：

| 入口 | 语义 | 可能的返回值 |
|---|---|---|
| `InsertAsync` | 唯一插入口；审计四列、雪花主键、`Version=1`、租户校验由背后 AOP 强制 | `DuplicateKey`（1062） |
| `UpdateAsync` | 更新；WHERE 带 `AND TenantId=@tenantId`；创建审计列永不进 SET | `VersionConflict` / `NotFound` / `RecordInTrash` / `DuplicateKey` |
| `SoftDeleteAsync` | 软删除唯一入口；写全 `IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy` 四列 | 见 §3（`RecordInTrash` 最常见） |
| `RestoreAsync` | 恢复；`DeletedToken` 归 0、`DeletedAt`/`DeletedBy` 清空；能否恢复由**库**判定（`WHERE … AND IsDeleted=1`），不看实体自报的标记 | 见 §3；`NotInTrash` 指目标本来就不在回收站，`DuplicateKey` 特指业务键已被活跃记录占用 |

读侧：`db.Queryable<T>()`；看回收站必须显式 `ClearFilter()`。硬删除只能给 `PersistenceOptions.HardDeleteTypes` 里登记过的类型。

理由（票面已写，这里只提一句）：`db.Updateable(entity)` 会把 `Version` 写成 0、抹掉创建审计、把整行搬到别的租户；`db.Deleteable(entity)` 会让回收站失效。两层网是「本契约 + 13 号票静态守卫」，静态那层还没织好。

## 3. 五种业务态 code（09 号票映射依据，别压平）

> **实现状态**：`7a412fe` 上只有前四种（`NotFound` / `RecordInTrash` / `VersionConflict` / `DuplicateKey`）。第五种 `NotInTrash` 是张一一 2026-10-30 的裁定（09 票 `698e4da`），**已由雨哥在 `fix/06-misskind-order` 落地**——`RestoreAsync` 改由库判定、目标活跃给 `NotInTrash`、`MissKind` 去掉数字编码改自描述字符串。下表已按该分支的实际代码与实测输出重核过，不是裁定转述。

| Kind | 状态码 | code | 什么时候 |
|---|---|---|---|
| `NotFound` | 404 | `Persistence.Record.NotFound` | 本租户下没这行；**跨租户也归这里**，不泄露别家存在性 |
| `RecordInTrash` | 409 | `Persistence.Record.InTrash` | 行在回收站（`IsDeleted=1`），不能直接改；文案写「请先恢复」，不用 410（可恢复，410 语义是不可恢复） |
| `NotInTrash` | 409 | `Persistence.Record.NotInTrash` | 恢复的目标不在回收站（那行是活跃的）。文案「这条记录不在回收站，无需恢复。」 |
| `VersionConflict` | 409 | `Persistence.Record.VersionConflict` | 版本对不上，影响 0 行；内存实体不被改写 |
| `DuplicateKey` | 409 | `Persistence.Record.DuplicateKey` | MySQL 1062 |
| `Unknown` | 500 | `Persistence.Record.Unknown` | 其它库错误，走异常不是业务态 |

三种 409（`InTrash` / `NotInTrash` / `VersionConflict`）`code` 与文案必须互不相同——压成一个的后果是前端提示「请刷新重试」，而重载永远读不到那条记录。`RecordInTrash` 与 `NotFound` 的区分是六审 high 的结论，别再合并回一个 409。

`NotInTrash` 取代的是一个 409：裁定前，恢复一条活跃记录返回 `VersionConflict`（七审 `R7`）；裁定后它变成独立的一种「恢复目标状态不对」。两者的区别对前端是实打实的——前者提示「被人改过，刷新重试」，后者提示「这条记录本来就没在回收站」。本分支已落地，`R7` 那个形状现在返回 `NotInTrash`。

### 边界：异常只留给「用错 API」，业务态一律返回值

这条是 06 全文的分界线，07/08/09/13 接的时候最容易搞混：

| 情形 | 表现 | 例子 |
|---|---|---|
| **业务态**（数据本身处于某种状态） | 走返回值 `PersistenceFailure`，调用方按 §3 映射状态码 | 行不存在、行在回收站、恢复的目标不在回收站、版本对不上、业务键撞了 |
| **用错 API**（调用方的代码就是错的） | 抛异常，不降级成返回值 | `db.Updateable(...)`/`db.Deleteable(...)` 被 AOP 无条件拒绝；写入口没拿到租户上下文（`InvalidOperationException`）；`UpdateAsync`/`SoftDeleteAsync` 传入内存里带删除标记的实体（`IsDeleted=true` 或 `DeletedToken≠0`） |
| **意外** | 抛异常，`Classify` 归 `Unknown`/500 | 其它数据库错误 |

判断口径：**这个失败是「数据就在这个状态」还是「你根本不该这么调」**。前者返回值（前端能给出重试/恢复的出口），后者抛异常（要改代码，不是要用户重来）。把用错 API 写进返回值，等于把编程错误伪装成可重试的业务失败。

**「活跃 / 已删」由库里那行判定，不由实体标记判定**（本分支起）。`Update`/`SoftDelete` 仍保留内存守卫去挡明显用错（实体自带删除标记），但真正的活跃条件写在 SQL 的 `AND IsDeleted=0` 里——所以从 HTTP 体转回来、不自报删除的实体照样穿不过去（P8）。`RestoreAsync` 那条实体自报守卫**已删**，能不能恢复完全由 `AND IsDeleted=1` 判定。改造前恢复的判据放在实体标记上，`IsDeleted=false` 的 DTO 会被抛成 500，那正是本条要修的东西。

**本表行号是标准答案**（张一一 2026-10-30 裁定：文档为准，探针与结论跟着文档走）。李总 [矩阵逐行验证](../验证/06/2026-10-30/证据/run-10-矩阵逐行验证/结论.md) 的 8 条探针按此编号；任何人引用「矩阵第 N 行」，两边指的是同一件事：

| # | 入口 | 情形 | 走哪条 | 结果 | 用户看到什么 |
|---|---|---|---|---|---|
| 1 | `UpdateAsync` | 实体 `IsDeleted=true` | 内存守卫 `:184-189` | **抛** `InvalidOperationException`，提示走 `SoftDeleteAsync`/`RestoreAsync` | 无业务文案（用错 API，见下「端点层」） |
| 2 | `UpdateAsync` | 实体说活跃但 `DeletedToken≠0` | 内存守卫 `:191-196` | **抛** `InvalidOperationException`，提示走 `RestoreAsync` | 同上 |
| 3 | `UpdateAsync` | 干净活跃快照（HTTP 转回来的实体就是这样） | SQL 带 `AND IsDeleted=0` → 0 行 → `Recycled` | **返回** `RecordInTrash`/409 | 「这条记录已被删除，如需修改请先恢复」——**准确** |
| 4 | `SoftDeleteAsync` | 实体已删、库里也已删 | `AND IsDeleted=0` → 0 行 → `Recycled` | **返回** `RecordInTrash`/409（七审 `R5` 的幂等口径） | 同上，重复删除不算错 |
| 5 | `RestoreAsync` | DTO 形态（`IsDeleted=false`）、库已是删除态 | `WHERE … AND IsDeleted=1` 命中 | **恢复成功**（本分支起不再抛异常） | 恢复成功 |
| 6 | `RestoreAsync` | 目标行活跃，压根不在回收站 | 0 行 → `Active` | **返回** `NotInTrash`/409（本分支起；改造前是 `VersionConflict`） | 「这条记录不在回收站，无需恢复。」——**准确**，不再指路去刷新 |
| 7 | `RestoreAsync` | 目标行已删，但版本对不上 | 0 行 → `Recycled`，`kind == Restore` 的例外不生效 | **返回** `VersionConflict`/409 | 「记录已被其他操作修改，请刷新后重试」——这里才名副其实 |

**改造前的第 6 行正是 `NotInTrash` 要取代的东西**：code 分得开、文案分不开，用户仍会被指去重试一件永远不会成功的事。这也是为什么它不只是「补个 code 更整齐」。本分支落地后，第 6 行换成了独立的 `NotInTrash` 和「不在回收站」的文案。

顺带一条给测试的教训：**断言要同时校验 `Kind` + `code` + 文案**。第 6 行这个错，偏偏是只比 `Kind` 和 `code` 时完全看不见的——两者都对，只有那句 `请刷新后重试` 是错的。07/08/09 之后加写路径用例时照此写。

实证三方交叉：李总 `run-10` 八条探针六行全绿（行号同本表，测的是改造前形态）；老登七审 `R7` 落在第 6 行、`R5` 落在第 4 行；李总合并后复验的活跃快照更新已删记录落在第 3 行。本分支按上表重跑：第 5 行恢复成功、第 6 行 `NotInTrash`/409/文案含「回收站」、第 7 行 `VersionConflict`/409，第 1–4 行行为未变。

**这条对端点层的实际后果（09 必读）**：从 HTTP 请求体反序列化出来的实体，`IsDeleted` 默认 `false`、`DeletedToken` 默认 `0`。本分支起，这种 DTO 形态直接丢进 `RestoreAsync` 是**正常路径**：库里是删除态就恢复成功（第 5 行），库里是活跃就返回 `NotInTrash`（第 6 行）——不需要先查一遍，也不需要捕异常。`UpdateAsync` 依旧靠库里状态返回 `RecordInTrash`（第 3 行）。仍会冒 500 的只剩「用错 API」那一类（`UpdateAsync`/`SoftDeleteAsync` 收到自报删除态的实体，第 1、2 行），那是调用方代码 bug，应当修代码而不是兜异常。

**改造已落地（分支 `fix/06-misskind-order`，基于 `7a412fe`、已同步 `19c4a05`）**。张一一 2026-10-30 的裁定（09 票 `698e4da`）与老登、李总三方指向同一个病根：该由库判定状态的地方用了实体上的标记。具体三件：

1. `RestoreAsync` 删掉「实体自报 `IsDeleted` 才让恢复」的守卫，能不能恢复改由 SQL 的 `AND IsDeleted=1` 判定。
2. `WriteAsync` 的 `affected == 0` 分型补一档：恢复种类且三态查到 `Active` → `PersistenceErrors.NotInTrash()`（409 + `Persistence.Record.NotInTrash`，文案「这条记录不在回收站，无需恢复。」）。
3. `MissKind` 去掉数字编码：`ResolveMissAsync` 的 SQL 直接返回 `'NoRow'`/`'Recycled'`/`'Active'`，`Enum.TryParse` 接；解析不出来直接抛（只在 SQL 与枚举名不一致时才可能，属于程序 bug）。「编码顺序反了」这种失败模式从此不存在。

新增三条用例：`DTO形态恢复已删记录应成功`（第 5 行）、`恢复活跃记录返回不在回收站`（第 6 行）、`三种未命中情形各自返回对应的失败种类`（`NotFound`/`InTrash`/`VersionConflict` 三个 code 两两不等，只走公开行为、不依赖内部编码）。

变异六处，全部精确红（没有一处是构建失败）：

| 变异 | 结果 |
|---|---|
| SQL 三态字符串把 `Recycled`/`Active` 对调 | `Failed: 4`，含 `三种未命中情形各自返回对应的失败种类` |
| 把 `NoRow` 吞成 `Active` | `Failed: 3`，含同一条 |
| `Enum.TryParse` 退回旧的「非 Recycled 全当 Active」形态 | `Failed: 3`，含同一条 |
| 恢复活跃记录不分型（回落 `VersionConflict`） | `Failed: 3`，含 `恢复活跃记录返回不在回收站` |
| 恢复路径的 `WHERE` 把 `IsDeleted=1` 改成 `=0`（不再要求目标是删除态） | `Failed: 4`，两条恢复用例都红 |
| `NotInTrash` 直接返回 `RecordInTrash()`（两个 code 合并） | `Failed: 1`，精确红 `恢复活跃记录返回不在回收站` |

账目：`PersistenceTests` 36/36、集成 44、单元 33、架构 1，四个退出码全 0，残库 `pzd_test%` 为 0。这条分支只改 `PersistenceErrors.cs`、`PersistenceWriter.cs`、`PersistenceTests.cs` 与本文件、06 票面——没碰 `tests/Pzd.IntegrationTests/Fixtures/**`，也没碰 03 的 `vite.config.ts` 与根 `README.md`。

## 4. 下游各引哪一段

| 票 | 引用什么 | 不要做什么 |
|---|---|---|
| **07** 多租户解析 | 租户谓词 **已在 06 内**：写入口 WHERE 自带 `AND TenantId=@tenantId`，租户上下文缺失时抛 `InvalidOperationException` 而非按 Id 更新 | 不重做谓词；07 只补跨租户的红用例与解析链路本身 |
| **08** UoW + Outbox | 提交路径必须经 `PersistenceWriter` 的四个入口 | 不自建 `Updateable`/`Insertable`，也不绕过写入口拼 SQL |
| **09** 异常映射 | §3 的五种 Kind 与 code 逐字对齐（票面 `698e4da` 已写五种，代码已在 `fix/06-misskind-order` 落地） | 不把 `RecordInTrash` 压成并发冲突，不把 `NotFound` 压成 409，三种 409 不许共用文案 |
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

1. **~~`RestoreAsync` 去实体自报守卫 + 新增 `NotInTrash`~~ 已办（`fix/06-misskind-order`）**：§3 与矩阵已按实际代码重核，第 5、6 行已改成「恢复成功」「`NotInTrash`」，新增第 7 行「已删但版本不符 → `VersionConflict`」。判据两条用例 `DTO形态恢复已删记录应成功`、`恢复活跃记录返回不在回收站` 都已入册，另加 `三种未命中情形各自返回对应的失败种类`。「09 只接返回值会冒 500」的风险随之消掉。
2. **`dotnet test` 本体仍未在普通终端跑过**：07/08 开工前，维护者补跑 `cd E:/pzd && dotnet test Pzd.slnx`，要看到 `PersistenceTests` 全绿（当前基线 36；整仓集成 44、单元 33、架构 1）。`总计: 0` 一律按未运行。
3. **~~`MissKind` 数字编码反序（07 开工前必修）~~ 已办（同分支）**：SQL 改成返回自描述字符串 `'NoRow'`/`'Recycled'`/`'Active'`，`Enum.TryParse` 接，数字编码与枚举顺序这层约定整个不存在了。07 的租户解析可以照旧碰 `ResolveMissAsync`，不必再记「别写成 `(MissKind)code`」。
4. ~~票面待补一句~~ **已办**：`DeletedAt` 必须可空已由雨哥补进 06 票面（`01fdb59`，建表约定节 `:100`，并在遗留里补了列类型），本文件 §6 也写进了正文，两处口径一致；`dotnet test` 与 `MissKind` 两条同样进了票面遗留。
5. 06 worktree `E:/pzd/.worktree/06`（`2c93cde`）在合入并确认后可清理，清理按[文件整理](../../.ai/规则/文件整理.md)走可恢复移动。

## 8. 接手先核对什么

```bash
cd E:/pzd
git log --oneline -2                     # 07 开工前应能看到 06 合并 7a412fe 与 MissKind 分支的合入提交
git status --porcelain                   # 应为空
dotnet build Pzd.slnx                    # 0 警告 0 错误
./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe   # Total: 44, Failed: 0
```
