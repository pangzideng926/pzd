namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 一条业务审计记录（架构 §9.3）：操作人、租户、模块、动作、结果、耗时、IP、UA。
/// 本票只负责采集与批量交给 <see cref="IAuditLogWriter"/>；落库在步骤 3（有租户库时）。
/// </summary>
public sealed record AuditLogEntry
{
    /// <summary>业务模块，如「用户管理」。</summary>
    public required string Module { get; init; }

    /// <summary>动作，如「新增用户」。</summary>
    public required string Action { get; init; }

    /// <summary>租户 ID；免租户端点为空。</summary>
    public long? TenantId { get; init; }

    /// <summary>操作人 ID；匿名请求为空。</summary>
    public string? UserId { get; init; }

    /// <summary>HTTP 结果状态码。</summary>
    public int StatusCode { get; init; }

    /// <summary>是否成功（<see cref="StatusCode"/> &lt; 400）。</summary>
    public bool Succeeded { get; init; }

    /// <summary>端点执行耗时（毫秒）。</summary>
    public double ElapsedMilliseconds { get; init; }

    /// <summary>客户端 IP（反向代理场景取 X-Forwarded-For）。</summary>
    public string? ClientIp { get; init; }

    /// <summary>User-Agent。</summary>
    public string? UserAgent { get; init; }

    /// <summary>发生时间（UTC，测试用固定时钟）。</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>请求参数（脱敏后）；本票不采集请求体，留给步骤 3 按端点补。</summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
}
