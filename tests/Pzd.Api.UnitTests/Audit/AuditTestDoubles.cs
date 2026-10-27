using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pzd.Infrastructure.Audit;

namespace Pzd.Api.UnitTests.Audit;

/// <summary>假审计消费者：断言后台服务批量消费到的条目（票 08 验收「假消费者断言」）。</summary>
internal sealed class RecordingAuditLogWriter : IAuditLogWriter
{
    private readonly ConcurrentQueue<IReadOnlyList<AuditLogEntry>> _batches = new();
    private readonly SemaphoreSlim _signal = new(0);
    private int _total;

    public IReadOnlyList<IReadOnlyList<AuditLogEntry>> Batches => _batches.ToArray();

    public int Total => Volatile.Read(ref _total);

    public Task WriteAsync(IReadOnlyList<AuditLogEntry> batch, CancellationToken cancellationToken)
    {
        _batches.Enqueue(batch);
        Interlocked.Add(ref _total, batch.Count);
        _signal.Release();
        return Task.CompletedTask;
    }

    public async Task WaitForCountAsync(int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (Total < expected)
        {
            var remaining = deadline - DateTime.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"审计消费者在 {timeout} 内只收到 {Total} 条，期望 {expected} 条。");
            }

            await _signal.WaitAsync(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50));
        }
    }
}

/// <summary>最小日志捕获器：验证「Channel 满时告警」而不引入测试日志包。</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly Lock _gate = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
