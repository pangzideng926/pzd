using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pzd.Infrastructure.Tenancy;

/// <summary>
/// 从配置读取本租户清单（本轮 <c>Tenants:*</c> 由 appsettings 提供，步骤 3 换 master 库）。
/// 不读环境变量里的密码：连接串由部署侧注入，仓库内 appsettings 不含真实凭据。
/// </summary>
public static class TenantConfiguration
{
    public static IReadOnlyList<TenantInfo> Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection("Tenants")
            .GetChildren()
            .Select(section => new TenantInfo(
                long.Parse(section["Id"] ?? "0", CultureInfo.InvariantCulture),
                section["Code"] ?? string.Empty,
                Enum.TryParse<TenantStatus>(section["Status"], ignoreCase: true, out var status)
                    ? status
                    : TenantStatus.Active,
                DateTimeOffset.TryParse(
                    section["ExpiresAt"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var expiresAt)
                    ? expiresAt
                    : null,
                section["ConnectionString"]))
            .Where(tenant => tenant.Id > 0 && !string.IsNullOrWhiteSpace(tenant.Code))
            .ToArray();
    }
}
