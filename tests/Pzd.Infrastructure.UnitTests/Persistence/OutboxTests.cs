using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pzd.Infrastructure.Persistence;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using SqlSugar;

namespace Pzd.Infrastructure.UnitTests.Persistence;

/// <summary>
/// 票 07：Outbox 存储、投递器与后台服务——租户上下文先 Set、F-05-8 显式重载、
/// 停用/过期拒绝、幂等、失败重试与优雅停止。
/// </summary>
public class OutboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task In_memory_store_returns_pending_messages_in_fifo_order()
    {
        var store = new InMemoryOutboxStore();
        var first = NewMessage(tenantId: 1, "First");
        var second = NewMessage(tenantId: 1, "Second");

        await store.AddAsync(first, CancellationToken);
        await store.AddAsync(second, CancellationToken);

        var batch = await store.DequeueBatchAsync(10, CancellationToken);

        Assert.Equal([first.Id, second.Id], batch.Select(message => message.Id));
    }

    [Fact]
    public async Task In_memory_store_skips_rejected_messages()
    {
        var store = new InMemoryOutboxStore();
        var pending = NewMessage(tenantId: 1, "Pending");
        var rejected = NewMessage(tenantId: 1, "Rejected");

        await store.AddAsync(pending, CancellationToken);
        await store.AddAsync(rejected, CancellationToken);
        await store.RejectAsync(rejected.Id, "Tenant.Disabled: 租户已停用。", CancellationToken);

        var batch = await store.DequeueBatchAsync(10, CancellationToken);

        Assert.Equal([pending.Id], batch.Select(message => message.Id));
        Assert.Contains(store.Messages, message => message.Id == rejected.Id && message.RejectionReason is not null);
    }

    [Fact]
    public async Task Dispatcher_sets_the_tenant_context_before_consuming()
    {
        var store = new InMemoryOutboxStore();
        await store.AddAsync(NewMessage(tenantId: 3), CancellationToken);

        var context = new TenantContext();
        var consumer = new RecordingConsumer(context);
        var dispatcher = CreateDispatcher(store, context, new RecordingTenantDbProvider(), consumer);

        await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(1, consumer.Calls);
        Assert.Equal(3, consumer.SeenTenantId);
    }

    [Fact]
    public async Task Dispatcher_uses_the_explicit_tenant_overload_and_never_the_parameterless_one()
    {
        var store = new InMemoryOutboxStore();
        await store.AddAsync(NewMessage(tenantId: 5), CancellationToken);

        var provider = new RecordingTenantDbProvider();
        var dispatcher = CreateDispatcher(store, new TenantContext(), provider, new RecordingConsumer());

        await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal([5L], provider.ExplicitTenantIds);
        Assert.Equal(0, provider.ParameterlessCalls);
    }

    [Fact]
    public async Task Disabled_tenant_message_is_rejected_with_a_reason_and_not_retried()
    {
        var store = new InMemoryOutboxStore();
        var message = NewMessage(tenantId: 3);
        await store.AddAsync(message, CancellationToken);

        var provider = new RecordingTenantDbProvider
        {
            Rejection = _ => new TenantNotResolvedException(TenantErrors.Disabled),
        };
        var consumer = new RecordingConsumer();
        var dispatcher = CreateDispatcher(store, new TenantContext(), provider, consumer);

        var processed = await dispatcher.DispatchPendingAsync(CancellationToken);
        var processedAgain = await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(0, processed);
        Assert.Equal(0, processedAgain);
        Assert.Equal(0, consumer.Calls);

        var rejected = Assert.Single(store.Messages);
        Assert.Contains("Tenant.Disabled", rejected.RejectionReason);
        Assert.Empty(await store.DequeueBatchAsync(10, CancellationToken));
    }

    [Fact]
    public async Task Expired_tenant_message_is_rejected_with_a_reason_and_not_retried()
    {
        var store = new InMemoryOutboxStore();
        var message = NewMessage(tenantId: 4);
        await store.AddAsync(message, CancellationToken);

        var provider = new RecordingTenantDbProvider
        {
            Rejection = _ => new TenantNotResolvedException(TenantErrors.Expired),
        };
        var consumer = new RecordingConsumer();
        var dispatcher = CreateDispatcher(store, new TenantContext(), provider, consumer);

        await dispatcher.DispatchPendingAsync(CancellationToken);
        await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(0, consumer.Calls);

        var rejected = Assert.Single(store.Messages);
        Assert.Contains("Tenant.Expired", rejected.RejectionReason);
    }

    [Fact]
    public async Task The_same_message_id_is_delivered_only_once()
    {
        var store = new InMemoryOutboxStore();
        var message = NewMessage(tenantId: 1);

        await store.AddAsync(message, CancellationToken);
        await store.AddAsync(message, CancellationToken);

        var consumer = new RecordingConsumer();
        var dispatcher = CreateDispatcher(store, new TenantContext(), new RecordingTenantDbProvider(), consumer);

        await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(1, consumer.Calls);
    }

    [Fact]
    public async Task Delivery_failure_keeps_the_message_pending_for_retry_and_logs_a_warning()
    {
        var store = new InMemoryOutboxStore();
        var message = NewMessage(tenantId: 1);
        await store.AddAsync(message, CancellationToken);

        var consumer = new FailingOnceConsumer();
        var dispatcher = CreateDispatcher(
            store,
            new TenantContext(),
            new RecordingTenantDbProvider(),
            consumer,
            out var logger);

        var firstRun = await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(0, firstRun);
        Assert.Contains(logger.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Single(store.Messages);
        Assert.Equal(1, store.Messages[0].Attempts);

        var secondRun = await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(1, secondRun);
        Assert.Equal(2, consumer.Calls);
        Assert.Empty(store.Messages);
    }

    [Fact]
    public async Task One_failing_message_does_not_block_the_rest_of_the_batch()
    {
        var store = new InMemoryOutboxStore();
        var failing = NewMessage(tenantId: 1, "Failing");
        var healthy = NewMessage(tenantId: 1, "Healthy");
        await store.AddAsync(failing, CancellationToken);
        await store.AddAsync(healthy, CancellationToken);

        var consumer = new SelectiveConsumer("Failing");
        var dispatcher = CreateDispatcher(store, new TenantContext(), new RecordingTenantDbProvider(), consumer);

        var processed = await dispatcher.DispatchPendingAsync(CancellationToken);

        Assert.Equal(1, processed);
        Assert.Equal(["Healthy"], consumer.HandledEventTypes);
        Assert.Equal([failing.Id], store.Messages.Select(message => message.Id));
    }

    [Fact]
    public async Task Hosted_service_stops_gracefully_when_cancelled()
    {
        var store = new InMemoryOutboxStore();
        await store.AddAsync(NewMessage(tenantId: 1), CancellationToken);

        var consumer = new CountingConsumer();
        using var provider = BuildServiceProvider(store, consumer);

        var hosted = new OutboxDispatcherHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxDispatcherHostedService>.Instance,
            TimeSpan.FromMilliseconds(20));

        await hosted.StartAsync(CancellationToken);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (consumer.Calls == 0 && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10, CancellationToken);
        }

        await hosted.StopAsync(CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);

        Assert.True(consumer.Calls >= 1);
    }

    private static ServiceProvider BuildServiceProvider(IOutboxStore store, IOutboxMessageHandler consumer)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton<ITenantContext>(new TenantContext());
        services.AddSingleton<ITenantDbProvider>(new RecordingTenantDbProvider());
        services.AddSingleton(consumer);
        services.AddScoped<OutboxDispatcher>();

        return services.BuildServiceProvider();
    }

    private static OutboxDispatcher CreateDispatcher(
        IOutboxStore store,
        ITenantContext context,
        ITenantDbProvider provider,
        IOutboxMessageHandler consumer) =>
        CreateDispatcher(store, context, provider, consumer, out _);

    private static OutboxDispatcher CreateDispatcher(
        IOutboxStore store,
        ITenantContext context,
        ITenantDbProvider provider,
        IOutboxMessageHandler consumer,
        out RecordingLogger<OutboxDispatcher> logger)
    {
        logger = new RecordingLogger<OutboxDispatcher>();

        return new OutboxDispatcher(store, provider, context, [consumer], logger);
    }

    private static OutboxMessage NewMessage(long tenantId, string eventType = "FakeEvent") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EventType = eventType,
        Payload = "{}",
        OccurredOn = Now,
    };

    private sealed class RecordingTenantDbProvider : ITenantDbProvider
    {
        public List<long> ExplicitTenantIds { get; } = [];

        public int ParameterlessCalls { get; private set; }

        public Func<long, TenantNotResolvedException?>? Rejection { get; init; }

        public Task<ISqlSugarClient> GetConnectionScopeAsync(CancellationToken cancellationToken = default)
        {
            ParameterlessCalls++;

            throw new InvalidOperationException("后台路径不得使用无参重载（F-05-8）。");
        }

        public Task<ISqlSugarClient> GetConnectionScopeAsync(
            long tenantId,
            CancellationToken cancellationToken = default)
        {
            ExplicitTenantIds.Add(tenantId);

            var rejection = Rejection?.Invoke(tenantId);

            if (rejection is not null)
            {
                throw rejection;
            }

            return Task.FromResult<ISqlSugarClient>(new SqlSugarScope(new ConnectionConfig
            {
                ConfigId = tenantId.ToString(),
                ConnectionString = $"Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_{tenantId}",
                DbType = DbType.MySql,
                IsAutoCloseConnection = true,
            }));
        }
    }

    private sealed class RecordingConsumer : IOutboxMessageHandler
    {
        private readonly ITenantContext? _context;

        public RecordingConsumer()
        {
        }

        public RecordingConsumer(ITenantContext context) => _context = context;

        public int Calls { get; private set; }

        public long? SeenTenantId { get; private set; }

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (_context is not null && _context.HasTenant)
            {
                SeenTenantId = _context.TenantId;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailingOnceConsumer : IOutboxMessageHandler
    {
        private bool _failed;

        public int Calls { get; private set; }

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (_failed)
            {
                return Task.CompletedTask;
            }

            _failed = true;

            throw new InvalidOperationException("consumer failed");
        }
    }

    private sealed class SelectiveConsumer : IOutboxMessageHandler
    {
        private readonly string _failingEventType;

        public SelectiveConsumer(string failingEventType) => _failingEventType = failingEventType;

        public List<string> HandledEventTypes { get; } = [];

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (message.EventType == _failingEventType)
            {
                throw new InvalidOperationException("consumer failed");
            }

            HandledEventTypes.Add(message.EventType);

            return Task.CompletedTask;
        }
    }

    private sealed class CountingConsumer : IOutboxMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
