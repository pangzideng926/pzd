using Pzd.Infrastructure;
using Pzd.IntegrationTests.Fixtures;
using Pzd.SharedKernel;

namespace Pzd.IntegrationTests;

/// <summary>审计五审探针：4c45fba 上验证两条 medium 的修复边界。跑完删除，不要提交。</summary>
public sealed class AuditRound5Probes(MySqlServerFixture fixture)
{
    private const long CurrentUser = 7;

    private const string LogPath = "E:/pzd/.tmp/2026-10-30-06五审/probe-results.txt";

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

    /// <summary>P1（四审 medium 1 / C11）：插入删除态实体应当被拒，且不留幽灵行。</summary>
    [Fact]
    public async Task P1_插入删除态实体()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        try
        {
            await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "幽灵", IsDeleted = true }, Options(), ct);
            Log("P1 InsertAsync(IsDeleted=true)：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            var rows = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
            Log($"P1 InsertAsync(IsDeleted=true)：抛 {exception.GetBaseException().GetType().Name}，物理行数={rows}");
        }

        var again = await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "幽灵" }, Options(), ct);
        var active = await db.Queryable<ProbeUser>().CountAsync(ct);
        Log($"P1 随后同名活跃插入：failure={(again is null ? "null（成功）" : again.Kind.ToString())}；活跃记录数={active}");
    }

    /// <summary>P2（四审 medium 2 / C9）：存量「IsDeleted=1 且 token=0」的行，两条用法各是什么结果。</summary>
    [Fact]
    public async Task P2_存量删除行()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "存量删除" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        await database.ExecuteAsync(
            $"UPDATE probe_user SET IsDeleted = 1, DeletedToken = 0, DeletedAt = NOW(6), DeletedBy = 5 WHERE Id = {loaded.Id}",
            ct);

        // 用法一：原样（IsDeleted=true）拿去 UpdateAsync。
        var asDeleted = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        try
        {
            await PersistenceWriter.UpdateAsync(db, asDeleted, Options(), ct);
            Log("P2 用法一（IsDeleted=true）：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            Log($"P2 用法一（IsDeleted=true）：抛 {exception.GetBaseException().GetType().Name}");
        }

        // 用法二：调用方把 IsDeleted 改回 false，再走 UpdateAsync（能不能悄悄复活）。
        var asActive = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        asActive.IsDeleted = false;
        asActive.UserName = "存量删除-改名";
        string outcome;
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, asActive, Options(), ct);
            outcome = $"failure={(failure is null ? "null（成功）" : failure.Kind.ToString())}";
        }
        catch (Exception exception)
        {
            outcome = $"抛 {exception.GetBaseException().GetType().Name}";
        }

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedAt残留=', ISNULL(DeletedAt) = 0, ' UserName=', UserName, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"P2 用法二（改成 IsDeleted=false）：{outcome}；库里 {state}");
    }

    /// <summary>P3（四审 C10）：删除态实体直接走 UpdateAsync 应当被拒，且库里不动。</summary>
    [Fact]
    public async Task P3_删除态实体更新()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "删了再改" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));

        loaded.UserName = "删了再改-2";
        try
        {
            await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
            Log("P3 删除态实体 UpdateAsync：未抛异常（应当抛）");
        }
        catch (Exception exception)
        {
            var state = await database.QueryScalarAsync<string>(
                $"SELECT CONCAT('IsDeleted=', IsDeleted, ' UserName=', UserName, ' Version=', Version, ' DeletedToken=当前行') FROM probe_user WHERE Id = {loaded.Id}",
                ct);
            Log($"P3 删除态实体 UpdateAsync：抛 {exception.GetBaseException().GetType().Name}；库里 {state}");
        }
    }

    /// <summary>P4（内存与库一致性）：实体带旧删除审计更新成功后，调用方手里的对象是什么样。</summary>
    [Fact]
    public async Task P4_内存实体与库一致性()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "不碰审计" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        var stale = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        loaded.IsDeleted = false;
        loaded.DeletedAt = stale;
        loaded.DeletedBy = 5;
        loaded.UserName = "改过名";

        var failure = await PersistenceWriter.UpdateAsync(db, loaded, Options(), ct);
        var dbAt = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('DeletedAt为NULL=', ISNULL(DeletedAt), ' DeletedBy=', DeletedBy, ' IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"P4 更新后：failure={(failure is null ? "null" : failure.Kind.ToString())}；库里 {dbAt}");
        Log($"P4 更新后：内存实体 DeletedAt={(loaded.DeletedAt is null ? "NULL" : loaded.DeletedAt.Value.ToString("O"))}、DeletedBy={loaded.DeletedBy}、IsDeleted={loaded.IsDeleted}、DeletedToken={loaded.DeletedToken}、Version={loaded.Version}");
    }

    /// <summary>P5：软删除与恢复仍写全/清空四列（防这轮改动误伤）。</summary>
    [Fact]
    public async Task P5_软删除与恢复不回归()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "软删恢复" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));

        var afterDelete = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' Token等于Id=', DeletedToken = Id, ' DeletedBy=', DeletedBy, ' DeletedAt为NULL=', ISNULL(DeletedAt)) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"P5 软删除后：{afterDelete}");

        var archived = (await db.Queryable<ProbeUser>().ClearFilter().ToListAsync(ct)).Single(user => user.Id == loaded.Id);
        var failure = await PersistenceWriter.RestoreAsync(db, archived, Options(), ct);
        var afterRestore = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' DeletedBy=', DeletedBy, ' DeletedAt为NULL=', ISNULL(DeletedAt)) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"P5 恢复后：failure={(failure is null ? "null" : failure.Kind.ToString())}；{afterRestore}");
    }

    /// <summary>P6：旁路未回归（Updateable 无条件拒，Deleteable 条件形态仍物理删）。</summary>
    [Fact]
    public async Task P6_旁路状态()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "旁路" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();

        loaded.TenantId = 2;
        try
        {
            await db.Updateable(loaded).ExecuteCommandAsync(ct);
            Log("P6 Updateable(已加载实体)：未抛异常（已回归）");
        }
        catch (Exception exception)
        {
            Log($"P6 Updateable(已加载实体)：抛 {exception.GetBaseException().GetType().Name}");
        }

        try
        {
            var affected = await db.Deleteable<ProbeUser>().Where(it => it.Id == loaded.Id).ExecuteCommandAsync(ct);
            var physical = await database.QueryScalarAsync<long>("SELECT COUNT(*) FROM probe_user", ct);
            Log($"P6 Deleteable<T>().Where：未抛异常，影响行={affected}，物理行数={physical}");
        }
        catch (Exception exception)
        {
            Log($"P6 Deleteable<T>().Where：抛 {exception.GetBaseException().GetType().Name}");
        }
    }

    /// <summary>P7：活跃记录正常更新仍是 1 条影响行、版本 +1（正路无回归）。</summary>
    [Fact]
    public async Task P7_正路更新()
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
            $"SELECT CONCAT('UserName=', UserName, ' Version=', Version, ' IsDeleted=', IsDeleted, ' UpdatedBy=', UpdatedBy) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        Log($"P7 活跃记录 UpdateAsync：failure={(failure is null ? "null" : failure.Kind.ToString())}；{state}；内存 Version={loaded.Version}");
    }

    /// <summary>P8：脱离实体（不带删除四列）更新一条已软删除的记录，会不会静默成功。</summary>
    [Fact]
    public async Task P8_脱离实体更新已删除记录()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await fixture.CreateDatabaseAsync(ct);
        await database.ExecuteAsync(SchemaSql, ct);
        var db = PzdPersistence.CreateClient(database.ConnectionString, Options());

        Assert.Null(await PersistenceWriter.InsertAsync(db, new ProbeUser { TenantId = 1, UserName = "已删-脱离" }, Options(), ct));
        var loaded = (await db.Queryable<ProbeUser>().ToListAsync(ct)).Single();
        Assert.Null(await PersistenceWriter.SoftDeleteAsync(db, loaded, Options(), ct));

        // 典型形态：DTO 转实体回来更新，没带 DeletedToken/IsDeleted，只带 Id 与 Version。
        var detached = new ProbeUser { TenantId = 1, UserName = "已删-脱离-改名", Version = loaded.Version };
        detached.ForceId(loaded.Id);

        string outcome;
        try
        {
            var failure = await PersistenceWriter.UpdateAsync(db, detached, Options(), ct);
            outcome = $"failure={(failure is null ? "null（成功，调用方以为改成了）" : failure.Kind.ToString())}";
        }
        catch (Exception exception)
        {
            outcome = $"抛 {exception.GetBaseException().GetType().Name}";
        }

        var state = await database.QueryScalarAsync<string>(
            $"SELECT CONCAT('IsDeleted=', IsDeleted, ' DeletedToken=', DeletedToken, ' UserName=', UserName, ' Version=', Version) FROM probe_user WHERE Id = {loaded.Id}",
            ct);
        var activeCount = await db.Queryable<ProbeUser>().CountAsync(ct);
        Log($"P8 脱离实体更新已删除记录：{outcome}；库里 {state}；活跃记录数={activeCount}");
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
