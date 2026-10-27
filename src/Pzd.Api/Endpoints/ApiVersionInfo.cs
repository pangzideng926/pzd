using System.Reflection;

namespace Pzd.Api.Endpoints;

/// <summary>
/// <c>/api/version</c> 的载荷。版本号由 MinVer 从 git tag 计算，来源是
/// <c>AssemblyInformationalVersion</c> 的 <c>{semver}+{sha}</c>；buildId 取 sha 前 7 位，
/// 用于前端比对新版本（架构 §15.1）。buildTime 取程序集文件写入时间——
/// 它等价于构建产物时间，前端只用来展示，不参与任何比对。
/// <para>
/// <c>runId</c> 是**本次启动**的唯一标记（F-03-A11）：由启动方通过环境变量 <c>PZD_RUN_ID</c> 注入，
/// 原样回显。它证明「应答的这个进程是本次运行拉起来的」，而 <c>buildId</c> 只能证明版本相同——
/// 同一 hash 的残留实例也满足后者，所以归属判定只认 runId。
/// </para>
/// </summary>
public sealed record ApiVersionInfo(string Version, string BuildId, DateTimeOffset BuildTime, string? RunId = null)
{
    public static ApiVersionInfo Current { get; } =
        Read(typeof(ApiVersionInfo).Assembly, Environment.GetEnvironmentVariable("PZD_RUN_ID"));

    internal static ApiVersionInfo Read(Assembly assembly, string? runId = null)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var buildTime = GetBuildTime(assembly);

        // 归属标记：没有就是没有（null），不编造默认值，验收侧取不到证据就判失败。
        if (string.IsNullOrWhiteSpace(runId))
        {
            runId = null;
        }

        if (string.IsNullOrWhiteSpace(informational))
        {
            return new ApiVersionInfo("0.0.0", "unknown", buildTime, runId);
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);

        if (plus < 0)
        {
            return new ApiVersionInfo(informational, "unknown", buildTime, runId);
        }

        var buildId = informational[(plus + 1)..];

        return new ApiVersionInfo(
            informational[..plus],
            buildId.Length > 7 ? buildId[..7] : buildId,
            buildTime,
            runId);
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
