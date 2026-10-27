using Pzd.Infrastructure;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

// 全局单例只注册 master；租户连接由 ITenantDbProvider 按需加入（禁止 ChangeDatabase）。
builder.Services.AddSingleton(_ => new SqlSugarScope(new ConnectionConfig
{
    ConfigId = "master",
    ConnectionString = builder.Configuration.GetConnectionString("master")
        ?? "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_master",
    DbType = DbType.MySql,
    IsAutoCloseConnection = true,
}));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITenantStore>(_ => new InMemoryTenantStore(TenantConfiguration.Load(builder.Configuration)));
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
