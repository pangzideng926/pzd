using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pzd.Infrastructure.Observability;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using Serilog;
using Serilog.Events;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>
/// 票 08：请求内日志自动带 <c>TenantId</c>/<c>UserId</c>/<c>TraceId</c>/<c>ClientIp</c>——
/// 一部分是中间件推进 <see cref="Serilog.Context.LogContext"/>，一部分是请求摘要行的富化回调。
/// </summary>
public class LogContextTests
{
    [Fact]
    public async Task Middleware_pushes_request_context_onto_every_log_event()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9, 10.0.0.1";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "42")],
            authenticationType: "Test"));

        var tenantContext = new TenantContext();
        tenantContext.Set(5);

        var middleware = new LogContextMiddleware(_ =>
        {
            logger.Information("inside the request");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenantContext);

        var entry = Assert.Single(sink.Snapshot());

        Assert.Equal(5L, Scalar(entry, LogContextMiddleware.TenantIdProperty));
        Assert.Equal("42", Scalar(entry, LogContextMiddleware.UserIdProperty));
        Assert.Equal("203.0.113.9", Scalar(entry, LogContextMiddleware.ClientIpProperty));
        Assert.Equal(context.TraceIdentifier, Scalar(entry, LogContextMiddleware.TraceIdProperty));
    }

    [Fact]
    public void Request_summary_enrichment_sets_tenant_user_trace_and_client_ip()
    {
        var tenantContext = new TenantContext();
        tenantContext.Set(7);

        var services = new ServiceCollection()
            .AddScoped<ITenantContext>(_ => tenantContext)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "77")],
            authenticationType: "Test"));

        var diagnosticContext = new RecordingDiagnosticContext();

        LogContextMiddleware.EnrichDiagnosticContext(diagnosticContext, context);

        Assert.Equal(7L, diagnosticContext.Properties[LogContextMiddleware.TenantIdProperty]);
        Assert.Equal("77", diagnosticContext.Properties[LogContextMiddleware.UserIdProperty]);
        Assert.Equal("203.0.113.9", diagnosticContext.Properties[LogContextMiddleware.ClientIpProperty]);
        Assert.Equal(context.TraceIdentifier, diagnosticContext.Properties[LogContextMiddleware.TraceIdProperty]);
    }

    [Fact]
    public void Missing_tenant_and_user_leave_the_properties_absent()
    {
        var services = new ServiceCollection()
            .AddScoped<ITenantContext>(_ => new TenantContext())
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var diagnosticContext = new RecordingDiagnosticContext();

        LogContextMiddleware.EnrichDiagnosticContext(diagnosticContext, context);

        Assert.False(diagnosticContext.Properties.ContainsKey(LogContextMiddleware.TenantIdProperty));
        Assert.False(diagnosticContext.Properties.ContainsKey(LogContextMiddleware.UserIdProperty));
    }

    private static object? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value
            : null;

    private sealed class RecordingDiagnosticContext : IDiagnosticContext
    {
        public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

        public void Set(string propertyName, object? value, bool destructureObjects = false) =>
            Properties[propertyName] = value;

        public void SetException(Exception exception) => Properties["Exception"] = exception;
    }
}
