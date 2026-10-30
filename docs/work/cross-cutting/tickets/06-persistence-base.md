状态：进行中
主写入者：PZD-实现Bot-2
更新日期：2026-10-30 09:40 Asia/Shanghai
下一步：实现持久化约定并写 PersistenceTests；完成后交李总复跑
交付状态：待收尾
实施条件：已具备（05 签名冻结并合入 `8116b32`；36 fixture 合入 `869e745`，外部真库账号 `pzd_dev` 可用）

# 06 持久化约定：映射 / 审计字段 / 软删除与唯一索引 / 并发 / UTC

## 目标

把数据库访问的约定一次定型：映射集中在 Infrastructure、雪花主键对外字符串、审计字段自动填充、软删除全局过滤且不挡同名重建、乐观并发 409、唯一索引含 TenantId、时间入库 UTC。

## 需求依据

- [architecture.md](../../../architecture.md) §5.2、§5.3
- [spec.md](../spec.md) AC-14（出口在 08 号票）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.2

## 范围与非范围

- 做：`src/Pzd.Infrastructure`：SqlSugarScope 注册（仅 master）、`ConfigureExternalServices.EntityService` 集中映射、雪花主键与序列化策略、审计字段 AOP、软删除 + QueryFilter、`Version` 乐观并发、**软删除实体的唯一索引方案**（删除后必须能用同一业务键重建）。
- 不做：不做租户连接管理（07）、不做 UoW/Outbox（08）、不写业务表、不建集成测试工程与 fixture（36 号票）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/**`、`tests/Pzd.IntegrationTests/PersistenceTests.cs`。不碰 `src/Pzd.SharedKernel`、不碰 `tests/Pzd.IntegrationTests/Fixtures/**`（36 号票的）。

## 关键接口与依赖

- 前置票：05、36（05 只需基类签名冻结并合入，不等 05 其余部分）
- 对外契约：Domain 禁止出现 ORM 特性（由 13 号票断言）；对外主键一律字符串；**唯一索引方案被 17、19、20、23、32 号票的建表 SQL 复用，定型后写进票内**。

## 步骤

1. 注册 SqlSugarScope 与映射配置，确认 Domain 无 `[SugarColumn]`。
2. 实现审计字段 AOP、软删除 QueryFilter、并发版本校验。
3. **定软删除唯一索引方案并写进票内**：可空 `DeletedToken`（删除时写入主键、未删除为 NULL）参与唯一索引，或等价方案；明确禁止用「去掉唯一索引」换取同名可重建。
4. 时间约定：入库强制 UTC；Infrastructure **不得**在读取环节改时区语义，出参转换统一交给 API 序列化层。
5. 用 36 号票的 fixture 写集成测试（类名 `PersistenceTests`）：审计字段自动填充、软删除默认不可见、软删除后同名可重建、并发冲突与 1062 可识别、读回时间与写入的 UTC 值一致。
6. 贴命令、通过数（>0）与关键输出行。

## 验收标准

- [x] 新增实体自动带 `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy`。
- [x] 软删除记录默认查询不可见，绕过过滤需显式调用（`ClearFilter()`）。
- [x] 软删除后能用同一业务键（`UserName`）重建成功，有可执行断言证明；唯一索引方案见下节，没有删除唯一索引。
- [x] 并发更新冲突可被识别为 409 的来源错误（`Persistence.Record.VersionConflict`）。
- [x] MySQL 1062 唯一键冲突可被识别为 409 的来源错误（`Persistence.Record.DuplicateKey`）。
- [x] 直接读回的时间与写入的 UTC 值一致；Infrastructure 未做时区转换。
- [x] 证据贴出 `PersistenceTests` 的实际通过数：**31 个用例，0 失败**（6 条基础约定 + 9 条首轮返工 + 3 条命名约定 Theory + 7 条重审返工 + 2 条旁路加固 + 2 条四审返工 + 2 条五审返工）。

## 对外契约（下游模块必须遵守）

业务代码**只允许** `Queryable` 读、`PersistenceWriter` 写：

| 允许 | 禁止 |
|---|---|
| `db.Queryable<T>()` 查询（含 `ClearFilter()` 看回收站） | `db.Insertable(...)` —— 用 `PersistenceWriter.InsertAsync` |
| `PersistenceWriter.InsertAsync` / `UpdateAsync` / `SoftDeleteAsync` / `RestoreAsync` | `db.Updateable(...)`（含 `.SetColumns(...)`）—— 用 `UpdateAsync` |
| `db.Ado.ExecuteCommandAsync`（仅限 Infrastructure 内部与 `db/` 版本化 SQL） | `db.Deleteable(...)` —— 用 `SoftDeleteAsync`；物理清理需在 `PersistenceOptions.HardDeleteTypes` 登记 |

**落地闸门**：13 号票的静态守卫合入之前，任何模块**不得**编写写路径代码（`Insertable`/`Updateable`/`Deleteable`）。原因是 `db.Deleteable<T>().Where(...)` 这类不带实体的形态 AOP 钩不到，实测仍能一路物理删（`物理行数=0`）；眼下「运行期 + 静态」两层网里，静态那层还没织好，只剩运行期一层。07/08 若在此之前开工，写路径必须先等 13。

理由：AOP 只能拦 `DeleteByObject` 这类带实体的形态；`db.Deleteable<T>().Where(...)` 一路物理删，`db.Updateable(entity)` 会把 `Version` 写成 0、抹掉创建审计、把整行搬到别的租户（06 重审 high 1–5 实测）。靠约定守不住，所以两层网：**本契约 + 13 号票的静态守卫**（`src/` 里除 `src/Pzd.Infrastructure/**` 外出现 `Updateable`/`Deleteable`/`Insertable` 即红）。

运行期兜底：`db.Updateable(...)` 被 AOP **无条件**拒绝（写入口走 `Ado.ExecuteCommand`，不经过 AOP，所以不需要「版本对了就放行」的折中）；`db.Deleteable(entity)` 因未登记白名单被拒绝。`db.Deleteable<T>().Where(...)` 这类不带实体的形态 AOP 钩不到，**只有 13 号票的静态守卫能拦**——这是已知缺口，不是许可。

## 定型的软删除唯一索引方案（供 17、19、20、23、32 号票复用）

**列**：`DeletedToken BIGINT NOT NULL DEFAULT 0`（不是可空列）。

**取值**：活记录恒为 `0`；软删除时写入该行自身主键 `Id`。

**索引**：`UNIQUE KEY (TenantId, <业务键>, DeletedToken)`，例如
`UNIQUE KEY ux_identity_user_tenant_name (TenantId, UserName, DeletedToken)`。

```sql
CREATE TABLE test_user (
  Id BIGINT NOT NULL PRIMARY KEY,
  TenantId BIGINT NOT NULL,
  UserName VARCHAR(64) NOT NULL,
  Version INT NOT NULL,
  IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
  DeletedToken BIGINT NOT NULL DEFAULT 0,
  DeletedAt DATETIME(6) NULL,
  DeletedBy BIGINT NOT NULL DEFAULT 0,
  CreatedAt DATETIME(6) NOT NULL, CreatedBy BIGINT NOT NULL,
  UpdatedAt DATETIME(6) NOT NULL, UpdatedBy BIGINT NOT NULL,
  UNIQUE KEY ux_test_user_tenant_name (TenantId, UserName, DeletedToken)
);
```

> 本节 SQL 与 `tests/Pzd.IntegrationTests/PersistenceTests.cs` 的 `SchemaSql` **必须逐字同形**（除表名与业务键外）。17 号票写 master 建表 SQL 时拿这两个做对照，不一致就先改这里。

**为什么不按票面建议用可空列 + NULL**：MySQL 的唯一索引允许多个 NULL，未删除写 NULL 时两条活跃同名记录都能插入，唯一约束等于没有；`0` 是能参加比较的哨兵值，活跃记录最多一条、已删记录彼此用主键区分。

**建表时必须一起带的约定**：主键 `BIGINT`（雪花，对外序列化成字符串）、`Version INT NOT NULL`、审计四列、时间列用 `DATETIME(6)`（微秒精度，对齐 .NET 侧截断），以及软删除四列——`IsDeleted`、`DeletedToken`（唯一索引机制）、`DeletedAt`（回收站排序）、`DeletedBy`（谁删的）。后两列是审计字段，不是可选项：缺了它们 `SoftDeleteAsync` 会报 `Unknown column`，而看报错的人会误以为是 ORM 映射问题。

## 验证入口

```bash
dotnet build Pzd.slnx
dotnet test tests/Pzd.IntegrationTests --filter PersistenceTests
# 本机执行环境跑不了 dotnet test 本体时，用替代方式（见下文「环境」）：
# tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe -class Pzd.IntegrationTests.PersistenceTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 交接（实现 → 测试）

**1. 目标**：证明持久化约定真的生效——审计字段自动填、软删除默认不可见且同名可重建、并发与 1062 都能识别成 409 来源错误、时间读写不多不少。

**2. 提交**

- 分支：`feat/06-persistence-base`
- 认领提交：`555a817596d214d19e6ad7099f1a906a3ca89be7`
- 首次实现：`91dabf438b9dcde3ca26b6231e1daeb042b81163`（审计 run-03 结论「需修改」）
- 首轮返工：`213753a79dab06b0b3fcfa0cc38d76d2681a9e4c`
- 第二轮返工：`dddcb5806a884ffabe9ad225665122ac1026de11`
- 审计返工第三轮（被测 SHA）：`21ce52f4097b8ad79875d7c5f794d5c032ebab3f`（代码、决策笔记与票面在同一个提交，`Bot分工 §6`）
- 审计返工第四轮（被测 SHA）：`4c45fba22744c0d196fbbc80016c64f42ce3a1d8`（同上，与决策笔记同一提交）
- 审计返工第五轮（被测 SHA）：`735de2445c50dc76f37458569f180b6ed7c661f9`（同上，与决策笔记同一提交）

**3. 验证**（命令、真实退出码、关键输出行，原样粘贴）

构建：

```
$ dotnet build Pzd.slnx
    0 个警告
    0 个错误
已用时间 00:00:01.43
```

`PersistenceTests`（本机 `dotnet test` 本体受执行环境限制，用直跑测试产物的替代方式，`-class` 与类名字面一致）：

```
$ ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe -class Pzd.IntegrationTests.PersistenceTests
=== TEST EXECUTION SUMMARY ===
   Pzd.IntegrationTests  Total: 31, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 11.638s
```

第一层钉约定本身：`插入后审计字段自动填充且版本从1开始`、`软删除记录默认查询不可见且可显式绕过过滤`、`软删除后能用同一业务键重建`、`并发更新冲突被识别为409来源错误`、`唯一键1062被识别为409来源错误`、`读回的时间与写入的UTC值一致`。

第二层是审计 run-03 的返工用例：`分离实体更新不抹掉创建审计`、`乙租户拿着同Id和版本也改不到甲租户的记录`、`租户上下文缺失时写入口抛异常而不是按Id更新`、`物理删除被拒绝不让回收站失效`、`软删除入口写全四列且同名可重建`、`SetColumns形态的旁路也被拒绝`、`版本冲突时内存实体不被改写`、`配置的worker与机房号真的被采纳`、`未配置worker时给出启动期警告`、`表名按snake_case分段`。

整套回归（确认没动坏 36 的载体与 05 的单元测试）：

```
$ ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe
   Pzd.IntegrationTests  Total: 39, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 11.670s
$ ./tests/Pzd.SharedKernel.UnitTests/bin/Debug/net10.0/Pzd.SharedKernel.UnitTests.exe
   Pzd.SharedKernel.UnitTests  Total: 33, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.110s
$ ./tests/Pzd.ArchitectureTests/bin/Debug/net10.0/Pzd.ArchitectureTests.exe
   Pzd.ArchitectureTests  Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.106s
```

**4. 返工（审计 run-03：3 high + 2 medium，全部测试先行）**

先让用例变红（补齐被测 API 的表面、保留旧行为），拿到 `Total: 15, Failed: 8`，再改实现到绿。

| 审计条目 | 实证与改法 | 用例 |
|---|---|---|
| high 1 分离实体抹掉创建审计 | `CreatedAt/CreatedBy` 与 `TenantId` 一起列为不可写列，SET 里永不出现 | `分离实体更新不抹掉创建审计` |
| high 2 写入口无租户谓词 | WHERE 加 `AND TenantId=@tenantId`；实体有租户列而 `CurrentTenantId()` 为 null 时抛 `InvalidOperationException`，不退回按 Id 更新 | `乙租户拿着同Id和版本也改不到甲租户的记录`、`租户上下文缺失时写入口抛异常而不是按Id更新` |
| high 3 软删除无强制点 | 新增 `PersistenceWriter.SoftDeleteAsync` 唯一入口；AOP 在 `DeleteByObject` 抛异常拦物理删除（`SetColumns` 形态当轮只能用「用例钉死代价」，第三轮起也被 `UpdateByObject` 的无条件拒绝挡住） | `物理删除被拒绝不让回收站失效`、`软删除入口写全四列且同名可重建`、`SetColumns形态的旁路也被拒绝` |
| medium 2 worker 写死 | `PersistenceOptions.WorkerId/DatacenterId`，两者都给才新建 `IdWorker`；未配置时 `OnWarning` 告警 | `配置的worker与机房号真的被采纳`、`未配置worker时给出启动期警告` |
| medium 3 冲突污染实体 | `ComputeUpdateStamp` 只算不改，SQL 影响 1 行后才回写 | `版本冲突时内存实体不被改写` |
| low 2 死分支 | `VersionExceptions` 分支保留但注释写明「仅防御外部写路径」 | 无（不改行为） |
| low 5 `a_p_i_log` | 连续大写算一个词（`APILogProbe → api_log_probe`） | `表名按snake_case分段` |

张一一裁定的两项照办了：租户谓词留在 06（07 只补跨租户红用例）；`DeletedToken` 保留给唯一索引，另按 §5.3 补 `DeletedAt DATETIME(6) NULL` 与 `DeletedBy BIGINT NOT NULL DEFAULT 0`，`SoftDeleteAsync` 与 AOP 的 `UpdateByObject` 两条路径都会写。

**变异验证（六处，每个都精确红一条）**：

| 变异 | 抓到它的用例 | 结果 |
|---|---|---|
| 去掉租户谓词 | `乙租户拿着同Id和版本也改不到甲租户的记录` | `Failed: 1`，还原后绿 |
| `CreatedAt/CreatedBy` 进 SET | `分离实体更新不抹掉创建审计` | `Failed: 1`，还原后绿 |
| 不拦 `DeleteByObject` | `物理删除被拒绝不让回收站失效` | `Failed: 1`，还原后绿 |
| 软删除不写 `DeletedAt/DeletedBy` | `软删除入口写全四列且同名可重建` | `Failed: 1`，还原后绿 |
| 冲突前就落实体 | `版本冲突时内存实体不被改写` | `Failed: 1`，还原后绿 |
| 忽略 worker 配置 | `配置的worker与机房号真的被采纳` | `Failed: 1`，还原后绿 |

**5. 返工第二轮（审计 run-04 重审：5 high + 2 medium + 2 low）**

老登把李总那 5 条旁路盲区全部独立复现后升级为 high，结论从「1 high + 4 medium」改成「5 high + 2 medium + 2 low」。红先：`Total: 24, Failed: 6`，再改到绿。

| 审计条目 | 改法 | 用例 |
|---|---|---|
| high 1 定型 DDL 缺两列 | 票面定型 SQL 补 `DeletedAt/DeletedBy`，注明「与 `SchemaSql` 逐字同形」 | 无（文档） |
| high 2（medium 1）`markDeleted`/`deleting` 混用 | 统一成一个语义：`WriteKind`（Update/SoftDelete/Restore）算一个布尔，`ValueFor` 与 `ApplyUpdateStamp` 都用它，不再出现两套判断 | `UpdateAsync带IsDeleted时也写全删除审计` |
| high 3（medium 2）回收站恢复破唯一索引 | 新增 `PersistenceWriter.RestoreAsync`：`DeletedToken` 归 0、`DeletedAt/DeletedBy` 清空，撞唯一索引时返回 409 `DuplicateKey` | `软删除后能恢复且同名可再用`、`同名记录已占用时恢复被拒绝` |
| high 4（李总 P4/P5）`Updateable`/`Deleteable` 旁路 | 新增 `InsertAsync` 唯一插入入口；AOP 对旁路更新的拦截口径两度收紧：**先只拦 `Version<=0`，被三返复验 P5a 推翻**（「加载→改→保存」的实体版本本来就对，正好穿过去），第三轮起改为**无条件拒绝 `UpdateByObject`**——现行口径以「返工第三轮」为准 | `绕过写入口的Updateable被拒且版本不会被写成0`、`带正确版本的Updateable同样被拒绝`、`SetColumns形态的旁路也被拒绝` |
| high 5（李总 P1/P2/medium 3）插入无租户校验 | `OnInsert` 补租户：实体未填按上下文填充，不一致抛异常，无上下文且未填也抛（不留 `TenantId=0` 幽灵行） | `插入时实体租户与上下文不一致被拒绝`、`插入未填租户时按上下文填充` |
| medium 4 验收条数过期 | 6 → 25，并写清构成 | 无（文档） |
| low 1 物理删除无白名单 | `PersistenceOptions.HardDeleteTypes`，为 08 的 Outbox 清理留显式豁免 | `白名单元组允许物理删除` |

老登另加两条也照办：①两条示范性用例（`软删除记录默认查询不可见`、`软删除后能用同一业务键重建`）从 `db.Updateable` 改走 `SoftDeleteAsync`；②票面新增「对外契约」节，写死「业务代码只允许 `Queryable` 读、`PersistenceWriter` 写」。

**变异验证（五处，四处精确红一条，一处红六条）**：

| 变异 | 结果 |
|---|---|
| `ValueFor` 退回到「只有 `SoftDeleteAsync` 才写删除审计」 | `Failed: 6`（整组删除相关用例全红，说明这条语义被多条用例守着） |
| 恢复不清 `DeletedToken` | `Failed: 2`，两条恢复用例 |
| 去掉插入侧租户校验 | `Failed: 2`，两条插入用例 |
| 去掉旁路 `Version` 防御 | `Failed: 2`，两条旁路用例 |
| 物理删除白名单恒放行 | `Failed: 1`，`物理删除被拒绝不让回收站失效` |

记一笔脚本缺陷：白名单那条变异我第一次写成 `if (false)`，触发 CS0162 不可达代码、在「警告即错误」下构建失败；按群规「构建失败判失败、不算红」重做成恒返回 true，才拿到真红。

**6. 返工第三轮（三返复验：P5a + P6）**

李总在我自报「25/25 全绿」之后又压了 8 条探针，剩 2 条真缺口。红先：两条新用例写好即红，改到绿后 `Total: 27, Failed: 0`。

| 问题 | 改法 | 用例 |
|---|---|---|
| P5a `Version>0` 的 `db.Updateable` 仍能把整行搬到别的租户 | 上一版只拦 `Version<=0`，而「加载→改→保存」的实体版本本来就是对的，正好穿过去。改成对 `UpdateByObject` **无条件拒绝**；写入口走 `Ado.ExecuteCommandAsync`，不经过 AOP，所以没有误伤 | `带正确版本的Updateable同样被拒绝` |
| P6 `UpdateAsync` 能把回收站记录改回活跃 | Update 种类下若 `DeletedToken != 0` 直接拒绝，要求走 `RestoreAsync`；否则 token 会带着主键写回，唯一索引失效、同名活跃记录可有两条 | `UpdateAsync不能把回收站记录改回活跃` |

**`InsertAsync` 的类型约束**：签名是 `where T : class, new()`（SqlSugar 的 `Insertable<T>` 要求公共无参构造）。聚合根若只有带参构造，07/08 接进来前要先补一个，否则编译不过。

**变异验证（两处）**：

| 变异 | 结果 |
|---|---|
| AOP 退回「只拦 `Version<=0`」 | `Failed: 3`，含 `带正确版本的Updateable同样被拒绝` |
| 去掉回收站误用校验 | `Failed: 1`，`UpdateAsync不能把回收站记录改回活跃` |

**6b. 返工第四轮（审计四审：2 medium + 3 low）**

老登四审用他自己的探针（C2/C5/C6）确认前两轮两条 high 已闭环，又挖出两条同一类问题：**两个合法入口各自都正常，接起来却造出非法状态**。红先：三条用例写好即红，`Total: 29, Failed: 3`，改到绿。

| 审计条目 | 改法 | 用例 |
|---|---|---|
| medium 1 官方插入入口能造「删除态但 `DeletedToken=0`」的幽灵行（C11） | `OnInsert` 遇 `IsDeleted=true` 直接抛并提示「先 `InsertAsync` 再 `SoftDeleteAsync`」；同时把 `IsDeleted` 归 false、`DeletedAt` 清空、`DeletedBy` 归 0——插入入口只写活跃记录 | `插入入口拒绝删除态实体` |
| medium 2 `UpdateAsync` 能把 `token=0` 的删除行改回活跃（C9），并把删除审计刷成当前时间（C10） | `WriteKind.Update` 下**一个删除列都不写**（`IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy`），且删除态实体直接拒绝；翻转只由 `SoftDeleteAsync`/`RestoreAsync` 负责。原用例 `UpdateAsync带IsDeleted时也写全删除审计` 改写为 `UpdateAsync带IsDeleted时被拒绝`——不为保住绿给 `Update` 留写 `IsDeleted` 的口子 | `UpdateAsync带IsDeleted时被拒绝`、`UpdateAsync不改动任何删除审计列` |
| low 1 `ReadBool` 残渣 | 删掉 `ReadLong(...) switch { _ => … }` 那段「先算一次再丢掉」的写法，直接读属性 | 无（由整套用例覆盖） |
| low 2 测试注释过期 | `SetColumns形态的旁路也被拒绝` 的注释不再说「连 `Version` 都是 0」，改成「不论 `Version` 正负一律拒绝，这里显式写了 `Version = 1` 同样被拒」 | 无（文档） |
| low 3 票面改法表旧口径 | 「返工第二轮」改法表的 `high 4` 那行注明口径沿革，并补上 `带正确版本的Updateable同样被拒绝` | 无（文档） |

**变异验证（三处，每处精确红一条）**：

| 变异 | 结果 |
|---|---|
| 去掉插入侧 `IsDeleted=true` 拒绝 | `Failed: 1`，`插入入口拒绝删除态实体` |
| `Update` 也写删除列 | `Failed: 1`，`UpdateAsync不改动任何删除审计列` |
| `Update` 不拒删除态实体（只留 `DeletedToken` 检查） | `Failed: 1`，`UpdateAsync带IsDeleted时被拒绝` |

**6c. 返工第五轮（审计五审：1 medium + 1 low）**

老登五审确认四审那 5 条全部闭环（P1/P2/P3 + 三条 low + 正路 P5/P7 无回归），又挖出一条**反着打**的 medium：前几轮堵的都是「加载回来的实体带着删除标记 → 被内存守卫拦下」，而 DTO 从 HTTP 请求体转回来的实体**不带标记**，两道内存守卫都不触发，`WHERE` 又不带活跃条件——写入成功、版本 +1、业务字段被改，库里那行仍是删除态，应用侧根本读不到。红先：两条用例（含改写的一条）写好即红，`Total: 30, Failed: 2`，改到绿。

| 审计条目 | 改法 | 用例 |
|---|---|---|
| medium `UpdateAsync` 认实体不认库，脱离实体能往已删除的记录上静默写入（P8） | `WHERE` 加 `AND IsDeleted=0`（恢复反过来加 `=1`），让**库**判定活跃态；`affected == 0` 时再按主键 + 租户查一次 `IsDeleted=1`，命中就抛「记录在回收站，请走 `RestoreAsync`」，不再回落成 `VersionConflict` | `脱离实体更新已删除记录被拒绝` |
| low 成功路径把没写进库的删除四列套回内存实体（P4） | `ApplyUpdateStamp` 增加 `touchDeleteColumns`，`Update` 种类连内存实体都不碰删除四列。张一一裁定**不擦、也不回查库**：正常路径上下载下来的实体本来就带活跃态常量，不碰即一致；只有调用方自己塞了脏值时才不一致，那是调用方的事 | `UpdateAsync不改动内存实体的删除审计列` |

**三种情形的表达口径**（09 需要分三个 `code`/状态码，张一一已写进 09 票 `7fa5f54`）：

| 情形 | `UpdateAsync` 的返回/抛出 |
|---|---|
| 版本已被别人改掉 | 返回 `Persistence.Record.VersionConflict` |
| 记录不存在，或不属于当前租户 | 返回 `Persistence.Record.VersionConflict`（`affected == 0` 且查不到回收站行） |
| 记录在回收站 | **抛** `InvalidOperationException`，文案指向 `RestoreAsync` |

**变异验证（三处，每处精确红一条）**：

| 变异 | 结果 |
|---|---|
| `Update`/`SoftDelete` 的 `WHERE` 不再带活跃条件 | `Failed: 1`，`脱离实体更新已删除记录被拒绝` |
| `affected == 0` 不分型（回落 `VersionConflict`） | `Failed: 1`，同上（拿到的是返回值而不是异常） |
| `Update` 也回写删除列（按活跃态） | `Failed: 1`，`UpdateAsync不改动内存实体的删除审计列` |

记一笔脚本缺陷：前两处变异第一次分别写成 `if (false)`（CS0162 不可达）和删掉 `isDeletedColumn is not null`（CS8604 可空），都在「警告即错误」下构建失败；按群规「构建失败判失败、不算红」重做成 `&& kind == WriteKind.Restore` 与 `&& currentTenantId is null`，才拿到真红。

**7. 遗留**

- **`dotnet test` 本体未运行**：本机执行环境跑不了 MTP 命名管道握手（36 已留证），本次用「直跑测试产物 + `-class` 过滤」替代，输出 `Total: 31, Failed: 0`。需在无管道限制的环境补跑一次 `dotnet test tests/Pzd.IntegrationTests --filter PersistenceTests`，看到 `总计: 31` 才算闭环；看到 `总计: 0` 一律按未运行处理。
- **`SetColumns` 形态已被运行期拒绝，但静态守卫仍必需**：`db.Updateable<T>().SetColumns(...)` 现在会被 `UpdateByObject` 的无条件拦截拒绝（用例 `SetColumns形态的旁路也被拒绝`），最初「漏写 `DeletedToken` → 同名重建 1062」的那条代价路径已经走不到了。注意这不是「可以不带守卫」的理由——AOP 钩不到的 `db.Deleteable<T>().Where(...)` 仍在，13 号票的静态守卫照旧必需。
- **存量 `Version = 0` 的行在运行期不可修复**（三审要求留痕）：本票之前几个版本里，`db.Updateable(entity)` 会把实体上的默认 `Version` 原样写回，产生 `Version=0` 的行。这类行现在**任何入口都修不了**——AOP 拒绝 `Updateable`，`PersistenceWriter` 拿 0 去比永远返回 `VersionConflict`（09 会映射成 409），排查时看起来像并发冲突。排查与修复只能用运维裸 SQL：

  ```sql
  -- 排查（每个带 Version 的表都查一遍）
  SELECT Id, TenantId, Version, UpdatedAt FROM <table> WHERE Version <= 0;
  -- 修复：把 Version 置回可用的当前值（通常是 1，或按业务历史值 +1）
  UPDATE <table> SET Version = 1 WHERE Id = <id>;
  ```

  这轮之后新数据写不出这种行，但历史库（如果有）必须按上面捞一遍。
- **`CurrentTenantId` 必须由 07 供给**：有 `TenantId` 列的类型在拿不到租户上下文时写不进去（这是设计选择，不是缺陷）。07 接租户解析时要把 `PersistenceOptions.CurrentTenantId` 填上，否则所有写路径会在运行期抛 `InvalidOperationException`。
- **`DeletedAt/DeletedBy` 是冻结前补的**：按张一一裁定与架构 §5.3 对齐，`DeletedToken` 只承担唯一索引机制，回收站排序与「谁删的」用 `DeletedAt/DeletedBy`。17 号票写 master 建表 SQL 时这四列都要有。
- **并发不走 SqlSugar 自带开关**：实测 `IsEnableUpdateVersionValidation`（列配置与链式方法都试过）在 `Updateable(entity)` 路径上生成的 SQL 没有版本列，第二次并发更新照样成功；因此并发写入口是 `PersistenceWriter.UpdateAsync`。SQL 证据与推理写在决策笔记里。
- **超出票面范围的 3 个文件**：`Directory.Packages.props`（加 `SqlSugarCore 5.1.4.221` 与顶掉漏洞版本的 `System.Security.Cryptography.Pkcs 10.0.14`）、`tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（加指向 `Pzd.Infrastructure` 的 `ProjectReference`，并改掉 36「绝不引用任何 Pzd.* 项目」的注释）、`Pzd.slnx`（挂新工程）。三者都是本票绕不开的，已在群里报备。
- **SqlSugar 的传递依赖带漏洞**：`System.Security.Cryptography.Pkcs 8.0.1` 触发 `NU1902/NU1903`，在「警告即错误」下直接构建失败，已在 Infrastructure 里直接引用 10.0.14 顶掉，代码不使用该包。
- **表名约定不做复数化**：`TestUser → test_user`。17 号票写 master 建表 SQL 时必须沿用 snake_case，冲突就改约定并在 `PersistenceConventions` 一处调整。
- **`SoftDeleteTypes` 要手工登记**：模块注册时漏登记等于软删除过滤不生效；08 号票接 UoW 时应把登记收进模块注册入口，别散落在各处。
- **未做的部分**：`Id` 对外序列化成字符串由 API 序列化层负责（不在本票）；`Version` 冲突转 ProblemDetails 由 09 号票；租户库切换由 07 号票；本票只按单库单连接验证。

## 恢复方式

删除 `src/Pzd.Infrastructure/`、`tests/Pzd.IntegrationTests/PersistenceTests.cs`，并从 `Pzd.slnx`、`Directory.Packages.props`、测试工程 csproj 回退三处改动即可；测试库为临时库，用例结束自动删除。

## 任务映射

- worktree：`.worktree/06/`
- 分支：`feat/06-persistence-base`
- 提交：认领 `555a817596d214d19e6ad7099f1a906a3ca89be7`；首次实现 `91dabf438b9dcde3ca26b6231e1daeb042b81163`；审计返工 `213753a79dab06b0b3fcfa0cc38d76d2681a9e4c`、第二轮 `dddcb5806a884ffabe9ad225665122ac1026de11`、第三轮 `21ce52f4097b8ad79875d7c5f794d5c032ebab3f`、第四轮 `4c45fba22744c0d196fbbc80016c64f42ce3a1d8`、第五轮 `735de2445c50dc76f37458569f180b6ed7c661f9`（均与决策笔记同一提交）
