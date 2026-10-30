namespace Pzd.SharedKernel;

/// <summary>
/// 仓储契约：只为聚合根建仓储，按聚合整体加载与保存（架构书 §3.3）。
/// 提交由 UoW 负责（一个 Command 一次提交），因此这里没有 Save。
/// </summary>
public interface IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    Task<TAggregate?> FindAsync(long id, CancellationToken cancellationToken = default);

    void Add(TAggregate aggregate);

    void Remove(TAggregate aggregate);
}
