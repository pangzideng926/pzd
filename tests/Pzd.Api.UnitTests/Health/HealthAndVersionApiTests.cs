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

        // 本机没有可用实例时应为 Degraded 而不是 Unhealthy——降级启动是票 03 的明确要求。
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var status = payload.RootElement.GetProperty("status").GetString();

        Assert.Contains(status, new[] { "Healthy", "Degraded" });
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
