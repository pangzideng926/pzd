namespace Pzd.SharedKernel;

/// <summary>
/// 领域错误：错误码固定三段「模块.实体.原因」（如 <c>Identity.User.NotFound</c>），
/// 由 09 号票映射成 ProblemDetails 的 <c>code</c> 字段。
/// 错误码是代码里写死的字面量，格式非法属于开发期 bug，因此构造时直接抛异常（票面已定）。
/// </summary>
public sealed record Error
{
    private const char Separator = '.';
    private const int RequiredSegments = 3;

    public Error(string code, string message)
    {
        if (!IsWellFormed(code))
        {
            throw new ArgumentException(
                $"错误码格式非法：\"{code}\"，应为「模块.实体.原因」三段且每段非空。",
                nameof(code));
        }

        Code = code;
        Message = message;
    }

    /// <summary>形如 <c>模块.实体.原因</c> 的错误码。</summary>
    public string Code { get; }

    public string Message { get; }

    private static bool IsWellFormed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var segments = code.Split(Separator);
        return segments.Length == RequiredSegments
            && segments.All(segment => !string.IsNullOrWhiteSpace(segment));
    }
}
