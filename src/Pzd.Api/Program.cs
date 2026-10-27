using Pzd.Infrastructure;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

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

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => "PZD API");

app.Run();

public partial class Program;
