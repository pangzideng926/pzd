using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pzd.Api.UnitTests.Tenancy;
using Pzd.Infrastructure.Tenancy;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>
/// 观测测试宿主：保留生产 Serilog 管道；租户库与认证换成测试替身，
/// 便于在真实请求上验证日志富化与 OTel instrumentation。
/// </summary>
internal sealed class ObservabilityApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITenantStore>();
            services.AddSingleton<ITenantStore>(new InMemoryTenantStore(PzdApiFactory.Tenants));

            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}
