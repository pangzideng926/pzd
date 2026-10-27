using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure.UnitTests.Tenancy;

public class TenantResolutionMiddlewareTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);

    private static readonly TenantInfo[] Tenants =
    [
        new(1, "acme", TenantStatus.Active, null, "Server=127.0.0.1;Database=pzd_t_acme"),
        new(2, "beta", TenantStatus.Active, null, "Server=127.0.0.1;Database=pzd_t_beta"),
        new(3, "halted", TenantStatus.Disabled, null, "Server=127.0.0.1;Database=pzd_t_halted"),
        new(4, "lapsed", TenantStatus.Active, Now.AddDays(-1), "Server=127.0.0.1;Database=pzd_t_lapsed"),
        new(5, "later", TenantStatus.Active, Now.AddDays(1), "Server=127.0.0.1;Database=pzd_t_later"),
        new(6, "boundary", TenantStatus.Active, Now, "Server=127.0.0.1;Database=pzd_t_boundary"),
    ];

    [Fact]
    public async Task Logged_in_claim_wins_and_the_header_is_ignored()
    {
        var store = new RecordingTenantStore(Tenants);
        var (context, tenantContext, nextCalled) = await InvokeAsync(store, user: AuthenticatedUser(tenantId: 2), header: "acme");

        Assert.Equal(200, context.Response.StatusCode);
        Assert.True(nextCalled);
        Assert.True(tenantContext.HasTenant);
        Assert.Equal(2, tenantContext.TenantId);
        Assert.Equal(1, store.FindByIdCalls);
        Assert.Equal(0, store.FindByCodeCalls);
    }

    [Fact]
    public async Task Anonymous_header_resolves_tenant()
    {
        var store = new RecordingTenantStore(Tenants);
        var (context, tenantContext, nextCalled) = await InvokeAsync(store, header: "acme");

        Assert.True(nextCalled);
        Assert.Equal(1, tenantContext.TenantId);
        Assert.Equal(["acme"], store.RequestedCodes);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_subdomain_resolves_tenant()
    {
        var store = new RecordingTenantStore(Tenants);
        var (_, tenantContext, nextCalled) = await InvokeAsync(store, host: "beta.example.com");

        Assert.True(nextCalled);
        Assert.Equal(2, tenantContext.TenantId);
        Assert.Equal(["beta"], store.RequestedCodes);
    }

    [Fact]
    public async Task Subdomain_wins_over_header()
    {
        var store = new RecordingTenantStore(Tenants);
        var (_, tenantContext, _) = await InvokeAsync(store, host: "acme.example.com", header: "beta");

        Assert.Equal(1, tenantContext.TenantId);
        Assert.Equal(["acme"], store.RequestedCodes);
    }

    [Fact]
    public async Task Missing_tenant_source_returns_400_tenant_not_found()
    {
        var (context, tenantContext, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants));

        Assert.False(nextCalled);
        Assert.False(tenantContext.HasTenant);
        Assert.Equal(400, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.NotFound", problem.Code);
        Assert.Equal("https://pzd/errors/tenant.not-found", problem.Type);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal(problem.TraceId, context.Response.Headers["X-Trace-Id"].ToString());
    }

    [Fact]
    public async Task Disabled_tenant_returns_403_tenant_disabled()
    {
        var (context, tenantContext, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), header: "halted");

        Assert.False(nextCalled);
        Assert.False(tenantContext.HasTenant);
        Assert.Equal(403, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.Disabled", problem.Code);
        Assert.Equal("https://pzd/errors/tenant.disabled", problem.Type);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal(problem.TraceId, context.Response.Headers["X-Trace-Id"].ToString());
    }

    [Fact]
    public async Task Expired_tenant_returns_403_tenant_expired()
    {
        var (context, _, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), header: "lapsed");

        Assert.False(nextCalled);
        Assert.Equal(403, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.Expired", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal(problem.TraceId, context.Response.Headers["X-Trace-Id"].ToString());
    }

    [Fact]
    public async Task Tenant_expiring_later_is_allowed()
    {
        var (_, tenantContext, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), header: "later");

        Assert.True(nextCalled);
        Assert.Equal(5, tenantContext.TenantId);
    }

    [Fact]
    public async Task Tenant_expiring_exactly_now_is_rejected()
    {
        var (context, _, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), header: "boundary");

        Assert.False(nextCalled);
        Assert.Equal(403, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.Expired", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
        Assert.Equal(problem.TraceId, context.Response.Headers["X-Trace-Id"].ToString());
    }

    [Fact]
    public async Task Logged_in_without_tid_claim_returns_400_tenant_not_found()
    {
        var (context, _, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), user: AuthenticatedUser(claim: null));

        Assert.False(nextCalled);
        Assert.Equal(400, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.NotFound", problem.Code);
    }

    [Fact]
    public async Task Logged_in_with_invalid_tid_claim_returns_400_tenant_not_found()
    {
        var (context, _, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), user: AuthenticatedUser(claim: "abc"));

        Assert.False(nextCalled);
        Assert.Equal(400, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.NotFound", problem.Code);
    }

    [Fact]
    public async Task Logged_in_with_unknown_tid_returns_400_tenant_not_found()
    {
        var (context, _, nextCalled) = await InvokeAsync(new RecordingTenantStore(Tenants), user: AuthenticatedUser(claim: "999"));

        Assert.False(nextCalled);
        Assert.Equal(400, context.Response.StatusCode);

        var problem = await ReadProblemAsync(context);

        Assert.Equal("Tenant.NotFound", problem.Code);
    }

    [Fact]
    public async Task Store_is_queried_on_every_request()
    {
        var store = new RecordingTenantStore(Tenants);
        var middleware = new TenantResolutionMiddleware(_ => Task.CompletedTask);

        await InvokeAsync(store, header: "acme", middleware: middleware);
        await InvokeAsync(store, header: "beta", middleware: middleware);

        Assert.Equal(2, store.FindByCodeCalls);
        Assert.Equal(["acme", "beta"], store.RequestedCodes);
    }

    [Fact]
    public void Subdomain_parsing_rules()
    {
        Assert.Equal("acme", TenantResolutionMiddleware.ResolveSubdomainCode("acme.example.com"));
        Assert.Equal("acme", TenantResolutionMiddleware.ResolveSubdomainCode("acme.localhost"));
        Assert.Null(TenantResolutionMiddleware.ResolveSubdomainCode("localhost"));
        Assert.Null(TenantResolutionMiddleware.ResolveSubdomainCode("127.0.0.1"));
        Assert.Null(TenantResolutionMiddleware.ResolveSubdomainCode("www.example.com"));
        Assert.Null(TenantResolutionMiddleware.ResolveSubdomainCode(""));
    }

    [Theory]
    [InlineData("Identity.User.NameDuplicated", "identity.user.name-duplicated")]
    [InlineData("Tenant.NotFound", "tenant.not-found")]
    [InlineData("ClientTooOld", "client-too-old")]
    public void Error_codes_map_to_kebab_case_urls(string code, string expected) =>
        Assert.Equal(expected, Pzd.Infrastructure.Errors.ProblemDetailsWriter.ToKebabCase(code));

    internal static async Task<(HttpContext Context, TenantContext TenantContext, bool NextCalled)> InvokeAsync(
        RecordingTenantStore store,
        string? host = null,
        string? header = null,
        ClaimsPrincipal? user = null,
        IClock? clock = null,
        TenantResolutionMiddleware? middleware = null)
    {
        var nextCalled = false;
        middleware ??= new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity()),
        };

        context.Request.Host = new HostString(host ?? "localhost");
        context.Response.Body = new MemoryStream();

        if (header is not null)
        {
            context.Request.Headers[TenantResolutionMiddleware.TenantHeaderName] = header;
        }

        var tenantContext = new TenantContext();
        await middleware.InvokeAsync(context, tenantContext, store, clock ?? new FixedClock(Now));

        return (context, tenantContext, nextCalled);
    }

    internal static ClaimsPrincipal AuthenticatedUser(long tenantId) => AuthenticatedUser(tenantId.ToString());

    internal static ClaimsPrincipal AuthenticatedUser(string? claim)
    {
        var claims = claim is null
            ? Array.Empty<Claim>()
            : [new Claim(TenantResolutionMiddleware.TenantClaimType, claim)];

        var identity = new ClaimsIdentity(claims, authenticationType: "Test");

        return new ClaimsPrincipal(identity);
    }

    internal static async Task<ProblemBody> ReadProblemAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;

        return new ProblemBody(
            root.GetProperty("status").GetInt32(),
            root.GetProperty("type").GetString() ?? string.Empty,
            root.GetProperty("title").GetString() ?? string.Empty,
            root.GetProperty("code").GetString() ?? string.Empty,
            root.GetProperty("traceId").GetString() ?? string.Empty);
    }

    internal sealed record ProblemBody(int Status, string Type, string Title, string Code, string TraceId);

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; }
    }

    internal sealed class RecordingTenantStore : ITenantStore
    {
        private readonly InMemoryTenantStore _inner;
        private readonly List<string> _requestedCodes = [];

        public RecordingTenantStore(params TenantInfo[] tenants) => _inner = new InMemoryTenantStore(tenants);

        public int FindByIdCalls { get; private set; }

        public int FindByCodeCalls { get; private set; }

        public IReadOnlyList<string> RequestedCodes => _requestedCodes;

        public Task<TenantInfo?> FindByIdAsync(long tenantId, CancellationToken cancellationToken = default)
        {
            FindByIdCalls++;
            return _inner.FindByIdAsync(tenantId, cancellationToken);
        }

        public Task<TenantInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            FindByCodeCalls++;
            _requestedCodes.Add(code);
            return _inner.FindByCodeAsync(code, cancellationToken);
        }
    }
}
