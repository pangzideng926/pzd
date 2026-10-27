using Microsoft.Extensions.Configuration;
using Pzd.Infrastructure.Jobs;
using Quartz;

namespace Pzd.Infrastructure.UnitTests.Jobs;

/// <summary>
/// 票 09：Quartz 注册的配置契约。
/// <para>
/// 主路径是 MySQL AdoJobStore（有 <c>ConnectionStrings:master</c>）；没有连接串时降级内存存储，
/// 但**绝不**把显式配错的值静默吞掉（与 <c>Outbox:MaxAttempts</c> 同一套口径）。
/// </para>
/// </summary>
public class QuartzRegistrationTests
{
    private const string MasterConnectionStringWithPassword =
        "Server=127.0.0.1;Port=3306;User ID=root;Password=SuperSecret123;Database=pzd_master";

    [Fact]
    public void Plan_falls_back_to_in_memory_store_when_master_connection_string_is_absent()
    {
        var plan = QuartzRegistration.LoadPlan(BuildConfiguration());

        Assert.False(plan.UsePersistentStore);
        Assert.Null(plan.ConnectionString);
        Assert.Equal(QuartzRegistration.DefaultSchedulerName, plan.SchedulerName);
        Assert.True(plan.ClusterEnabled);
        Assert.Equal(QuartzRegistration.DefaultInstanceId, plan.InstanceId);
    }

    [Fact]
    public void Plan_uses_persistent_store_when_master_connection_string_is_present()
    {
        var plan = QuartzRegistration.LoadPlan(BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:master"] = MasterConnectionStringWithPassword,
        }));

        Assert.True(plan.UsePersistentStore);
        Assert.Equal(MasterConnectionStringWithPassword, plan.ConnectionString);
        // 票面：SchedulerName = "pzd" + 集群配置。
        Assert.Equal("pzd", plan.SchedulerName);
        Assert.True(plan.ClusterEnabled);
    }

    [Fact]
    public void Plan_honours_explicit_scheduler_cluster_and_instance_settings()
    {
        var plan = QuartzRegistration.LoadPlan(BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:master"] = "Server=127.0.0.1;Database=pzd_master",
            ["Quartz:SchedulerName"] = "pzd-dev",
            ["Quartz:ClusterEnabled"] = "false",
            ["Quartz:InstanceId"] = "node-1",
        }));

        Assert.Equal("pzd-dev", plan.SchedulerName);
        Assert.False(plan.ClusterEnabled);
        Assert.Equal("node-1", plan.InstanceId);
    }

    [Fact]
    public void Plan_fails_fast_on_invalid_cluster_flag_and_never_echoes_the_password()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:master"] = MasterConnectionStringWithPassword,
            ["Quartz:ClusterEnabled"] = "maybe",
        });

        var error = Assert.Throws<InvalidOperationException>(() => QuartzRegistration.LoadPlan(configuration));

        Assert.Contains("ClusterEnabled", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecret123", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Heartbeat_job_is_discovered_with_asia_shanghai_timezone_and_cron()
    {
        var jobs = QuartzRegistration.DiscoverJobs(BuildConfiguration(), typeof(HeartbeatJob).Assembly);

        var heartbeat = Assert.Single(jobs, job => job.JobType == typeof(HeartbeatJob));
        Assert.Equal("heartbeat", heartbeat.Name);
        Assert.Equal(JobDefinitionAttribute.DefaultGroup, heartbeat.Group);
        Assert.Equal("Asia/Shanghai", heartbeat.TimeZoneId);
        Assert.True(CronExpression.TryParse(heartbeat.Cron, out _));
        Assert.Equal(heartbeat.Name, heartbeat.JobKey.Name);
        Assert.Equal(heartbeat.Name, heartbeat.TriggerKey.Name);
    }

    [Fact]
    public void Heartbeat_job_declares_disallow_concurrent_execution()
    {
        // 票面明写 [DisallowConcurrentExecution]：这条只能靠反射守住，否则改掉没人发现。
        var attribute = typeof(HeartbeatJob)
            .GetCustomAttributes(typeof(DisallowConcurrentExecutionAttribute), inherit: false)
            .Cast<object>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public void Discover_jobs_honours_cron_override_from_configuration()
    {
        var jobs = QuartzRegistration.DiscoverJobs(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["Quartz:Jobs:heartbeat:Cron"] = "0 0/5 * * * ?",
            }),
            [typeof(HeartbeatJob)]);

        var heartbeat = Assert.Single(jobs);
        Assert.Equal("0 0/5 * * * ?", heartbeat.Cron);
    }

    [Fact]
    public void Discover_jobs_fails_fast_on_invalid_cron()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            QuartzRegistration.DiscoverJobs(BuildConfiguration(), [typeof(BrokenCronJob)]));

        Assert.Contains("BrokenCronJob", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_jobs_fails_fast_when_job_type_does_not_implement_ijob()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            QuartzRegistration.DiscoverJobs(BuildConfiguration(), [typeof(NotAJob)]));

        Assert.Contains("IJob", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_jobs_fails_fast_on_duplicate_job_keys()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            QuartzRegistration.DiscoverJobs(BuildConfiguration(), [typeof(DuplicateJobA), typeof(DuplicateJobB)]));

        Assert.Contains("重复", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Job_execution_logger_exposes_a_stable_name()
    {
        Assert.Equal(JobExecutionLogger.ListenerName, "pzd-job-execution");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();
    }

    [JobDefinition("broken-cron", "not a cron")]
    private sealed class BrokenCronJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    [JobDefinition("not-a-job", "0 0/1 * * * ?")]
    private sealed class NotAJob;

    [JobDefinition("duplicate", "0 0/1 * * * ?")]
    private sealed class DuplicateJobA : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    [JobDefinition("duplicate", "0 0/2 * * * ?")]
    private sealed class DuplicateJobB : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
