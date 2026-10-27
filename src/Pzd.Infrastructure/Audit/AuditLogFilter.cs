using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Pzd.Infrastructure.Observability;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 票 08：审计端点过滤器。端点执行完（含抛异常）后把一条 <see cref="AuditLogEntry"/> 丢进有界 Channel：
/// 操作人、租户、模块、动作、结果、耗时、IP、UA 一次采齐，请求线程不做落库。
/// </summary>
public sealed class AuditLogFilter : IEndpointFilter
{
    private readonly AuditLogChannel _channel;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public AuditLogFilter(AuditLogChannel channel, ITenantContext tenantContext, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(clock);

        _channel = channel;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;
        var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<AuditLogMetadata>();
        var started = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status200OK;
        var succeeded = true;

        try
        {
            var result = await next(context);

            statusCode = result switch
            {
                IStatusCodeHttpResult { StatusCode: { } code } => code,
                _ => httpContext.Response.StatusCode,
            };
            succeeded = statusCode < StatusCodes.Status400BadRequest;

            return result;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            succeeded = false;
            throw;
        }
        finally
        {
            if (metadata is not null)
            {
                _channel.TryEnqueue(new AuditLogEntry
                {
                    Module = metadata.Module,
                    Action = metadata.Action,
                    TenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : null,
                    UserId = LogContextMiddleware.UserId(httpContext),
                    StatusCode = statusCode,
                    Succeeded = succeeded,
                    ElapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    ClientIp = LogContextMiddleware.ClientIp(httpContext),
                    UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                    OccurredAt = _clock.UtcNow,
                });
            }
        }
    }
}
