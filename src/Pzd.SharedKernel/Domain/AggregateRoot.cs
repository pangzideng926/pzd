namespace Pzd.SharedKernel.Domain;

/// <summary>
/// 聚合根基类：在实体语义之上收集领域事件，事件由 UnitOfWork 在提交前统一取出分发。
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IDomainEventCollector
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }

    public void ClearEvents() => _domainEvents.Clear();
}
