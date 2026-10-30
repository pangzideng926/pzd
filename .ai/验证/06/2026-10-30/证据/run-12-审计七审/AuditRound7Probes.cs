using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计七审探针：2096a0b 上验证四种返回值与删除列规范化。跑完删除，不要提交。</summary>
public sealed class AuditRound7Probes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-06七审/probe-results.txt";

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

    private static string Describe(PersistenceFailure? failure) => failure is null
        ? "null（成功）"
        : $"{failure.Kind}/{failure.HttpStatusCode}/{failure.Error.Code}";

    /// <summary>R1：回收站记录（DTO 形态）→ 应返回 RecordInTrash 且行不动。</summary>
    [Fact]
    public async Task R1_回收站记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "回收站" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        var detached = new ProbeUser { TenantId = 1, UserName = "回收站-改名", Version = purged.Version };
        detached.ForceId(purged.Id);
        var failure = await PersistenceWriter.UpdateAsync(db, detached, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' UserName=', UserName, ' Version=', Version) FROM probe_user WHERE Id = {purged.Id}",
            ct);
        Log($"R1 更新回收站记录：{Describe(failure)}；文案含 RestoreAsync={failure?.Error.Message.Contains("RestoreAsync", StringComparison.Ordinal)}；库里 {state}");
    }

    /// <summary>R2：记录不存在 → NotFound 404。</summary>
    [Fact]
    public async Task R2_记录不存在()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        var ghost = new ProbeUser { TenantId = 1, UserName = "不存在", Version = 1 };
        ghost.ForceId(1234567890123456789L);
        Log($"R2 更新不存在记录：{Describe(await PersistenceWriter.UpdateAsync(db, ghost, Options(), ct))}");

        var ghostDelete = new ProbeUser { TenantId = 1, UserName = "不存在", Version = 1 };
        ghostDelete.ForceId(1234567890123456788L);
        Log($"R2 软删除不存在记录：{Describe(await PersistenceWriter.SoftDeleteAsync(db, ghostDelete, Options(), ct))}");
    }

    /// <summary>R3：别家租户的行 → NotFound，且别家那行一个字段都不动。</summary>
    [Fact]
    public async Task R3_别家租户()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await database.ExecuteAsync(
            "INSERT INTO probe_user (Id, TenantId, UserName, Version, IsDeleted, DeletedToken, DeletedAt, DeletedBy, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) "
            + "VALUES (42, 2, '别家', 3, 0, 0, NULL, 0, NOW(6), 9, NOW(6), 9)",
            ct);

        var probe = new ProbeUser { TenantId = 1, UserName = "本家", Version = 3 };
        probe.ForceId(42);
        var failure = await PersistenceWriter.UpdateAsync(db, probe, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            "SELECT CONCAT('TenantId=', TenantId, ' UserName=', UserName, ' Version=', Version) FROM probe_user WHERE Id = 42",
            ct);
        Log($"R3 更新别家租户的行：{Describe(failure)}；别家那行 {state}");
    }

    /// <summary>R4：活跃行版本被改掉 → VersionConflict。</summary>
    [Fact]
    public async Task R4_版本冲突()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "并发" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        await database.ExecuteAsync($"UPDATE probe_user SET Version = Version + 1 WHERE Id = {loaded.Id}", ct);
        loaded.UserName = "并发-改";
        Log($"R4 版本被改掉：{Describe(await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct))}；内存 Version={loaded.Version}");
    }

    /// <summary>R5：重复软删除 → RecordInTrash（幂等口径）。</summary>
    [Fact]
    public async Task R5_重复软删除()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "重复删" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        Log($"R5 重复软删除：{Describe(await PersistenceWriter.SoftDeleteAsync(db, archived, Options(), ct))}");
    }

    /// <summary>R6：恢复时版本被改掉 → VersionConflict（不是 InTrash：恢复的目标本来就是删除态）。</summary>
    [Fact]
    public async Task R6_恢复版本冲突()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "恢复并发" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        await database.ExecuteAsync($"UPDATE probe_user SET Version = Version + 1 WHERE Id = {archived.Id}", ct);
        Log($"R6 恢复时版本被改掉：{Describe(await PersistenceWriter.RestoreAsync(db, archived, Options(), ct))}");
    }

    /// <summary>R7：恢复一条「实体说删了、库里其实活跃」的行 → 看语义落在哪一档。</summary>
    [Fact]
    public async Task R7_恢复一条活跃行()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "其实活跃" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var fake = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        fake.IsDeleted = true;
        var failure = await PersistenceWriter.RestoreAsync(db, fake, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"R7 恢复一条活跃行：{Describe(failure)}；库里 {state}");
    }

    /// <summary>R8：Update 把删除四列规范化（实体与库都是活跃态常量）。</summary>
    [Fact]
    public async Task R8_规范化()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "规范化" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        loaded.DeletedAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        loaded.DeletedBy = 5;
        loaded.UserName = "规范化-改";

        var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        var dbState = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedAt为NULL=', ISNULL(DeletedAt), ' DeletedBy=', DeletedBy, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"R8 更新后：{Describe(failure)}；库里 {dbState}");
        Log($"R8 更新后：内存 IsDeleted={loaded.IsDeleted}、DeletedToken={loaded.DeletedToken}、DeletedAt={(loaded.DeletedAt is null ? "NULL" : loaded.DeletedAt.Value.ToString("O"))}、DeletedBy={loaded.DeletedBy}、Version={loaded.Version}");
    }

    /// <summary>R9：正路——更新、软删、恢复各一次，四列与版本都对。</summary>
    [Fact]
    public async Task R9_正路()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "正路" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        loaded.UserName = "正路-改";
        var update = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        var del = await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct);
        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var restore = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('Version=', Version, ' IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedAt为NULL=', ISNULL(DeletedAt), ' DeletedBy=', DeletedBy, ' UserName=', UserName) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"R9 更新={Describe(update)} 软删={Describe(del)} 恢复={Describe(restore)}；{state}");
    }

    /// <summary>R10：恢复时业务键被活跃记录占着 → DuplicateKey，不出现两条活跃同名。</summary>
    [Fact]
    public async Task R10_恢复撞唯一键()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "重名" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "重名" }, Options(), ct));
        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Log($"R10 恢复撞唯一键：{Describe(failure)}；活跃记录数={active}");
    }

    private static readonly object LogLock = new();

    private static void Log(string text)
    {
        lock (LogLock)
        {
            File.AppendAllText(LogPath, text + Environment.NewLine, System.Text.Encoding.UTF8);
        }
    }

    private static PersistenceOptions Options() => new()
    {
        SoftDeleteTypes = [typeof(ProbeUser)],
        CurrentUserId = () => CurrentUser,
        CurrentTenantId = () => 1,
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
