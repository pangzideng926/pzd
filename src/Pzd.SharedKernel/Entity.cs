namespace Pzd.SharedKernel;

/// <summary>
/// 实体基类：主键是雪花 <c>long</c>（架构书 §5.3），对外序列化为字符串由 API 层负责。
/// 赋值方是 Infrastructure 的 <c>EntityService</c>（反射或仓储），Domain 层不感知 ORM。
/// </summary>
public abstract class Entity
{
    public long Id { get; protected set; }
}
