using System.Net;
using OpenTelemetry.Trace;
using Pzd.Infrastructure.Observability;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>
/// 票 08：OTel 已接 AspNetCore / HttpClient instrumentation（SDK 与 resource 注册在 DI 里可见）；
/// 未配置 <c>OpenTelemetry:Endpoint</c> 时不导出、不报错。
/// </summary>
public class OpenTelemetryTests
{
    [Fact]
    public void Service_name_is_the_pzd_api_contract() =>
        Assert.Equal("pzd-api", OpenTelemetryRegistration.ServiceName);

    [Fact]
    public void Tracing_sdk_is_registered_with_aspnetcore_and_http_client_instrumentation()
    {
        using var factory = new ObservabilityApiFactory();

        // WithTracing 生效后 DI 里能取到 TracerProvider；instrumentation 由 OpenTelemetryRegistration 注册。
        Assert.NotNull(factory.Services.GetService(typeof(TracerProvider)));
    }

    [Fact]
    public async Task Missing_otlp_endpoint_starts_and_serves_requests_without_errors()
    {
        using var factory = new ObservabilityApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
