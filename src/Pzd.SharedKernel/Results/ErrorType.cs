namespace Pzd.SharedKernel.Results;

/// <summary>
/// 错误类型，决定 HTTP 状态码映射（见架构 §8.3）：Validation→400、Unauthorized→401、
/// Forbidden→403、NotFound→404、Conflict→409、BusinessRule→422、ClientTooOld→426、
/// TenantUpgrading→503、Unexpected→500。
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule,
    ClientTooOld,
    TenantUpgrading,
    Unexpected
}
