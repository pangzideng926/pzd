using Pzd.SharedKernel.Domain;

namespace Pzd.SharedKernel.Abstractions;

/// <summary>领域事件分发器：按事件类型解析并调用 <see cref="IDomainEventHandler{TEvent}"/>。</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}

/// <summary>领域事件处理器：同一事件可有多个处理器，按注册顺序依序执行。</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
