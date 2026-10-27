using Pzd.SharedKernel.Results;

namespace Pzd.Api.Errors;

/// <summary>
/// 端点统一返回 <c>result.ToHttpResult()</c>：按 <see cref="ErrorType"/> 映射准确状态码与统一 ProblemDetails，
/// 成功则 204（非泛型）或 200 + 值（<see cref="Result{T}"/>）。不使用「永远 200 + 包装体」。
/// </summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    /// <summary>架构 §8.3 的状态码映射。</summary>
    public static int MapStatusCode(ErrorType errorType) => errorType switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.ClientTooOld => StatusCodes.Status426UpgradeRequired,
        ErrorType.TenantUpgrading => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static IResult Problem(Error error) =>
        new PzdProblemResult(error, MapStatusCode(error.Type));

    /// <summary>
    /// 在 <see cref="ExecuteAsync"/> 时才写 traceId 与响应头（此时能拿到请求上下文），
    /// 保证 body 的 traceId 与响应头 <c>X-Trace-Id</c> 一致。
    /// </summary>
    private sealed class PzdProblemResult : IResult
    {
        private readonly Error _error;
        private readonly int _statusCode;

        public PzdProblemResult(Error error, int statusCode)
        {
            _error = error;
            _statusCode = statusCode;
        }

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var problem = ProblemDetailsCustomization.Create(_error, _statusCode, httpContext);

            httpContext.Response.StatusCode = _statusCode;
            httpContext.Response.Headers[TraceIds.HeaderName] =
                problem.Extensions["traceId"]?.ToString() ?? string.Empty;

            return httpContext.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json",
                httpContext.RequestAborted);
        }
    }
}
