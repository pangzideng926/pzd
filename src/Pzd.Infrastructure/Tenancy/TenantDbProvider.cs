using Pzd.SharedKernel.Abstractions;
using SqlSugar;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户连接工厂：按需为租户建立独立连接（懒加载 + 双检缓存），返回该租户的连接作用域。
/// 启动时不预加载任何租户连接；每个租户独立 scope，禁止在共享客户端上 ChangeDatabase。
/// <para>
/// 请求路径（无参重载）由 <see cref="TenantResolutionMiddleware"/> 保证租户状态；
/// 显式租户重载（后台任务用）每次都会重新校验停用/到期，避免后台拿到已停用租户的连接（F-05-3）。
/// </para>
/// </summary>
public sealed class TenantDbProvider : ITenantDbProvider
{
    private readonly TenantConnectionCache _connectionCache;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public TenantDbProvider(
        TenantConnectionCache connectionCache,
        ITenantStore tenantStore,
        ITenantContext tenantContext,
        IClock clock)
    {
        _connectionCache = connectionCache;
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<ISqlSugarClient> GetCurrentConnectionScopeAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant)
        {
            throw new TenantNotResolvedException(TenantErrors.NotResolved);
        }

        return GetScopeAsync(
            _tenantContext.TenantId,
            async token =>
            {
                var tenant = await _tenantStore.FindByIdAsync(_tenantContext.TenantId, token).ConfigureAwait(false)
                    ?? throw new TenantNotResolvedException(TenantErrors.NotFound);

                return tenant;
            },
            cancellationToken);
    }

    public async Task<ISqlSugarClient> GetConnectionScopeAsync(
        long tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantStore.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new TenantNotResolvedException(TenantErrors.NotFound);

        EnsureTenantUsable(tenant);

        return await GetScopeAsync(tenantId, _ => Task.FromResult(tenant), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ISqlSugarClient> GetScopeAsync(
        long tenantId,
        Func<CancellationToken, Task<TenantInfo>> tenantFactory,
        CancellationToken cancellationToken)
    {
        var configId = TenantConnectionCache.ConfigIdOf(tenantId);

        var scope = await _connectionCache.GetOrCreateAsync(
            tenantId,
            async token =>
            {
                var tenant = await tenantFactory(token).ConfigureAwait(false);

                return BuildConnectionConfig(configId, tenant);
            },
            cancellationToken).ConfigureAwait(false);

        return scope.GetConnectionScope(configId);
    }

    private void EnsureTenantUsable(TenantInfo tenant)
    {
        if (tenant.IsDisabled)
        {
            throw new TenantNotResolvedException(TenantErrors.Disabled);
        }

        if (tenant.ExpiresAt is { } expiresAt && expiresAt <= _clock.UtcNow)
        {
            throw new TenantNotResolvedException(TenantErrors.Expired);
        }
    }

    private static ConnectionConfig BuildConnectionConfig(string configId, TenantInfo tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant.ConnectionString))
        {
            throw new TenantNotResolvedException(
                TenantErrors.NotResolved,
                new InvalidOperationException($"Tenant '{configId}' has no connection string."));
        }

        return new ConnectionConfig
        {
            ConfigId = configId,
            ConnectionString = tenant.ConnectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
        };
    }
}
