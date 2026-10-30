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

    /// <summary>审计与并发字段名；改成别的名字时用例会一起改，不做多套并存。</summary>
    public static class Columns
    {
        public const string Id = "Id";
        public const string Version = "Version";
        public const string IsDeleted = "IsDeleted";
        public const string DeletedToken = "DeletedToken";
        public const string CreatedAt = "CreatedAt";
        public const string CreatedBy = "CreatedBy";
        public const string UpdatedAt = "UpdatedAt";
        public const string UpdatedBy = "UpdatedBy";
    }
}
