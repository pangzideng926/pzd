using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Pzd.Infrastructure.Observability;

/// <summary>
/// 票 08：OpenTelemetry traces / metrics。service name 固定为 <see cref="ServiceName"/>；
/// 只配 <c>OpenTelemetry:Endpoint</c> 时才挂 OTLP 导出器，未配置时不导出、不报错。
/// </summary>
public static class OpenTelemetryRegistration
{
    /// <summary>OTel resource 的 service.name（架构 §9.2）。</summary>
    public const string ServiceName = "pzd-api";

    /// <summary>OTLP 端点配置键：留空 = 只采集不导出（本地/CI）。</summary>
    public const string EndpointConfigKey = "OpenTelemetry:Endpoint";

    /// <summary>注册 AspNetCore / HttpClient instrumentation，并按配置决定是否导出 OTLP。</summary>
    public static WebApplicationBuilder AddPzdOpenTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var endpoint = builder.Configuration[EndpointConfigKey];
        var hasEndpoint = Uri.TryCreate(endpoint, UriKind.Absolute, out var otlpEndpoint);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();

                if (hasEndpoint)
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();

                if (hasEndpoint)
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            });

        return builder;
    }
}
