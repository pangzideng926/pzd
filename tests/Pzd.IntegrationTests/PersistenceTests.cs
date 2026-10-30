using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 06 号票的 seam：持久化约定的对外行为。测试只用公开入口
/// <see cref="PzdPersistence.CreateClient"/> 与 <see cref="PersistenceErrors.Classify"/>，
/// 不碰内部实现，改实现不该改这些断言。
/// 表结构用真 SQL 建（architecture.md §5.3 禁止 CodeFirst 比对），与 db/ 下的版本化 SQL 同形。
/// </summary>
public sealed class PersistenceTests(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    /// <summary>表名约定：实体类型名的 snake_case（TestUser → test_user）；列名与属性同名。</summary>
    private const string SchemaSql = """
        CREATE TABLE test_user (
          Id BIGINT NOT NULL PRIMARY KEY,
          TenantId BIGINT NOT NULL,
          UserName VARCHAR(64) NOT NULL,
          Version INT NOT NULL,
          IsDeleted TINYINT(1) NOT NULL DEFAULT 0,
          DeletedToken BIGINT NOT NULL DEFAULT 0,
          DeletedAt DATETIME(6) NULL,
          DeletedBy BIGINT NOT NULL DEFAULT 0,
          CreatedAt DATETIME(6) NOT NULL,
          CreatedBy BIGINT NOT NULL,
          UpdatedAt DATETIME(6) NOT NULL,
          UpdatedBy BIGINT NOT NULL,
          UNIQUE KEY ux_test_user_tenant_name (TenantId, UserName, DeletedToken)
        )
        """;

    [Fact]
    public async Task 插入后审计字段自动填充且版本从1开始()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var before = DateTime.UtcNow;
        var user = new TestUser { TenantId = 1, UserName = "张三" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);
        var after = DateTime.UtcNow;

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        Assert.True(stored.Id > 0, "主键应由雪花生成");
        Assert.Equal(1, stored.Version);
        Assert.Equal(CurrentUser, stored.CreatedBy);
        Assert.Equal(CurrentUser, stored.UpdatedBy);
        Assert.InRange(stored.CreatedAt, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task 软删除记录默认查询不可见且可显式绕过过滤()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var user = new TestUser { TenantId = 1, UserName = "李四" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);

        // 软删除只走唯一入口（06 重审 high 4）：不再演示 db.Updateable 这种可绕过版本与审计的写法。
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, user, CreateOptions(), ct));

        Assert.Equal(0, await db.Queryable<TestUser>().CountAsync(ct));
        Assert.Equal(1, await db.Queryable<TestUser>().ClearFilter().CountAsync(ct));

        var purged = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        Assert.True(purged.DeletedToken > 0, "软删除时应把主键写进 DeletedToken，唯一索引才能放行同名重建");
        Assert.Equal(CurrentUser, purged.UpdatedBy);
    }

    [Fact]
    public async Task 软删除后能用同一业务键重建()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var first = new TestUser { TenantId = 1, UserName = "王五" };
        await PersistenceWriter.InsertAsync(db, first, CreateOptions(), ct);
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, first, CreateOptions(), ct));

        var second = new TestUser { TenantId = 1, UserName = "王五" };
        await PersistenceWriter.InsertAsync(db, second, CreateOptions(), ct);

        Assert.Equal(1, await db.Queryable<TestUser>().CountAsync(ct));
        Assert.Equal(2, await db.Queryable<TestUser>().ClearFilter().CountAsync(ct));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task 并发更新冲突被识别为409来源错误()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var user = new TestUser { TenantId = 1, UserName = "赵六" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);

        var stale = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        var fresh = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        fresh.UserName = "赵六-先改";
        var firstWrite = await PersistenceWriter.UpdateAsync(db, fresh, CreateOptions(), ct);
        Assert.Null(firstWrite);

        stale.UserName = "赵六-后改";
        var failure = await PersistenceWriter.UpdateAsync(db, stale, CreateOptions(), ct);

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.VersionConflict, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.VersionConflict", failure.Error.Code);

        // 冲突方的改动没有落库，先写的那次成功且版本只加了一次
        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal("赵六-先改", stored.UserName);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task 唯一键1062被识别为409来源错误()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "重名" }, CreateOptions(), ct);

        var duplicate = new TestUser { TenantId = 1, UserName = "重名" };
        var failure = await PersistenceWriter.InsertAsync(db, duplicate, CreateOptions(), ct);

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.DuplicateKey, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.DuplicateKey", failure.Error.Code);
    }

    [Fact]
    public async Task 读回的时间与写入的UTC值一致()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var user = new TestUser { TenantId = 1, UserName = "时间" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);

        var written = await database.QueryScalarAsync<DateTime>(
            "SELECT CreatedAt FROM test_user WHERE UserName = '时间'",
            ct);

        Assert.True(
            (written - user.CreatedAt).Duration().Ticks == 0,
            "入库与读回必须是同一 UTC 原值，Infrastructure 不得改时区语义");
    }

    // ---- 06 审计 high 1：分离实体更新不得抹掉创建审计 ----

    [Fact]
    public async Task 分离实体更新不抹掉创建审计()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var inserted = new TestUser { TenantId = 1, UserName = "审计" };
        await PersistenceWriter.InsertAsync(db, inserted, CreateOptions(), ct);

        var baseline = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal(CurrentUser, baseline.CreatedBy);

        // 分离实体：只带 Id / Version / 业务字段，审计列全是默认值。
        var detached = new TestUser { TenantId = 1, UserName = "审计-改", Version = baseline.Version };
        detached.ForceId(baseline.Id);

        var failure = await PersistenceWriter.UpdateAsync(db, detached, CreateOptions(), ct);
        Assert.Null(failure);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal("审计-改", stored.UserName);
        Assert.Equal(baseline.CreatedAt, stored.CreatedAt);
        Assert.Equal(baseline.CreatedBy, stored.CreatedBy);
    }

    // ---- 06 审计 high 2：写入口必须有租户谓词 ----

    [Fact]
    public async Task 乙租户拿着同Id和版本也改不到甲租户的记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var owner = new TestUser { TenantId = 1, UserName = "甲租户" };
        await PersistenceWriter.InsertAsync(db, owner, CreateOptions(), ct);
        var baseline = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        // 乙租户手里有完整快照：Id 与 Version 都对，只是把自己的租户号填进去。
        var intruder = new TestUser { TenantId = 2, UserName = "乙租户改写", Version = baseline.Version };
        intruder.ForceId(baseline.Id);

        var failure = await PersistenceWriter.UpdateAsync(db, intruder, CreateOptions(tenantId: 2), ct);

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.VersionConflict, failure!.Kind);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal(1, stored.TenantId);
        Assert.Equal("甲租户", stored.UserName);
    }

    [Fact]
    public async Task 租户上下文缺失时写入口抛异常而不是按Id更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "无上下文" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        loaded.UserName = "无上下文-改";

        var options = new PersistenceOptions
        {
            SoftDeleteTypes = [typeof(TestUser)],
            CurrentUserId = () => CurrentUser,
            WorkerId = 1,
            DatacenterId = 1,
        };

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => PersistenceWriter.UpdateAsync(db, loaded, options, ct));

        Assert.Contains("租户", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal("无上下文", stored.UserName);
    }

    // ---- 06 审计 high 3：软删除必须有强制点 ----

    [Fact]
    public async Task 物理删除被拒绝不让回收站失效()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var user = new TestUser { TenantId = 1, UserName = "物理删" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await db.Deleteable(user).ExecuteCommandAsync(ct));

        Assert.Contains("软删除", exception.Message, StringComparison.Ordinal);

        var physicalRows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM test_user", ct);
        Assert.Equal(1, physicalRows);
    }

    [Fact]
    public async Task 软删除入口写全四列且同名可重建()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "入口删" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        var before = DateTime.UtcNow;

        var failure = await PersistenceWriter.SoftDeleteAsync(db, loaded, CreateOptions(), ct);
        Assert.Null(failure);

        Assert.True(loaded.IsDeleted, "成功后内存实体应同步为已删除");
        Assert.Equal(0, await db.Queryable<TestUser>().CountAsync(ct));

        var purged = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        Assert.True(purged.IsDeleted);
        Assert.Equal(purged.Id, purged.DeletedToken);
        Assert.Equal(CurrentUser, purged.DeletedBy);
        Assert.NotNull(purged.DeletedAt);
        Assert.InRange(purged.DeletedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(2, purged.Version);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "入口删" }, CreateOptions(), ct);
        Assert.Equal(1, await db.Queryable<TestUser>().CountAsync(ct));
    }

    [Fact]
    public async Task SetColumns形态的旁路也被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "边界" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        // 这条写法原来是「最能绕过软删除约定」的一条：SetColumns 只写 IsDeleted，不写 DeletedToken，
        // 随后同名重建会撞 1062。现在它被 AOP 对 UpdateByObject 的无条件拒绝挡在门外，跟 Version
        // 填不填无关——这里显式写了 Version = 1，同样被拒（06 重审 high 4、06 四审 low 2）。
        // 用例的价值因此变成：证明这条旁路也走不通。
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await db.Updateable<TestUser>()
                .SetColumns(it => new TestUser { IsDeleted = true, Version = 1 })
                .Where(it => it.Id == loaded.Id)
                .ExecuteCommandAsync(ct));

        Assert.Contains("PersistenceWriter", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.False(stored.IsDeleted);
        Assert.Equal(0, stored.DeletedToken);
        Assert.Equal(1, stored.Version);
    }

    // ---- 06 审计 medium 3：冲突时内存实体不被改写 ----

    [Fact]
    public async Task 版本冲突时内存实体不被改写()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "脏写" }, CreateOptions(), ct);
        var stale = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        var fresh = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        fresh.UserName = "先改";
        Assert.Null(await PersistenceWriter.UpdateAsync(db, fresh, CreateOptions(), ct));

        var staleVersion = stale.Version;
        var staleUpdatedAt = stale.UpdatedAt;
        var staleUpdatedBy = stale.UpdatedBy;

        stale.UserName = "后改";
        var failure = await PersistenceWriter.UpdateAsync(db, stale, CreateOptions(), ct);

        Assert.NotNull(failure);
        Assert.Equal(staleVersion, stale.Version);
        Assert.Equal(staleUpdatedAt, stale.UpdatedAt);
        Assert.Equal(staleUpdatedBy, stale.UpdatedBy);
    }

    // ---- 06 审计 medium 2：雪花 worker 必须可配 ----

    [Fact]
    public void 配置的worker与机房号真的被采纳()
    {
        var generator = PzdPersistence.CreateIdGenerator(new PersistenceOptions
        {
            WorkerId = 2,
            DatacenterId = 3,
        });

        // 委托的 Target 就是实际生成 Id 的那个 IdWorker 实例。
        var target = Assert.IsAssignableFrom<SqlSugar.DistributedSystem.Snowflake.IdWorker>(generator.Target);
        Assert.Equal(2L, target.WorkerId);
        Assert.Equal(3L, target.DatacenterId);
        Assert.True(generator() > 0);
    }

    [Fact]
    public void 未配置worker时给出启动期警告()
    {
        var warnings = new List<string>();

        var generator = PzdPersistence.CreateIdGenerator(new PersistenceOptions { OnWarning = warnings.Add });

        Assert.Single(warnings);
        Assert.Contains("多副本", warnings[0], StringComparison.Ordinal);
        Assert.True(generator() > 0);
    }

    // ---- 审计 run-04 重审：medium 1 / 2 / 3 与 low 1 ----

    [Fact]
    public async Task UpdateAsync带IsDeleted时被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "走更新删" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        // 删除态的翻转只由 SoftDeleteAsync / RestoreAsync 负责。Update 顺带写 IsDeleted 的话，
        // 既可造出「删除态但 DeletedToken=0」的幽灵行，也会把回收站的删除审计刷成当前时间
        // （06 四审 medium 2）。
        loaded.IsDeleted = true;
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => PersistenceWriter.UpdateAsync(db, loaded, CreateOptions(), ct));

        Assert.Contains("SoftDeleteAsync", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.False(stored.IsDeleted);
        Assert.Equal(0, stored.DeletedToken);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task 软删除后能恢复且同名可再用()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "可恢复" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, CreateOptions(), ct));

        var deleted = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        var failure = await PersistenceWriter.RestoreAsync(db, deleted, CreateOptions(), ct);
        Assert.Null(failure);

        var restored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.False(restored.IsDeleted);
        Assert.Equal(0, restored.DeletedToken);
        Assert.Null(restored.DeletedAt);
        Assert.Equal(0, restored.DeletedBy);
        Assert.Equal(3, restored.Version);

        // 恢复之后这个业务键被活记录占着，同名重建必须重新撞 1062。
        var duplicate = new TestUser { TenantId = 1, UserName = "可恢复" };
        var conflict = await PersistenceWriter.InsertAsync(db, duplicate, CreateOptions(), ct);
        Assert.NotNull(conflict);
        Assert.Equal(PersistenceFailureKind.DuplicateKey, conflict!.Kind);
    }

    [Fact]
    public async Task 同名记录已占用时恢复被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "抢占" }, CreateOptions(), ct);
        var old = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, old, CreateOptions(), ct));

        // 同名重建成功后，老记录再恢复就必须被唯一索引挡住，否则会有两条活跃同名记录。
        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "抢占" }, CreateOptions(), ct);

        var deleted = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single(current => current.IsDeleted);
        var failure = await PersistenceWriter.RestoreAsync(db, deleted, CreateOptions(), ct);

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.DuplicateKey, failure!.Kind);
        Assert.Equal(1, await db.Queryable<TestUser>().CountAsync(ct));
    }

    [Fact]
    public async Task 插入时实体租户与上下文不一致被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var intruder = new TestUser { TenantId = 2, UserName = "越权插入" };

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await PersistenceWriter.InsertAsync(db, intruder, CreateOptions(), ct));

        Assert.Contains("租户", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM test_user", ct));
    }

    [Fact]
    public async Task 插入未填租户时按上下文填充()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { UserName = "没填租户" }, CreateOptions(), ct);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal(1, stored.TenantId);
    }

    [Fact]
    public async Task 白名单元组允许物理删除()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var user = new TestUser { TenantId = 1, UserName = "归档清理" };
        await PersistenceWriter.InsertAsync(db, user, CreateOptions(), ct);

        // 08 的 Outbox 已消费清理这类合法物理删除，必须能显式登记后放行。
        var archiveOptions = CreateOptions();
        var archiveDb = PzdPersistence.CreateClient(
            database.ConnectionString,
            new PersistenceOptions
            {
                SoftDeleteTypes = archiveOptions.SoftDeleteTypes,
                CurrentUserId = archiveOptions.CurrentUserId,
                CurrentTenantId = archiveOptions.CurrentTenantId,
                WorkerId = archiveOptions.WorkerId,
                DatacenterId = archiveOptions.DatacenterId,
                HardDeleteTypes = [typeof(TestUser)],
            });

        await archiveDb.Deleteable(user).ExecuteCommandAsync(ct);

        Assert.Equal(0, await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM test_user", ct));
    }

    [Fact]
    public async Task 绕过写入口的Updateable被拒且版本不会被写成0()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "旁路" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        // 分离实体：Version 是默认值 0，Updateable 会把 0 原样写回库，那行就永久锁死了。
        var detached = new TestUser { TenantId = 1, UserName = "旁路-改" };
        detached.ForceId(loaded.Id);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await db.Updateable(detached).ExecuteCommandAsync(ct));

        Assert.Contains("PersistenceWriter", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal(1, stored.Version);
        Assert.Equal("旁路", stored.UserName);
        Assert.Equal(1, stored.TenantId);
    }

    [Fact]
    public async Task 带正确版本的Updateable同样被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "带版本旁路" }, CreateOptions(), ct);

        // 「加载 → 改 → 保存」这种最正常的用法，Version 本来就是对的（=1）。
        // 只按 Version<=0 拦的话这条正好穿过去，把整行搬到租户 2 —— 所以 AOP 无条件拒绝 Updateable。
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        loaded.TenantId = 2;
        loaded.UserName = "被搬";

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            async () => await db.Updateable(loaded).ExecuteCommandAsync(ct));

        Assert.Contains("PersistenceWriter", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal(1, stored.TenantId);
        Assert.Equal("带版本旁路", stored.UserName);
        Assert.Equal(1, stored.Version);
        Assert.Equal(CurrentUser, stored.CreatedBy);
    }

    [Fact]
    public async Task UpdateAsync不能把回收站记录改回活跃()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "复活" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, CreateOptions(), ct));

        var purged = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        purged.IsDeleted = false;

        // 走 UpdateAsync 复活会把 DeletedToken 一起写回，唯一索引当场失效 —— 必须要求走 RestoreAsync。
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => PersistenceWriter.UpdateAsync(db, purged, CreateOptions(), ct));

        Assert.Contains("RestoreAsync", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Queryable<TestUser>().CountAsync(ct));
    }

    /// <summary>
    /// 06 四审 medium 1：<c>InsertAsync</c> 传 <c>IsDeleted=true</c> 原来会写出一行
    /// 「删除态但四列删除审计全空」的记录——应用里查不到（被软删除过滤器挡着），却占着唯一索引，
    /// 同名记录再也建不出来，调用方只看到 409。插入入口只写活跃记录。
    /// </summary>
    [Fact]
    public async Task 插入入口拒绝删除态实体()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => PersistenceWriter.InsertAsync(
                db,
                new TestUser { TenantId = 1, UserName = "幽灵", IsDeleted = true },
                CreateOptions(),
                ct));

        Assert.Contains("SoftDeleteAsync", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Queryable<TestUser>().ClearFilter().CountAsync(ct));

        // 关键后果：不再有幽灵行占住唯一索引，同名活跃记录能正常插入。
        Assert.Null(await PersistenceWriter.InsertAsync(
            db, new TestUser { TenantId = 1, UserName = "幽灵" }, CreateOptions(), ct));
        Assert.Equal(1, await db.Queryable<TestUser>().CountAsync(ct));
    }

    /// <summary>
    /// 06 五审 medium（P8）：DTO 形状的「脱离实体」只带主键、版本和业务字段，
    /// <c>IsDeleted</c> 默认 false、<c>DeletedToken</c> 默认 0，两道内存守卫都不触发。
    /// 若 <c>WHERE</c> 不带活跃条件，它会把已删除行的业务字段改掉、版本 +1、**还返回成功**，
    /// 而应用侧因为软删除过滤器根本读不到这条——07/08 接 API 后最常见的形状。
    /// </summary>
    [Fact]
    public async Task 脱离实体更新已删除记录被拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "脱离" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, CreateOptions(), ct));

        var purged = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();

        // 典型 DTO 转回来的形状：不带删除标记，只带主键、版本与业务字段。
        var detached = new TestUser { TenantId = 1, UserName = "脱离-改名", Version = purged.Version };
        detached.ForceId(purged.Id);

        // 拒绝理由必须指 RestoreAsync：笼统返回版本冲突会被 09 映射成 409「并发冲突」，
        // 调用方重载之后依然查不到这条记录（票面第 258 行已为 Version=0 行记过同样的坑）。
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => PersistenceWriter.UpdateAsync(db, detached, CreateOptions(), ct));
        Assert.Contains("RestoreAsync", exception.Message, StringComparison.Ordinal);

        var stored = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        Assert.True(stored.IsDeleted);
        Assert.Equal("脱离", stored.UserName);
        Assert.Equal(purged.Version, stored.Version);
        Assert.Equal(0, await db.Queryable<TestUser>().CountAsync(ct));
    }

    /// <summary>
    /// 06 四审 medium 2（含 C10）：<c>Update</c> 一个删除列都不写。否则实体上带着的旧删除审计
    /// 会被写回库里，回收站按 <c>DeletedAt</c> 排序时这条就被顶到最新。
    /// 06 五审 low（P4）补：内存实体也要跟库一致——调用方会把它序列化给前端，
    /// 不能带着「库里根本没有」的删除时间回去。
    /// </summary>
    [Fact]
    public async Task UpdateAsync不改动任何删除审计列()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "不碰审计" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        loaded.IsDeleted = false;
        loaded.DeletedAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        loaded.DeletedBy = 5;
        loaded.UserName = "改过名";

        Assert.Null(await PersistenceWriter.UpdateAsync(db, loaded, CreateOptions(), ct));

        var stored = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();
        Assert.Equal("改过名", stored.UserName);
        Assert.False(stored.IsDeleted);
        Assert.Equal(0, stored.DeletedToken);
        Assert.Null(stored.DeletedAt);
        Assert.Equal(0, stored.DeletedBy);
        Assert.Equal(2, stored.Version);
    }

    /// <summary>
    /// 06 五审 low（P4）：<c>Update</c> 连内存实体也不碰删除四列。那组值没写进库，回写时就不该被改成
    /// 「另一个也没进库的值」——张一一裁定不回查库，正常路径上从库里加载的实体本来就带着活跃态常量，
    /// 不碰即一致；只有调用方自己塞了脏值时才不一致，那是调用方的事。
    /// </summary>
    [Fact]
    public async Task UpdateAsync不改动内存实体的删除审计列()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await PersistenceWriter.InsertAsync(db, new TestUser { TenantId = 1, UserName = "内存不动" }, CreateOptions(), ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        var stale = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        loaded.DeletedAt = stale;
        loaded.DeletedBy = 5;

        Assert.Null(await PersistenceWriter.UpdateAsync(db, loaded, CreateOptions(), ct));

        // 实体上原样保留调用方塞的值，写入口不替它擦（也没往库里写）。
        Assert.Equal(stale, loaded.DeletedAt);
        Assert.Equal(5L, loaded.DeletedBy);
        Assert.False(loaded.IsDeleted);
        Assert.Equal(0, loaded.DeletedToken);
        Assert.Equal(2, loaded.Version);
    }

    /// <summary>表名约定（06 审计 low 5）：连续大写算一个词，别拆成 a_p_i_log。</summary>
    [Theory]
    [InlineData(typeof(TestUser), "test_user")]
    [InlineData(typeof(APILogProbe), "api_log_probe")]
    [InlineData(typeof(PermissionCodeProbe), "permission_code_probe")]
    public void 表名按snake_case分段(Type entityType, string expected) =>
        Assert.Equal(expected, PersistenceConventions.TableNameFor(entityType));

    private static PersistenceOptions CreateOptions(long tenantId = 1) => new()
    {
        SoftDeleteTypes = [typeof(TestUser)],
        CurrentUserId = () => CurrentUser,
        CurrentTenantId = () => tenantId,
        WorkerId = 1,
        DatacenterId = 1,
    };

    private static SqlSugar.SqlSugarClient CreateClient(TestDatabase database) =>
        PzdPersistence.CreateClient(database.ConnectionString, CreateOptions());

    /// <summary>把「抛异常」收敛成一个可断言的失败对象，避免用例里写满 try/catch。</summary>
    private static async Task<PersistenceFailure> CaptureFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            return PersistenceErrors.Classify(exception);
        }

        throw new Xunit.Sdk.XunitException("预期抛出异常，但操作成功了");
    }

    /// <summary>
    /// 测试实体：故意做成普通类而不是实现任何 Infrastructure 接口——
    /// Domain 不许引用 Infrastructure（architecture.md §3.2），约定只认属性名。
    /// </summary>
    private sealed class APILogProbe
    {
    }

    private sealed class PermissionCodeProbe
    {
    }

    private sealed class TestUser : Entity
    {
        public long TenantId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public int Version { get; set; }

        public bool IsDeleted { get; set; }

        public long DeletedToken { get; set; }

        public DateTime? DeletedAt { get; set; }

        public long DeletedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public long CreatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long UpdatedBy { get; set; }

        /// <summary><c>Entity.Id</c> 是 05 冻结的 <c>protected set</c>，分离实体只能在子类里造。</summary>
        public void ForceId(long id) => Id = id;
    }
}
