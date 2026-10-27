using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Pzd.SharedKernel.Abstractions;
using Pzd.SharedKernel.Domain;

namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// 领域事件分发器：按事件运行时类型解析 <see cref="IDomainEventHandler{TEvent}"/>，
/// 同一事件的多个处理器按 DI 注册顺序依序执行（无处理器时视为成功）。
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _services;

    public DomainEventDispatcher(IServiceProvider services) => _services = services;

    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
                ?? throw new InvalidOperationException($"类型 {handlerType} 缺少 HandleAsync 方法。");

            foreach (var handler in _services.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                var task = (Task?)handleMethod.Invoke(handler, [domainEvent, cancellationToken])
                    ?? throw new InvalidOperationException($"处理器 {handler.GetType()} 未返回 Task。");

                await task.ConfigureAwait(false);
            }
        }
    }
}
