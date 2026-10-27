namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 标记端点无需租户上下文（F-05-4，规格 §4.3）。
/// <see cref="TenantResolutionMiddleware"/> 见到该 metadata 就跳过租户解析与状态校验，
/// 无 claim / <c>X-Tenant</c> / 子域时也照常执行端点。
/// 用于 <c>/health/live</c>、<c>/health/ready</c>、<c>/api/version</c> 与后续登录类端点。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class AllowAnonymousTenantAttribute : Attribute;
