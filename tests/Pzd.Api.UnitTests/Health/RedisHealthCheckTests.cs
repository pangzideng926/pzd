using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Pzd.Api.Health;
using Pzd.Api.UnitTests.Tenancy;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// 票 03 老登③ / 老何必修：连接串解析出空 host 时必须 <see cref="HealthStatus.Degraded"/>，
/// 不能让 <c>ArgumentException</c> 冒到 <c>HealthCheckService</c> 变成 Unhealthy + 503。
/// 缺资源只降级，绝不 503——这是票面口径。
/// </summary>
public class RedisHealthCheckTests
{
    [Theory]
    [InlineData("host=")]
    [InlineData("host=;port=6379")]
    [InlineData("host=   ")]
    public async Task Empty_host_degrades_instead_of_failing(string connectionString)
    {
        var check = CreateCheck(connectionString);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Missing_connection_string_degrades_instead_of_failing()
    {
        var check = CreateCheck(connectionString: null);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Readiness_stays_200_and_reports_redis_degraded_when_host_is_empty()
    {
        using var factory = new PzdApiFactory();
        using var factoryWithEmptyHost = factory.WithWebHostBuilder(
            builder => builder.UseSetting("ConnectionStrings:redis", "host="));
        using var client = factoryWithEmptyHost.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        // 缺资源只降级、不 503：Degraded 在 MapHealthChecks 默认映射里仍是 200。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Degraded", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Degraded",
            payload.RootElement.GetProperty("checks").GetProperty("redis").GetProperty("status").GetString());
    }

    private static RedisHealthCheck CreateCheck(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis"] = connectionString,
            })
            .Build();

        return new RedisHealthCheck(configuration, NullLogger<RedisHealthCheck>.Instance);
    }
}
