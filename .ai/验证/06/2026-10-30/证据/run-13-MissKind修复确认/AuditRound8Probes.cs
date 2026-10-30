using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计八审探针（9c05fa5）：删掉 RestoreAsync 实体自报守卫后新开的输入面。跑完删除，不要提交。</summary>
public sealed class AuditRound8Probes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-misskind-audit/probe-results.txt";

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

    /// <summary>S1：恢复一条根本不存在的记录（DTO 形态，IsDeleted 默认 false）→ 期望 NotFound/404。</summary>
    [Fact]
    public async Task S1_恢复不存在的记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        var ghost = new ProbeUser { TenantId = 1, UserName = "没有这行", Version = 1 };
        ghost.ForceId(999999);
        var failure = await PersistenceWriter.RestoreAsync(db, ghost, Options(1), ct);

        var rows = await database.QueryScalarAsync<int>("SELECT COUNT(*) FROM probe_user", ct);
        Log($"S1 恢复不存在的记录：{Describe(failure)}；物理行数={rows}");
    }

    /// <summary>S2：拿租户 1 的上下文恢复租户 2 的已删记录 → 期望 NotFound/404，且别家那行原样。</summary>
    [Fact]
    public async Task S2_恢复别家租户的已删记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);

        var db2 = PzdPersistence.CreateClient(database.ConnectionString, Options(2));
        Assert.Null(await PersistenceWriter.InsertAsync(db2, new ProbeUser { TenantId = 2, UserName = "别家-恢复" }, Options(2), ct));
        var theirs = (await db2.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db2, theirs, Options(2), ct));

        var purged = (await db2.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        var db1 = PzdPersistence.CreateClient(database.ConnectionString, Options(1));
        var fromBody = new ProbeUser { TenantId = 1, UserName = purged.UserName, Version = purged.Version };
        fromBody.ForceId(purged.Id);
        var failure = await PersistenceWriter.RestoreAsync(db1, fromBody, Options(1), ct);

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken<>0=', DeletedToken<>0, ' TenantId=', TenantId, ' Version=', Version) FROM probe_user WHERE Id = {purged.Id}",
            ct);
        Log($"S2 恢复别家租户的已删记录：{Describe(failure)}；别家那行 {state}");
    }

    /// <summary>S3：没有租户上下文去恢复 → 期望仍抛异常（用错 API 不降级成返回值）。</summary>
    [Fact]
    public async Task S3_无租户上下文恢复()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "无上下文" }, Options(1), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));
        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        var fromBody = new ProbeUser { TenantId = 1, UserName = purged.UserName, Version = purged.Version };
        fromBody.ForceId(purged.Id);

        string result;
        try
        {
            var failure = await PersistenceWriter.RestoreAsync(db, fromBody, Options(null), ct);
            result = $"未抛异常，返回 {Describe(failure)}";
        }
        catch (Exception exception)
        {
            result = $"抛 {exception.GetType().Name}";
        }

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' Version=', Version) FROM probe_user WHERE Id = {purged.Id}",
            ct);
        Log($"S3 无租户上下文恢复：{result}；库里 {state}");
    }

    /// <summary>S4：恢复一条已删但版本不符的记录 → 期望 VersionConflict，不被误恢复。</summary>
    [Fact]
    public async Task S4_恢复已删但版本不符()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options(1));

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "版本不符" }, Options(1), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(1), ct));
        var purged = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single();

        var stale = new ProbeUser { TenantId = 1, UserName = purged.UserName, Version = purged.Version + 99 };
        stale.ForceId(purged.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, stale, Options(1), ct);

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' Version=', Version) FROM probe_user WHERE Id = {purged.Id}",
            ct);
        Log($"S4 恢复已删但版本不符：{Describe(failure)}；库里 {state}");
    }

    private static readonly object LogLock = new();

    private static void Log(string text)
    {
        lock (LogLock)
        {
            File.AppendAllText(LogPath, text + Environment.NewLine, System.Text.Encoding.UTF8);
        }
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
