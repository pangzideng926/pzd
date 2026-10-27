using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pzd.Infrastructure.Security;

/// <summary>
/// 票 08：Data Protection 密钥环持久化。路径来自 <c>DataProtection:KeyPath</c>；
/// 留空时落到当前用户的本地应用数据目录，避免密钥随进程重启丢失（生产可用环境变量覆盖）。
/// <para>接 Redis / 证书保护在步骤 3（架构 §7.3）。</para>
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>密钥环路径配置键。</summary>
    public const string KeyPathConfigKey = "DataProtection:KeyPath";

    /// <summary>Data Protection 应用名：同一应用名 + 同一密钥环才能跨实例解密。</summary>
    public const string ApplicationName = "pzd";

    /// <summary>注册 Data Protection，并把密钥持久化到配置指定的目录。</summary>
    public static IServiceCollection AddPzdDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = configuration[KeyPathConfigKey];

        var keyPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, ".dp-keys")
            : Path.GetFullPath(configured, environment.ContentRootPath);

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));

        return services;
    }
}
