using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pzd.Infrastructure.Security;

namespace Pzd.Api.UnitTests.Security;

/// <summary>
/// 票 08：写请求默认 Antiforgery 校验。两个 token：可读 cookie token（<c>XSRF-TOKEN</c>，不能 HttpOnly）
/// + 请求头 <c>X-XSRF-TOKEN</c>；缺 token 400（<c>Security.Csrf.Invalid</c>），带正确 token 放行。
/// </summary>
public class AntiforgeryTests
{
    [Fact]
    public async Task Unsafe_request_without_token_is_rejected_with_400()
    {
        await using var services = BuildServices();
        var antiforgery = services.GetRequiredService<IAntiforgery>();
        var context = CreateContext(services, "POST");
        var nextCalled = false;
        var middleware = new AntiforgeryValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, antiforgery);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(AntiforgeryValidationMiddleware.ErrorCode, payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unsafe_request_with_cookie_and_header_token_is_allowed()
    {
        await using var services = BuildServices();
        var antiforgery = services.GetRequiredService<IAntiforgery>();

        var tokenContext = new DefaultHttpContext { RequestServices = services };
        var tokens = antiforgery.GetAndStoreTokens(tokenContext);
        Assert.NotNull(tokens.RequestToken);
        Assert.NotNull(tokens.CookieToken);

        var context = CreateContext(services, "POST");
        context.Request.Headers[AntiforgerySetup.HeaderName] = tokens.RequestToken;
        context.Request.Headers.Cookie = $"{AntiforgerySetup.CookieName}={tokens.CookieToken}";

        var nextCalled = false;
        var middleware = new AntiforgeryValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, antiforgery);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    [Fact]
    public void Cookie_token_is_readable_by_the_frontend()
    {
        using var services = new ServiceCollection().AddPzdAntiforgery().BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        // 前端要读 cookie token 放进请求头：HttpOnly 会让整个 XSRF 方案失效。
        Assert.False(options.Cookie.HttpOnly);
        Assert.Equal(AntiforgerySetup.CookieName, options.Cookie.Name);
        Assert.Equal(AntiforgerySetup.HeaderName, options.HeaderName);
    }

    [Fact]
    public async Task Safe_methods_are_not_challenged()
    {
        await using var services = BuildServices();
        var antiforgery = services.GetRequiredService<IAntiforgery>();
        var context = CreateContext(services, "GET");
        var nextCalled = false;
        var middleware = new AntiforgeryValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, antiforgery);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Requests_without_a_matched_endpoint_are_left_to_the_framework()
    {
        await using var services = BuildServices();
        var antiforgery = services.GetRequiredService<IAntiforgery>();
        var context = CreateContext(services, "POST");
        context.SetEndpoint(null);

        var nextCalled = false;
        var middleware = new AntiforgeryValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, antiforgery);

        // 404 / 405 由框架状态码处理，不能被 CSRF 抢成 400。
        Assert.True(nextCalled);
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string method)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
        context.Request.Method = method;
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new HttpMethodMetadata([method])),
            displayName: "csrf-test-endpoint"));
        return context;
    }

    private static ServiceProvider BuildServices()
    {
        var keyPath = Path.Combine(
            AppContext.BaseDirectory,
            "csrf-keys",
            Guid.NewGuid().ToString("N"));

        return new ServiceCollection()
            .AddLogging()
            .AddPzdAntiforgery()
            .AddDataProtection()
            .SetApplicationName("pzd-csrf-test")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
            .Services
            .BuildServiceProvider();
    }
}
