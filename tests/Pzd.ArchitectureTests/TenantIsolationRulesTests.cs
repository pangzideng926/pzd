namespace Pzd.ArchitectureTests;

/// <summary>
/// 租户隔离的代码检查（架构 §6.2 要点）：禁止在全局单例上 <c>ChangeDatabase</c>，
/// 租户连接只能经 Scoped 的 <c>ITenantDbProvider</c> 获取。
/// </summary>
public class TenantIsolationRulesTests
{
    [Fact]
    public void Change_database_is_never_used_in_product_sources()
    {
        var repositoryRoot = ArchRules.FindRepositoryRoot();
        var sourceDirectories = new[]
        {
            Path.Combine(repositoryRoot, "src", "Pzd.Infrastructure"),
            Path.Combine(repositoryRoot, "src", "Pzd.Api"),
        };

        var offenders = sourceDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(IsNotBuildOutput)
            .Where(path => File.ReadAllText(path).Contains(".ChangeDatabase(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "禁止在共享客户端上调用 .ChangeDatabase(，违规文件：" + string.Join(", ", offenders));
    }

    private static bool IsNotBuildOutput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
