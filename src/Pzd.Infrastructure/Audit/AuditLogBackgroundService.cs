using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 审计后台消费：从 <see cref="AuditLogChannel"/> 批量取条目交给 <see cref="IAuditLogWriter"/>，
/// 请求线程只负责 <c>TryEnqueue</c>。停机时尽量把已入队的条目刷完。
/// </summary>
public sealed class AuditLogBackgroundService : BackgroundService
{
    private readonly AuditLogChannel _channel;
    private readonly IAuditLogWriter _writer;
    private readonly AuditOptions _options;
    private readonly ILogger<AuditLogBackgroundService> _logger;

    public AuditLogBackgroundService(
        AuditLogChannel channel,
        IAuditLogWriter writer,
        AuditOptions options,
        ILogger<AuditLogBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _channel = channel;
        _writer = writer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLogEntry>(_options.BatchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (batch.Count < _options.BatchSize && _channel.Reader.TryRead(out var entry))
                {
                    batch.Add(entry);
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                var current = batch.ToArray();
                batch.Clear();
                await WriteBatchAsync(current, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机，下面把剩余条目尽力刷完。
        }
        finally
        {
            while (batch.Count < _options.BatchSize && _channel.Reader.TryRead(out var remaining))
            {
                batch.Add(remaining);
            }

            if (batch.Count > 0)
            {
                await WriteBatchAsync(batch.ToArray(), CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteBatchAsync(IReadOnlyList<AuditLogEntry> batch, CancellationToken cancellationToken)
    {
        try
        {
            await _writer.WriteAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 落库失败不能打断后台消费，也不能丢出未观察异常：记 Error 后继续。
            _logger.LogError(exception, "Failed to persist an audit batch of {Count} entry(ies).", batch.Count);
        }
    }
}
