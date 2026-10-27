using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// Scoped 租户上下文实现：一个请求一个实例，禁止被单例捕获（否则会跨请求串租户）。
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private long? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public long TenantId => _tenantId
        ?? throw new InvalidOperationException("Tenant context has not been set for this request.");

    public void Set(long tenantId) => _tenantId = tenantId;

    public void Clear() => _tenantId = null;
}
