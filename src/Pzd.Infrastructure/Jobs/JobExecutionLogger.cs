using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Extensibility;

namespace Pzd.Infrastructure.Jobs;

/// <summary>
/// 任务执行记录器（架构 §11.4）：以 <see cref="IJobListener"/> 形式记录每次触发的
/// 开始、跳过、结束与耗时。
/// </summary>
/// <remarks>
/// 票 09 只落结构化日志（技术日志即可举证）；执行记录表 <c>job_execution_logs</c>
/// 属于业务表，按 A6 留到步骤 3（本票只允许新增 Quartz 官方表）。
/// 记录内容不含连接串、JobDataMap 原文等敏感数据。
/// </remarks>
public sealed class JobExecutionLogger(ILogger<JobExecutionLogger> logger) : IJobListener
{
    /// <summary>监听器名（Quartz 用它在日志里区分监听器）。</summary>
    public const string ListenerName = "pzd-job-execution";

    /// <summary>日志里标识执行记录的固定前缀。</summary>
    public const string LogMarker = "JobExecution";

    /// <inheritdoc />
    public string Name => ListenerName;

    /// <inheritdoc />
    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.LogInformation(
            "{Marker} started: job={JobKey}, trigger={TriggerKey}, fireInstanceId={FireInstanceId}, scheduledFireTimeUtc={ScheduledFireTimeUtc}",
            LogMarker,
            context.JobDetail.Key,
            context.Trigger.Key,
            context.FireInstanceId,
            context.ScheduledFireTimeUtc);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        logger.LogWarning(
            "{Marker} vetoed: job={JobKey}, trigger={TriggerKey}, fireInstanceId={FireInstanceId}",
            LogMarker,
            context.JobDetail.Key,
            context.Trigger.Key,
            context.FireInstanceId);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 失败按 Error 记：票面要求执行记录能区分状态，异常文本只取 Message，不把堆栈/连接串写进结构化字段。
        if (jobException is null)
        {
            logger.LogInformation(
                "{Marker} finished: job={JobKey}, duration={DurationMs}ms, status=Succeeded",
                LogMarker,
                context.JobDetail.Key,
                context.JobRunTime.TotalMilliseconds);
        }
        else
        {
            logger.LogError(
                "{Marker} finished: job={JobKey}, duration={DurationMs}ms, status=Failed, error={Error}",
                LogMarker,
                context.JobDetail.Key,
                context.JobRunTime.TotalMilliseconds,
                jobException.Message);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask JobProgressChanged(IJobExecutionContext context, FireInstanceProgress progress, CancellationToken cancellationToken = default)
    {
        // 票 09 不消费进度（长任务进度推送属于后续 SignalR 需求），保持静默避免日志噪声。
        return ValueTask.CompletedTask;
    }
}
