using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pzd.Api.UnitTests.Tenancy;

public class TenantResolutionApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Anonymous_header_resolves_tenant_and_reaches_the_endpoint()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant", "acme");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("acme", Assert.Single(factory.TenantStore.RequestedCodes));
    }

    [Fact]
    public async Task Missing_tenant_source_returns_400_tenant_not_found()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(problem);
        Assert.Equal("Tenant.NotFound", problem.Code);
        Assert.Equal("https://pzd/errors/tenant.not-found", problem.Type);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task Subdomain_resolves_tenant()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "acme.example.com";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("acme", Assert.Single(factory.TenantStore.RequestedCodes));
    }

    [Fact]
    public async Task Subdomain_wins_over_header()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "acme.example.com";
        request.Headers.Add("X-Tenant", "beta");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("acme", Assert.Single(factory.TenantStore.RequestedCodes));
    }

    [Fact]
    public async Task Logged_in_claim_wins_and_header_is_ignored()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(TestAuthenticationHandler.UserHeader, "42");
        request.Headers.Add(TestAuthenticationHandler.TenantClaimHeader, "2");
        request.Headers.Add("X-Tenant", "acme");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.TenantStore.FindByIdCalls);
        Assert.Equal(0, factory.TenantStore.FindByCodeCalls);
    }

    [Fact]
    public async Task Disabled_tenant_returns_403_tenant_disabled()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant", "halted");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(problem);
        Assert.Equal("Tenant.Disabled", problem.Code);
        Assert.Equal("https://pzd/errors/tenant.disabled", problem.Type);
    }

    [Fact]
    public async Task Expired_tenant_returns_403_tenant_expired()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Tenant", "lapsed");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, TestContext.Current.CancellationToken);

        Assert.NotNull(problem);
        Assert.Equal("Tenant.Expired", problem.Code);
    }

    [Fact]
    public async Task Store_is_queried_on_every_request()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        foreach (var tenantCode in new[] { "acme", "beta" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Add("X-Tenant", tenantCode);

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(2, factory.TenantStore.FindByCodeCalls);
        Assert.Equal(["acme", "beta"], factory.TenantStore.RequestedCodes);
    }

    private sealed record ProblemBody(int Status, string Type, string Title, string Code);
}
