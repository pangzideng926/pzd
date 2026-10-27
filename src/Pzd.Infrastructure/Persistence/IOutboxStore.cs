namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// Outbox 存储：本轮内存实现，步骤 3 换成与业务同库的 outbox 表（与聚合写入同一事务）。
/// </summary>
public interface IOutboxStore
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>取待投递消息（跳过已处理与被拒绝的），按入队顺序。</summary>
    Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(
        int maxCount,
        CancellationToken cancellationToken = default);

    Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>投递失败：累加尝试次数并记录原因，消息保留待重试。</summary>
    Task RecordFailureAsync(Guid messageId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 拒绝投递（终态，如租户停用/过期、重试超上限）：同时记录拒绝原因与最后一次失败原因，
    /// 之后不再返回给消费者；消息本身保留，不等于死信队列。
    /// </summary>
    Task RejectAsync(
        Guid messageId,
        string reason,
        string lastError,
        CancellationToken cancellationToken = default);
}
