using Pzd.SharedKernel.Results;

namespace Pzd.Api.Errors;

/// <summary>平台级错误码（框架路径与意外异常），Domain 层错误仍由各模块 <c>Errors</c> 提供。</summary>
public static class CommonErrors
{
    public static readonly Error Unexpected = new("System.Unexpected", ErrorType.Unexpected, "服务器内部错误。");

    /// <summary>框架生成的裸 401（无 body）：认证失败，不得落成 <see cref="Unexpected"/>。</summary>
    public static readonly Error Unauthorized = new("Auth.Unauthorized", ErrorType.Unauthorized, "未登录或会话已过期。");

    /// <summary>框架生成的裸 403（无 body）：已认证但无权限。</summary>
    public static readonly Error Forbidden = new("Auth.Forbidden", ErrorType.Forbidden, "没有权限访问该资源。");

    public static readonly Error NotFound = new("Http.NotFound", ErrorType.NotFound, "请求的资源不存在。");

    public static readonly Error MethodNotAllowed = new("Http.MethodNotAllowed", ErrorType.Validation, "请求方法不被允许。");
}
