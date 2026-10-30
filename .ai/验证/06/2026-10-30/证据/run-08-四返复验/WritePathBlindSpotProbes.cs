using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 李总的独立探针第二轮（被测 <c>dddcb58</c>，06 号票第三次返工）。
/// 第一轮（<c>213753a</c>）的 P1–P5 全部为红，本轮同一套探针复跑，看返工是否真把它们变绿；
/// P5 拆成两个方向，因为新加的运行期防御只拒绝 <c>Version &lt;= 0</c>，
/// 「版本正确但绕过入口」这个更危险的形态要单独验。
/// 实体、表、SQL 全部自建，与 06 票的 TestUser 不共享代码。
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

    /// <summary>P1（上轮红）：无租户上下文插入。</summary>
    [Fact]
    public async Task 探针1_无租户上下文插入不应当成功()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: null));

        Exception? caught = null;
        try
        {
            await db.Insertable(new ProbeUser { UserName = "无租户插入" }).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var rows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        Console.WriteLine($"[P1] caught={caught?.GetType().Name ?? "无异常"} rows={rows} CurrentTenantId=null");

        Assert.NotNull(caught);
        Assert.Equal(0, rows);
    }

    /// <summary>P2（上轮红）：插入到与上下文不同的租户。</summary>
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
            await db.Insertable(new ProbeUser { TenantId = 2, UserName = "别家租户插入" }).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var rows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        Console.WriteLine($"[P2] caught={caught?.GetType().Name ?? "无异常"} rows={rows} CurrentTenantId=1");

        Assert.NotNull(caught);
        Assert.Equal(0, rows);
    }

    /// <summary>P3（上轮红）：走新的 RestoreAsync 恢复，删除痕迹应当清干净且唯一性守住。</summary>
    [Fact]
    public async Task 探针3_恢复入口应当清干净删除痕迹()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "恢复" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(tenantId: 1), ct));

        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var restore = await PersistenceWriter.RestoreAsync(db, purged, Options(tenantId: 1), ct);

        var tokenInDb = await database.QueryScalarAsync<long>(
            $"SELECT DeletedToken FROM probe_user WHERE Id = {purged.Id}", ct);
        var deletedByInDb = await database.QueryScalarAsync<long>(
            $"SELECT DeletedBy FROM probe_user WHERE Id = {purged.Id}", ct);
        var deletedAtLeft = await database.QueryScalarAsync<long>(
            $"SELECT COUNT(*) FROM probe_user WHERE Id = {purged.Id} AND DeletedAt IS NOT NULL", ct);

        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Console.WriteLine(
            $"[P3] restoreFailure={restore?.Error.Code ?? "null"} DeletedToken={tokenInDb} DeletedBy={deletedByInDb} "
            + $"DeletedAt残留={deletedAtLeft} 活跃记录={active}");

        Assert.Null(restore);
        Assert.Equal(0, tokenInDb);
        Assert.Equal(0, deletedByInDb);
        Assert.Equal(0, deletedAtLeft);
        Assert.Equal(1, active);
    }

    /// <summary>P4（上轮红，雨哥已声明运行期修不了）：不带实体的条件删除。</summary>
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
            await db.Deleteable<ProbeUser>().Where(it => it.Id == loaded.Id).ExecuteCommandAsync(ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        Console.WriteLine($"[P4] caught={caught?.GetType().Name ?? "无异常"} 物理行数={physical}");

        Assert.NotNull(caught);
        Assert.Equal(1, physical);
    }

    /// <summary>P5a（上轮红）：绕过入口的 Updateable，实体 Version 是**正确值**（最危险的形态）。</summary>
    [Fact]
    public async Task 探针5a_带正确版本号的直连Updateable不应当跨租户更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "甲租户" }).ExecuteCommandAsync(ct);
        var owner = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 乙租户手里有同 Id、且 Version 填的是正确值 1（不是 0）。
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
            $"[P5a] Version填的是{owner.Version} caught={caught?.GetType().Name ?? "无异常"} "
            + $"TenantId={stored.TenantId} UserName={stored.UserName} Version={stored.Version} CreatedBy={stored.CreatedBy}");

        Assert.NotNull(caught);
        Assert.Equal(1, stored.TenantId);
        Assert.Equal("甲租户", stored.UserName);
    }

    /// <summary>P5b：同一旁路，但实体 Version 漏填（0）——返工声称拦住的正是这一形态。</summary>
    [Fact]
    public async Task 探针5b_版本漏填的直连Updateable应当被拒()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "版本漏填" }).ExecuteCommandAsync(ct);
        var owner = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        var intruder = new ProbeUser { TenantId = 2, UserName = "改写", Version = 0 };
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
            $"[P5b] caught={caught?.GetType().Name ?? "无异常"} TenantId={stored.TenantId} "
            + $"UserName={stored.UserName} Version={stored.Version}");

        Assert.NotNull(caught);
        Assert.Equal(1, stored.Version);
        Assert.Equal("版本漏填", stored.UserName);
    }

    /// <summary>P5c：旁路写成功后，这行的乐观并发是否还有效（版本号还是不是内容指纹）。</summary>
    [Fact]
    public async Task 探针5c_旁路写成功后乐观并发不应当失效()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "并发甲" }).ExecuteCommandAsync(ct);

        // 并发者 A 手里的旧快照（Version=1），读完之后这行被别人绕过入口改掉了。
        var concurrent = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        var intruder = new ProbeUser { TenantId = 2, UserName = "绕过改", Version = concurrent.Version };
        intruder.ForceId(concurrent.Id);
        try
        {
            await db.Updateable(intruder).ExecuteCommandAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // 兜底挡住了就不必再验并发；这里只关心「挡住了没有」。
        }

        // 被拒之后这行应当一点没动：租户没搬、创建审计没抹、版本没变。
        var afterBypass = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[P5c] 旁路被拒后 TenantId={afterBypass.TenantId} UserName={afterBypass.UserName} "
            + $"CreatedBy={afterBypass.CreatedBy} Version={afterBypass.Version}");

        Assert.Equal(1, afterBypass.TenantId);
        Assert.Equal("并发甲", afterBypass.UserName);
        Assert.Equal(CurrentUser, afterBypass.CreatedBy);
        Assert.Equal(1, afterBypass.Version);

        // 没人改动过这行，A 拿旧快照走正规入口应当成功——这是正确行为，不是并发失效。
        concurrent.UserName = "甲的后续修改";
        var failure = await PersistenceWriter.UpdateAsync(db, concurrent, Options(tenantId: 1), ct);
        Console.WriteLine($"[P5c] A的更新={(failure is null ? "成功" : failure.Error.Code)}（无他人改动，应成功）");

        Assert.Null(failure);
    }

    /// <summary>P6：拿 UpdateAsync 当恢复用（没有 RestoreAsync 时的做法），删除痕迹是否残留。</summary>
    [Fact]
    public async Task 探针6_用UpdateAsync当恢复用不应当留下删除痕迹()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(tenantId: 1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "误用恢复" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(tenantId: 1), ct));

        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        purged.IsDeleted = false;

        // 错误用法现在应当被直接拒绝（提示走 RestoreAsync），而不是静默写坏唯一索引。
        Exception? misuse = null;
        try
        {
            await PersistenceWriter.UpdateAsync(db, purged, Options(tenantId: 1), ct);
        }
        catch (Exception exception)
        {
            misuse = exception;
        }

        var tokenInDb = await database.QueryScalarAsync<long>(
            $"SELECT DeletedToken FROM probe_user WHERE Id = {purged.Id}", ct);
        var stillDeleted = await db.Queryable<ProbeUser>().CountAsync(ct);

        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Console.WriteLine(
            $"[P6] misuseCaught={misuse?.GetType().Name ?? "无异常"} DeletedToken={tokenInDb} 活跃记录={active}");

        Assert.NotNull(misuse);
        Assert.Contains("RestoreAsync", misuse!.Message, StringComparison.Ordinal);
        Assert.Equal(0, active);
        Assert.True(tokenInDb != 0, "被拒后记录仍处于软删除状态，token 应保持原值");
        Assert.Equal(0, stillDeleted);
    }

    private static PersistenceOptions Options(long? tenantId) => new()
    {
        SoftDeleteTypes = [typeof(ProbeUser)],
        CurrentUserId = () => CurrentUser,
        CurrentTenantId = () => tenantId,
        WorkerId = 1,
        DatacenterId = 1,
    };

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

        public void ForceId(long id) => Id = id;
    }
}
