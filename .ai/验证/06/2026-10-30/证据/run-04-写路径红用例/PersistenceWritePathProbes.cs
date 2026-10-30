using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 06 写路径的回归探针（李总）：把老登审计的三条 high 变成可执行的失败用例。
/// 只用公开入口 PzdPersistence.CreateClient / PersistenceWriter.UpdateAsync。
/// </summary>
public sealed class PersistenceWritePathProbes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string SchemaSql = """
        CREATE TABLE probe_user (
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
          UNIQUE KEY ux_probe_user_tenant_name (TenantId, UserName, DeletedToken)
        )
        """;

    [Fact]
    public async Task 分离实体更新不得抹掉创建审计字段()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, CreateOptions());

        var user = new ProbeUser { TenantId = 1, UserName = "张三" };
        await db.Insertable(user).ExecuteCommandAsync(ct);

        // 只带 Id / Version / UserName 的「分离实体」（真实场景：只加载了一部分列）
        var patch = new ProbeUser { Version = user.Version, UserName = "张三-改" };
        WithId(patch, user.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, patch, CreateOptions(), ct);
        Assert.Null(failure);

        var createdBy = await database.QueryScalarAsync<long>($"SELECT CreatedBy FROM probe_user WHERE Id = {user.Id}", ct);
        var createdAt = await database.QueryScalarAsync<DateTime>($"SELECT CreatedAt FROM probe_user WHERE Id = {user.Id}", ct);

        Assert.Equal(CurrentUser, createdBy);
        Assert.True(createdAt.Year > 2000, $"创建时间被抹成 {createdAt:O}");
    }

    [Fact]
    public async Task 更新不得把整行搬到别的租户()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, CreateOptions());

        var user = new ProbeUser { TenantId = 1, UserName = "张三" };
        await db.Insertable(user).ExecuteCommandAsync(ct);

        // 乙租户拿着同 Id、同 Version 的实体来更新
        var foreign = new ProbeUser { Version = user.Version, TenantId = 2, UserName = "张三" };
        WithId(foreign, user.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, foreign, CreateOptions(), ct);

        var tenant = await database.QueryScalarAsync<long>($"SELECT TenantId FROM probe_user WHERE Id = {user.Id}", ct);

        Assert.True(failure is not null, "跨租户更新应当被拒绝，但调用返回了成功");
        Assert.Equal(1, tenant);
    }

    /// <summary>Id 在 05 里是 protected set，测试里用反射造「分离实体」的主键。</summary>
    private static void WithId(Entity entity, long id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

    private static PersistenceOptions CreateOptions() => new()
    {
        SoftDeleteTypes = [typeof(ProbeUser)],
        CurrentUserId = () => CurrentUser,
    };

    private sealed class ProbeUser : Entity
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
