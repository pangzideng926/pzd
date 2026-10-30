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

    private static PersistenceOptions CreateOptions() => new()
    {
        SoftDeleteTypes = [typeof(TestUser)],
        CurrentUserId = () => CurrentUser,
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
    private sealed class TestUser : Entity
    {
        public long TenantId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public int Version { get; set; }

        public bool IsDeleted { get; set; }

        public long DeletedToken { get; set; }

        public DateTime CreatedAt { get; set; }

        public long CreatedBy { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long UpdatedBy { get; set; }
    }
}
