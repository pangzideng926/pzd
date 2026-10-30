namespace Pzd.Infrastructure;

/// <summary>
/// 建立 SqlSugar 客户端时的约定参数。默认值让调用方只关心「哪些实体要软删除过滤」。
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// 需要挂「未删除」全局过滤的实体类型。按类型登记而不是靠接口标记：
    /// Domain 禁止引用 Infrastructure（architecture.md §3.2），标记接口无处安放。
    /// </summary>
    public IReadOnlyList<Type> SoftDeleteTypes { get; init; } = [];

    /// <summary>写入 <c>CreatedBy</c> / <c>UpdatedBy</c> 的当前用户；登录链路接入前默认 0（系统用户）。</summary>
    public Func<long> CurrentUserId { get; init; } = static () => 0;

    /// <summary>
    /// 当前租户。写入口用它拼 <c>WHERE TenantId=@tenant</c>；**缺失时写入口抛异常，不按 Id 静默更新**
    /// （06 审计 high 2：不知道租户就可能写穿别家记录并把整行搬到自己的租户名下）。
    /// 07 号票负责把租户解析结果接进来，本属性签名不变。
    /// </summary>
    public Func<long?> CurrentTenantId { get; init; } = static () => null;

    /// <summary>
    /// 雪花 worker 序号（0–31），由部署配置按实例给。与 <see cref="DatacenterId"/> 都不填时退回
    /// SqlSugar 的进程级单例（<c>WorkerId=1/DatacenterId=1</c>），多副本会撞主键，因此启动期给一条警告。
    /// </summary>
    public int? WorkerId { get; init; }

    /// <summary>雪花机房序号（0–31），与 <see cref="WorkerId"/> 一起决定 Id 的机器位。</summary>
    public int? DatacenterId { get; init; }

    /// <summary>启动期警告的出口；不设置时静默（测试与命令行不被打扰）。</summary>
    public Action<string>? OnWarning { get; init; }

    /// <summary>审计与并发字段名；改成别的名字时用例会一起改，不做多套并存。</summary>
    public static class Columns
    {
        public const string Id = "Id";
        public const string TenantId = "TenantId";
        public const string Version = "Version";
        public const string IsDeleted = "IsDeleted";
        public const string DeletedToken = "DeletedToken";
        public const string DeletedAt = "DeletedAt";
        public const string DeletedBy = "DeletedBy";
        public const string CreatedAt = "CreatedAt";
        public const string CreatedBy = "CreatedBy";
        public const string UpdatedAt = "UpdatedAt";
        public const string UpdatedBy = "UpdatedBy";
    }
}
