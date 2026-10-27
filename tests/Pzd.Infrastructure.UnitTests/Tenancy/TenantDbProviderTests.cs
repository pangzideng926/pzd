using Pzd.Infrastructure.Tenancy;

namespace Pzd.Infrastructure.UnitTests.Tenancy;

public class TenantDbProviderTests
{
    private static readonly TenantInfo[] Tenants =
    [
        new(1, "acme", TenantStatus.Active, null, "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_acme"),
        new(2, "beta", TenantStatus.Active, null, "Server=127.0.0.1;Port=3306;User ID=root;Password=;Database=pzd_t_beta"),
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_tenant_context_it_throws_tenant_not_resolved()
    {
        var (provider, _, _, _) = CreateProvider();

        var exception = await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => provider.GetConnectionScopeAsync(CancellationToken));

        Assert.Equal("Tenant.NotResolved", exception.Error.Code);
    }

    [Fact]
    public async Task Returns_a_scope_for_the_request_tenant_and_loads_it_once()
    {
        var (provider, store, _, context) = CreateProvider();
        context.Set(1);

        var client = await provider.GetConnectionScopeAsync(CancellationToken);

        Assert.NotNull(client);
        Assert.Equal("1", client.CurrentConnectionConfig.ConfigId);
        Assert.Equal(1, store.FindByIdCalls);
    }

    [Fact]
    public async Task Repeated_calls_reuse_the_loaded_connection()
    {
        var (provider, store, cache, context) = CreateProvider();
        context.Set(1);

        await provider.GetConnectionScopeAsync(CancellationToken);
        await provider.GetConnectionScopeAsync(CancellationToken);

        Assert.Equal(1, store.FindByIdCalls);
        Assert.True(cache.IsCached(1));
    }

    [Fact]
    public async Task Concurrent_calls_load_the_connection_only_once()
    {
        var (provider, store, _, context) = CreateProvider();
        context.Set(1);

        var clients = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => provider.GetConnectionScopeAsync(CancellationToken)));

        Assert.All(clients, Assert.NotNull);
        Assert.Equal(1, store.FindByIdCalls);
    }

    [Fact]
    public async Task Explicit_tenant_does_not_need_request_context()
    {
        var (provider, store, _, _) = CreateProvider();

        var client = await provider.GetConnectionScopeAsync(2, CancellationToken);

        Assert.NotNull(client);
        Assert.Equal("2", client.CurrentConnectionConfig.ConfigId);
        Assert.Equal(1, store.FindByIdCalls);
    }

    [Fact]
    public async Task Unknown_tenant_throws_tenant_not_found()
    {
        var (provider, _, _, _) = CreateProvider();

        var exception = await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => provider.GetConnectionScopeAsync(99, CancellationToken));

        Assert.Equal("Tenant.NotFound", exception.Error.Code);
    }

    private static (TenantDbProvider Provider, RecordingTenantStore Store, TenantConnectionCache Cache, TenantContext Context) CreateProvider()
    {
        var store = new RecordingTenantStore(Tenants);
        var cache = new TenantConnectionCache();
        var context = new TenantContext();
        var provider = new TenantDbProvider(cache, store, context);

        return (provider, store, cache, context);
    }

    private sealed class RecordingTenantStore : ITenantStore
    {
        private readonly InMemoryTenantStore _inner;

        public RecordingTenantStore(params TenantInfo[] tenants) => _inner = new InMemoryTenantStore(tenants);

        public int FindByIdCalls { get; private set; }

        public Task<TenantInfo?> FindByIdAsync(long tenantId, CancellationToken cancellationToken = default)
        {
            FindByIdCalls++;
            return _inner.FindByIdAsync(tenantId, cancellationToken);
        }

        public Task<TenantInfo?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            _inner.FindByCodeAsync(code, cancellationToken);
    }
}
