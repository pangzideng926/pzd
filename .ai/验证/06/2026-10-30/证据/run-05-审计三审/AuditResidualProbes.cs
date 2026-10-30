using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计三审探针：运行期还剩哪些旁路。跑完删除。</summary>
public sealed class AuditResidualProbes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-audit-06d/residual-results.txt";

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

    /// <summary>C1：不带实体的条件删除，运行期还有没有拦截。</summary>
    [Fact]
    public async Task C1_不带实体的Deleteable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "条件删" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        try
        {
            var affected = await db.Deleteable<ProbeUser>().Where(it => it.Id == loaded.Id).ExecuteCommandAsync(ct);
            var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
            Log($"C1 Deleteable<T>().Where 未抛异常，影响行={affected}，物理行数={physical}");
        }
        catch (Exception exception)
        {
            Log($"C1 Deleteable<T>().Where 抛异常 {exception.GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>C2：已加载实体（Version>0）直接 Updateable，版本闸挡不挡得住。</summary>
    [Fact]
    public async Task C2_已加载实体直接Updateable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        await db.Insertable(new ProbeUser { TenantId = 1, UserName = "已加载" }).ExecuteCommandAsync(ct);
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        loaded.TenantId = 2;
        loaded.UserName = "已加载-搬走";
        try
        {
            var affected = await db.Updateable(loaded).ExecuteCommandAsync(ct);
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('TenantId=', TenantId, ' UserName=', UserName, ' Version=', Version, ' CreatedBy=', CreatedBy) FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"C2 Updateable(已加载实体) 未抛异常，影响行={affected}；{state}");
        }
        catch (Exception exception)
        {
            Log($"C2 Updateable(已加载实体) 抛异常 {exception.GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>C3：插入侧的租户校验（上下文 1，实体填 2 / 不填）。</summary>
    [Fact]
    public async Task C3_插入租户校验()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        try
        {
            await db.Insertable(new ProbeUser { TenantId = 2, UserName = "错租户" }).ExecuteCommandAsync(ct);
            Log("C3 实体填 2：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            Log($"C3 实体填 2：抛 {exception.GetBaseException().GetType().Name}");
        }

        await db.Insertable(new ProbeUser { UserName = "没填租户" }).ExecuteCommandAsync(ct);
        var filled = await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user WHERE UserName = '没填租户'", ct);
        Log($"C3 实体不填：库里 TenantId={filled}");
    }

    /// <summary>C4：恢复时业务键被占用，是否返回 DuplicateKey（409）而不是抛。</summary>
    [Fact]
    public async Task C4_恢复撞唯一键()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        var first = new ProbeUser { TenantId = 1, UserName = "恢复" };
        Assert.Null(await PersistenceWriter.InsertAsync(db, first, Options(), ct));
        var original = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, original, Options(), ct));

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "恢复" }, Options(), ct));

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == original.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Log($"C4 恢复 failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}；活跃记录数={active}");
    }

    /// <summary>C5（李总 P6）：用 UpdateAsync 代替 RestoreAsync 恢复，会不会破唯一索引。</summary>
    [Fact]
    public async Task C5_用UpdateAsync代替RestoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        var first = new ProbeUser { TenantId = 1, UserName = "错恢复" };
        Assert.Null(await PersistenceWriter.InsertAsync(db, first, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "错恢复" }, Options(), ct));

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        archived.IsDeleted = false;
        var failure = await PersistenceWriter.UpdateAsync(db, archived, Options(), ct);

        var token = await database.QueryScalarAsync<long>($"SELECT DeletedToken FROM probe_user WHERE Id = {loaded.Id}", ct);
        var activeSameName = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Count(user => user.UserName == "错恢复");
        Log($"C5 UpdateAsync 当恢复用：failure={(failure is null ? "null（未拦）" : failure.Kind.ToString())}；"
            + $"残留 DeletedToken={token}；同名活跃记录数={activeSameName}");
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
    }
}
