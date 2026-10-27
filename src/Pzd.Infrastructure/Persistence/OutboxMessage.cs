namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// Outbox 消息：领域事件的可重放副本。带 <see cref="TenantId"/> 供后台消费者重建租户上下文，
/// <see cref="Id"/> 用于幂等（已处理集合按 Id 去重）。
/// </summary>
public sealed class OutboxMessage
{
    public required Guid Id { get; init; }

    public required long TenantId { get; init; }

    public required string EventType { get; init; }

    public required string Payload { get; init; }

    public required DateTimeOffset OccurredOn { get; init; }

    public int Attempts { get; set; }

    /// <summary>最近一次投递失败的原因（仅诊断用，不阻止重试）。</summary>
    public string? LastError { get; set; }

    /// <summary>被拒绝的原因（如租户停用/过期）；非空表示不再投递。</summary>
    public string? RejectionReason { get; set; }
}
