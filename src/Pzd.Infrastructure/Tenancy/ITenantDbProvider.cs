using SqlSugar;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户连接工厂：按需把租户连接加入共享 <see cref="SqlSugarScope"/>，返回该租户的连接作用域。
/// 不在启动时预加载（架构 §6.2），也禁止在全局单例上 <c>ChangeDatabase</c>。
/// </summary>
public interface ITenantDbProvider
{
    /// <summary>取当前请求租户的连接；没有租户上下文时抛 <see cref="TenantNotResolvedException"/>。</summary>
    Task<ISqlSugarClient> GetConnectionScopeAsync(CancellationToken cancellationToken = default);

    /// <summary>取指定租户的连接（后台任务用）；连接未注册时按需加入。</summary>
    Task<ISqlSugarClient> GetConnectionScopeAsync(long tenantId, CancellationToken cancellationToken = default);
}
