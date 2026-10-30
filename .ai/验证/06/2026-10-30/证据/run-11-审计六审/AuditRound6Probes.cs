using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计六审探针：735de24 上验证分型是否真能支撑 09 票的三个 code。跑完删除，不要提交。</summary>
public sealed class AuditRound6Probes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-06六审/probe-results.txt";

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

    /// <summary>Q1（五审 medium / P8）：脱离实体更新已删除记录，必被拒且行不动。</summary>
    [Fact]
    public async Task Q1_脱离实体更新已删除记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "脱离" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        var detached = new ProbeUser { TenantId = 1, UserName = "脱离-改名", Version = purged.Version };
        detached.ForceId(purged.Id);
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, detached, Options(), ct);
            Log($"Q1 脱离实体更新已删除记录：未抛异常，返回 {(failure is null ? "null（成功）" : failure.Kind.ToString())}（应当抛）");
        }
        catch (Exception exception)
        {
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('IsDeleted=', IsDeleted, ' UserName=', UserName, ' Version=', Version) FROM probe_user WHERE Id = {purged.Id}",
                ct);
            Log($"Q1 脱离实体更新已删除记录：抛 {exception.GetBaseException().GetType().FullName}；库里 {state}");
        }
    }

    /// <summary>Q2（五审 low / P4）：带旧删除审计更新成功后，库与内存各是什么。</summary>
    [Fact]
    public async Task Q2_内存实体与库()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "内存不动" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var stale = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        loaded.DeletedAt = stale;
        loaded.DeletedBy = 5;
        loaded.UserName = "改过名";

        var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        var dbState = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('DeletedAt为NULL=', ISNULL(DeletedAt), ' DeletedBy=', DeletedBy, ' IsDeleted=', IsDeleted, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"Q2 更新后：failure={(failure is null ? "null" : failure.Kind.ToString())}；库里 {dbState}");
        Log($"Q2 更新后：内存 DeletedAt={(loaded.DeletedAt is null ? "NULL" : loaded.DeletedAt.Value.ToString("O"))}、DeletedBy={loaded.DeletedBy}、Version={loaded.Version}");
    }

    /// <summary>Q3：活跃记录版本被改掉 → 返回什么（应与「不存在」区分开）。</summary>
    [Fact]
    public async Task Q3_版本冲突()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "并发" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        await database.ExecuteAsync($"UPDATE probe_user SET Version = Version + 1 WHERE Id = {loaded.Id}", ct);

        var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        Log($"Q3 版本被改掉：failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}");
    }

    /// <summary>Q4：记录不存在 → 返回什么（和 Q3 比，看 09 能不能分开）。</summary>
    [Fact]
    public async Task Q4_记录不存在()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        var ghost = new ProbeUser { TenantId = 1, UserName = "不存在", Version = 1 };
        ghost.ForceId(1234567890123456789L);
        var failure = await PersistenceWriter.UpdateAsync(db, ghost, Options(), ct);
        Log($"Q4 记录不存在：failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}");
    }

    /// <summary>Q5：对已经软删除的记录再软删除一次 → 返回什么（DELETE 幂等性相关）。</summary>
    [Fact]
    public async Task Q5_重复软删除()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "重复删" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var failure = await PersistenceWriter.SoftDeleteAsync(db, archived, Options(), ct);
        Log($"Q5 重复软删除：failure={(failure is null ? "null（幂等成功）" : failure.Kind.ToString())}");
    }

    /// <summary>Q6：恢复时版本被改掉 → 返回什么异常类型（不该被「在回收站」的分型吞掉）。</summary>
    [Fact]
    public async Task Q6_恢复版本冲突()
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

        try
        {
            var failure = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
            Log($"Q6 恢复版本冲突：failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}");
        }
        catch (Exception exception)
        {
            Log($"Q6 恢复版本冲突：抛 {exception.GetBaseException().GetType().FullName}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>Q7：别的租户里同名 Id 的已删记录，本租户更新时会不会漏出「在回收站」。</summary>
    [Fact]
    public async Task Q7_别家租户的回收站记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        // 直接在租户 2 造一条已删记录（Id 与我们要更新的实体相同）。
        await database.ExecuteAsync(
            "INSERT INTO probe_user (Id, TenantId, UserName, Version, IsDeleted, DeletedToken, DeletedAt, DeletedBy, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy) "
            + "VALUES (42, 2, '别家', 3, 1, 42, NOW(6), 9, NOW(6), 9, NOW(6), 9)",
            ct);

        var probe = new ProbeUser { TenantId = 1, UserName = "本家", Version = 3 };
        probe.ForceId(42);
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, probe, Options(), ct);
            Log($"Q7 别家租户的已删记录：failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}");
        }
        catch (Exception exception)
        {
            Log($"Q7 别家租户的已删记录：抛 {exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>Q8：无租户上下文时抛的异常类型（和「在回收站」是不是同一个类型）。</summary>
    [Fact]
    public async Task Q8_无租户上下文的异常类型()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var noTenant = new PersistenceOptions
        {
            SoftDeleteTypes = [typeof(ProbeUser)],
            CurrentUserId = () => CurrentUser,
            CurrentTenantId = () => null,
            WorkerId = 1,
            DatacenterId = 1,
        };
        var db = PzdPersistence.CreateClient(database.ConnectionString, noTenant);

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "无上下文" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        loaded.UserName = "无上下文-改";
        try
        {
            await PersistenceWriter.UpdateAsync(db, loaded, noTenant, ct);
            Log("Q8 无租户上下文：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            Log($"Q8 无租户上下文：抛 {exception.GetBaseException().GetType().FullName}");
        }
    }

    /// <summary>Q9：正路无回归——活跃更新 +1、软删与恢复仍工作。</summary>
    [Fact]
    public async Task Q9_正路()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "正路" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        loaded.UserName = "正路-改";
        Assert.Null(await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct));
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();
        var restore = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('Version=', Version, ' IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedAt为NULL=', ISNULL(DeletedAt), ' UserName=', UserName) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"Q9 更新→软删→恢复：restore failure={(restore is null ? "null" : restore.Kind.ToString())}；{state}");
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
