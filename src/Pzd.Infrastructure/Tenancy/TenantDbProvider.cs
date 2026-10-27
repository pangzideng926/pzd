using Pzd.SharedKernel.Abstractions;
using SqlSugar;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户连接工厂：按需为租户建立连接（懒加载 + 双检缓存），返回该租户的连接作用域。
/// 启动时不预加载任何租户连接；每个租户独立 scope，禁止在全局单例上 ChangeDatabase。
/// </summary>
public sealed class TenantDbProvider : ITenantDbProvider
{
    private readonly TenantConnectionCache _connectionCache;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantContext _tenantContext;

    public TenantDbProvider(
        TenantConnectionCache connectionCache,
        ITenantStore tenantStore,
        ITenantContext tenantContext)
    {
        _connectionCache = connectionCache;
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
    }

    public Task<ISqlSugarClient> GetConnectionScopeAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant)
        {
            throw new TenantNotResolvedException(TenantErrors.NotResolved);
        }

        return GetConnectionScopeAsync(_tenantContext.TenantId, cancellationToken);
    }

    public async Task<ISqlSugarClient> GetConnectionScopeAsync(
        long tenantId,
        CancellationToken cancellationToken = default)
    {
        var configId = TenantConnectionCache.ConfigIdOf(tenantId);

        var scope = await _connectionCache.GetOrCreateAsync(
            tenantId,
            async token =>
            {
                var tenant = await _tenantStore.FindByIdAsync(tenantId, token).ConfigureAwait(false)
                    ?? throw new TenantNotResolvedException(TenantErrors.NotFound);

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
            },
            cancellationToken).ConfigureAwait(false);

        return scope.GetConnectionScope(configId);
    }
}
