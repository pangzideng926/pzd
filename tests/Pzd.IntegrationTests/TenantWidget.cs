using SqlSugar;

namespace Pzd.IntegrationTests;

/// <summary>
/// 测试用租户表实体（与 <see cref="TestSchema"/> 的 <c>it_widgets</c> 对应）。
/// <see cref="TenantIsolationTests.QueryFilter_hides_rows_that_carry_another_tenants_id"/> 会在租户连接上
/// 显式挂 <c>TenantId = 当前租户</c> 的 QueryFilter，验证「物理行存在但跨租户不可见」的纵深防御语义。
/// </summary>
[SugarTable(TestSchema.TableName)]
public sealed class TenantWidget
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; }

    public long TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
}
