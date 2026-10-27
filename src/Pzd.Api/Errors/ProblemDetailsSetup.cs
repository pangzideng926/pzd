using Pzd.Infrastructure.Errors;

namespace Pzd.Api.Errors;

/// <summary>
/// ProblemDetails 与框架状态码的接线：统一 <c>AddProblemDetails</c>、异常处理中间件，
/// 以及把「空 body 的错误状态码」（404/405 等框架路径）补成带 code/traceId 的 ProblemDetails。
/// 构造逻辑统一在 <see cref="ProblemDetailsWriter"/>，这里只做接线。
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddPzdProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsWriter.Customize);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UsePzdStatusCodePages(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var httpContext = context.HttpContext;

            if (httpContext.Response.HasStarted)
            {
                return;
            }

            var statusCode = httpContext.Response.StatusCode;

            if (statusCode is < 400 or >= 600)
            {
                return;
            }

            var error = statusCode switch
            {
                StatusCodes.Status401Unauthorized => CommonErrors.Unauthorized,
                StatusCodes.Status403Forbidden => CommonErrors.Forbidden,
                StatusCodes.Status404NotFound => CommonErrors.NotFound,
                StatusCodes.Status405MethodNotAllowed => CommonErrors.MethodNotAllowed,
                _ => CommonErrors.Unexpected,
            };

            await ProblemDetailsWriter
                .WriteAsync(
                    httpContext,
                    ProblemDetailsWriter.Create(error, statusCode, httpContext),
                    httpContext.RequestAborted)
                .ConfigureAwait(false);
        });
}
