using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var build = BuildInfo.Current;

app.Logger.LogInformation("Pzd.Api {Version}（build {BuildId}）已启动", build.Version, build.BuildId);

app.MapGet("/", () => Results.Ok(new { name = "Pzd.Api", version = build.Version, buildId = build.BuildId }));

app.Run();

/// <summary>
/// 当前程序集的版本信息。版本号由 MinVer 从 git tag 推导，写入 AssemblyInformationalVersion。
/// </summary>
internal sealed record BuildInfo(string Version, string BuildId)
{
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    private static BuildInfo FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return new BuildInfo("0.0.0-unknown", string.Empty);
        }

        var separator = informational.IndexOf('+', StringComparison.Ordinal);
        return separator < 0
            ? new BuildInfo(informational, string.Empty)
            : new BuildInfo(informational[..separator], informational[(separator + 1)..]);
    }
}
