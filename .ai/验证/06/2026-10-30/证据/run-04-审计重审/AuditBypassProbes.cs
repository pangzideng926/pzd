using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计交叉印证：验证「绕过 PersistenceWriter 直接用 ORM」这条路。跑完删除。</summary>
public sealed class AuditBypassProbes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-audit-06c/bypass-results.txt";

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

    /// <summary>B1：不带实体的 Deleteable 会不会绕过 AOP，物理删掉？</summary>
    [Fact]
    public async Task B1_不带实体的Deleteable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "条件删" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        try
        {
            var affected = await db.Deleteable<ProbeUser>()
                .Where(it => it.Id == loaded.Id)
                .ExecuteCommandAsync(ct);
            var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
            Log($"B1 Deleteable<T>().Where 未抛异常，影响行={affected}，物理行数={physical}");
        }
        catch (Exception exception)
        {
            Log($"B1 Deleteable<T>().Where 抛异常 {exception.GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>B2：Updateable(entity) 能不能整行搬租户、抹创建审计、让版本不自增？</summary>
    [Fact]
    public async Task B2_直接Updateable实体()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "直改" }).ExecuteCommandAsync(ct);
        var baseline = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 入侵者：只有 Id 与目标租户，其他字段都是默认值
        var intruder = new ProbeUser { TenantId = 2, UserName = "直改-被搬" };
        intruder.ForceId(baseline.Id);
        var affected = await db.Updateable(intruder).ExecuteCommandAsync(ct);

        var after = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('TenantId=', TenantId, ' UserName=', UserName, ' CreatedBy=', CreatedBy, ' Version=', Version) FROM probe_user WHERE Id = {baseline.Id}",
            ct);
        Log($"B2 Updateable(entity) 影响行={affected}；{after}；基线 CreatedBy={baseline.CreatedBy} Version={baseline.Version}");
    }

    /// <summary>B3：插入时上下文是租户 1、实体填 2 / 不填。</summary>
    [Fact]
    public async Task B3_插入租户()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await db.Insertable(new ProbeUser { TenantId = 2, UserName = "错租户" }).ExecuteCommandAsync(ct);
        await db.Insertable(new ProbeUser { UserName = "没填租户" }).ExecuteCommandAsync(ct);

        var wrong = await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user WHERE UserName = '错租户'", ct);
        var missing = await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user WHERE UserName = '没填租户'", ct);
        Log($"B3 上下文租户=1：填2→库里{wrong}；不填→库里{missing}");
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
        public void ForceId(long id) => Id = id;

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
    }
}
