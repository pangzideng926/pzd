using Microsoft.Extensions.Configuration;
using Pzd.Infrastructure.Tenancy;

namespace Pzd.Infrastructure.UnitTests.Tenancy;

/// <summary>
/// F-05-1：租户配置必须严格解析，任何非法/可疑值在启动期抛异常，绝不 fail-open。
/// </summary>
public class TenantConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static IConfiguration SingleTenant(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tenants:0:Id"] = "1",
            ["Tenants:0:Code"] = "acme",
            ["Tenants:0:Status"] = "Active",
            ["Tenants:0:ConnectionString"] = "Server=127.0.0.1;Database=pzd_t_acme",
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Parses_all_fields()
    {
        var tenant = Assert.Single(TenantConfiguration.Load(SingleTenant(
            ("Tenants:0:ExpiresAt", "2027-01-31T00:00:00Z"))));

        Assert.Equal(1, tenant.Id);
        Assert.Equal("acme", tenant.Code);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero), tenant.ExpiresAt);
        Assert.Equal("Server=127.0.0.1;Database=pzd_t_acme", tenant.ConnectionString);
    }

    [Fact]
    public void Missing_expiry_means_never_expires()
    {
        var tenant = Assert.Single(TenantConfiguration.Load(SingleTenant()));

        Assert.Null(tenant.ExpiresAt);
    }

    [Theory]
    [InlineData("Disabeld")]
    [InlineData("42")]
    [InlineData("1")]
    [InlineData("")]
    public void Invalid_status_throws_instead_of_defaulting_to_active(string status)
    {
        var configuration = SingleTenant(("Tenants:0:Status", status));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));

        Assert.Contains("Status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_status_throws()
    {
        var configuration = Config(
            ("Tenants:0:Id", "1"),
            ("Tenants:0:Code", "acme"),
            ("Tenants:0:ConnectionString", "Server=127.0.0.1;Database=pzd_t_acme"));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));

        Assert.Contains("Status", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-13-99")]
    [InlineData("tomorrow")]
    public void Invalid_expiry_throws_instead_of_never_expiring(string expiresAt)
    {
        var configuration = SingleTenant(("Tenants:0:ExpiresAt", expiresAt));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));

        Assert.Contains("ExpiresAt", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void Invalid_id_throws(string id)
    {
        var configuration = SingleTenant(("Tenants:0:Id", id));

        Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));
    }

    [Fact]
    public void Blank_code_throws()
    {
        var configuration = SingleTenant(("Tenants:0:Code", " "));

        Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));
    }

    [Fact]
    public void Duplicate_ids_throw()
    {
        var configuration = Config(
            ("Tenants:0:Id", "1"), ("Tenants:0:Code", "acme"), ("Tenants:0:Status", "Active"),
            ("Tenants:1:Id", "1"), ("Tenants:1:Code", "beta"), ("Tenants:1:Status", "Active"));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));

        Assert.Contains("Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_codes_ignoring_case_throw()
    {
        var configuration = Config(
            ("Tenants:0:Id", "1"), ("Tenants:0:Code", "acme"), ("Tenants:0:Status", "Active"),
            ("Tenants:1:Id", "2"), ("Tenants:1:Code", "ACME"), ("Tenants:1:Status", "Active"));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantConfiguration.Load(configuration));

        Assert.Contains("Code", exception.Message, StringComparison.Ordinal);
    }
}
