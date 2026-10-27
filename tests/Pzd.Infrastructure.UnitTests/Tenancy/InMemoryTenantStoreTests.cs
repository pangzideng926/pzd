using Pzd.Infrastructure.Tenancy;

namespace Pzd.Infrastructure.UnitTests.Tenancy;

public class InMemoryTenantStoreTests
{
    private static readonly TenantInfo[] Tenants =
    [
        new(1, "acme", TenantStatus.Active, null, "Server=127.0.0.1;Database=pzd_t_acme"),
        new(2, "beta", TenantStatus.Disabled, null, "Server=127.0.0.1;Database=pzd_t_beta"),
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Finds_by_id()
    {
        var store = new InMemoryTenantStore(Tenants);

        var tenant = await store.FindByIdAsync(1, CancellationToken);

        Assert.NotNull(tenant);
        Assert.Equal("acme", tenant.Code);
    }

    [Fact]
    public async Task Finds_by_code_ignoring_case()
    {
        var store = new InMemoryTenantStore(Tenants);

        var tenant = await store.FindByCodeAsync("ACME", CancellationToken);

        Assert.NotNull(tenant);
        Assert.Equal(1, tenant.Id);
    }

    [Fact]
    public async Task Unknown_id_returns_null() =>
        Assert.Null(await new InMemoryTenantStore(Tenants).FindByIdAsync(99, CancellationToken));

    [Theory]
    [InlineData("nope")]
    [InlineData(" ")]
    [InlineData("")]
    public async Task Unknown_or_blank_code_returns_null(string code) =>
        Assert.Null(await new InMemoryTenantStore(Tenants).FindByCodeAsync(code, CancellationToken));
}
