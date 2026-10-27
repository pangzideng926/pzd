using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger)
    {
        _services = services;
        _logger = logger;
    }

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

            var handlers = _services.GetServices(handlerType).Where(handler => handler is not null).ToArray();

            if (handlers.Length == 0)
            {
                _logger.LogWarning(
                    "领域事件 {EventType} 没有注册处理器，事件被忽略。",
                    domainEvent.GetType().Name);
            }

            foreach (var handler in handlers)
            {
                try
                {
                    var task = (Task?)handleMethod.Invoke(handler, [domainEvent, cancellationToken])
                        ?? throw new InvalidOperationException($"处理器 {handler!.GetType()} 未返回 Task。");

                    await task.ConfigureAwait(false);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    // 反射调用把同步异常包成 TargetInvocationException，解包后保持原始异常语义。
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }
        }
    }
}
