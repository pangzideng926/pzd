using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Pzd.Infrastructure.Security;

/// <summary>
/// 票 08：安全响应头（架构 §7.3）。用 <c>OnStarting</c> 在响应真正开始写时补齐，
/// 这样异常处理重写响应后错误响应也带得上头；HSTS 只在非开发环境出现。
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>CSP：禁止内联脚本、禁止被嵌入 iframe。</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
        + "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>HSTS 值（仅非开发环境）。</summary>
    public const string HstsValue = "max-age=31536000; includeSubDomains";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environment);

        var isDevelopment = environment.IsDevelopment();

        context.Response.OnStarting(
            () =>
            {
                var headers = context.Response.Headers;

                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["Referrer-Policy"] = "no-referrer";

                if (!isDevelopment)
                {
                    headers["Strict-Transport-Security"] = HstsValue;
                }

                return Task.CompletedTask;
            });

        return _next(context);
    }
}
