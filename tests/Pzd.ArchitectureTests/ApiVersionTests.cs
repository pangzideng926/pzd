using System.Reflection;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 版本号来自 git tag（architecture.md §15.1）。这里只证明 MinVer 真的生效：
/// MSBuild 默认值是 1.0.0，出现 1.0.0 说明 MinVer 没跑。
/// 真正的分层规则不在这里，见 13 号票。
/// </summary>
public class ApiVersionTests
{
    [Fact]
    public void Api程序集版本由MinVer从git_tag推导()
    {
        var apiAssembly = Assembly.Load("Pzd.Api");
        var informational = apiAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informational), "Pzd.Api 缺少 AssemblyInformationalVersion");

        var parts = informational!.Split('+', 2);
        var version = parts[0];
        var buildId = parts.Length == 2 ? parts[1] : string.Empty;

        Assert.Matches(@"^\d+\.\d+\.\d+", version);
        Assert.NotEqual("1.0.0", version);
        Assert.Matches("^[0-9a-f]{7,40}$", buildId);
    }
}
