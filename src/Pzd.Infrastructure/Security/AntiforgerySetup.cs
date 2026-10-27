using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Pzd.Infrastructure.Security;

/// <summary>
/// 票 08：Antiforgery 配置。两个 token：可读 cookie token（<c>XSRF-TOKEN</c>）由前端读取后
/// 放进请求头 <c>X-XSRF-TOKEN</c>；cookie 必须 <c>HttpOnly = false</c>，否则前端读不到。
/// </summary>
public static class AntiforgerySetup
{
    /// <summary>cookie token 的 Cookie 名（前端可读）。</summary>
    public const string CookieName = "XSRF-TOKEN";

    /// <summary>请求头名（前端从 cookie 读取后回填）。</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>注册 Antiforgery 服务与 token 契约。</summary>
    public static IServiceCollection AddPzdAntiforgery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = false;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
}

/// <summary>
/// 标记端点跳过 Antiforgery 校验（仅用于确实不需要 CSRF 防护的机器对机器端点；
/// 健康/版本端点是 GET，不需要它）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IgnoreAntiforgeryAttribute : Attribute;
