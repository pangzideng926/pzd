using Microsoft.Extensions.DependencyInjection;
using Pzd.Infrastructure.Persistence;
using Pzd.SharedKernel.Abstractions;
using Pzd.SharedKernel.Domain;

namespace Pzd.Infrastructure.UnitTests.Persistence;

/// <summary>
/// 票 07 / F-07-2：<see cref="DomainEventDispatcher"/> 的反射解析——泛型参数必须取自事件运行时类型，
/// 同一事件的多个处理器按 DI 注册顺序执行，无处理器视为成功。
/// </summary>
public class DomainEventDispatcherTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dispatches_the_same_event_instance_to_the_handler_for_its_runtime_type()
    {
        var domainEvent = new UserRegistered(Guid.NewGuid());
        var handler = new RecordingUserRegisteredHandler();
        var dispatcher = CreateDispatcher(services =>
            services.AddSingleton<IDomainEventHandler<UserRegistered>>(handler));

        await dispatcher.DispatchAsync([domainEvent], CancellationToken);

        // 若泛型参数写成 typeof(IDomainEvent)，这里拿不到处理器，Same 会失败。
        Assert.Same(domainEvent, handler.Received);
    }

    [Fact]
    public async Task Handlers_for_the_same_event_run_in_registration_order()
    {
        var order = new List<string>();
        var dispatcher = CreateDispatcher(services =>
        {
            services.AddSingleton<IDomainEventHandler<UserRegistered>>(new OrderedHandler("first", order));
            services.AddSingleton<IDomainEventHandler<UserRegistered>>(new OrderedHandler("second", order));
        });

        await dispatcher.DispatchAsync([new UserRegistered(Guid.NewGuid())], CancellationToken);

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task An_event_without_handlers_is_dispatched_successfully()
    {
        var dispatcher = CreateDispatcher(_ => { });

        await dispatcher.DispatchAsync([new UserRegistered(Guid.NewGuid())], CancellationToken);
    }

    [Fact]
    public async Task Each_event_in_a_batch_goes_to_its_own_handler_type()
    {
        var registered = new RecordingUserRegisteredHandler();
        var deleted = new RecordingUserDeletedHandler();
        var dispatcher = CreateDispatcher(services =>
        {
            services.AddSingleton<IDomainEventHandler<UserRegistered>>(registered);
            services.AddSingleton<IDomainEventHandler<UserDeleted>>(deleted);
        });

        var registeredEvent = new UserRegistered(Guid.NewGuid());
        var deletedEvent = new UserDeleted(Guid.NewGuid());

        await dispatcher.DispatchAsync([registeredEvent, deletedEvent], CancellationToken);

        Assert.Same(registeredEvent, registered.Received);
        Assert.Same(deletedEvent, deleted.Received);
    }

    private static DomainEventDispatcher CreateDispatcher(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        return new DomainEventDispatcher(services.BuildServiceProvider());
    }

    private sealed record UserRegistered(Guid UserId) : IDomainEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UnixEpoch;
    }

    private sealed record UserDeleted(Guid UserId) : IDomainEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UnixEpoch;
    }

    private sealed class RecordingUserRegisteredHandler : IDomainEventHandler<UserRegistered>
    {
        public UserRegistered? Received { get; private set; }

        public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken = default)
        {
            Received = domainEvent;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUserDeletedHandler : IDomainEventHandler<UserDeleted>
    {
        public UserDeleted? Received { get; private set; }

        public Task HandleAsync(UserDeleted domainEvent, CancellationToken cancellationToken = default)
        {
            Received = domainEvent;

            return Task.CompletedTask;
        }
    }

    private sealed class OrderedHandler : IDomainEventHandler<UserRegistered>
    {
        private readonly string _name;
        private readonly List<string> _order;

        public OrderedHandler(string name, List<string> order)
        {
            _name = name;
            _order = order;
        }

        public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken = default)
        {
            _order.Add(_name);

            return Task.CompletedTask;
        }
    }
}
