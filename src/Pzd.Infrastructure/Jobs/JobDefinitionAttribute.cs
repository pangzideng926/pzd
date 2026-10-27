namespace Pzd.Infrastructure.Jobs;

/// <summary>
/// 任务定义标记（架构 §11.4）：任务**只来自代码注册**，启动时扫描带本特性的类型，
/// 禁止界面填写类名创建任务。
/// </summary>
/// <remarks>
/// 本特性只承载元数据，不解析 Cron；Cron 与时区的合法性由
/// <see cref="QuartzRegistration.DiscoverJobs"/> 在启动期统一校验，非法值直接启动失败，
/// 不静默退回默认值（与 Outbox:MaxAttempts 同一套口径）。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JobDefinitionAttribute : Attribute
{
    /// <summary>默认时区：票面要求 Trigger 显式 <c>Asia/Shanghai</c>（架构 §11.5）。</summary>
    public const string DefaultTimeZoneId = "Asia/Shanghai";

    /// <param name="name">任务名（JobKey 的 name 部分），启动期校验非空白。</param>
    /// <param name="defaultCron">默认 Cron（Quartz 六段语法，含秒与 <c>?</c>）。</param>
    public JobDefinitionAttribute(string name, string defaultCron)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCron);

        Name = name;
        DefaultCron = defaultCron;
    }

    /// <summary>任务名（JobKey 的 name 部分）。</summary>
    public string Name { get; }

    /// <summary>默认 Cron；可被配置 <c>Quartz:Jobs:&lt;Name&gt;:Cron</c> 覆盖。</summary>
    public string DefaultCron { get; }

    /// <summary>JobKey 的 group 部分，缺省 <see cref="DefaultGroup"/>。</summary>
    public const string DefaultGroup = "pzd";

    /// <summary>任务分组（JobKey 的 group 部分）。</summary>
    public string Group { get; init; } = DefaultGroup;

    /// <summary>时区 ID，缺省 <see cref="DefaultTimeZoneId"/>。</summary>
    public string TimeZoneId { get; init; } = DefaultTimeZoneId;

    /// <summary>任务描述（写进 <c>QRTZ_JOB_DETAILS.DESCRIPTION</c>，便于运维识别）。</summary>
    public string? Description { get; init; }
}
