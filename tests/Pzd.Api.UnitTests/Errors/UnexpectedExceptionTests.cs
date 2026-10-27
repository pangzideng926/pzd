using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Pzd.Api.Errors;
using Pzd.Api.UnitTests.Tenancy;

namespace Pzd.Api.UnitTests.Errors;

/// <summary>
/// 票 06：意外异常统一 500 + ProblemDetails；生产环境不泄露异常类型与堆栈，开发环境只多给类型名。
/// HTTP 接缝跑生产环境（开发环境会让框架的 DeveloperExceptionPage 先拦截，差异用处理器级用例覆盖）。
/// </summary>
public class UnexpectedExceptionTests
{
    [Fact]
    public async Task Unhandled_exception_returns_500_without_leaking_details_in_production()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        Assert.Equal("System.Unexpected", root.GetProperty("code").GetString());
        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(root.GetProperty("traceId").GetString(), response.Headers.GetValues("X-Trace-Id").Single());

        Assert.DoesNotContain("boom", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(root.TryGetProperty("exceptionType", out _));
    }

    [Fact]
    public async Task Development_environment_only_adds_the_exception_type_name()
    {
        var body = await HandleAsync(environmentName: "Development");
        using var json = JsonDocument.Parse(body);

        Assert.Equal("InvalidOperationException", json.RootElement.GetProperty("exceptionType").GetString());
        Assert.DoesNotContain("boom", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_environment_does_not_add_the_exception_type()
    {
        var body = await HandleAsync(environmentName: "Production");
        using var json = JsonDocument.Parse(body);

        Assert.False(json.RootElement.TryGetProperty("exceptionType", out _));
        Assert.Equal("System.Unexpected", json.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> HandleAsync(string environmentName)
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            new HostingEnvironment { EnvironmentName = environmentName });

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("boom: secret stack marker"),
            TestContext.Current.CancellationToken);

        Assert.True(handled);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);

        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/__tests__/throw");
        request.Headers.Add("X-Tenant", "acme");

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
