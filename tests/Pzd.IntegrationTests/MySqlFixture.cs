using Pzd.Infrastructure;
using Pzd.Infrastructure.Tenancy;

namespace Pzd.IntegrationTests;

/// <summary>
/// 本机 MySQL 集成测试宿主（票 10）：有建库权限时自动建/清 <c>pzd_it_a</c>、<c>pzd_it_b</c>；
/// 无建库权限但显式提供 <c>PZD_TEST_MYSQL_DB_A/B</c> 时改用既有库；实例/凭据缺失时保持不可用，
/// 由用例调用 <see cref="EnsureReady"/> 动态 Skip（而不是抛错）。
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    public const long TenantAId = 10_001;
    public const long TenantBId = 10_002;
    public const long DisabledTenantId = 10_003;

    public const string TenantACode = "it-a";
    public const string TenantBCode = "it-b";
    public const string DisabledTenantCode = "it-halted";

    private bool _createdDatabaseA;
    private bool _createdDatabaseB;

    public string? UnavailableReason { get; private set; }

    public bool IsAvailable => UnavailableReason is null;

    public string Version { get; private set; } = string.Empty;

    internal MySqlTestSettings? Settings { get; private set; }

    public string DatabaseAConnectionString { get; private set; } = string.Empty;

    public string DatabaseBConnectionString { get; private set; } = string.Empty;

    public TenantInfo TenantA { get; private set; } = null!;

    public TenantInfo TenantB { get; private set; } = null!;

    public TenantInfo DisabledTenant { get; private set; } = null!;

    /// <summary>不可用则 Skip（动态原因），可用则什么也不做。</summary>
    public void EnsureReady()
    {
        if (UnavailableReason is not null)
        {
            Assert.Skip(UnavailableReason);
        }
    }

    public async ValueTask InitializeAsync()
    {
        var probe = MySqlAvailability.Probe();

        if (!probe.IsAvailable)
        {
            UnavailableReason = probe.SkipReason;
            return;
        }

        Settings = probe.Settings!;
        Version = probe.Version!;

        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await PrepareDatabasesAsync(probe.Mode, cancellationToken).ConfigureAwait(false);

            DatabaseAConnectionString = Settings.ConnectionStringFor(Settings.DatabaseA);
            DatabaseBConnectionString = Settings.ConnectionStringFor(Settings.DatabaseB);

            await TestSchema.RecreateSchemaAsync(DatabaseAConnectionString, cancellationToken).ConfigureAwait(false);
            await TestSchema.RecreateSchemaAsync(DatabaseBConnectionString, cancellationToken).ConfigureAwait(false);

            TenantA = new TenantInfo(
                TenantAId, TenantACode, TenantStatus.Active, null, DatabaseAConnectionString);
            TenantB = new TenantInfo(
                TenantBId, TenantBCode, TenantStatus.Active, null, DatabaseBConnectionString);
            DisabledTenant = new TenantInfo(
                DisabledTenantId, DisabledTenantCode, TenantStatus.Disabled, null, DatabaseAConnectionString);
        }
        catch (Exception exception) when (MySqlAvailability.IsConnectivityFailure(exception))
        {
            // 探测通过后实例掉线：保持可读的 Skip，而不是把整批用例判红（CI 兜底）。
            UnavailableReason = "MySQL unreachable";
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() => await CleanupAsync().ConfigureAwait(false);

    private async Task PrepareDatabasesAsync(MySqlDatabaseMode mode, CancellationToken cancellationToken)
    {
        var settings = Settings!;

        if (mode == MySqlDatabaseMode.CreateAndDrop)
        {
            TestSchema.EnsureSafeDatabaseName(settings.DatabaseA);
            TestSchema.EnsureSafeDatabaseName(settings.DatabaseB);

            await TestSchema.DropDatabaseAsync(settings.AdminConnectionString, settings.DatabaseA, cancellationToken)
                .ConfigureAwait(false);
            await TestSchema.DropDatabaseAsync(settings.AdminConnectionString, settings.DatabaseB, cancellationToken)
                .ConfigureAwait(false);

            await TestSchema.CreateDatabaseAsync(settings.AdminConnectionString, settings.DatabaseA, cancellationToken)
                .ConfigureAwait(false);
            _createdDatabaseA = true;

            await TestSchema.CreateDatabaseAsync(settings.AdminConnectionString, settings.DatabaseB, cancellationToken)
                .ConfigureAwait(false);
            _createdDatabaseB = true;

            return;
        }

        foreach (var database in new[] { settings.DatabaseA, settings.DatabaseB })
        {
            if (!await TestSchema.DatabaseExistsAsync(settings.AdminConnectionString, database, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"PZD_TEST_MYSQL_DB_A/B 指定的测试库不存在：{database}（仍按测试库前缀安全约束处理）。");
            }
        }
    }

    private async Task CleanupAsync()
    {
        if (Settings is null)
        {
            return;
        }

        var cancellationToken = CancellationToken.None;

        if (_createdDatabaseA)
        {
            await TestSchema.DropDatabaseAsync(Settings.AdminConnectionString, Settings.DatabaseA, cancellationToken)
                .ConfigureAwait(false);
            _createdDatabaseA = false;
        }
        else if (DatabaseAConnectionString.Length > 0)
        {
            // 既有库模式只删测试表，不删库（库可能被其他用途复用）。
            await TestSchema.DropSchemaAsync(DatabaseAConnectionString, cancellationToken).ConfigureAwait(false);
        }

        if (_createdDatabaseB)
        {
            await TestSchema.DropDatabaseAsync(Settings.AdminConnectionString, Settings.DatabaseB, cancellationToken)
                .ConfigureAwait(false);
            _createdDatabaseB = false;
        }
        else if (DatabaseBConnectionString.Length > 0)
        {
            await TestSchema.DropSchemaAsync(DatabaseBConnectionString, cancellationToken).ConfigureAwait(false);
        }

        DatabaseAConnectionString = string.Empty;
        DatabaseBConnectionString = string.Empty;
    }
}

/// <summary>所有 MySQL 用例串行执行，避免多个 fixture 同时建/删同一批测试库。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MySqlCollection : ICollectionFixture<MySqlFixture>
{
    public const string Name = "mysql-integration";
}
