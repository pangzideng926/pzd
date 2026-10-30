using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>
/// 李总对「入口 × 实体标记」矩阵的逐行实测（代码对象 `7a412fe`）。
/// 老卫在交接里把「哪两种形态」写成六行矩阵，那是读代码写的；这里用自建实体把每一行钉成实测事实，
/// 供他核 §3 与边界表用。每行只验三件对外可观察的事：抛不抛、Kind、Error.Code。
/// </summary>
public sealed class WriteEntryMatrixProbes(MySqlServerFixture fixture)
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

    // ---- 第 1 行：UpdateAsync × 实体自称已删 ----
    [Fact]
    public async Task 行1_UpdateAsync_实体IsDeleted为真_抛异常()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "甲" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var caught = await CatchAsync(() => PersistenceWriter.UpdateAsync(db, trashed, Options(1), ct));
        Console.WriteLine($"[行1] caught={caught.Type} msg={Trim(caught.Message)}");

        Assert.NotNull(caught.Exception);
        Assert.IsType<InvalidOperationException>(caught.Exception);
    }

    // ---- 第 2 行：UpdateAsync × 实体说活跃但 DeletedToken≠0 ----
    [Fact]
    public async Task 行2_UpdateAsync_实体IsDeleted假但DeletedToken非0_抛异常()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "乙" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        // 实体自称活跃（IsDeleted=false），但 token 还是回收站里的非 0 值。
        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        trashed.IsDeleted = false;
        var caught = await CatchAsync(() => PersistenceWriter.UpdateAsync(db, trashed, Options(1), ct));
        Console.WriteLine($"[行2] caught={caught.Type} msg={Trim(caught.Message)}");

        Assert.NotNull(caught.Exception);
        Assert.IsType<InvalidOperationException>(caught.Exception);
    }

    // ---- 第 3 行：UpdateAsync × 干净活跃快照，库已删（老登 R1 / 我的 M1） ----
    [Fact]
    public async Task 行3_UpdateAsync_干净活跃快照而库已删_返回InTrash()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "丙" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var version = loaded.Version;
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        var snapshot = new ProbeUser { TenantId = 1, UserName = "丙-改", Version = version };
        snapshot.ForceId(loaded.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, snapshot, Options(1), ct);
        Console.WriteLine($"[行3] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} msg={Trim(failure?.Error.Message)}");

        Assert.Equal(PersistenceFailureKind.RecordInTrash, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
    }

    // ---- 第 4 行：SoftDeleteAsync × 重复软删（老登 R5 幂等口径） ----
    [Fact]
    public async Task 行4_SoftDeleteAsync_重复软删_返回InTrash()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "丁" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var failure = await PersistenceWriter.SoftDeleteAsync(db, trashed, Options(1), ct);
        Console.WriteLine($"[行4] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} msg={Trim(failure?.Error.Message)}");

        Assert.Equal(PersistenceFailureKind.RecordInTrash, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
    }

    // ---- 第 5 行：RestoreAsync × 实体自称活跃（DTO 形态）→ 现状抛异常，正是 09 会冒 500 的那条 ----
    [Fact]
    public async Task 行5_RestoreAsync_实体IsDeleted为假_现状抛异常()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "戊" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));
        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        // DTO 从 HTTP 体转回来的实体：IsDeleted 默认 false，版本与 Id 都对。
        var dto = new ProbeUser { TenantId = 1, UserName = "戊", Version = trashed.Version };
        dto.ForceId(trashed.Id);
        var caught = await CatchAsync(() => PersistenceWriter.RestoreAsync(db, dto, Options(1), ct));
        Console.WriteLine($"[行5] caught={caught.Type} msg={Trim(caught.Message)} ← 09 若只接返回值，这条冒 500");

        Assert.NotNull(caught.Exception);
        Assert.IsType<InvalidOperationException>(caught.Exception);
    }

    // ---- 第 6 行：RestoreAsync × 实体说删但库活跃 ----
    [Fact]
    public async Task 行6_RestoreAsync_实体说删而库活跃_返回VersionConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "己" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 实体硬说自己是删除态，但库里这行是活跃的。
        var lying = new ProbeUser
        {
            TenantId = 1,
            UserName = "己",
            Version = loaded.Version,
            IsDeleted = true,
            DeletedToken = loaded.Id,
        };
        lying.ForceId(loaded.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, lying, Options(1), ct);

        var stored = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[行6] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} "
            + $"msg={Trim(failure?.Error.Message)} 库里IsDeleted={stored.IsDeleted} Version={stored.Version}");

        Assert.Equal(PersistenceFailureKind.VersionConflict, failure!.Kind);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.False(stored.IsDeleted);
    }

    // ---- 正路：RestoreAsync 正常恢复 ----
    [Fact]
    public async Task 正路_RestoreAsync_正常恢复成功()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "庚" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));

        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var failure = await PersistenceWriter.RestoreAsync(db, trashed, Options(1), ct);
        var restored = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Console.WriteLine(
            $"[正路] failure={(failure is null ? "null（成功）" : failure.Error.Code)} 活跃记录=1 "
            + $"IsDeleted={restored.IsDeleted} DeletedToken={restored.DeletedToken} Version={restored.Version}");

        Assert.Null(failure);
        Assert.False(restored.IsDeleted);
        Assert.Equal(0, restored.DeletedToken);
    }

    // ---- 三种 409 的文案是否互不相同（老卫写死的要求） ----
    [Fact]
    public void 三种409文案互不相同()
    {
        var messages = new[]
        {
            PersistenceErrors.VersionConflict().Error.Message,
            PersistenceErrors.RecordInTrash().Error.Message,
            PersistenceErrors.NotFound().Error.Message,
        };
        Console.WriteLine($"[文案] VersionConflict={Trim(messages[0])} | InTrash={Trim(messages[1])} | NotFound={Trim(messages[2])}");
        Assert.Equal(3, messages.Distinct(StringComparer.Ordinal).Count());
    }

    private async Task<(TestDatabase Database, SqlSugar.SqlSugarClient Db)> SetupAsync(CancellationToken ct)
    {
        var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        return (database, PzdPersistence.CreateClient(database.ConnectionString, Options(1)));
    }

    private static async Task<(Exception? Exception, string Type, string Message)> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return (null, "无异常", string.Empty);
        }
        catch (Exception exception)
        {
            return (exception, exception.GetType().Name, exception.Message);
        }
    }

    private static string Trim(string? message) =>
        message is null ? "null" : message.Length <= 60 ? message : message[..60] + "…";

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
