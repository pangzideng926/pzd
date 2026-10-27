using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pzd.Api.Endpoints;
using Pzd.Api.Errors;
using Pzd.Api.Health;
using Pzd.Infrastructure;
using Pzd.Infrastructure.Persistence;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

// 只走控制台日志：Windows 上默认还会挂 EventLog provider，沙箱/服务账户无写权限时
// 写日志本身会抛异常并打断请求（实测 /health/ready 降级路径直接 500→连接中断）。
// Serilog + OTel 在票 08 接管，这里先保证「日志不成为故障源」。
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddPzdProblemDetails();

// 票 03：就绪探针。MySQL / Redis 任一不可用都返回 Degraded（不是 Unhealthy），
// 让「实例缺失时降级启动」与「真故障」在语义上分开；明细由 HealthResponseWriter 输出 mysql/redis 两项。
builder.Services
    .AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql")
    .AddCheck<RedisHealthCheck>("redis");

// 启动期加载并校验租户配置：非法配置直接启动失败，不等首个请求（F-05-1）。
var tenants = TenantConfiguration.Load(builder.Configuration);

// 全局单例只注册 master；租户连接由 ITenantDbProvider 按需建立独立 scope（禁止 ChangeDatabase）。
builder.Services.AddSingleton(_ => new SqlSugarScope(new ConnectionConfig
{
    ConfigId = "master",
    ConnectionString = builder.Configuration.GetConnectionString("master")
        ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_master",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true,
}));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITenantStore>(_ => new InMemoryTenantStore(tenants));
builder.Services.AddSingleton<TenantConnectionCache>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantDbProvider, TenantDbProvider>();

// 票 07：UoW + Outbox。本轮全内存实现，步骤 3 换成租户库事务与 outbox 表。
// F-07-5：重试上限只从配置读（`Outbox:MaxAttempts`，默认 5），启动期校验，非法值直接启动失败。
var outboxOptions = OutboxOptions.Load(builder.Configuration);
builder.Services.AddSingleton(outboxOptions);
builder.Services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
builder.Services.AddSingleton<IUnitOfWorkTransactionFactory, InMemoryUnitOfWorkTransactionFactory>();
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddHostedService<OutboxDispatcherHostedService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UsePzdStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => "PZD API");

// F-05-4：健康检查与版本端点必须在无租户上下文时可用（k8s / 监控 / 一键脚本都不带租户）。
// 标记 AllowAnonymousTenant 后中间件跳过解析与状态校验；其余端点缺租户仍是 400 Tenant.NotFound。
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .WithMetadata(new AllowAnonymousTenantAttribute());

app.MapGet("/api/version", () => Results.Ok(ApiVersionInfo.Current))
    .WithMetadata(new AllowAnonymousTenantAttribute());

// 就绪探针：带 mysql/redis 明细；两项缺配置即 Degraded，MapHealthChecks 对 Degraded 仍回 200。
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).WithMetadata(new AllowAnonymousTenantAttribute());

// 启动期降级日志（票 03 ④）：对每个不可用资源各打一条 Warning，文本固定为
// `Degraded mode: <MySQL|Redis> unavailable（...）`，验收时用真实启动的 stdout grep 举证。
var startupReport = await app.Services.GetRequiredService<HealthCheckService>()
    .CheckHealthAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

foreach (var entry in startupReport.Entries)
{
    if (entry.Value.Status == HealthStatus.Healthy)
    {
        continue;
    }

    var resource = entry.Key switch
    {
        "mysql" => "MySQL",
        "redis" => "Redis",
        _ => entry.Key,
    };

    app.Logger.LogWarning(
        "Degraded mode: {Resource} unavailable（{Detail}）",
        resource,
        entry.Value.Description);
}

app.Run();

public partial class Program;
