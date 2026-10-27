using Microsoft.Extensions.Logging;
using Quartz;

namespace Pzd.Infrastructure.Jobs;

/// <summary>
/// 心跳任务（票 09 示例任务）：只证明「Quartz 起来了、Trigger 到点会执行、执行有日志」，
/// 不碰任何租户库（架构 §11.2 的平台任务）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DisallowConcurrentExecutionAttribute"/>：同一 JobKey 不并发执行（架构 §11.5）。
/// </para>
/// <para>
/// 时区由 <see cref="JobDefinitionAttribute.TimeZoneId"/> 决定（默认 Asia/Shanghai），
/// Trigger 在 <see cref="QuartzRegistration"/> 里显式 <c>InTimeZone</c>。
/// </para>
/// <para>
/// 本任务**不得**调用 <c>ITenantDbProvider</c>：它是后台路径，票 07 的架构规则会把它判为违规。
/// </para>
/// </remarks>
[DisallowConcurrentExecution]
[JobDefinition("heartbeat", "0 0/1 * * * ?", Description = "Quartz 心跳：每分钟一次，用于验证调度器与持久化")]
public sealed class HeartbeatJob(ILogger<HeartbeatJob> logger) : IJob
{
    /// <summary>日志/断言里用来识别心跳的固定前缀。</summary>
    public const string LogMarker = "Heartbeat job fired";

    /// <inheritdoc />
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "{Marker}: fireInstanceId={FireInstanceId}, scheduledFireTimeUtc={ScheduledFireTimeUtc}, nextFireTimeUtc={NextFireTimeUtc}",
            LogMarker,
            context.FireInstanceId,
            context.ScheduledFireTimeUtc,
            context.NextFireTimeUtc);

        return ValueTask.CompletedTask;
    }
}
