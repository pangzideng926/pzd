using Pzd.Infrastructure.Persistence;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using Pzd.SharedKernel.Domain;

namespace Pzd.Infrastructure.UnitTests.Persistence;

/// <summary>
/// 票 07：UnitOfWork 的提交顺序（领域事件分发 → 写 Outbox → 事务提交）、失败回滚与事件清除。
/// </summary>
public class UnitOfWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commit_dispatches_events_then_writes_outbox_then_commits_the_transaction()
    {
        var calls = new List<string>();
        var unitOfWork = CreateUnitOfWork(calls, tenantId: 7);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await unitOfWork.CommitAsync([aggregate], CancellationToken);

        Assert.Equal(["dispatch:FakeEvent", "outbox-add:FakeEvent", "commit"], calls);
    }

    [Fact]
    public async Task Handler_exception_prevents_commit_and_leaves_no_outbox_message()
    {
        var calls = new List<string>();
        var transactions = new RecordingTransactionFactory(calls);
        var outbox = new RecordingOutboxStore(calls);
        var unitOfWork = CreateUnitOfWork(
            calls,
            tenantId: 7,
            dispatcher: new ThrowingDomainEventDispatcher(),
            outbox: outbox,
            transactions: transactions);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync([aggregate], CancellationToken));

        Assert.Equal(0, transactions.CommitCalls);
        Assert.Equal(1, transactions.RollbackCalls);
        Assert.Empty(outbox.Added);
        Assert.NotEmpty(aggregate.DomainEvents);
    }

    [Fact]
    public async Task Events_are_cleared_after_commit_and_not_dispatched_again()
    {
        var calls = new List<string>();
        var dispatcher = new RecordingDomainEventDispatcher(calls);
        var unitOfWork = CreateUnitOfWork(calls, tenantId: 7, dispatcher: dispatcher);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await unitOfWork.CommitAsync([aggregate], CancellationToken);

        Assert.Empty(aggregate.DomainEvents);

        await unitOfWork.CommitAsync([aggregate], CancellationToken);

        Assert.Equal(["FakeEvent"], dispatcher.DispatchedTypes);
    }

    [Fact]
    public async Task Outbox_message_carries_the_current_tenant_and_the_event_payload()
    {
        var calls = new List<string>();
        var outbox = new RecordingOutboxStore(calls);
        var unitOfWork = CreateUnitOfWork(calls, tenantId: 7, outbox: outbox);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await unitOfWork.CommitAsync([aggregate], CancellationToken);

        var message = Assert.Single(outbox.Added);
        Assert.Equal(7, message.TenantId);
        Assert.Equal("FakeEvent", message.EventType);
        Assert.Equal(Now, message.OccurredOn);
        Assert.False(string.IsNullOrWhiteSpace(message.Payload));
    }

    [Fact]
    public async Task Missing_tenant_context_throws_and_does_not_commit()
    {
        var calls = new List<string>();
        var transactions = new RecordingTransactionFactory(calls);
        var outbox = new RecordingOutboxStore(calls);
        var unitOfWork = CreateUnitOfWork(calls, tenantId: null, outbox: outbox, transactions: transactions);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync([aggregate], CancellationToken));

        Assert.Equal(0, transactions.CommitCalls);
        Assert.Empty(outbox.Added);
    }

    [Fact]
    public async Task Commit_without_events_still_commits_the_transaction()
    {
        var calls = new List<string>();
        var dispatcher = new RecordingDomainEventDispatcher(calls);
        var transactions = new RecordingTransactionFactory(calls);
        var outbox = new RecordingOutboxStore(calls);
        var unitOfWork = CreateUnitOfWork(
            calls,
            tenantId: 7,
            dispatcher: dispatcher,
            outbox: outbox,
            transactions: transactions);

        await unitOfWork.CommitAsync([], CancellationToken);

        Assert.Equal(["commit"], calls);
        Assert.Empty(dispatcher.DispatchedTypes);
        Assert.Empty(outbox.Added);
        Assert.Equal(1, transactions.CommitCalls);
    }

    [Fact]
    public async Task Commit_failure_rolls_back_and_removes_the_written_outbox_messages()
    {
        var calls = new List<string>();
        var transactions = new RecordingTransactionFactory(calls) { FailOnCommit = true };
        var outbox = new RecordingOutboxStore(calls);
        var unitOfWork = CreateUnitOfWork(calls, tenantId: 7, outbox: outbox, transactions: transactions);

        var aggregate = new FakeAggregate();
        aggregate.Raise(new FakeEvent(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync([aggregate], CancellationToken));

        Assert.Empty(outbox.Added);
        Assert.Equal(1, transactions.RollbackCalls);
        Assert.NotEmpty(aggregate.DomainEvents);
    }

    private static UnitOfWork CreateUnitOfWork(
        List<string> calls,
        long? tenantId,
        IDomainEventDispatcher? dispatcher = null,
        IOutboxStore? outbox = null,
        RecordingTransactionFactory? transactions = null)
    {
        var context = new TenantContext();

        if (tenantId.HasValue)
        {
            context.Set(tenantId.Value);
        }

        return new UnitOfWork(
            dispatcher ?? new RecordingDomainEventDispatcher(calls),
            outbox ?? new RecordingOutboxStore(calls),
            transactions ?? new RecordingTransactionFactory(calls),
            context,
            new FixedClock(Now));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeEvent : IDomainEvent
    {
        public FakeEvent(DateTimeOffset occurredOn) => OccurredOn = occurredOn;

        public DateTimeOffset OccurredOn { get; }
    }

    private sealed class FakeAggregate : AggregateRoot<Guid>
    {
        public FakeAggregate()
            : base(Guid.NewGuid())
        {
        }

        public void Raise(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
    }

    private sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
    {
        private readonly List<string> _calls;

        public RecordingDomainEventDispatcher(List<string> calls) => _calls = calls;

        public List<string> DispatchedTypes { get; } = [];

        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
        {
            foreach (var domainEvent in domainEvents)
            {
                var name = domainEvent.GetType().Name;
                DispatchedTypes.Add(name);
                _calls.Add($"dispatch:{name}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IReadOnlyCollection<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("handler failed");
    }

    private sealed class RecordingOutboxStore : IOutboxStore
    {
        private readonly List<string> _calls;
        private readonly List<OutboxMessage> _messages = [];

        public RecordingOutboxStore(List<string> calls) => _calls = calls;

        public List<OutboxMessage> Added => _messages;

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            _messages.Add(message);
            _calls.Add($"outbox-add:{message.EventType}");

            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            _messages.RemoveAll(message => message.Id == messageId);
            _calls.Add("outbox-remove");

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> DequeueBatchAsync(
            int maxCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(_messages.Take(maxCount).ToArray());

        public Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordFailureAsync(
            Guid messageId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RejectAsync(Guid messageId, string reason, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingTransactionFactory : IUnitOfWorkTransactionFactory
    {
        private readonly List<string> _calls;

        public RecordingTransactionFactory(List<string> calls) => _calls = calls;

        public bool FailOnCommit { get; init; }

        public int CommitCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IUnitOfWorkTransaction>(new Transaction(this));

        private sealed class Transaction : IUnitOfWorkTransaction
        {
            private readonly RecordingTransactionFactory _owner;

            public Transaction(RecordingTransactionFactory owner) => _owner = owner;

            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                _owner.CommitCalls++;
                _owner._calls.Add("commit");

                return _owner.FailOnCommit
                    ? throw new InvalidOperationException("commit failed")
                    : Task.CompletedTask;
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                _owner.RollbackCalls++;
                _owner._calls.Add("rollback");

                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
