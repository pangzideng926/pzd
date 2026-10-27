using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Pzd.Infrastructure.Observability;

/// <summary>
/// 票 08：Serilog 接管技术日志。
/// <para>
/// D2 硬约束（票 03）：日志不得成为故障源——默认 provider 全部清掉（Windows EventLog 在沙箱/服务账号下
/// 写入会抛异常），只留 Serilog 控制台 sink；测试通过 DI 注入 <see cref="ILogEventSink"/> 取日志事件。
/// </para>
/// <para>
/// 框架探活的逐次 Degraded Warn 压到 Error（提交 d17df6c，F-03-4）：<c>builder.Logging.AddFilter</c>
/// 与 Serilog <c>MinimumLevel.Override</c> 双保险，后者才是 Serilog 工厂下真正生效的那道。
/// </para>
/// </summary>
public static class SerilogRegistration
{
    /// <summary>健康检查框架日志类别：逐次探测的 Degraded Warn 由票 03 压到 Error，不要恢复成 Warn。</summary>
    public const string HealthChecksLogCategory = "Microsoft.Extensions.Diagnostics.HealthChecks";

    /// <summary>请求摘要行（<c>UseSerilogRequestLogging</c>）的 SourceContext，测试按它筛摘要行。</summary>
    public const string RequestLoggingSourceContext = "Serilog.AspNetCore.RequestLoggingMiddleware";

    /// <summary>
    /// Serilog 接管日志：控制台 + DI 里注册的 sink（测试接缝）；脱敏策略与 LogContext 统一在这里挂上。
    /// 用 <c>AddSerilog(IServiceProvider, …)</c> 重载，确保能解析到应用容器里的 sink。
    /// </summary>
    public static WebApplicationBuilder AddPzdSerilog(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.ClearProviders();
        builder.Logging.AddFilter(HealthChecksLogCategory, LogLevel.Error);

        builder.Services.AddSerilog(
            (_, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Destructure.With(new SensitiveDataDestructuring())
                    .MinimumLevel.Override(HealthChecksLogCategory, LogEventLevel.Error)
                    // 文本模板：Message 渲染出可读文案（票 03 的 `Degraded mode: MySQL unavailable（…）`
                    // 仍可直接 grep stdout 举证），Properties 仍带 TenantId/UserId/TraceId/ClientIp 等结构化属性。
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} | {Properties:j}{NewLine}{Exception}");
            });

        return builder;
    }
}
