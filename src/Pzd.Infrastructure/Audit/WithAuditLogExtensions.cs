using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Pzd.Infrastructure.Audit;

/// <summary>端点审计元数据：模块 + 动作，由 <see cref="AuditLogFilter"/> 读取。</summary>
public sealed record AuditLogMetadata(string Module, string Action);

/// <summary>
/// 票 08：<c>WithAuditLog(模块, 动作)</c> 给端点挂上审计元数据与过滤器。
/// 用法与架构 §9.3 一致：<c>group.MapPost("/users", CreateUser).WithAuditLog("用户管理", "新增用户")</c>。
/// </summary>
public static class WithAuditLogExtensions
{
    /// <summary>给端点标注审计模块/动作并挂上审计过滤器。</summary>
    public static RouteHandlerBuilder WithAuditLog(this RouteHandlerBuilder builder, string module, string action)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        builder.WithMetadata(new AuditLogMetadata(module, action));
        builder.AddEndpointFilter<AuditLogFilter>();

        return builder;
    }
}
