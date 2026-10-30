using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 李总的独立探针（不是 06 号票的验收用例，不进主干默认集合前先定性）。
/// 目标是被测 <c>213753a</c> 没覆盖到的写路径：插入、回收站恢复、以及绕过唯一入口的
/// <c>Deleteable&lt;T&gt;().Where</c> / <c>Updateable(entity)</c> 两条 SqlSugar 直连形态。
/// 实体、表、建表 SQL 全部自建，与 06 票的 <c>TestUser</c> 不共享任何代码。
/// 断言按「应当出现的行为」写，因此缺口存在时用例会红。
/// </summary>
public sealed class WritePathBlindSpotProbes(MySqlServerFixture fixture)
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
          DeletedAt DATETIME(6) NULL,
          DeletedBy BIGINT NOT NULL DEFAULT 0,
          CreatedAt DATETIME(6) NOT NULL,
          CreatedBy BIGINT NOT NULL,
          UpdatedAt DATETIME(6) NOT NULL,
          UpdatedBy BIGINT NOT NULL,
          UNIQUE KEY ux_probe_user_tenant_name (TenantId, UserName, DeletedToken)
        )
        """;

    /// <summary>P1：插入路径完全不看租户上下文——更新侧有闸，插入侧没有。</summary>
    [Fact]
    public async Task 探针1_无租户上下文插入不应当成功()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);

        // CurrentTenantId 返回 null，与「租户上下文缺失」同形。
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: null));

        Exception? caught = null;
        try
        {
            // 调用方忘了填 TenantId（默认 0），也没有租户上下文。
            await db.Insertable(new ProbeUser { UserName = "无租户插入" }).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var rows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        var tenantId = rows == 0 ? -1 : await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user", ct);
        Console.WriteLine($"[P1] caught={caught?.GetType().Name ?? "无异常"} rows={rows} TenantId={tenantId} CurrentTenantId=null");

        Assert.NotNull(caught);
        Assert.Equal(0, rows);
    }

    /// <summary>P2：插入可以写入与当前上下文不同的租户，没有任何校验。</summary>
    [Fact]
    public async Task 探针2_插入到别的租户不应当成功()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);

        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        Exception? caught = null;
        try
        {
            // 上下文是 1，实体却写 2。
            await db.Insertable(new ProbeUser { TenantId = 2, UserName = "别家租户插入" }).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var rows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        var tenantId = rows == 0 ? -1 : await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user", ct);
        Console.WriteLine($"[P2] caught={caught?.GetType().Name ?? "无异常"} rows={rows} TenantId={tenantId} CurrentTenantId=1");

        Assert.NotNull(caught);
        Assert.Equal(0, rows);
    }

    /// <summary>P3：回收站恢复（IsDeleted 置回 false）之后，删除痕迹是否真的清干净。</summary>
    [Fact]
    public async Task 探针3_回收站恢复后删除痕迹应当清干净()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "恢复" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(tenantId: 1), ct));

        // 回收站取回，模拟「恢复」：只把 IsDeleted 改回 false，其余照回收站里的原样。
        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        purged.IsDeleted = false;
        var restore = await PersistenceWriter.UpdateAsync(db, purged, Options(tenantId: 1), ct);

        var tokenInDb = await database.QueryScalarAsync<long>(
            $"SELECT DeletedToken FROM probe_user WHERE Id = {purged.Id}", ct);
        var deletedByInDb = await database.QueryScalarAsync<long>(
            $"SELECT DeletedBy FROM probe_user WHERE Id = {purged.Id}", ct);
        var deletedAtLeft = await database.QueryScalarAsync<long>(
            $"SELECT COUNT(*) FROM probe_user WHERE Id = {purged.Id} AND DeletedAt IS NOT NULL", ct);

        // 恢复后再建同名：唯一索引里 DeletedToken 若不是 0，就跟这条恢复的行撞不上。
        Exception? duplicate = null;
        try
        {
            await db.Insertable(new ProbeUser { TenantId = 1, UserName = "恢复" }).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            duplicate = exception;
        }

        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Console.WriteLine(
            $"[P3] restoreFailure={restore?.Error.Code ?? "null"} DeletedToken={tokenInDb} DeletedBy={deletedByInDb} "
            + $"DeletedAt残留={deletedAtLeft} 同名重建={(duplicate is null ? "成功" : "抛异常")} 活跃同名记录={active}");

        Assert.Null(restore);
        Assert.Equal(0, tokenInDb);
        Assert.Equal(0, deletedByInDb);
        Assert.Equal(0, deletedAtLeft);
        Assert.NotNull(duplicate);
        Assert.Equal(1, active);
    }

    /// <summary>P4：软删除的另一条路径——不带实体的 <c>Deleteable&lt;T&gt;().Where</c>。</summary>
    [Fact]
    public async Task 探针4_不带实体的条件删除也应当被拦()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "条件删" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        Exception? caught = null;
        try
        {
            await db.Deleteable<ProbeUser>()
                .Where(it => it.Id == loaded.Id)
                .ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        var soft = await db.Queryable<ProbeUser>().ClearFilter().CountAsync(ct);
        Console.WriteLine(
            $"[P4] caught={caught?.GetType().Name ?? "无异常"} 物理行数={physical} 逻辑行数={soft}");

        Assert.NotNull(caught);
        Assert.Equal(1, physical);
    }

    /// <summary>P5：更新旁路——<c>db.Updateable(entity)</c> 既不看租户也不看版本。</summary>
    [Fact]
    public async Task 探针5_直连Updateable不应当跨租户按Id更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "甲租户" }).ExecuteCommandAsync(ct);
        var owner = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 乙租户手里有同 Id 同 Version 的分离实体，直接走 SqlSugar 的 Updateable(entity)。
        var intruder = new ProbeUser { TenantId = 2, UserName = "乙租户改写", Version = owner.Version };
        intruder.ForceId(owner.Id);

        Exception? caught = null;
        try
        {
            await db.Updateable(intruder).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var stored = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[P5] caught={caught?.GetType().Name ?? "无异常"} TenantId={stored.TenantId} UserName={stored.UserName} "
            + $"Version={stored.Version}（调用前 {owner.Version}）CreatedBy={stored.CreatedBy}");

        Assert.NotNull(caught);
        Assert.Equal(1, stored.TenantId);
        Assert.Equal("甲租户", stored.UserName);
    }

    private static PersistenceOptions Options(long? tenantId) => new()
    {
        SoftDeleteTypes = [typeof(ProbeUser)],
        CurrentUserId = () => CurrentUser,
        CurrentTenantId = () => tenantId,
        WorkerId = 1,
        DatacenterId = 1,
    };

    /// <summary>探针实体：普通类继承 <see cref="Entity"/>，约定只认属性名。</summary>
    private sealed class ProbeUser : Entity
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

        /// <summary><c>Entity.Id</c> 是 05 冻结的 <c>protected set</c>，子类才能造分离实体。</summary>
        public void ForceId(long id) => Id = id;
    }
}
