namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 内存租户库（本轮实现）：租户编码比较不区分大小写，步骤 3 换成 master 库查询 + HybridCache。
/// </summary>
public sealed class InMemoryTenantStore : ITenantStore
{
    private readonly IReadOnlyDictionary<long, TenantInfo> _byId;
    private readonly IReadOnlyDictionary<string, TenantInfo> _byCode;

    public InMemoryTenantStore(IEnumerable<TenantInfo> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        var list = tenants.ToArray();
        _byId = list.ToDictionary(tenant => tenant.Id);
        _byCode = list.ToDictionary(tenant => tenant.Code, StringComparer.OrdinalIgnoreCase);
    }

    public Task<TenantInfo?> FindByIdAsync(long tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.GetValueOrDefault(tenantId));

    public Task<TenantInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(code) ? null : _byCode.GetValueOrDefault(code));
}
