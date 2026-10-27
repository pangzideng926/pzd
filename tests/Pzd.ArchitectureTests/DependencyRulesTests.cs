namespace Pzd.ArchitectureTests;

/// <summary>
/// 分层依赖规则在**当前仓库**上的数据源与用例：规则本身定义在 <see cref="ArchRules"/>，
/// 反向用例复用同一份定义（见 <see cref="ReverseCaseTests"/>）。
/// </summary>
public class DependencyRulesTests
{
    private static readonly string RepositoryRoot = ArchRules.FindRepositoryRoot();

    [Fact]
    public void SharedKernel_does_not_reference_Infrastructure_or_Api() =>
        Assert.Empty(ArchRules.ForbiddenReferences(
            ArchRules.SharedKernelAssembly,
            "Pzd.Infrastructure",
            "Pzd.Api"));

    [Fact]
    public void Infrastructure_does_not_reference_Api() =>
        Assert.Empty(ArchRules.ForbiddenReferences(
            ArchRules.InfrastructureAssembly,
            "Pzd.Api"));

    [Theory]
    [InlineData(ArchRules.SqlSugarDependency)]
    [InlineData(ArchRules.AspNetCoreDependency)]
    [InlineData(ArchRules.InfrastructureProject)]
    [InlineData(ArchRules.ApiProject)]
    public void Domain_namespaces_do_not_depend_on_persistence_web_or_infrastructure_libraries(string forbiddenDependency)
    {
        var result = ArchRules.DomainDependencyViolations(ArchRules.ProductAssemblies, forbiddenDependency);

        Assert.True(
            result.IsSuccessful,
            "违规类型：" + string.Join(", ", ArchRules.FailingTypeNames(result)));
    }

    [Fact]
    public void Repository_root_points_at_a_real_source_tree()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "Pzd.slnx")), $"{RepositoryRoot} 下没有 Pzd.slnx");
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot, "src")), $"{RepositoryRoot} 下没有 src");
    }

    [Fact]
    public void Modules_in_this_repository_only_reference_core_and_other_modules_contracts()
    {
        var violations = ArchRules.DiscoverModules(RepositoryRoot)
            .SelectMany(module => ArchRules.ModuleReferenceViolations(module.Name, module.ReferencedProjects))
            .ToArray();

        Assert.True(violations.Length == 0, "违规的模块引用：" + string.Join(", ", violations));
    }

    [Fact]
    public void Module_discovery_reads_the_source_tree_and_skips_core_and_contracts_projects()
    {
        var repositoryRoot = CreateTemporaryRepositoryRoot(
            ("src/Pzd.Platform/Pzd.Platform.csproj", ProjectWithReferences(@"..\Pzd.Identity.Contracts\Pzd.Identity.Contracts.csproj")),
            ("src/Pzd.Identity/Pzd.Identity.csproj", ProjectWithReferences(@"..\Pzd.Identity.Contracts\Pzd.Identity.Contracts.csproj")),
            ("src/Pzd.Identity.Contracts/Pzd.Identity.Contracts.csproj", "<Project />"),
            ("src/Pzd.SharedKernel/Pzd.SharedKernel.csproj", "<Project />"));

        try
        {
            var modules = ArchRules.DiscoverModules(repositoryRoot);

            Assert.Equal(
                ["Pzd.Identity", "Pzd.Platform"],
                modules.Select(module => module.Name).Order(StringComparer.Ordinal));

            var platform = Assert.Single(modules, module => module.Name == "Pzd.Platform");
            Assert.Equal(["Pzd.Identity.Contracts"], platform.ReferencedProjects);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Modules_may_reference_other_modules_contracts()
    {
        var violations = ArchRules.ModuleReferenceViolations(
            "Pzd.Platform",
            ["Pzd.SharedKernel", "Pzd.Infrastructure", "Pzd.Identity.Contracts"]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Modules_must_not_reference_other_modules_directly()
    {
        var violations = ArchRules.ModuleReferenceViolations(
            "Pzd.Platform",
            ["Pzd.Identity", "Pzd.Api"]);

        Assert.Equal(["Pzd.Platform -> Pzd.Identity", "Pzd.Platform -> Pzd.Api"], violations);
    }

    private static string ProjectWithReferences(params string[] references) =>
        "<Project><ItemGroup>"
        + string.Concat(references.Select(reference =>
            $"<ProjectReference Include=\"{reference}\" />"))
        + "</ItemGroup></Project>";

    private static string CreateTemporaryRepositoryRoot(params (string RelativePath, string Content)[] files)
    {
        // Low IL 沙箱只允许写工作区，夹具目录放在测试输出目录下（bin/ 已被 .gitignore 忽略）。
        var root = Path.Combine(AppContext.BaseDirectory, "arch-fixtures", Guid.NewGuid().ToString("N"));

        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return root;
    }
}
