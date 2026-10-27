using System.Text.Json;
using Pzd.SharedKernel.Abstractions;
using Pzd.SharedKernel.Domain;

namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// 事务边界抽象：本轮内存实现，步骤 3 换成租户库的真实事务。禁止跨库（master 与租户库不共享事务）。
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>事务工厂：可替换，便于测试断言「分发 → 写 Outbox → 提交」的调用顺序。</summary>
public interface IUnitOfWorkTransactionFactory
{
    Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 工作单元实现：聚合领域事件先分发、再写 Outbox、最后提交事务；任一步失败整体回滚。
/// 成功后清空聚合事件，避免重复分发；失败时保留事件以便调用方重试。
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly IOutboxStore _outbox;
    private readonly IUnitOfWorkTransactionFactory _transactions;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public UnitOfWork(
        IDomainEventDispatcher dispatcher,
        IOutboxStore outbox,
        IUnitOfWorkTransactionFactory transactions,
        ITenantContext tenantContext,
        IClock clock)
    {
        _dispatcher = dispatcher;
        _outbox = outbox;
        _transactions = transactions;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task CommitAsync(
        IReadOnlyCollection<IDomainEventCollector> aggregates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);

        if (!_tenantContext.HasTenant)
        {
            throw new InvalidOperationException(
                "工作单元提交需要租户上下文：Outbox 消息必须携带 TenantId（架构 §6.3）。");
        }

        var tenantId = _tenantContext.TenantId;
        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        await using var transaction = await _transactions.BeginAsync(cancellationToken).ConfigureAwait(false);
        var written = new List<OutboxMessage>(domainEvents.Length);

        try
        {
            if (domainEvents.Length > 0)
            {
                await _dispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);

                foreach (var domainEvent in domainEvents)
                {
                    var message = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EventType = domainEvent.GetType().Name,
                        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                        OccurredOn = domainEvent.OccurredOn,
                    };

                    await _outbox.AddAsync(message, cancellationToken).ConfigureAwait(false);
                    written.Add(message);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 内存存储没有真正的事务回滚，用补偿删除模拟；步骤 3 换成真实事务后删掉这段。
            foreach (var message in written)
            {
                await _outbox.RemoveAsync(message.Id, CancellationToken.None).ConfigureAwait(false);
            }

            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            throw;
        }

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearEvents();
        }
    }
}

/// <summary>
/// 内存事务工厂（本轮）：<see cref="BeginAsync"/> 返回空操作事务，不做任何持久化保证；
/// 步骤 3 换成租户库的 SqlSugar 事务，并删掉 <see cref="UnitOfWork"/> 里的补偿删除。
/// </summary>
public sealed class InMemoryUnitOfWorkTransactionFactory : IUnitOfWorkTransactionFactory
{
    public Task<IUnitOfWorkTransaction> BeginAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IUnitOfWorkTransaction>(new NoOpTransaction());

    private sealed class NoOpTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
