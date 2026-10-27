using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Pzd.Api.Errors;
using Pzd.SharedKernel.Results;

namespace Pzd.Api.UnitTests;

/// <summary>
/// 测试专用最小端点（不进生产代码）：按 scenario 返回不同的 <see cref="Result"/>，
/// 让映射表走真实 HTTP 接缝。通过 <see cref="IStartupFilter"/> 挂在生产管道之前。
/// </summary>
internal sealed class TestEndpointsStartupFilter : IStartupFilter
{
    public const string PathPrefix = "/__tests__";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        // 挂在生产管道末尾：未匹配到生产端点的 /__tests__ 请求会落到这里，
        // 异常仍会向上穿过 UseExceptionHandler，从而验证真实异常处理链路。
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Path.StartsWithSegments(PathPrefix))
            {
                await HandleAsync(context);
                return;
            }

            await nextMiddleware(context);
        });
    };

    private static Task HandleAsync(HttpContext context) =>
        Execute(context.Request.Path.Value?.Split('/').LastOrDefault() ?? string.Empty).ExecuteAsync(context);

    private static IResult Execute(string scenario) => scenario switch
    {
        "success" => Result.Success().ToHttpResult(),
        "success-value" => Result.Success("pong").ToHttpResult(),
        "validation" => Result.Failure(BuildValidationError()).ToHttpResult(),
        "unauthorized" => Result.Failure(new Error("Identity.User.NotAuthenticated", ErrorType.Unauthorized, "未登录或会话已过期")).ToHttpResult(),
        "forbidden" => Result.Failure(new Error("Identity.User.Forbidden", ErrorType.Forbidden, "没有权限")).ToHttpResult(),
        "notfound" => Result.Failure(new Error("Identity.User.NotFound", ErrorType.NotFound, "用户不存在")).ToHttpResult(),
        "conflict" => Result.Failure(new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在")).ToHttpResult(),
        "businessrule" => Result.Failure(new Error("Identity.Role.InUse", ErrorType.BusinessRule, "角色仍被用户使用")).ToHttpResult(),
        "clienttooold" => Result.Failure(new Error("Client.TooOld", ErrorType.ClientTooOld, "客户端版本过低")).ToHttpResult(),
        "tenantupgrading" => Result.Failure(new Error("Tenant.Upgrading", ErrorType.TenantUpgrading, "系统升级中")).ToHttpResult(),
        "throw" => throw new InvalidOperationException("boom: secret stack marker"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "unknown test scenario"),
    };

    /// <summary>
    /// 模拟 FluentValidation 的实际输出：错误字典的 key 是 PascalCase 属性名（如 <c>UserName</c>），
    /// 响应里必须被转成 camelCase——用例刻意喂 PascalCase，避免「用例自己喂期望输入」（F-06-1）。
    /// </summary>
    private static Error BuildValidationError() => new(
        "Identity.User.NameDuplicated",
        ErrorType.Validation,
        "用户名已存在",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["UserName"] = ["用户名已存在"],
            },
        });
}
