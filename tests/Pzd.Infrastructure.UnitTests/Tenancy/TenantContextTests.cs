using Microsoft.Extensions.DependencyInjection;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure.UnitTests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void Set_exposes_the_tenant()
    {
        var context = new TenantContext();

        Assert.False(context.HasTenant);

        context.Set(42);

        Assert.True(context.HasTenant);
        Assert.Equal(42, context.TenantId);
    }

    [Fact]
    public void Clear_removes_the_tenant()
    {
        var context = new TenantContext();
        context.Set(42);

        context.Clear();

        Assert.False(context.HasTenant);
        Assert.Throws<InvalidOperationException>(() => context.TenantId);
    }

    [Fact]
    public void Reading_an_unset_tenant_throws()
    {
        var context = new TenantContext();

        Assert.Throws<InvalidOperationException>(() => context.TenantId);
    }

    [Fact]
    public void Scoped_contexts_do_not_share_state()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        using var provider = services.BuildServiceProvider();

        using (var firstScope = provider.CreateScope())
        using (var secondScope = provider.CreateScope())
        {
            var first = firstScope.ServiceProvider.GetRequiredService<ITenantContext>();
            var second = secondScope.ServiceProvider.GetRequiredService<ITenantContext>();

            first.Set(1);
            second.Set(2);

            Assert.Equal(1, first.TenantId);
            Assert.Equal(2, second.TenantId);
            Assert.NotSame(first, second);
        }
    }
}
