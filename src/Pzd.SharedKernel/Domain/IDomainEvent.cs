namespace Pzd.SharedKernel.Domain;

/// <summary>
/// 领域事件标记接口。聚合在状态变更时收集事件，由 UnitOfWork 在提交前统一分发。
/// 事件只描述已发生的事实，不携带行为。
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
