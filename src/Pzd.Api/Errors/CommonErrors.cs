using Pzd.SharedKernel.Results;

namespace Pzd.Api.Errors;

/// <summary>平台级错误码（框架路径与意外异常），Domain 层错误仍由各模块 <c>Errors</c> 提供。</summary>
public static class CommonErrors
{
    public static readonly Error Unexpected = new("System.Unexpected", ErrorType.Unexpected, "服务器内部错误。");

    public static readonly Error NotFound = new("Http.NotFound", ErrorType.NotFound, "请求的资源不存在。");

    public static readonly Error MethodNotAllowed = new("Http.MethodNotAllowed", ErrorType.Validation, "请求方法不被允许。");
}
