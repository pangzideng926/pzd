using System.Net;
using System.Text.Json;
using Pzd.Api.UnitTests.Tenancy;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// 票 03 / F-05-4：<c>/health/live</c>、<c>/health/ready</c>、<c>/api/version</c> 必须免租户，
/// 无 claim / 无 <c>X-Tenant</c> / 无子域时也返回 200；豁免之外的端点仍被租户中间件拦成 400。
/// </summary>
public class HealthAndVersionApiTests
{
    [Fact]
    public async Task Liveness_returns_200_without_any_tenant_signal()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_returns_200_without_any_tenant_signal()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 单元测试只守「200 + 免租户 + JSON 结构」，不断言 Healthy 还是 Degraded：
        // 两个分支都算过的断言等于没验（票 03 老登②）。「有实例时必须是 Healthy」
        // 只能由真实实例断言，落在 scripts/verify-apphost.ps1 里。
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = payload.RootElement;

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("status").GetString()));

        var checks = root.GetProperty("checks");

        foreach (var resource in new[] { "mysql", "redis" })
        {
            Assert.True(checks.TryGetProperty(resource, out var check), $"/health/ready 明细缺少 {resource}");
            Assert.False(string.IsNullOrWhiteSpace(check.GetProperty("status").GetString()));
            Assert.True(check.TryGetProperty("description", out _), $"{resource} 明细缺少 description 字段");
        }
    }

    [Fact]
    public async Task Version_returns_200_with_version_build_id_and_build_time()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("buildId").GetString()));
        Assert.NotEqual(default, payload.RootElement.GetProperty("buildTime").GetDateTimeOffset());
    }

    [Fact]
    public async Task Endpoints_outside_the_exemption_still_require_a_tenant()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Tenant.NotFound", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Exempt_endpoints_ignore_a_bogus_tenant_header()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Tenant", "no-such-tenant");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
