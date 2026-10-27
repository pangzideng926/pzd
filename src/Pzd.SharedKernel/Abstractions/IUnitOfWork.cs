using Pzd.SharedKernel.Domain;

namespace Pzd.SharedKernel.Abstractions;

/// <summary>
/// 工作单元：一次 Command 提交时，先分发聚合收集的领域事件，再把事件写入 Outbox，
/// 最后提交事务。分发或提交失败则整体回滚，聚合事件保留以便重试。
/// </summary>
public interface IUnitOfWork
{
    Task CommitAsync(
        IReadOnlyCollection<IDomainEventCollector> aggregates,
        CancellationToken cancellationToken = default);
}
