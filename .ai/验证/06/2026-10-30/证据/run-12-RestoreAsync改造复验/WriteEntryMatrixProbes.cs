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

    // ======== 目标态探针（张一一的裁定：RestoreAsync 改库判定、补第五种 NotInTrash）========
    // 这两条现在**应当红**：红的理由就是「改造还没落地」。雨哥分支落地后它们必须转绿，
    // 而任何「只改了枚举/注释、行为没变」的做法都会让它们继续红。

    /// <summary>
    /// 删守卫后的新输入面（一）：DTO 形态恢复一条**不存在**的记录 → 必须 `NotFound`/404。
    /// 去掉实体自报守卫之后，「行在不在」完全由库判定，这条断言的是新表面上没有洞。
    /// </summary>
    [Fact]
    public async Task 新面_恢复不存在的记录返回NotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        var ghost = new ProbeUser { TenantId = 1, UserName = "查无此恢复", Version = 1 };
        ghost.ForceId(9876543213L);
        var failure = await PersistenceWriter.RestoreAsync(db, ghost, Options(1), ct);
        Console.WriteLine($"[新面1] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code}");

        Assert.Equal(PersistenceFailureKind.NotFound, failure!.Kind);
        Assert.Equal(404, failure.HttpStatusCode);
        Assert.Equal("Persistence.Record.NotFound", failure.Error.Code);
    }

    /// <summary>
    /// 删守卫后的新输入面（二）：拿别家租户的已删记录来恢复 → 必须 `NotFound`/404，
    /// 且别家那行仍在回收站、一字未改（不泄露别家存在性，也不跨租户复活）。
    /// </summary>
    [Fact]
    public async Task 新面_恢复别家租户的记录返回NotFound且不动别家数据()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);

        // 别家租户按自己的上下文插入并软删。
        var foreignDb = PzdPersistence.CreateClient(database.ConnectionString, Options(2));
        await foreignDb.Insertable(new ProbeUser { TenantId = 2, UserName = "别家已删" }).ExecuteCommandAsync(ct);
        var foreign = (await foreignDb.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(foreignDb, foreign, Options(2), ct));

        // 甲租户拿着别家那条的 Id 与版本尝试恢复。
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));
        var intruder = new ProbeUser { TenantId = 2, UserName = "别家已删", Version = foreign.Version };
        intruder.ForceId(foreign.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, intruder, Options(1), ct);

        var stillDeleted = await database.QueryScalarAsync<long>(
            $"SELECT IsDeleted FROM probe_user WHERE Id = {foreign.Id}", ct);
        var tenant = await database.QueryScalarAsync<long>(
            $"SELECT TenantId FROM probe_user WHERE Id = {foreign.Id}", ct);
        Console.WriteLine(
            $"[新面2] Kind={failure?.Kind} Http={failure?.HttpStatusCode} Code={failure?.Error.Code} "
            + $"别家 IsDeleted={stillDeleted} TenantId={tenant}");

        Assert.Equal(PersistenceFailureKind.NotFound, failure!.Kind);
        Assert.Equal(404, failure.HttpStatusCode);
        Assert.Equal(1, stillDeleted);
        Assert.Equal(2, tenant);
    }

    /// <summary>
    /// 目标态零（文档第 6 行拆出的另一半）：**已删但版本不符** → 仍应是 `VersionConflict`。
    /// 这条现在就该绿，改造后也必须保持绿——用来防「改库判定」时把版本校验一并弄丢。
    /// </summary>
    [Fact]
    public async Task 稳定_恢复已删但版本不符仍应返回VersionConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "子" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var staleVersion = loaded.Version;
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));
        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        // 目标是删除态（守卫能过），但拿的是删除前的旧版本号 1，库里已经是 2。
        var stale = new ProbeUser
        {
            TenantId = 1,
            UserName = "子",
            Version = staleVersion,
            IsDeleted = true,
            DeletedToken = trashed.Id,
        };
        stale.ForceId(trashed.Id);

        var failure = await PersistenceWriter.RestoreAsync(db, stale, Options(1), ct);
        var stillDeleted = await database.QueryScalarAsync<long>(
            $"SELECT IsDeleted FROM probe_user WHERE Id = {trashed.Id}", ct);
        Console.WriteLine(
            $"[稳定] Kind={failure?.Kind} Code={failure?.Error.Code} 库里IsDeleted={stillDeleted} "
            + $"（库里Version={trashed.Version}，实体拿的是{staleVersion}）");

        Assert.Equal(PersistenceFailureKind.VersionConflict, failure!.Kind);
        Assert.Equal("Persistence.Record.VersionConflict", failure.Error.Code);
        Assert.Equal(1, stillDeleted);
    }

    /// <summary>目标态一（文档第 6 行改造后）：恢复一条活跃记录 → `NotInTrash`/409，文案指向「不在回收站」。</summary>
    [Fact]
    public async Task 目标态_恢复活跃记录应返回NotInTrash且文案说明不在回收站()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "辛" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 实体自称删除态、库里这行是活跃的；改造后要由库判定并给业务态，不再靠实体标记。
        var lying = new ProbeUser
        {
            TenantId = 1,
            UserName = "辛",
            Version = loaded.Version,
            IsDeleted = true,
            DeletedToken = loaded.Id,
        };
        lying.ForceId(loaded.Id);

        var failure = await PersistenceWriter.RestoreAsync(db, lying, Options(1), ct);
        Console.WriteLine($"[目标态1] Kind={failure?.Kind} Code={failure?.Error.Code} msg={Trim(failure?.Error.Message)}");

        Assert.NotNull(failure);
        Assert.Equal("Persistence.Record.NotInTrash", failure!.Error.Code);
        Assert.Equal(409, failure.HttpStatusCode);
        Assert.Contains("不在回收站", failure.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("刷新", failure.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>目标态二（文档第 5 行改造后）：DTO 形态恢复已删记录 → 成功，不抛异常。</summary>
    [Fact]
    public async Task 目标态_DTO形态恢复已删记录应成功而不是抛异常()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "壬" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));
        var trashed = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        // DTO 从 HTTP 体转回来：IsDeleted 默认 false，Id 与 Version 都对。
        var dto = new ProbeUser { TenantId = 1, UserName = "壬", Version = trashed.Version };
        dto.ForceId(trashed.Id);

        var caught = await CatchAsync(() => PersistenceWriter.RestoreAsync(db, dto, Options(1), ct));
        var restored = await db.Queryable<ProbeUser>().CountAsync(ct);
        Console.WriteLine($"[目标态2] caught={caught.Type} 活跃记录={restored} ← 09 若走这条，现状会冒 500");

        Assert.Null(caught.Exception);
        Assert.Equal(1, restored);
    }

    // ======== 文案快照：把每一行「用户实际看到什么」钉住，只比 code 会漏掉的东西 ========

    /// <summary>现行四条业务态的文案快照；任何一条被改动都会在这里体现。</summary>
    [Fact]
    public async Task 文案快照_四种业务态用户看到什么()
    {
        var ct = TestContext.Current.CancellationToken;
        var (database, db) = await SetupAsync(ct);
        await using var _ = database;

        var rows = new List<string>();

        // InTrash：干净活跃快照 + 库已删
        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "癸" }).ExecuteCommandAsync(ct);
        var loadedA = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var versionA = loadedA.Version;
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loadedA, Options(1), ct));
        var snapshot = new ProbeUser { TenantId = 1, UserName = "癸-改", Version = versionA };
        snapshot.ForceId(loadedA.Id);
        var inTrash = await PersistenceWriter.UpdateAsync(db, snapshot, Options(1), ct);
        rows.Add($"InTrash      → {inTrash?.Error.Code} | {inTrash?.Error.Message}");

        // NotFound：不存在的行
        var ghost = new ProbeUser { TenantId = 1, UserName = "查无", Version = 1 };
        ghost.ForceId(9876543211L);
        var notFound = await PersistenceWriter.UpdateAsync(db, ghost, Options(1), ct);
        rows.Add($"NotFound     → {notFound?.Error.Code} | {notFound?.Error.Message}");

        // VersionConflict：版本被改掉
        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "冲突" }).ExecuteCommandAsync(ct);
        var stale = (await db.Queryable<ProbeUser>().Where(it => it.UserName == "冲突").ToListAsync(ct)).Single();
        var fresh = (await db.Queryable<ProbeUser>().Where(it => it.UserName == "冲突").ToListAsync(ct)).Single();
        fresh.UserName = "冲突-先改";
        Assert.Null(await PersistenceWriter.UpdateAsync(db, fresh, Options(1), ct));
        stale.UserName = "冲突-后改";
        var conflict = await PersistenceWriter.UpdateAsync(db, stale, Options(1), ct);
        rows.Add($"VersionConflict → {conflict?.Error.Code} | {conflict?.Error.Message}");

        foreach (var row in rows)
        {
            Console.WriteLine($"[文案快照] {row}");
        }

        // 每条都必须有非空文案，且三条两两不同——只断言 code 会漏掉「code 对、文案误导」。
        Assert.All(rows, row => Assert.Contains("| ", row, StringComparison.Ordinal));
        Assert.Equal(3, rows.Select(row => row[(row.IndexOf('|') + 1)..].Trim()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Persistence.Record.VersionConflict", conflict!.Error.Code);
        Assert.Equal("Persistence.Record.NotFound", notFound!.Error.Code);
        Assert.Equal("Persistence.Record.InTrash", inTrash!.Error.Code);
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
