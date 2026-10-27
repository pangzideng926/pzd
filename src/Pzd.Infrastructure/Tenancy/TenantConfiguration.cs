using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 从配置读取租户清单（本轮 <c>Tenants:*</c> 由 appsettings / 环境变量提供，步骤 3 换 master 库）。
/// <para>
/// 严格解析（F-05-1）：这是「停用/过期立即 403」的数据源，失败方向必须是拒绝而不是放行。
/// 任何出现但解析失败的 Status / ExpiresAt、非法 Id / Code、重复条目都在<strong>启动期</strong>抛异常。
/// </para>
/// </summary>
public static class TenantConfiguration
{
    public static IReadOnlyList<TenantInfo> Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var tenants = configuration.GetSection("Tenants")
            .GetChildren()
            .Select(ParseTenant)
            .ToArray();

        EnsureUnique(tenants, tenant => tenant.Id.ToString(CultureInfo.InvariantCulture), "Id");
        EnsureUnique(tenants, tenant => tenant.Code, "Code", StringComparer.OrdinalIgnoreCase);

        return tenants;
    }

    private static TenantInfo ParseTenant(IConfigurationSection section)
    {
        var key = section.Path;

        return new TenantInfo(
            ParseId(section, key),
            RequireNonBlank(section["Code"], key, "Code"),
            ParseStatus(section, key),
            ParseExpiresAt(section, key),
            section["ConnectionString"]);
    }

    private static long ParseId(IConfigurationSection section, string key)
    {
        var raw = section["Id"];

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            throw new InvalidOperationException(
                $"Tenants 配置 {key}.Id 非法: '{raw}'（必须是正整数，不允许静默丢弃）。");
        }

        return id;
    }

    private static string RequireNonBlank(string? value, string key, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tenants 配置 {key}.{name} 不能为空。");
        }

        return value;
    }

    /// <summary>只接受 <c>Active</c> / <c>Disabled</c> 名字；数字、未定义值、拼写错误一律拒绝。</summary>
    private static TenantStatus ParseStatus(IConfigurationSection section, string key)
    {
        var raw = section["Status"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"Tenants 配置 {key}.Status 缺失（必须显式写 Active 或 Disabled，禁止默认放行）。");
        }

        var isKnownName =
            string.Equals(raw, nameof(TenantStatus.Active), StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, nameof(TenantStatus.Disabled), StringComparison.OrdinalIgnoreCase);

        if (!isKnownName
            || !Enum.TryParse<TenantStatus>(raw, ignoreCase: true, out var status)
            || !Enum.IsDefined(status))
        {
            throw new InvalidOperationException(
                $"Tenants 配置 {key}.Status 非法: '{raw}'（只接受 Active 或 Disabled）。");
        }

        return status;
    }

    /// <summary>缺省（未出现）才表示永不过期；出现但解析失败必须抛。</summary>
    private static DateTimeOffset? ParseExpiresAt(IConfigurationSection section, string key)
    {
        var raw = section["ExpiresAt"];

        if (raw is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var expiresAt))
        {
            throw new InvalidOperationException(
                $"Tenants 配置 {key}.ExpiresAt 非法: '{raw}'（ISO 8601，例如 2027-01-31T00:00:00Z）。");
        }

        return expiresAt;
    }

    private static void EnsureUnique(
        IReadOnlyCollection<TenantInfo> tenants,
        Func<TenantInfo, string> selector,
        string name,
        StringComparer? comparer = null)
    {
        var duplicate = tenants
            .GroupBy(selector, comparer ?? StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tenants 配置中 {name} 重复: '{duplicate.Key}'（不允许静默覆盖）。");
        }
    }
}
