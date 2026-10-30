namespace Pzd.SharedKernel;

/// <summary>
/// 领域事件标记接口：聚合内收集，UoW 提交前分发（架构书 §3.3、§5.2）。
/// 跨模块通信要转成集成事件走 Outbox，那是 08 号票的事，这里只做收集。
/// </summary>
public interface IDomainEvent
{
}
