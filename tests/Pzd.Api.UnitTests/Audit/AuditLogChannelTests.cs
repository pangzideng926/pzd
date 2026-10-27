using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pzd.Infrastructure.Audit;

namespace Pzd.Api.UnitTests.Audit;

/// <summary>
/// 票 08：审计走有界 Channel，满时按策略丢弃并计数告警，绝不阻塞请求；容量只从
/// <c>Audit:QueueCapacity</c> 读，非法值启动期失败。
/// </summary>
public class AuditLogChannelTests
{
    [Fact]
    public void Full_channel_drops_entry_counts_it_and_never_blocks_the_caller()
    {
        var logger = new RecordingLogger<AuditLogChannel>();
        var channel = new AuditLogChannel(new AuditOptions { QueueCapacity = 2 }, logger);

        var watch = Stopwatch.StartNew();
        var accepted = channel.TryEnqueue(Entry("a")) & channel.TryEnqueue(Entry("b"));
        var dropped = channel.TryEnqueue(Entry("c"));
        watch.Stop();

        Assert.True(accepted);
        Assert.False(dropped);
        Assert.Equal(1, channel.DroppedCount);
        Assert.True(watch.ElapsedMilliseconds < 500, $"入队不应阻塞请求，实际耗时 {watch.ElapsedMilliseconds}ms");
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("dropped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Queue_capacity_is_read_from_the_audit_section()
    {
        var options = AuditOptions.Load(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:QueueCapacity"] = "7",
        }));

        Assert.Equal(7, options.QueueCapacity);
        Assert.Equal(AuditOptions.DefaultQueueCapacity, AuditOptions.Load(BuildConfiguration()).QueueCapacity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Invalid_queue_capacity_fails_fast(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Audit:QueueCapacity"] = configured,
        });

        Assert.Throws<InvalidOperationException>(() => AuditOptions.Load(configuration));
    }

    private static AuditLogEntry Entry(string action) => new()
    {
        Module = "用户管理",
        Action = action,
        TenantId = 1,
        UserId = "42",
        StatusCode = 200,
        Succeeded = true,
        ElapsedMilliseconds = 1,
        OccurredAt = DateTimeOffset.UnixEpoch,
    };

    private static IConfigurationRoot BuildConfiguration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();
}
