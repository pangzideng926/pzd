using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Pzd.Api.UnitTests.Tenancy;
using Pzd.Infrastructure.Security;

namespace Pzd.Api.UnitTests.Security;

/// <summary>票 08：安全响应头就位；HSTS 只在非开发环境出现。</summary>
public class SecurityHeadersTests
{
    [Fact]
    public async Task Production_responses_carry_csp_frame_options_content_type_options_and_hsts()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal(SecurityHeadersMiddleware.HstsValue, Header(response, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_responses_do_not_send_hsts()
    {
        using var factory = new SecurityApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Error_responses_also_carry_the_security_headers()
    {
        using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join("; ", values)
            : throw new Xunit.Sdk.XunitException($"响应缺少安全头 {name}");

    private sealed class SecurityApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;

        public SecurityApiFactory(string environment) => _environment = environment;

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(_environment);
    }
}
