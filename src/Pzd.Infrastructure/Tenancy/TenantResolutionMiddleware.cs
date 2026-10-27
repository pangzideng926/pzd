using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Pzd.Infrastructure.Errors;
using Pzd.SharedKernel.Abstractions;
using Pzd.SharedKernel.Results;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 租户解析：已登录按 claim <c>tid</c>（忽略 X-Tenant）；匿名按子域名优先、X-Tenant 头次之。
/// 每次请求都向 <see cref="ITenantStore"/> 重新校验状态与到期时间（架构 §6.2）。
/// 解析失败直接写 ProblemDetails 并短路，不进入业务管道。
/// </summary>
public sealed class TenantResolutionMiddleware
{
    public const string TenantClaimType = "tid";
    public const string TenantHeaderName = "X-Tenant";

    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        IClock clock)
    {
        var (tenant, error) = await ResolveAsync(context, tenantStore);

        if (tenant is null)
        {
            await WriteProblemAsync(context, error ?? TenantErrors.NotFound);
            return;
        }

        if (tenant.IsDisabled)
        {
            await WriteProblemAsync(context, TenantErrors.Disabled);
            return;
        }

        if (tenant.ExpiresAt is { } expiresAt && expiresAt <= clock.UtcNow)
        {
            await WriteProblemAsync(context, TenantErrors.Expired);
            return;
        }

        tenantContext.Set(tenant.Id);

        await _next(context);
    }

    internal static async Task<(TenantInfo? Tenant, Error? Error)> ResolveAsync(
        HttpContext context,
        ITenantStore tenantStore)
    {
        var user = context.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            // 已登录：只认 claim tid，X-Tenant 头必须被忽略。
            var claim = user.FindFirst(TenantClaimType)?.Value;

            if (!long.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claimTenantId))
            {
                return (null, TenantErrors.NotFound);
            }

            var byClaim = await tenantStore.FindByIdAsync(claimTenantId, context.RequestAborted);

            return byClaim is null ? (null, TenantErrors.NotFound) : (byClaim, null);
        }

        var subdomain = ResolveSubdomainCode(context.Request.Host.Host);

        if (subdomain is not null)
        {
            var bySubdomain = await tenantStore.FindByCodeAsync(subdomain, context.RequestAborted);

            return bySubdomain is null ? (null, TenantErrors.NotFound) : (bySubdomain, null);
        }

        var headerValue = context.Request.Headers[TenantHeaderName].ToString();

        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            var byHeader = await tenantStore.FindByCodeAsync(headerValue.Trim(), context.RequestAborted);

            return byHeader is null ? (null, TenantErrors.NotFound) : (byHeader, null);
        }

        return (null, TenantErrors.NotFound);
    }

    /// <summary>
    /// 子域名即租户编码：<c>acme.example.com</c> / <c>acme.localhost</c>。
    /// 裸主机、IP、www/api 前缀不算租户。
    /// </summary>
    internal static string? ResolveSubdomainCode(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var candidate = labels.Length switch
        {
            2 when labels[1].Equals("localhost", StringComparison.OrdinalIgnoreCase) => labels[0],
            >= 3 => labels[0],
            _ => null,
        };

        return candidate is null or "www" or "api" or "localhost" ? null : candidate;
    }

    /// <summary>
    /// 短路响应用与 <c>ToHttpResult</c> / 异常处理完全相同的构造（<see cref="ProblemDetailsWriter"/>）：
    /// 状态码、<c>type</c>/<c>code</c>/<c>traceId</c> 与 <c>X-Trace-Id</c> 响应头一处产出（F-06-1）。
    /// </summary>
    private static Task WriteProblemAsync(HttpContext context, Error error)
    {
        var problem = ProblemDetailsWriter.Create(
            error,
            ProblemDetailsWriter.MapStatusCode(error.Type),
            context);

        return ProblemDetailsWriter.WriteAsync(context, problem, context.RequestAborted);
    }
}
