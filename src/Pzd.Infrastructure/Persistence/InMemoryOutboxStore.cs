namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// 内存 Outbox 存储（本轮实现，步骤 3 换落库）。
/// 已处理集合按消息 Id 去重，保证重复投递不产生副作用；被拒绝的消息保留原因但不再出队。
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly Lock _gate = new();
    private readonly List<OutboxMessage> _messages = [];
    private readonly HashSet<Guid> _processed = [];

    internal IReadOnlyList<OutboxMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _messages.RemoveAll(message => message.Id == messageId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var batch = _messages
                .Where(message => message.RejectionReason is null && !_processed.Contains(message.Id))
                .Take(maxCount)
                .ToArray();

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(batch);
        }
    }

    public Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_processed.Contains(messageId));
        }
    }

    public Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _processed.Add(messageId);
            _messages.RemoveAll(message => message.Id == messageId);
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(
        Guid messageId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var message = _messages.Find(item => item.Id == messageId);

            if (message is not null)
            {
                message.Attempts++;
            }
        }

        return Task.CompletedTask;
    }

    public Task RejectAsync(Guid messageId, string reason, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var message = _messages.Find(item => item.Id == messageId);

            if (message is not null)
            {
                message.RejectionReason = reason;
            }
        }

        return Task.CompletedTask;
    }
}
