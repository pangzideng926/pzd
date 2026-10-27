using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Quartz;

namespace Pzd.Infrastructure.Jobs;

/// <summary>
/// Quartz 注册（票 09，架构 §11.3）：主路径 **MySQL AdoJobStore**（表在 master 库），
/// 无连接串时降级为内存存储并让宿主打一条 Warning（CI 兜底，启动不失败）。
/// </summary>
/// <remarks>
/// <para>
/// 任务只来自代码：扫描带 <see cref="JobDefinitionAttribute"/> 的类型，
/// 用特性上的名字/分组/Cron/时区建 JobKey 与 Trigger（架构 §11.4）。
/// </para>
/// <para>
/// 所有配置键都有代码内默认值，<c>appsettings.json</c> 不配也能跑；
/// 一旦显式配错（Cron 非法、布尔值非法、时区不存在）启动期直接失败，不静默退回默认值。
/// </para>
/// </remarks>
public static class QuartzRegistration
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Quartz";

    /// <summary>默认调度器名（票面契约：<c>SchedulerName = "pzd"</c>）。</summary>
    public const string DefaultSchedulerName = "pzd";

    /// <summary>默认连接串名（<c>ConnectionStrings:master</c>）。</summary>
    public const string DefaultConnectionStringName = "master";

    /// <summary>默认实例 ID；集群下每个节点必须不同，<c>AUTO</c> 由 Quartz 按主机名+时间戳生成。</summary>
    public const string DefaultInstanceId = "AUTO";

    /// <summary>加载后的调度器计划；持久化与否、连接串、调度器名都在这里定死。</summary>
    public sealed record QuartzPlan(
        bool UsePersistentStore,
        string? ConnectionString,
        string ConnectionStringName,
        string SchedulerName,
        bool ClusterEnabled,
        string InstanceId);

    /// <summary>一个已校验的任务定义（来自代码扫描 + 配置覆盖）。</summary>
    public sealed record JobRegistration(
        Type JobType,
        string Name,
        string Group,
        string Cron,
        string TimeZoneId,
        string? Description)
    {
        /// <summary>JobKey = (name, group)。</summary>
        public JobKey JobKey => new(Name, Group);

        /// <summary>TriggerKey = (name, group)，与 JobKey 同名便于在库里对照。</summary>
        public TriggerKey TriggerKey => new(Name, Group);
    }

    /// <summary>
    /// 读配置并校验，得到调度器计划。
    /// </summary>
    /// <exception cref="InvalidOperationException">显式配置了非法值时抛出（不静默退回默认值）。</exception>
    public static QuartzPlan LoadPlan(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        var schedulerName = section["SchedulerName"];
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            schedulerName = DefaultSchedulerName;
        }

        var instanceId = section["InstanceId"];
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            instanceId = DefaultInstanceId;
        }

        var clusterEnabled = ReadBool(section["ClusterEnabled"], defaultValue: true);

        var connectionStringName = section["ConnectionStringName"];
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            connectionStringName = DefaultConnectionStringName;
        }

        var connectionString = configuration.GetConnectionString(connectionStringName);
        var usePersistentStore = !string.IsNullOrWhiteSpace(connectionString);

        return new QuartzPlan(
            usePersistentStore,
            usePersistentStore ? connectionString : null,
            connectionStringName,
            schedulerName,
            clusterEnabled,
            instanceId);

        static bool ReadBool(string? raw, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            if (!bool.TryParse(raw, out var parsed))
            {
                throw new InvalidOperationException(
                    $"配置 {SectionName}:ClusterEnabled 必须是 true/false，实际为「{raw}」。");
            }

            return parsed;
        }
    }

    /// <summary>
    /// 启动期探测 master 库是否真的可连：票面要求「实例或凭据不可用时降级为内存存储并打 Warning」，
    /// 而不是让 AdoJobStore 在 Scheduler.Start 时抛异常把宿主带崩。
    /// </summary>
    /// <remarks>
    /// 探测**不是**持久化证据（票面明确禁止「以服务器 ping 代替 AdoJobStore 持久化证据」）；
    /// 它只决定用哪个 JobStore，真正的证据仍是重启后 <c>QRTZ_JOB_DETAILS</c> 里任务还在。
    /// </remarks>
    public static async Task<bool> CanReachMasterAsync(
        string connectionString,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            var builder = new MySqlConnectionStringBuilder(connectionString)
            {
                ConnectionTimeout = (uint)Math.Max(1, Math.Ceiling(timeout.TotalSeconds)),
            };

            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            // 降级路径：连不上就换内存存储，原因由调用方打一条 Warning；异常类型不重要，也不能回显连接串。
            return false;
        }
    }

    /// <summary>
    /// 扫描程序集里带 <see cref="JobDefinitionAttribute"/> 的任务并校验 Cron/时区/重复键。
    /// Jobs 段可用 <c>Quartz:Jobs:&lt;任务名&gt;:Cron</c> 覆盖默认 Cron。
    /// </summary>
    /// <exception cref="InvalidOperationException">任务定义非法时抛出，启动期即失败。</exception>
    public static IReadOnlyList<JobRegistration> DiscoverJobs(IConfiguration configuration, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        assembly ??= typeof(HeartbeatJob).Assembly;

        return DiscoverJobs(configuration, assembly.GetTypes());
    }

    /// <summary>
    /// 同上，但由调用方给定候选类型集合：便于单测用合成的任务类型验证校验分支，
    /// 又不会让合成的非法类型污染整程序集扫描。
    /// </summary>
    public static IReadOnlyList<JobRegistration> DiscoverJobs(IConfiguration configuration, IEnumerable<Type> candidateTypes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(candidateTypes);

        var jobOverrides = configuration.GetSection(SectionName).GetSection("Jobs");

        var registrations = new List<JobRegistration>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in candidateTypes)
        {
            var attribute = type.GetCustomAttribute<JobDefinitionAttribute>(inherit: false);
            if (attribute is null)
            {
                continue;
            }

            if (type.IsAbstract || type.IsInterface || !typeof(IJob).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"任务类型 {type.FullName} 标了 [JobDefinition] 但没实现 Quartz 的 IJob。");
            }

            var cron = jobOverrides.GetSection(attribute.Name)["Cron"];
            if (string.IsNullOrWhiteSpace(cron))
            {
                cron = attribute.DefaultCron;
            }

            if (!CronExpression.TryParse(cron, out _))
            {
                throw new InvalidOperationException(
                    $"任务 {attribute.Name}（{type.Name}）的 Cron「{cron}」不是合法的 Quartz 表达式（需要六段，含秒与 ?）。");
            }

            if (!TimeZoneInfo.TryFindSystemTimeZoneById(attribute.TimeZoneId, out _))
            {
                throw new InvalidOperationException(
                    $"任务 {attribute.Name}（{type.Name}）的时区「{attribute.TimeZoneId}」在本机不可解析。");
            }

            var key = $"{attribute.Name}.{attribute.Group}";
            if (!seenKeys.Add(key))
            {
                throw new InvalidOperationException(
                    $"任务键重复：{key}。JobKey 必须由 [JobDefinition] 的 name + group 唯一确定。");
            }

            registrations.Add(new JobRegistration(
                type,
                attribute.Name,
                attribute.Group,
                cron,
                attribute.TimeZoneId,
                attribute.Description));
        }

        return registrations;
    }

    /// <summary>
    /// 注册 Quartz：持久化存储（连接串存在时）或内存存储（降级），挂载执行记录监听器，
    /// 并按任务定义建 Job + Cron Trigger（时区显式 Asia/Shanghai）。
    /// </summary>
    public static IServiceCollection AddPzdQuartz(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var plan = LoadPlan(configuration);
        var jobs = DiscoverJobs(configuration, assembly);

        return services.AddPzdQuartz(plan, jobs);
    }

    /// <summary>
    /// 用已经加载好的计划与任务列表注册 Quartz（Program 里需要先拿到 plan 打降级 Warning 时用这个重载）。
    /// </summary>
    public static IServiceCollection AddPzdQuartz(
        this IServiceCollection services,
        QuartzPlan plan,
        IReadOnlyList<JobRegistration> jobs)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(jobs);

        services.AddQuartz(q =>
        {
            // Quartz 4：SchedulerName 是只读投影，真正的调度器名走 ConfigureScheduler（落到 QRTZ_*.SCHED_NAME）。
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = plan.SchedulerName;
                options.InstanceId = plan.InstanceId;
            });

            if (plan.UsePersistentStore)
            {
                q.UsePersistentStore(store =>
                {
                    // AdoJobStore + MySqlConnector：连接串只从配置注入，绝不写进代码或日志。
                    store.UseMySqlConnector(plan.ConnectionString!);
                    if (plan.ClusterEnabled)
                    {
                        store.UseClustering(_ => { });
                    }

                    store.UseSystemTextJsonSerializer();
                });
            }
            else
            {
                q.UseInMemoryStore(_ => { });
            }

            q.AddJobListener<JobExecutionLogger>([GroupMatcher<JobKey>.AnyGroup()]);

            foreach (var job in jobs)
            {
                q.AddJob(job.JobType, configurator =>
                {
                    configurator.WithIdentity(job.JobKey);
                    if (!string.IsNullOrWhiteSpace(job.Description))
                    {
                        configurator.WithDescription(job.Description);
                    }
                });

                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(job.TimeZoneId);
                q.AddTrigger(trigger =>
                {
                    trigger
                        .ForJob(job.JobKey)
                        .WithIdentity(job.TriggerKey)
                        .WithCronSchedule(job.Cron, schedule => schedule.InTimeZone(timeZone));
                });
            }
        });

        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }
}
