using System.Collections.Concurrent;
using System.Globalization;
using SqlSugar;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户连接缓存（Singleton）：每个租户一个 <see cref="SqlSugarScope"/>，首次使用时按需创建，
/// 之后复用。<see cref="ConcurrentDictionary{TKey,TValue}"/> 保证读写并发安全，
/// 信号量保证同一租户只创建一次、只查一次租户库（F-05-7）。
/// </summary>
/// <remarks>
/// 为什么不是「单例 SqlSugarScope + AddConnection(tid)」：实测 <c>SqlSugarScope.AddConnection</c>
/// 在 async 上下文中是上下文私有的，方法返回后对调用方不可见（每次请求都会重新加、且
/// <c>GetConnectionScope</c> 拿不到），无法满足「按需加载且只加一次」。构造期传入配置的
/// scope 则跨上下文可见，因此改为每租户一个 scope。
/// </remarks>
public sealed class TenantConnectionCache
{
    private readonly ConcurrentDictionary<long, SqlSugarScope> _scopes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SqlSugarScope> GetOrCreateAsync(
        long tenantId,
        Func<CancellationToken, Task<ConnectionConfig>> connectionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        if (_scopes.TryGetValue(tenantId, out var existing))
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_scopes.TryGetValue(tenantId, out var created))
            {
                return created;
            }

            var config = await connectionFactory(cancellationToken).ConfigureAwait(false);
            var scope = new SqlSugarScope(config);
            _scopes[tenantId] = scope;

            return scope;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal bool IsCached(long tenantId) => _scopes.ContainsKey(tenantId);

    internal static string ConfigIdOf(long tenantId) => tenantId.ToString(CultureInfo.InvariantCulture);
}
