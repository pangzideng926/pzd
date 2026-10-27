namespace Pzd.SharedKernel.Domain;

/// <summary>
/// 领域事件收集者的最小契约：让持久化基础设施无需了解具体聚合类型即可取出并清空事件。
/// </summary>
public interface IDomainEventCollector
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearEvents();
}
