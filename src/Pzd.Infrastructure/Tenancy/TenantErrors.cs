using Pzd.SharedKernel.Results;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户相关错误。错误码沿用架构 §6.2 的平台级两段式（<c>Tenant.*</c>）。
/// </summary>
public static class TenantErrors
{
    /// <summary>请求没有可识别的租户来源（无 claim / 头 / 子域），或来源指向不存在租户。</summary>
    public static readonly Error NotFound = new("Tenant.NotFound", ErrorType.Validation, "未识别到有效租户。");

    public static readonly Error Disabled = new("Tenant.Disabled", ErrorType.Forbidden, "租户已停用。");

    public static readonly Error Expired = new("Tenant.Expired", ErrorType.Forbidden, "租户已到期。");

    /// <summary>请求内没有可信租户上下文却尝试取租户连接。</summary>
    public static readonly Error NotResolved = new("Tenant.NotResolved", ErrorType.Forbidden, "当前请求没有租户上下文，禁止获取租户连接。");
}

/// <summary>无租户上下文或租户不可用时取连接抛出的明确异常，携带错误码供上层映射。</summary>
public sealed class TenantNotResolvedException : InvalidOperationException
{
    public TenantNotResolvedException(Error error)
        : base($"{error.Code}: {error.Message}") => Error = error;

    public TenantNotResolvedException(Error error, Exception innerException)
        : base($"{error.Code}: {error.Message}", innerException) => Error = error;

    public Error Error { get; }
}
