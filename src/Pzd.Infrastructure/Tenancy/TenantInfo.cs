namespace Pzd.Infrastructure.Tenancy;

public enum TenantStatus
{
    Active,
    Disabled,
}

/// <summary>
/// 租户元数据（来自 master 库 tenants 表；本轮由内存 Store 提供，步骤 3 换落库）。
/// <see cref="ConnectionString"/> 在步骤 3 改为密文解密后的内存值。
/// </summary>
public sealed record TenantInfo(
    long Id,
    string Code,
    TenantStatus Status,
    DateTimeOffset? ExpiresAt,
    string? ConnectionString)
{
    public bool IsDisabled => Status == TenantStatus.Disabled;
}
