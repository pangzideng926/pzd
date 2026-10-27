namespace Pzd.SharedKernel.Domain;

/// <summary>
/// 反向用例专用类型：故意让 Domain 命名空间依赖 SqlSugar，用于证明架构规则确实会报违规。
/// 只存在于测试程序集，不参与生产构建。
/// </summary>
public sealed class Fake
{
    public SqlSugar.SqlSugarClient? Client { get; set; }
}

/// <summary>
/// 反向用例专用类型：故意让 Domain 命名空间依赖 <c>Pzd.Infrastructure</c>（架构 §3.2 禁止的箭头）。
/// </summary>
public sealed class FakeInfrastructureDependency
{
    public Pzd.Infrastructure.Tenancy.TenantInfo? Tenant { get; set; }
}
