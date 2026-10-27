namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户元数据来源。每次解析都要重新查（不允许跨请求缓存「已通过」的结论，架构 §6.2）。
/// </summary>
public interface ITenantStore
{
    Task<TenantInfo?> FindByIdAsync(long tenantId, CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
