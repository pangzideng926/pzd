using SqlSugar;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户连接工厂：按需为租户建立独立连接（每租户一个 <see cref="SqlSugarScope"/>，懒加载），
/// 返回该租户的连接作用域。禁止在共享客户端上 ChangeDatabase。
/// </summary>
public interface ITenantDbProvider
{
    /// <summary>取当前请求租户的连接；没有租户上下文时抛 <see cref="TenantNotResolvedException"/>。</summary>
    Task<ISqlSugarClient> GetConnectionScopeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 取指定租户的连接（后台任务用）：每次都会校验停用/到期，并按需建立该租户连接。
    /// </summary>
    Task<ISqlSugarClient> GetConnectionScopeAsync(long tenantId, CancellationToken cancellationToken = default);
}
