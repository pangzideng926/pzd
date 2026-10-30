using SqlSugar;
using SqlSugar.DistributedSystem.Snowflake;

namespace Pzd.Infrastructure;

/// <summary>
/// 主键生成（architecture.md §5.3 雪花 Id）：worker 序号必须来自部署配置，
/// 否则多副本同毫秒必然生成相同 Id（06 审计 medium 2）。
/// </summary>
internal static class PersistenceIdentity
{
    /// <summary>
    /// 按配置造一个 Id 生成器。两个都配了就用独立的 <see cref="IdWorker"/> 实例，
    /// 不碰进程级单例——单例的机器位在首次访问时就固定，之后再改静态字段不会生效（实测）。
    /// </summary>
    public static Func<long> CreateGenerator(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.WorkerId is { } workerId && options.DatacenterId is { } datacenterId)
        {
            var worker = new IdWorker(workerId, datacenterId, 0);
            return worker.NextId;
        }

        options.OnWarning?.Invoke(
            "未配置 PersistenceOptions.WorkerId / DatacenterId，回退到进程级雪花单例（WorkerId=1、DatacenterId=1）："
            + "单实例可用，多副本部署会在同一毫秒生成相同主键。");

        return () => SnowFlakeSingle.Instance.NextId();
    }
}
