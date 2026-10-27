using System.Reflection;

namespace Pzd.Api.Endpoints;

/// <summary>
/// <c>/api/version</c> 的载荷。版本号由 MinVer 从 git tag 计算，来源是
/// <c>AssemblyInformationalVersion</c> 的 <c>{semver}+{sha}</c>；buildId 取 sha 前 7 位，
/// 用于前端比对新版本（架构 §15.1）。buildTime 取程序集文件写入时间——
/// 它等价于构建产物时间，前端只用来展示，不参与任何比对。
/// </summary>
public sealed record ApiVersionInfo(string Version, string BuildId, DateTimeOffset BuildTime)
{
    public static ApiVersionInfo Current { get; } = Read(typeof(ApiVersionInfo).Assembly);

    internal static ApiVersionInfo Read(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var buildTime = GetBuildTime(assembly);

        if (string.IsNullOrWhiteSpace(informational))
        {
            return new ApiVersionInfo("0.0.0", "unknown", buildTime);
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        if (plus < 0)
        {
            return new ApiVersionInfo(informational, "unknown", buildTime);
        }

        var buildId = informational[(plus + 1)..];

        return new ApiVersionInfo(
            informational[..plus],
            buildId.Length > 7 ? buildId[..7] : buildId,
            buildTime);
    }

    private static DateTimeOffset GetBuildTime(Assembly assembly)
    {
        var location = assembly.Location;

        if (string.IsNullOrEmpty(location) || !File.Exists(location))
        {
            return DateTimeOffset.UnixEpoch;
        }

        return new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
    }
}
