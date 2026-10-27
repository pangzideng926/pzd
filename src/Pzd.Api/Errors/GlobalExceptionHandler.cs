using Microsoft.AspNetCore.Diagnostics;
using Pzd.Api.Errors;

namespace Pzd.Api.Errors;

/// <summary>
/// 意外异常统一出口：500 + <see cref="CommonErrors.Unexpected"/> ProblemDetails。
/// 生产环境不返回异常类型与堆栈；开发环境额外给出异常类型名便于排查（不含堆栈）。
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);

        var problem = ProblemDetailsCustomization.Create(
            CommonErrors.Unexpected,
            StatusCodes.Status500InternalServerError,
            httpContext);

        if (_environment.IsDevelopment())
        {
            problem.Extensions["exceptionType"] = exception.GetType().Name;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.Headers[TraceIds.HeaderName] =
            problem.Extensions["traceId"]?.ToString() ?? string.Empty;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }
}
