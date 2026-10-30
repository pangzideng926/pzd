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
- [x] 证据贴出 `PersistenceTests` 的实际通过数：**6 个用例，0 失败**。

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
  CreatedAt DATETIME(6) NOT NULL, CreatedBy BIGINT NOT NULL,
  UpdatedAt DATETIME(6) NOT NULL, UpdatedBy BIGINT NOT NULL,
  UNIQUE KEY ux_test_user_tenant_name (TenantId, UserName, DeletedToken)
);
```

**为什么不按票面建议用可空列 + NULL**：MySQL 的唯一索引允许多个 NULL，未删除写 NULL 时两条活跃同名记录都能插入，唯一约束等于没有；`0` 是能参加比较的哨兵值，活跃记录最多一条、已删记录彼此用主键区分。

**建表时必须一起带的约定**：主键 `BIGINT`（雪花，对外序列化成字符串）、`Version INT NOT NULL`、审计四列、时间列用 `DATETIME(6)`（微秒精度，对齐 .NET 侧截断）。

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
- 审计返工（被测 SHA）：`213753a79dab06b0b3fcfa0cc38d76d2681a9e4c`（代码、决策笔记与票面返工节在同一个提交，`Bot分工 §6`）

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
   Pzd.IntegrationTests  Total: 18, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 11.050s
```

第一层钉约定本身：`插入后审计字段自动填充且版本从1开始`、`软删除记录默认查询不可见且可显式绕过过滤`、`软删除后能用同一业务键重建`、`并发更新冲突被识别为409来源错误`、`唯一键1062被识别为409来源错误`、`读回的时间与写入的UTC值一致`。

第二层是审计 run-03 的返工用例：`分离实体更新不抹掉创建审计`、`乙租户拿着同Id和版本也改不到甲租户的记录`、`租户上下文缺失时写入口抛异常而不是按Id更新`、`物理删除被拒绝不让回收站失效`、`软删除入口写全四列且同名可重建`、`SetColumns形态不写DeletedToken是已知边界`、`版本冲突时内存实体不被改写`、`配置的worker与机房号真的被采纳`、`未配置worker时给出启动期警告`、`表名按snake_case分段`。

整套回归（确认没动坏 36 的载体与 05 的单元测试）：

```
$ ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe
   Pzd.IntegrationTests  Total: 26, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 10.869s
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
| high 3 软删除无强制点 | 新增 `PersistenceWriter.SoftDeleteAsync` 唯一入口；AOP 在 `DeleteByObject` 抛异常拦物理删除；`SetColumns` 形态的代价用用例钉死 | `物理删除被拒绝不让回收站失效`、`软删除入口写全四列且同名可重建`、`SetColumns形态不写DeletedToken是已知边界` |
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

**5. 遗留**

- **`dotnet test` 本体未运行**：本机执行环境跑不了 MTP 命名管道握手（36 已留证），本次用「直跑测试产物 + `-class` 过滤」替代，输出 `Total: 18, Failed: 0`。需在无管道限制的环境补跑一次 `dotnet test tests/Pzd.IntegrationTests --filter PersistenceTests`，看到 `总计: 18` 才算闭环；看到 `总计: 0` 一律按未运行处理。
- **`SetColumns` 形态仍是漏网的手写 SQL**：`db.Updateable<T>().SetColumns(it => new T { IsDeleted = true })` 不被拦截、也不会写 `DeletedToken`，代价已写成用例（同名重建 1062）。要堵死得靠 13 号票的静态守卫（禁止业务代码出现 `Deleteable`/`SetColumns` 写软删除列），集成测试拦不住。
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
- 提交：认领 `555a817596d214d19e6ad7099f1a906a3ca89be7`；首次实现 `91dabf438b9dcde3ca26b6231e1daeb042b81163`；审计返工 `213753a79dab06b0b3fcfa0cc38d76d2681a9e4c`（与决策笔记同一提交）
