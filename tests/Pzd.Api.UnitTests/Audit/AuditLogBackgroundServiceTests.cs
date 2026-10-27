using Microsoft.Extensions.Logging.Abstractions;
using Pzd.Infrastructure.Audit;

namespace Pzd.Api.UnitTests.Audit;

/// <summary>票 08：后台服务批量消费 Channel 并交给落库写入器；请求线程只做 TryWrite。</summary>
public class AuditLogBackgroundServiceTests
{
    [Fact]
    public async Task Background_service_drains_the_channel_into_the_writer()
    {
        var options = new AuditOptions { QueueCapacity = 16, BatchSize = 100 };
        var channel = new AuditLogChannel(options, NullLogger<AuditLogChannel>.Instance);
        var writer = new RecordingAuditLogWriter();
        var service = new AuditLogBackgroundService(
            channel,
            writer,
            options,
            NullLogger<AuditLogBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            for (var index = 0; index < 3; index++)
            {
                Assert.True(channel.TryEnqueue(Entry(index)));
            }

            await writer.WaitForCountAsync(3, TimeSpan.FromSeconds(10));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var entries = writer.Batches.SelectMany(batch => batch).ToArray();

        Assert.Equal(3, entries.Length);
        Assert.Equal(["action-0", "action-1", "action-2"], entries.Select(entry => entry.Action));
    }

    private static AuditLogEntry Entry(int index) => new()
    {
        Module = "用户管理",
        Action = $"action-{index}",
        TenantId = 1,
        UserId = "42",
        StatusCode = 200,
        Succeeded = true,
        ElapsedMilliseconds = index,
        OccurredAt = DateTimeOffset.UnixEpoch,
    };
}
