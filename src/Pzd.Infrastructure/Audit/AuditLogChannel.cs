using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 审计条目的有界 <see cref="Channel{T}"/>（架构 §9.3）：请求线程只做 <see cref="TryEnqueue"/>，
/// 队列满时按策略丢弃并计数告警，绝不阻塞请求。
/// </summary>
public sealed class AuditLogChannel
{
    private readonly Channel<AuditLogEntry> _channel;
    private readonly ILogger<AuditLogChannel> _logger;
    private long _dropped;

    public AuditLogChannel(AuditOptions options, ILogger<AuditLogChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Capacity = options.QueueCapacity;
        _logger = logger;
        _channel = Channel.CreateBounded<AuditLogEntry>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>队列容量（来自 <c>Audit:QueueCapacity</c>）。</summary>
    public int Capacity { get; }

    /// <summary>已丢弃条数（队列满时累计）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>消费者读取端。</summary>
    public ChannelReader<AuditLogEntry> Reader => _channel.Reader;

    /// <summary>入队：永远不阻塞；返回 false 表示队列已满、本条已丢弃。</summary>
    public bool TryEnqueue(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_channel.Writer.TryWrite(entry))
        {
            return true;
        }

        var dropped = Interlocked.Increment(ref _dropped);

        // 丢弃是异常事件：第一条必打，之后每 100 条一条，避免日志风暴反过来压垮请求。
        if (dropped == 1 || dropped % 100 == 0)
        {
            _logger.LogWarning(
                "Audit queue is full; dropped {DroppedCount} entry(ies) (capacity {Capacity}).",
                dropped,
                Capacity);
        }

        return false;
    }

    /// <summary>非阻塞读取一条（消费者批量排空时使用）。</summary>
    public bool TryRead(out AuditLogEntry? entry) => _channel.Reader.TryRead(out entry);
}
