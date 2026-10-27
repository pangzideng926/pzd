using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pzd.Api.Health;

/// <summary>
/// <c>/health/ready</c> 的响应体：<c>{ status, checks: { mysql: { status, description }, redis: {...} } }</c>。
/// 票 03 要求「明细 mysql=Up、redis=Up」，所以逐项状态必须出现在响应里，而不是只给一个总状态。
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
            },
            StringComparer.Ordinal);

        var payload = new
        {
            status = report.Status.ToString(),
            checks,
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options), context.RequestAborted);
    }
}
