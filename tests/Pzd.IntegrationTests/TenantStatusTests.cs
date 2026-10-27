using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pzd.Infrastructure.Tenancy;

namespace Pzd.IntegrationTests;

/// <summary>停用租户经真实 HTTP 管道（WebApplicationFactory）返回 403 <c>Tenant.Disabled</c>。</summary>
[Collection(MySqlCollection.Name)]
public class TenantStatusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MySqlFixture _fixture;

    public TenantStatusTests(MySqlFixture fixture) => _fixture = fixture;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [IntegrationFact]
    public async Task Disabled_tenant_returns_403_tenant_disabled_over_http()
    {
        _fixture.EnsureReady();

        await using var factory = new IntegrationApiFactory(
            [_fixture.TenantA, _fixture.TenantB, _fixture.DisabledTenant]);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant", MySqlFixture.DisabledTenantCode);

        using var response = await client.SendAsync(request, CancellationToken).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, CancellationToken)
            .ConfigureAwait(false);

        Assert.NotNull(problem);
        Assert.Equal("Tenant.Disabled", problem.Code);
        Assert.Equal("https://pzd/errors/tenant.disabled", problem.Type);
        Assert.Equal(403, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [IntegrationFact]
    public async Task Active_tenant_still_reaches_the_api()
    {
        _fixture.EnsureReady();

        await using var factory = new IntegrationApiFactory(
            [_fixture.TenantA, _fixture.TenantB, _fixture.DisabledTenant]);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant", MySqlFixture.TenantACode);

        using var response = await client.SendAsync(request, CancellationToken).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record ProblemBody(int Status, string Type, string Title, string Code, string TraceId);
}

/// <summary>API 接缝宿主：租户库指向真实测试库，换掉内存 Store 以覆盖停用租户。</summary>
internal sealed class IntegrationApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyList<TenantInfo> _tenants;

    public IntegrationApiFactory(IReadOnlyList<TenantInfo> tenants) => _tenants = tenants;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        // 沙箱里 EventLog 写入会被拒绝，测试宿主清空日志提供方，避免异常路径被日志失败打断。
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITenantStore>();
            services.AddSingleton<ITenantStore>(new InMemoryTenantStore(_tenants));
        });
    }
}
