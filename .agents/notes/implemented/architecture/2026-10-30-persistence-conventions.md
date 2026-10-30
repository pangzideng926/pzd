# Agent Note: 持久化约定由属性名定schema，并发与软删除各用一条显式规则

Status: implemented

## Problem

[architecture.md](../../../../docs/architecture.md) §5.2 要求「领域层零 ORM 特性、映射集中在 Infrastructure」，§5.3 又要求审计字段自动填充、软删除全局过滤、`Version` 乐观并发、唯一索引带 `TenantId`。落到 SqlSugar 上，有四处会互相顶住：

- **软删除过滤要按实体类型挂**，但 Domain 不能引用 Infrastructure（§3.2），标记接口 `ISoftDeletable` 无处安放；SharedKernel 的签名已由 05 冻结。
- **软删除后同名可重建**要求唯一索引对已删记录放行，而 MySQL 唯一索引**对 NULL 不去重**——票面建议的「可空 `DeletedToken`，未删除为 NULL」会让两条活跃同名记录都能插进去，正好丢掉唯一约束。
- **`Version` 乐观并发**：SqlSugar 的 `IsEnableUpdateVersionValidation` 属性在 `Updateable(entity)` 路径上实测不生效（下面有 SQL 证据），冲突拦不住。
- **时间 UTC**：.NET 的 `DateTime` 精度 100ns、MySQL `DATETIME(6)` 只有微秒，写进去被截断，读回就和写出的值不相等。

## Decision

**1. 表名与列名用命名约定，Domain 一个 ORM 特性都不加。**
表名 = 实体类型名 snake_case（`TestUser → test_user`），列名 = 属性名。命名转换只在 `PersistenceConventions` 一处。

**2. 软删除过滤按类型登记，不用标记接口。**
`PersistenceOptions.SoftDeleteTypes` 列出要过滤的类型，`PersistenceSoftDelete` 用 `QueryFilter.AddTableFilter(Type, Expression, FilterJoinPosition.Where)` 挂 `it => it.IsDeleted == false`。要绕过用 `db.Queryable<T>().ClearFilter()`（回收站）。

**3. 软删除唯一索引用 0/Id 双态 `DeletedToken`，不是 NULL。**
活记录 `DeletedToken = 0`，删除时写入自身主键，唯一索引 `(TenantId, 业务键, DeletedToken)`：
同一租户下最多一条活记录（`0` 参与唯一比较），已删记录彼此用主键区分，所以同名重建可以成功。

**4. 乐观并发由 `PersistenceWriter.UpdateAsync` 自己拼 SQL。**
`UPDATE … SET … , Version=Version+1 WHERE Id=@id AND Version=@version`，影响 0 行即返回版本冲突（`Persistence.Record.VersionConflict`，409）。不用 SqlSugar 的属性开关。

**5. 审计时间入库前对齐到微秒，不做任何时区转换。**
`CreatedAt/UpdatedAt` 由 AOP 填 `DateTime.UtcNow` 截断到 1µs；读出转换留给 API 序列化层（§5.3）。

**6. 写入口带租户谓词，缺上下文就抛异常。**
`PersistenceWriter` 发出的 UPDATE 一律是 `WHERE Id=@id AND Version=@version AND TenantId=@tenant`。实体有 `TenantId` 列、而 `PersistenceOptions.CurrentTenantId()` 返回 `null` 时抛 `InvalidOperationException`，**不退回「按 Id 更新」**。`TenantId` 与 `CreatedAt/CreatedBy` 一起列进不可写列，SET 里永不出现——租户归属只能由 07 的租户解析决定，不能由实体自带的值决定。

**7. 软删除有唯一入口，物理删除被强制拦截。**
`PersistenceWriter.SoftDeleteAsync` 一次写 `IsDeleted/DeletedToken/DeletedAt/DeletedBy`，同样带版本与租户条件；AOP 在 `DataFilterType.DeleteByObject` 直接抛异常，让 `db.Deleteable(entity)` 立刻失败，而不是悄悄物理删、回收站再也恢复不了。
`SetColumns(it => new T { IsDeleted = true })` 这条写法当轮只能靠一条用例把代价钉死（`DeletedToken` 仍为 0 → 同名重建撞 1062）；第三轮起 `UpdateByObject` 改成**无条件拒绝**，它已经走不通，用例保留下来当回归（`SetColumns形态的旁路也被拒绝`）。

**8. 雪花 worker 必须来自部署配置，且不能靠改 `SnowFlakeSingle` 的静态字段。**
`WorkerId` 与 `DatacenterId` 都给了才新建 `IdWorker(workerId, datacenterId, 0)`；否则回退进程级单例并调 `OnWarning` 告警一次。
实测（SqlSugarCore 5.1.4.221）：`SnowFlakeSingle` 的 `WorkId`/`DatacenterId` 是静态字段，单例一旦被访问过就固定，之后改字段**不生效**——设置成 5/9 之后 `ReferenceEquals` 仍为 true、`WorkerId` 还是 1。所以只能新造实例，改单例是假的。

**9. 审计值先算后写，冲突不污染调用方实体。**
`ComputeUpdateStamp` 只计算结果、不动实体；SQL 影响 1 行之后才 `ApplyUpdateStamp` 回写。版本冲突时调用方手里的对象仍保持加载时的样子，可以直接重载重试，不会把错的时间戳带进下一次写。

**10. 写侧只留四个入口，业务代码不碰 `Insertable`/`Updateable`/`Deleteable`。**
`PersistenceWriter` 提供 `InsertAsync` / `UpdateAsync` / `SoftDeleteAsync` / `RestoreAsync`；读侧仍用 `Queryable`。
这四个方法的操作语义由一个 `WriteKind` 决定，审计值、租户谓词、版本条件都从同一个判断推出来——
上一版把「是否软删除」拆成两个布尔（`deleting` 与 `markDeleted`），算值用前者、拼 SET 用后者，
`UpdateAsync` 走软删除时 `DeletedAt/DeletedBy` 就被静默丢了（06 重审 high 2）。一个语义只能有一个布尔。

**11. 恢复（`RestoreAsync`）必须把 `DeletedToken` 归 0；删除态只能由软删除 / 恢复两个入口翻转。**
`UpdateAsync` 现在拒绝两种形态：`IsDeleted=true`，以及 `DeletedToken != 0`。
前一种是「用更新当删除」，写下去就成了 `IsDeleted=1` 而 `DeletedToken=0` 的幽灵行——应用里查不到、却占着唯一索引（06 四审 medium 1 造出来的正是这种行，medium 2 的 C9 再从这条行复活，两条互相喂）；
后一种是「活跃却占着已删 token」，唯一索引对它失效，同名活跃记录可以有两条（三返复验 P6）。
`WriteKind.Update` 干脆**一个删除列都不写**：`IsDeleted`/`DeletedToken`/`DeletedAt`/`DeletedBy` 都只能由 `SoftDeleteAsync`/`RestoreAsync` 改写，
否则实体上带着的旧 `DeletedAt/DeletedBy` 会被写回库，回收站按 `DeletedAt` 排序时那条被顶到最新（06 四审 C10）。
恢复时若把 `IsDeleted` 置回 false 而留着 `DeletedToken=Id`，唯一索引会认为已删记录仍占着「另一个 token」，
于是同名活跃记录可以有两条（06 重审 high 3 实测 `活跃同名记录=2`）。归 0 之后，业务键若已被新记录占用，
唯一索引自己会撞 1062，翻译成 409 `DuplicateKey`——「不能恢复」是有返回值的，不靠调用方自己查。

**12. 旁路给两层网：运行期兜底 + 静态守卫。**
运行期：`db.Deleteable(entity)` 被 `DeleteByObject` 拦（合法物理清理在 `PersistenceOptions.HardDeleteTypes` 登记）；
`db.Updateable(...)` 被 `UpdateByObject` **无条件**拒绝。这里走过两版：先按「`Version<=0` 才拒绝」，
但「加载→改→保存」的实体版本本来就对，正好穿过去把整行搬到别的租户（三返复验 P5a），
所以放弃「版本对了就放行」的折中——写入口走 `Ado.ExecuteCommandAsync`，根本不经过 AOP，无条件拦没有误伤。
静态：13 号票按命名空间断言 `src/` 里除 `src/Pzd.Infrastructure/**` 外不出现这三个方法。
票面另写了「对外契约」一节，因为静态守卫只覆盖 `src/`，新目录靠契约兜。

**13. 插入入口只写活跃记录，四个删除列由 AOP 强制归位。**
`OnInsert` 遇 `IsDeleted=true` 直接抛，`DeletedAt` 清空、`DeletedBy` 归 0、`DeletedToken` 归 0。
理由与第 11 条同源：插入是造新记录，删除态不是它的合法输入。放行的话会写出一行「删除态、四列删除审计全空」的记录——
软删除过滤器把它挡在查询外，唯一索引却仍认它，同名记录再也建不出来，调用方只看到 409「已存在」却找不到那条（06 四审 C11 实测 `物理行数=1`、`活跃记录数=0`）。
拒绝的文案要指第二条路（先 `InsertAsync` 再 `SoftDeleteAsync`），否则调用方会反复重试同一次插入。

## Alternatives considered

- **在 SharedKernel 加 `ISoftDeletable` 标记接口** — 最强理由：过滤器用泛型 `AddTableFilter<ISoftDeletable>` 一行搞定，类型安全。没选，是因为 05 的签名已冻结，加接口要重开 05 的审计；而且 Domain 实体为了实现持久化接口而改签名，等于把 ORM 关注点渗进领域层。
- **`DeletedToken` 可空、未删除写 NULL**（票面原建议）— 最强理由：语义直观，NULL 天然表示「没删」。没选，是因为 MySQL 唯一索引允许多个 NULL，两条活跃同名记录都能插入，唯一约束形同虚设；`0` 是能参加比较的哨兵值。
- **依赖 `IsEnableUpdateVersionValidation` 属性** — 最强理由：框架自带，少写 SQL。没选，是因为实测 SQL 里没有版本列：`UPDATE probe_user SET …,Version=@Version,… WHERE Id=@Id`，链式 `.IsEnableUpdateVersionValidation()` 也一样只发一条按主键的 UPDATE，第二次并发更新照样成功（影响 1 行）。
- **只写票面契约、不补运行期防御** — 最强理由：约定清楚就够，代码里少一层判断。没选，是因为 `db.Updateable(entity)` 把 `Version` 写成 0 这件事没有报错、没有日志，而后果是那一行永久锁死；运行期拒绝能让错误在第一次写错时就炸出来，成本只有一个分支。
- **`UpdateAsync` 顺带支持「置 `IsDeleted=true` 即软删除」** — 最强理由：调用方少记一个入口，改个字段就能删，兼容已经这样写的代码。没选，是因为它写出的正是 `IsDeleted=1 DeletedToken=0` 的幽灵行（占唯一索引、应用里查不到），并把 `DeletedAt/DeletedBy` 刷成当前时间、让回收站排序失真；为保住一条旧用例的绿而留这个口子，等于用用例换漏洞（06 四审 medium 2）。
- **软删除只提供唯一入口、不拦 `Deleteable`** — 最强理由：不改变 ORM 的默认行为，兼容别人已经写的删除代码。没选，是因为架构 §5.3 承诺「软删除 + 回收站可恢复」，物理删掉就永远恢复不了且毫无提示；拦截能让错误在第一次写错时就炸出来，代价只是一行异常文案。
- **靠 `SnowFlakeSingle` 的静态字段配置 worker** — 最强理由：不用自建实例，一处赋值全局生效。没选，是因为单例懒初始化后字段就固定了（实测赋 5/9 之后 `WorkerId` 仍是 1），多副本场景等于没配。
- **创建审计也进 SET、由调用方保证传全字段** — 最强理由：写入口逻辑更简单，一条 SQL 覆盖所有列。没选，是因为分离实体会把 `CreatedAt/CreatedBy` 抹成 `1900-01-01`/`0` 且返回成功（06 审计 high 1 实测），审计痕迹静默丢失比功能缺失更难发现。
- **写库时保留 .NET 的 100ns 精度、读回时允许误差** — 最强理由：不动写入值，最贴近日志时间。没选，是因为「读回等于写入」是可断言的硬条件，留误差就得放宽断言，等于让精度问题长期藏在容差里。

## Consequences

- **收益**：Domain 保持零 ORM 特性；软删除不挡同名重建且唯一约束仍真生效；并发冲突有明确返回值和错误码，不靠异常类型猜；时间读写在微秒级完全相等。
- **代价**：`PersistenceWriter` 是一条自己拼 SQL 的写路径，审计列得自己补（已复用同一套规则）；表名约定不做复数化，将来 `db/` 里的版本化 SQL 必须与 snake_case 约定一致；`SoftDeleteTypes` 要在模块注册时手工登记，漏登记就等于没过滤；要写删除只能走 `SoftDeleteAsync`，`db.Deleteable(entity)` 被拦、`db.Updateable(...)`（含 `SetColumns` 形态）被无条件拒绝（两者都有用例钉住）；有 `TenantId` 列的类型在拿不到租户上下文时**写不进去**，07 之前所有写路径都必须先供给 `CurrentTenantId`。
- **重访信号**：⓪07 接入租户解析后确认 `CurrentTenantId` 的来源与生命周期（Scoped）；①SqlSugar 升级后版本校验在 `Updateable(entity)` 上真生效，可考虑收回自拼 SQL；②17 号票建 master 表时发现表名约定与既定 SQL 冲突；③出现需要历史版本链的实体（软删除要保留多条已删同名记录时，`DeletedToken=Id` 已够用，但若同一主键被反复删除需重新设计）。

## Verification

```bash
cd .worktree/06
dotnet build Pzd.slnx                                   # 0 警告 0 错误
./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe -class Pzd.IntegrationTests.PersistenceTests
# Total: 29, Failed: 0
```

用例分两层：第一层钉约定本身（审计填充、软删除不可见、同名重建、并发 409、1062、UTC 原值）；
第二层是审计 run-03 的返工用例——分离实体不抹创建审计、跨租户写不穿、缺租户上下文抛异常、
物理删除被拦、软删除入口写全四列、`SetColumns` 边界与代价、冲突不污染实体、worker 配置真的被采纳。

**变异验证（六处，每个都被精确抓到一条红灯）**：

| 变异 | 抓到它的用例 |
|---|---|
| 去掉租户谓词 | `乙租户拿着同Id和版本也改不到甲租户的记录` |
| `CreatedAt/CreatedBy` 进 SET | `分离实体更新不抹掉创建审计` |
| 不拦 `DeleteByObject` | `物理删除被拒绝不让回收站失效` |
| 软删除不写 `DeletedAt/DeletedBy` | `软删除入口写全四列且同名可重建` |
| 冲突前就落实体 | `版本冲突时内存实体不被改写` |
| 忽略 worker 配置 | `配置的worker与机房号真的被采纳` |
