using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pzd.SharedKernel.Abstractions;
using Serilog;
using Serilog.Context;

namespace Pzd.Infrastructure.Observability;

/// <summary>
/// 把 <c>TenantId</c> / <c>UserId</c> / <c>TraceId</c> / <c>ClientIp</c> 推入 <see cref="LogContext"/>，
/// 让请求内的业务日志自动带上请求上下文；请求摘要行由 <see cref="EnrichDiagnosticContext"/> 富化。
/// <para>位置在租户解析与认证之后，四类属性此时都已就绪。</para>
/// </summary>
public sealed class LogContextMiddleware
{
    /// <summary>日志属性名：租户 ID。</summary>
    public const string TenantIdProperty = "TenantId";

    /// <summary>日志属性名：操作人 ID。</summary>
    public const string UserIdProperty = "UserId";

    /// <summary>日志属性名：链路 TraceId。</summary>
    public const string TraceIdProperty = "TraceId";

    /// <summary>日志属性名：客户端 IP。</summary>
    public const string ClientIpProperty = "ClientIp";

    private readonly RequestDelegate _next;

    public LogContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        using (LogContext.PushProperty(TenantIdProperty, tenantContext.HasTenant ? tenantContext.TenantId : null))
        using (LogContext.PushProperty(UserIdProperty, UserId(context)))
        using (LogContext.PushProperty(TraceIdProperty, TraceId(context)))
        using (LogContext.PushProperty(ClientIpProperty, ClientIp(context)))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// <c>UseSerilogRequestLogging</c> 的摘要行富化：回调发生在响应结束时，
    /// 此时租户上下文已由租户中间件写入，因此摘要行能拿到真实 <c>TenantId</c>。
    /// </summary>
    public static void EnrichDiagnosticContext(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(diagnosticContext);
        ArgumentNullException.ThrowIfNull(context);

        diagnosticContext.Set(TraceIdProperty, TraceId(context));
        diagnosticContext.Set(ClientIpProperty, ClientIp(context));

        var tenantContext = context.RequestServices.GetService<ITenantContext>();

        if (tenantContext is { HasTenant: true })
        {
            diagnosticContext.Set(TenantIdProperty, tenantContext.TenantId);
        }

        if (UserId(context) is { } userId)
        {
            diagnosticContext.Set(UserIdProperty, userId);
        }
    }

    /// <summary>已登录用户 ID（<see cref="ClaimTypes.NameIdentifier"/>），未登录返回 null。</summary>
    public static string? UserId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    /// <summary>客户端 IP：反向代理场景优先取 <c>X-Forwarded-For</c> 第一段，否则取连接远端地址。</summary>
    public static string? ClientIp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();

        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',', 2)[0].Trim();

            if (first.Length > 0)
            {
                return first;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>链路 ID：优先 W3C <see cref="Activity.TraceId"/>，没有 Activity 时退回请求 TraceIdentifier。</summary>
    public static string TraceId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
