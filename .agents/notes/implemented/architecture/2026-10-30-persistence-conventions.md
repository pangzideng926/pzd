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

## Alternatives considered

- **在 SharedKernel 加 `ISoftDeletable` 标记接口** — 最强理由：过滤器用泛型 `AddTableFilter<ISoftDeletable>` 一行搞定，类型安全。没选，是因为 05 的签名已冻结，加接口要重开 05 的审计；而且 Domain 实体为了实现持久化接口而改签名，等于把 ORM 关注点渗进领域层。
- **`DeletedToken` 可空、未删除写 NULL**（票面原建议）— 最强理由：语义直观，NULL 天然表示「没删」。没选，是因为 MySQL 唯一索引允许多个 NULL，两条活跃同名记录都能插入，唯一约束形同虚设；`0` 是能参加比较的哨兵值。
- **依赖 `IsEnableUpdateVersionValidation` 属性** — 最强理由：框架自带，少写 SQL。没选，是因为实测 SQL 里没有版本列：`UPDATE probe_user SET …,Version=@Version,… WHERE Id=@Id`，链式 `.IsEnableUpdateVersionValidation()` 也一样只发一条按主键的 UPDATE，第二次并发更新照样成功（影响 1 行）。
- **写库时保留 .NET 的 100ns 精度、读回时允许误差** — 最强理由：不动写入值，最贴近日志时间。没选，是因为「读回等于写入」是可断言的硬条件，留误差就得放宽断言，等于让精度问题长期藏在容差里。

## Consequences

- **收益**：Domain 保持零 ORM 特性；软删除不挡同名重建且唯一约束仍真生效；并发冲突有明确返回值和错误码，不靠异常类型猜；时间读写在微秒级完全相等。
- **代价**：`PersistenceWriter` 是一条自己拼 SQL 的写路径，`UpdatedAt/UpdatedBy` 得自己补（已复用同一套规则）；表名约定不做复数化，将来 `db/` 里的版本化 SQL 必须与 snake_case 约定一致；`SoftDeleteTypes` 要在模块注册时手工登记，漏登记就等于没过滤。
- **重访信号**：①SqlSugar 升级后版本校验在 `Updateable(entity)` 上真生效，可考虑收回自拼 SQL；②17 号票建 master 表时发现表名约定与既定 SQL 冲突；③出现需要历史版本链的实体（软删除要保留多条已删同名记录时，`DeletedToken=Id` 已够用，但若同一主键被反复删除需重新设计）。

## Verification

```bash
cd .worktree/06
dotnet build Pzd.slnx                                   # 0 警告 0 错误
./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe -class Pzd.IntegrationTests.PersistenceTests
# Total: 6, Failed: 0（审计字段、软删除不可见、同名重建、并发 409、1062、UTC 原值）
```
