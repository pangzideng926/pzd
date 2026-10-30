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
        await db.Insertable(user).ExecuteCommandAsync(ct);
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
        await db.Insertable(user).ExecuteCommandAsync(ct);

        user.IsDeleted = true;
        await db.Updateable(user).ExecuteCommandAsync(ct);

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
        await db.Insertable(first).ExecuteCommandAsync(ct);
        first.IsDeleted = true;
        await db.Updateable(first).ExecuteCommandAsync(ct);

        var second = new TestUser { TenantId = 1, UserName = "王五" };
        await db.Insertable(second).ExecuteCommandAsync(ct);

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
        await db.Insertable(user).ExecuteCommandAsync(ct);

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

        await db.Insertable(new TestUser { TenantId = 1, UserName = "重名" }).ExecuteCommandAsync(ct);

        var duplicate = new TestUser { TenantId = 1, UserName = "重名" };
        var failure = await CaptureFailureAsync(async () => await db.Insertable(duplicate).ExecuteCommandAsync(ct));

        Assert.Equal(PersistenceFailureKind.DuplicateKey, failure.Kind);
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
        await db.Insertable(user).ExecuteCommandAsync(ct);

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
        await db.Insertable(inserted).ExecuteCommandAsync(ct);

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
        await db.Insertable(owner).ExecuteCommandAsync(ct);
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

        await db.Insertable(new TestUser { TenantId = 1, UserName = "无上下文" }).ExecuteCommandAsync(ct);
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
        await db.Insertable(user).ExecuteCommandAsync(ct);

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

        await db.Insertable(new TestUser { TenantId = 1, UserName = "入口删" }).ExecuteCommandAsync(ct);
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

        await db.Insertable(new TestUser { TenantId = 1, UserName = "入口删" }).ExecuteCommandAsync(ct);
        Assert.Equal(1, await db.Queryable<TestUser>().CountAsync(ct));
    }

    [Fact]
    public async Task SetColumns形态不写DeletedToken是已知边界()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await db.Insertable(new TestUser { TenantId = 1, UserName = "边界" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<TestUser>().ToListAsync(ct)).Single();

        // 这条路绕过了唯一软删除入口：AOP 只补 UpdatedAt/UpdatedBy，不会补 DeletedToken。
        await db.Updateable<TestUser>()
            .SetColumns(it => new TestUser { IsDeleted = true })
            .Where(it => it.Id == loaded.Id)
            .ExecuteCommandAsync(ct);

        var purged = (await db.Queryable<TestUser>().ClearFilter().ToListAsync(ct)).Single();
        Assert.True(purged.IsDeleted);
        Assert.Equal(0, purged.DeletedToken);

        // 代价也钉住：同名重建会撞 1062，下一个人看到这条就知道必须走入口。
        var duplicate = new TestUser { TenantId = 1, UserName = "边界" };
        var failure = await CaptureFailureAsync(async () => await db.Insertable(duplicate).ExecuteCommandAsync(ct));
        Assert.Equal(PersistenceFailureKind.DuplicateKey, failure.Kind);
    }

    // ---- 06 审计 medium 3：冲突时内存实体不被改写 ----

    [Fact]
    public async Task 版本冲突时内存实体不被改写()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = CreateClient(database);

        await db.Insertable(new TestUser { TenantId = 1, UserName = "脏写" }).ExecuteCommandAsync(ct);
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
