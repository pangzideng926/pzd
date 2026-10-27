namespace Pzd.SharedKernel.Abstractions;

/// <summary>
/// 可信租户上下文（Scoped，禁止静态单例保存）。由 TenantResolutionMiddleware 在请求内解析后 Set，
/// 仓储与后台任务通过它选择租户连接。
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    long TenantId { get; }

    void Set(long tenantId);

    void Clear();
}
