using Pzd.Infrastructure;
using Pzd.Infrastructure.Tenancy;
using SqlSugar;

namespace Pzd.IntegrationTests;

/// <summary>
/// 票 10 主路径：连本机真实 MySQL，两个真实租户库（<c>pzd_it_a</c>/<c>pzd_it_b</c>）证明跨租户隔离，
/// 并证明租户库内的 QueryFilter 是第二道防线（物理行在库中，但带别的 TenantId 就查不到）。
/// </summary>
[Collection(MySqlCollection.Name)]
public class TenantIsolationTests
{
    private readonly MySqlFixture _fixture;

    public TenantIsolationTests(MySqlFixture fixture) => _fixture = fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [IntegrationFact]
    public async Task Tenant_a_and_tenant_b_only_see_rows_in_their_own_database()
    {
        _fixture.EnsureReady();
        await ResetAsync().ConfigureAwait(false);

        await TestSchema.InsertAsync(
            _fixture.DatabaseAConnectionString, 1, MySqlFixture.TenantAId, "a-1", CancellationToken).ConfigureAwait(false);
        await TestSchema.InsertAsync(
            _fixture.DatabaseAConnectionString, 2, MySqlFixture.TenantAId, "a-2", CancellationToken).ConfigureAwait(false);
        await TestSchema.InsertAsync(
            _fixture.DatabaseBConnectionString, 3, MySqlFixture.TenantBId, "b-1", CancellationToken).ConfigureAwait(false);

        var rowsA = await QueryWidgetsAsync(MySqlFixture.TenantAId).ConfigureAwait(false);
        var rowsB = await QueryWidgetsAsync(MySqlFixture.TenantBId).ConfigureAwait(false);

        Assert.Equal([1L, 2L], rowsA.Select(row => row.Id));
        Assert.Equal([3L], rowsB.Select(row => row.Id));
        Assert.All(rowsA, row => Assert.Equal(MySqlFixture.TenantAId, row.TenantId));
        Assert.All(rowsB, row => Assert.Equal(MySqlFixture.TenantBId, row.TenantId));
    }

    [IntegrationFact]
    public async Task QueryFilter_hides_rows_that_carry_another_tenants_id()
    {
        _fixture.EnsureReady();
        await ResetAsync().ConfigureAwait(false);

        // 故意把 B 的 TenantId 写进 A 库（模拟历史脏数据 / 误共享库），A 的查询必须看不到它。
        await TestSchema.InsertAsync(
            _fixture.DatabaseAConnectionString, 10, MySqlFixture.TenantBId, "foreign-b-in-a", CancellationToken)
            .ConfigureAwait(false);
        await TestSchema.InsertAsync(
            _fixture.DatabaseAConnectionString, 11, MySqlFixture.TenantAId, "own-a", CancellationToken)
            .ConfigureAwait(false);
        await TestSchema.InsertAsync(
            _fixture.DatabaseBConnectionString, 20, MySqlFixture.TenantAId, "foreign-a-in-b", CancellationToken)
            .ConfigureAwait(false);
        await TestSchema.InsertAsync(
            _fixture.DatabaseBConnectionString, 21, MySqlFixture.TenantBId, "own-b", CancellationToken)
            .ConfigureAwait(false);

        var rowsA = await QueryWidgetsAsync(MySqlFixture.TenantAId).ConfigureAwait(false);
        var rowsB = await QueryWidgetsAsync(MySqlFixture.TenantBId).ConfigureAwait(false);

        Assert.Equal([11L], rowsA.Select(row => row.Id));
        Assert.Equal([21L], rowsB.Select(row => row.Id));

        // 对照（票 10）：不经过 QueryFilter 的裸 SQL 必须看得见脏行，
        // 否则「过滤后只剩本租户」可能只是数据没插进去的假绿。
        var rawA = await TestSchema.ReadRowsAsync(_fixture.DatabaseAConnectionString, CancellationToken)
            .ConfigureAwait(false);
        var rawB = await TestSchema.ReadRowsAsync(_fixture.DatabaseBConnectionString, CancellationToken)
            .ConfigureAwait(false);

        Assert.Equal([10L, 11L], rawA.Select(row => row.Id));
        Assert.Equal([20L, 21L], rawB.Select(row => row.Id));
        Assert.Contains(rawA, row => row.Id == 10 && row.TenantId == MySqlFixture.TenantBId);
        Assert.Contains(rawB, row => row.Id == 20 && row.TenantId == MySqlFixture.TenantAId);

        // 物理行确实存在：过滤发生在查询层，不是数据没插进去。
        Assert.Equal(2, await TestSchema.CountAllRowsAsync(_fixture.DatabaseAConnectionString, CancellationToken)
            .ConfigureAwait(false));
        Assert.Equal(2, await TestSchema.CountAllRowsAsync(_fixture.DatabaseBConnectionString, CancellationToken)
            .ConfigureAwait(false));
    }

    [IntegrationFact]
    public async Task Tenant_connection_targets_its_own_database()
    {
        _fixture.EnsureReady();

        var databaseA = await QueryDatabaseNameAsync(MySqlFixture.TenantAId).ConfigureAwait(false);
        var databaseB = await QueryDatabaseNameAsync(MySqlFixture.TenantBId).ConfigureAwait(false);

        Assert.Equal(_fixture.Settings!.DatabaseA, databaseA);
        Assert.Equal(_fixture.Settings.DatabaseB, databaseB);
    }

    [IntegrationFact]
    public async Task Local_server_is_mysql_8_0()
    {
        _fixture.EnsureReady();

        Assert.Matches(@"^8\.0\.\d+$", _fixture.Version);
        Assert.NotEmpty(_fixture.Settings!.Host);
    }

    [IntegrationFact]
    public async Task Schema_setup_is_idempotent_and_leaves_no_rows()
    {
        _fixture.EnsureReady();

        await TestSchema.RecreateSchemaAsync(_fixture.DatabaseAConnectionString, CancellationToken).ConfigureAwait(false);
        await TestSchema.RecreateSchemaAsync(_fixture.DatabaseAConnectionString, CancellationToken).ConfigureAwait(false);

        Assert.True(await TestSchema.TableExistsAsync(_fixture.DatabaseAConnectionString, CancellationToken)
            .ConfigureAwait(false));
        Assert.Equal(0, await TestSchema.CountAllRowsAsync(_fixture.DatabaseAConnectionString, CancellationToken)
            .ConfigureAwait(false));
    }

    [Fact]
    public void Database_guard_only_allows_pzd_it_prefixed_databases()
    {
        TestSchema.EnsureSafeDatabaseName("pzd_it_a");
        TestSchema.EnsureSafeDatabaseName("pzd_it_b");
        TestSchema.EnsureSafeDatabaseName("pzd_it_custom_01");

        foreach (var forbidden in new[] { "pzd_master", "mysql", "information_schema", "pzd_t_a", "pzd_it_a; DROP DATABASE x", "" })
        {
            // F-10-A3：不只断类型，还要断「因为库名不安全被拒」，否则别的异常也会让这条假绿。
            var exception = Assert.Throws<InvalidOperationException>(() => TestSchema.EnsureSafeDatabaseName(forbidden));
            Assert.Contains("pzd_it_", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Skip_reasons_are_exactly_the_three_the_ticket_allows()
    {
        // F-10-A5：票面验收要求 Skip 原因只能是这三种之一，用一条断言锁住措辞，防止改一处漏一处。
        Assert.Equal(
            ["MySQL credentials not configured", "MySQL unreachable", "no CREATE DATABASE privilege"],
            MySqlAvailability.SkipReasons);
    }

    [IntegrationFact]
    public async Task This_runs_test_databases_are_present_and_no_system_schema_is_listed()
    {
        _fixture.EnsureReady();

        var databases = TestSchema.ListTestDatabases(_fixture.Settings!.AdminConnectionString, CancellationToken);

        // F-10-A1（审计口径）：这里只证明「本次两个测试库确实建出来了」。
        // - 删掉 Assert.Equal(2, databases.Count)：它把别人的 pzd_it_* 库算成本次失败，
        //   既有库模式 + 机器上残留默认名库时必然误红，票 11 一键验收会被它卡死。
        // - 也不再补 StartsWith("pzd_it_") 之类：查询本身就是 LIKE 'pzd_it_%'，恒为真。
        // 「本次运行不残留」分两层：进程内由 MySqlFixture.CleanupAsync 对本次建过的库复查；
        // 进程外由票 11 的 verify.ps1 跑前/跑后各取一次 pzd_it_% 快照比对（能抓到崩溃残留）。
        Assert.Contains(_fixture.Settings.DatabaseA, databases);
        Assert.Contains(_fixture.Settings.DatabaseB, databases);

        // 防御性保留（按审计清单要求）：LIKE 'pzd_it_%' 已把它排除，这里只作显式声明。
        Assert.DoesNotContain("pzd_master", databases);
    }

    private async Task ResetAsync()
    {
        await TestSchema.TruncateAsync(_fixture.DatabaseAConnectionString, CancellationToken).ConfigureAwait(false);
        await TestSchema.TruncateAsync(_fixture.DatabaseBConnectionString, CancellationToken).ConfigureAwait(false);
    }

    private async Task<List<TenantWidget>> QueryWidgetsAsync(long tenantId)
    {
        var database = await OpenTenantDatabaseAsync(tenantId).ConfigureAwait(false);

        // 票 10 偏差（实测）：SqlSugar 的 QueryFilter 是「每个 async 上下文私有」的，
        // 在 async 的 ITenantDbProvider 内部挂载不会随返回值带到调用方上下文（ContextID 不同）。
        // 因此在真正执行查询的接缝（等价仓储层）显式挂载 `TenantId = 当前租户`，
        // 验证过滤器语义；自动挂载与「每请求 SqlSugarClient」一并留到步骤 3 仓储层落地。
        database.QueryFilter.AddTableFilter<TenantWidget>(widget => widget.TenantId == tenantId);

        return database.Queryable<TenantWidget>()
            .OrderBy(widget => widget.Id)
            .ToList();
    }

    private async Task<string> QueryDatabaseNameAsync(long tenantId)
    {
        var database = await OpenTenantDatabaseAsync(tenantId).ConfigureAwait(false);

        return database.Ado.GetString("SELECT DATABASE()");
    }

    private async Task<ISqlSugarClient> OpenTenantDatabaseAsync(long tenantId)
    {
        var store = new InMemoryTenantStore([_fixture.TenantA, _fixture.TenantB]);
        var context = new TenantContext();
        context.Set(tenantId);

        var provider = new TenantDbProvider(new TenantConnectionCache(), store, context, new SystemClock());

        return await provider.GetCurrentConnectionScopeAsync(CancellationToken).ConfigureAwait(false);
    }
}
