using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计四审探针：21ce52f 上运行期还剩哪些旁路。跑完删除，不要提交。</summary>
public sealed class AuditRound4Probes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-06四审/probe-results.txt";

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

    /// <summary>C1：不带实体的条件删除，运行期仍无拦截（13 号票静态守卫的专属责任）。</summary>
    [Fact]
    public async Task C1_不带实体的Deleteable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "条件删" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        try
        {
            var affected = await db.Deleteable<ProbeUser>().Where(it => it.Id == loaded.Id).ExecuteCommandAsync(ct);
            var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
            Log($"C1 Deleteable<T>().Where 未抛异常，影响行={affected}，物理行数={physical}");
        }
        catch (Exception exception)
        {
            Log($"C1 Deleteable<T>().Where 抛异常 {exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}");
        }
    }

    /// <summary>C2（李总 P5a / 上轮 high 1）：已加载实体带正确版本，直接 Updateable 现在拦不拦得住。</summary>
    [Fact]
    public async Task C2_已加载实体直接Updateable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "已加载" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        loaded.TenantId = 2;
        loaded.UserName = "已加载-搬走";
        try
        {
            var affected = await db.Updateable(loaded).ExecuteCommandAsync(ct);
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('TenantId=', TenantId, ' UserName=', UserName, ' Version=', Version, ' CreatedBy=', CreatedBy) FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"C2 Updateable(已加载实体 Version=1) 未抛异常，影响行={affected}；{state}");
        }
        catch (Exception exception)
        {
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('TenantId=', TenantId, ' UserName=', UserName, ' Version=', Version, ' CreatedBy=', CreatedBy) FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"C2 Updateable(已加载实体 Version=1) 抛 {exception.GetBaseException().GetType().Name}；库里 {state}");
        }
    }

    /// <summary>C3：插入侧租户校验是否仍生效（实体填 2 / 不填）。</summary>
    [Fact]
    public async Task C3_插入租户校验()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        try
        {
            await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 2, UserName = "错租户" }, Options(), ct);
            Log("C3 实体填 2（上下文 1）：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            Log($"C3 实体填 2（上下文 1）：抛 {exception.GetBaseException().GetType().Name}");
        }

        try
        {
            await PersistenceWriter.InsertAsync(db, new ProbeUser { UserName = "没填租户" }, Options(), ct);
            var filled = await database.QueryScalarAsync<long>("SELECT TenantId FROM probe_user WHERE UserName = '没填租户'", ct);
            Log($"C3 实体不填：库里 TenantId={filled}");
        }
        catch (Exception exception)
        {
            Log($"C3 实体不填：抛 {exception.GetBaseException().GetType().Name}");
        }
    }

    /// <summary>C4：恢复撞唯一键仍返回 DuplicateKey，不出现两条活跃同名。</summary>
    [Fact]
    public async Task C4_恢复撞唯一键()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "恢复" }, Options(), ct));
        var original = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, original, Options(), ct));
        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "恢复" }, Options(), ct));

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == original.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Log($"C4 恢复 failure={(failure is null ? "null（不该成功）" : failure.Kind.ToString())}；活跃记录数={active}");
    }

    /// <summary>C5（李总 P6 / 上轮 high 2）：UpdateAsync 当恢复用，现在还过得去吗。</summary>
    [Fact]
    public async Task C5_用UpdateAsync代替RestoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "错恢复" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));
        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "错恢复" }, Options(), ct));

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        archived.IsDeleted = false;
        string outcome;
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, archived, Options(), ct);
            outcome = $"failure={(failure is null ? "null（未拦）" : failure.Kind.ToString())}";
        }
        catch (Exception exception)
        {
            outcome = $"抛 {exception.GetBaseException().GetType().Name}";
        }

        var token = await database.QueryScalarAsync<long>($"SELECT DeletedToken FROM probe_user WHERE Id = {loaded.Id}", ct);
        var activeSameName = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Count(user => user.UserName == "错恢复");
        Log($"C5 UpdateAsync 当恢复用：{outcome}；残留 DeletedToken={token}；同名活跃记录数={activeSameName}");
    }

    /// <summary>C6：不带实体的 SetColumns 形态（显式带 Version=1）是否也被无条件拒绝。</summary>
    [Fact]
    public async Task C6_SetColumns形态()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "集合改" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        try
        {
            var affected = await db.Updateable<ProbeUser>()
                .SetColumns(it => new ProbeUser { IsDeleted = true, Version = 1 })
                .Where(it => it.Id == loaded.Id)
                .ExecuteCommandAsync(ct);
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"C6 SetColumns(Version=1) 未抛异常，影响行={affected}；{state}");
        }
        catch (Exception exception)
        {
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"C6 SetColumns(Version=1) 抛 {exception.GetBaseException().GetType().Name}；库里 {state}");
        }
    }

    /// <summary>C7：直接 db.Insertable，运行期有没有拦截（静态守卫的专属责任）。</summary>
    [Fact]
    public async Task C7_直接Insertable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        try
        {
            var affected = await db.Insertable(new ProbeUser { TenantId = 1, UserName = "直插" }).ExecuteCommandAsync(ct);
            var state = await database.QueryScalarAsync<string>(
                "SELECT CONCAT('Version=', Version, ' CreatedBy=', CreatedBy, ' DeletedToken=', DeletedToken) FROM probe_user WHERE UserName = '直插'",
                ct);
            Log($"C7 db.Insertable 未抛异常，影响行={affected}；{state}");
        }
        catch (Exception exception)
        {
            Log($"C7 db.Insertable 抛 {exception.GetBaseException().GetType().Name}");
        }
    }

    /// <summary>C8：正路有没有被新校验误伤——活跃记录走 UpdateAsync 应正常成功、版本 1→2。</summary>
    [Fact]
    public async Task C8_活跃记录正常更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "正路" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        loaded.UserName = "正路-改";

        var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('UserName=', UserName, ' Version=', Version, ' DeletedToken=', DeletedToken, ' DeletedAt为NULL=', ISNULL(DeletedAt)) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"C8 活跃记录 UpdateAsync：failure={(failure is null ? "null" : failure.Kind.ToString())}；{state}；内存实体 Version={loaded.Version}");
    }

    /// <summary>C9：存量「IsDeleted=1 且 DeletedToken=0」的历史行，用 UpdateAsync 复活会留下什么。</summary>
    [Fact]
    public async Task C9_存量删除行用UpdateAsync复活()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "存量删除" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        // 模拟旧实现留下的删除行：只置 IsDeleted，没写 DeletedToken。
        await database.ExecuteAsync(
            $"UPDATE probe_user SET IsDeleted = 1, DeletedToken = 0, DeletedAt = NOW(6), DeletedBy = 5 WHERE Id = {loaded.Id}",
            ct);

        var legacy = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        legacy.IsDeleted = false;

        string outcome;
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, legacy, Options(), ct);
            outcome = $"failure={(failure is null ? "null（未拦）" : failure.Kind.ToString())}";
        }
        catch (Exception exception)
        {
            outcome = $"抛 {exception.GetBaseException().GetType().Name}";
        }

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedAt残留=', ISNULL(DeletedAt) = 0, ' DeletedBy=', DeletedBy, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"C9 存量删除行(token=0) 走 UpdateAsync：{outcome}；库里 {state}");
    }

    /// <summary>C10：仍然处于删除态的记录再走 UpdateAsync（IsDeleted 保持 true）应放行且维持删除态。</summary>
    [Fact]
    public async Task C10_删除态记录继续更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "删了再改" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));

        loaded.UserName = "删了再改-2";
        string outcome;
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
            outcome = $"failure={(failure is null ? "null" : failure.Kind.ToString())}";
        }
        catch (Exception exception)
        {
            outcome = $"抛 {exception.GetBaseException().GetType().Name}";
        }

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"C10 删除态记录 UpdateAsync：{outcome}；库里 {state}");
    }

    /// <summary>C11：官方入口 InsertAsync 插入 IsDeleted=true 的实体，会不会造出「删除态但 token=0」的行，并挡住同名新建。</summary>
    [Fact]
    public async Task C11_插入即删除态()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        var seed = new ProbeUser { TenantId = 1, UserName = "种子", IsDeleted = true };
        var insertFailure = await PersistenceWriter.InsertAsync(db, seed, Options(), ct);
        var state = await database.QueryScalarAsync<string>(
            "SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedBy=', DeletedBy, ' DeletedAt为NULL=', ISNULL(DeletedAt), ' Version=', Version) FROM probe_user WHERE UserName = '种子'",
            ct);
        Log($"C11 InsertAsync(IsDeleted=true)：failure={(insertFailure is null ? "null" : insertFailure.Kind.ToString())}；库里 {state}");

        var again = await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "种子" }, Options(), ct);
        var activeCount = await db.Queryable<ProbeUser>().CountAsync(ct);
        var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
        Log($"C11 随后同名活跃插入：failure={(again is null ? "null（成功）" : again.Kind.ToString())}；活跃记录数={activeCount}；物理行数={physical}");
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
