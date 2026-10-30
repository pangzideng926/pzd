using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 李总在**合并后的 main** 上对「四种业务态」的独立复核（`7a412fe`）。
/// 老卫正在拿这四个 code 写交接、07/08/09/13 要照着接，所以用我这套自建实体再验一遍，
/// 不依赖 06 票自己的 TestUser 与用例。
/// 只验对外可观察的三件事：<c>Kind</c>、HTTP 状态码、<c>Error.Code</c>，外加「库里没被动过」。
/// </summary>
public sealed class MainMergeContractProbes(MySqlServerFixture fixture)
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

    /// <summary>业务态一：更新一条已软删除的记录 → `RecordInTrash` / 409 / `Persistence.Record.InTrash`。</summary>
    [Fact]
    public async Task 合并后_更新回收站记录返回InTrash()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "回收站" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        // 形态一（老登 R1）：调用方手里是「活跃快照」（IsDeleted=false、DeletedToken=0、Version 对得上），
        // 而库里这条已经被别人删了 → 应返回 RecordInTrash，而不是抛异常。
        var activeSnapshot = new ProbeUser
        {
            TenantId = 1,
            UserName = "想改回收站里的",
            Version = loaded.Version,
        };
        activeSnapshot.ForceId(loaded.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, activeSnapshot, Options(1), ct);

        var stored = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[M1] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} "
            + $"库里UserName={stored.UserName} Version={stored.Version}");

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.RecordInTrash, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.InTrash", failure.Error.Code);
        Assert.Equal("回收站", stored.UserName);
    }

    /// <summary>形态二：实体自己带着 IsDeleted=true 来更新 → 入口前置守卫抛异常（不是返回值）。</summary>
    [Fact]
    public async Task 合并后_拿删除态实体调用UpdateAsync被前置守卫拒绝()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "删除态实体" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        // 这条实体从回收站读出来，IsDeleted 仍为 true，直接拿去 UpdateAsync。
        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Exception? caught = null;
        try
        {
            await PersistenceWriter.UpdateAsync(db, trashed, Options(1), ct);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        Console.WriteLine($"[M1b] caught={caught?.GetType().Name ?? "无异常"} 消息含RestoreAsync={caught?.Message.Contains("RestoreAsync", StringComparison.Ordinal)}");

        Assert.NotNull(caught);
        Assert.IsType<InvalidOperationException>(caught);
    }

    /// <summary>业务态二：更新一条不存在的记录 → `NotFound` / 404。</summary>
    [Fact]
    public async Task 合并后_更新不存在的记录返回NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        var ghost = new ProbeUser { TenantId = 1, UserName = "不存在", Version = 1 };
        ghost.ForceId(9876543210L);
        var failure = await PersistenceWriter.UpdateAsync(db, ghost, Options(1), ct);

        Console.WriteLine($"[M2] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code}");

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.NotFound, failure!.Kind);
        Assert.Equal(404, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.NotFound", failure.Error.Code);
    }

    /// <summary>业务态三：拿到别家租户的行 → 必须是 `NotFound`/404，且别家那行一字不动。</summary>
    [Fact]
    public async Task 合并后_跨租户更新返回NotFound且不改别家数据()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        // 别家租户的行要按它自己的租户上下文插进去（插入守卫会拒绝跨租户写入）。
        var foreignDb = PzdPersistence.CreateClient(database.ConnectionString, Options(2));
        await foreignDb.Insertable(new ProbeUser { TenantId = 2, UserName = "别家" }).ExecuteCommandAsync(ct);
        var foreign = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var foreignVersion = foreign.Version;

        var intruder = new ProbeUser { TenantId = 2, UserName = "改写别家", Version = foreign.Version };
        intruder.ForceId(foreign.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, intruder, Options(1), ct);

        var stored = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[M3] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} "
            + $"别家 TenantId={stored.TenantId} UserName={stored.UserName} Version={stored.Version}");

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.NotFound, failure!.Kind);
        Assert.Equal(404, failure.HttpStatusCode);
        Assert.Equal(2, stored.TenantId);
        Assert.Equal("别家", stored.UserName);
        Assert.Equal(foreignVersion, stored.Version);
    }

    /// <summary>业务态四：版本被改掉 → `VersionConflict` / 409，且内存实体不被改写。</summary>
    [Fact]
    public async Task 合并后_版本冲突返回VersionConflict且不改内存实体()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "并发" }).ExecuteCommandAsync(ct);
        var stale = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var fresh = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        fresh.UserName = "先改";
        Assert.Null(await PersistenceWriter.UpdateAsync(db, fresh, Options(1), ct));

        var beforeVersion = stale.Version;
        var beforeUpdatedAt = stale.UpdatedAt;
        stale.UserName = "后改";
        var failure = await PersistenceWriter.UpdateAsync(db, stale, Options(1), ct);

        Console.WriteLine(
            $"[M4] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} "
            + $"内存Version={stale.Version}(调用前{beforeVersion}) 内存UpdatedAt未变={stale.UpdatedAt == beforeUpdatedAt}");

        Assert.NotNull(failure);
        Assert.Equal(PersistenceFailureKind.VersionConflict, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.VersionConflict", failure.Error.Code);
        Assert.Equal(beforeVersion, stale.Version);
        Assert.Equal(beforeUpdatedAt, stale.UpdatedAt);
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
