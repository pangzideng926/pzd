namespace Pzd.SharedKernel;

/// <summary>
/// 聚合根基类：守护不变量，状态只通过方法变更、属性 <c>private set</c>（架构书 §3.3）。
/// 领域事件在聚合内按添加顺序收集，同一个事件实例重复添加只保留一次，提交前由 UoW 分发并清空。
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>本聚合待分发的领域事件，按加入顺序排列。</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>收集一个领域事件；同一实例重复收集不会产生第二条。</summary>
    public void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (_domainEvents.Exists(existing => ReferenceEquals(existing, domainEvent)))
        {
            return;
        }

        _domainEvents.Add(domainEvent);
    }

    /// <summary>分发完成后清空，避免重复分发。</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
