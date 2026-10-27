using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pzd.Infrastructure.Errors;
using Pzd.SharedKernel.Results;

namespace Pzd.Infrastructure.Security;

/// <summary>
/// 票 08：写请求默认 Antiforgery 校验。安全方法（GET/HEAD/OPTIONS/TRACE）与显式
/// <see cref="IgnoreAntiforgeryAttribute"/> 端点放行；其余缺 token / token 非法一律 400，
/// 错误码 <c>Security.Csrf.Invalid</c>，响应体走统一 ProblemDetails。
/// </summary>
public sealed class AntiforgeryValidationMiddleware
{
    /// <summary>CSRF 校验失败错误码。</summary>
    public const string ErrorCode = "Security.Csrf.Invalid";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "OPTIONS",
        "TRACE",
    };

    private readonly RequestDelegate _next;

    public AntiforgeryValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);

        // 只校验真正命中端点的写请求：未匹配到端点（404）或方法不匹配（405）交给框架状态码兜底。
        // 路由对 405 给的端点没有方法元数据，这里要求「端点方法列表明确包含本次方法」才校验，
        // 否则 CSRF 会抢在 405 之前回 400（票 06 的 405 用例）。
        var endpoint = context.GetEndpoint();

        if (endpoint is null
            || SafeMethods.Contains(context.Request.Method)
            || endpoint.Metadata.GetMetadata<IgnoreAntiforgeryAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var allowedMethods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;

        if (allowedMethods is not { Count: > 0 }
            || !allowedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            var error = new Error(ErrorCode, ErrorType.Validation, "缺少或非法的 CSRF token");
            var problem = ProblemDetailsWriter.Create(error, StatusCodes.Status400BadRequest, context);

            await ProblemDetailsWriter.WriteAsync(context, problem, context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
