using System.Reflection;
using NetArchTest.Rules;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 分层依赖规则（架构 §2 / §4）：不引用的方式由 NetArchTest 读取 IL 元数据判定。
/// 规则 4 在业务模块为空时允许空集（本轮只有底座工程）。
/// </summary>
public class DependencyRulesTests
{
    private const string SqlSugar = "SqlSugar";
    private const string AspNetCore = "Microsoft.AspNetCore";

    private static readonly Assembly SharedKernelAssembly = Assembly.Load("Pzd.SharedKernel");
    private static readonly Assembly InfrastructureAssembly = Assembly.Load("Pzd.Infrastructure");
    private static readonly Assembly ApiAssembly = Assembly.Load("Pzd.Api");

    private static readonly Assembly[] ProductAssemblies =
        [SharedKernelAssembly, InfrastructureAssembly, ApiAssembly];

    [Fact]
    public void SharedKernel_does_not_reference_Infrastructure_or_Api()
    {
        var references = ReferencedAssemblyNames(SharedKernelAssembly);

        Assert.DoesNotContain("Pzd.Infrastructure", references);
        Assert.DoesNotContain("Pzd.Api", references);
    }

    [Fact]
    public void Infrastructure_does_not_reference_Api()
    {
        var references = ReferencedAssemblyNames(InfrastructureAssembly);

        Assert.DoesNotContain("Pzd.Api", references);
    }

    [Theory]
    [InlineData(SqlSugar)]
    [InlineData(AspNetCore)]
    public void Domain_namespaces_do_not_depend_on_persistence_or_web_libraries(string forbiddenDependency)
    {
        var result = Types.InAssemblies(ProductAssemblies)
            .That()
            .ResideInNamespaceEndingWith(".Domain")
            .Should()
            .NotHaveDependencyOn(forbiddenDependency)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "违规类型：" + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Modules_only_reference_their_own_contracts_plus_the_core_projects()
    {
        var violations = new List<string>();

        foreach (var module in ModuleAssemblies())
        {
            var moduleName = module.GetName().Name!;
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "Pzd.SharedKernel",
                "Pzd.Infrastructure",
                moduleName,
                moduleName + ".Contracts",
            };

            violations.AddRange(ReferencedAssemblyNames(module)
                .Where(name => name.StartsWith("Pzd.", StringComparison.Ordinal) && !allowed.Contains(name))
                .Select(name => $"{moduleName} -> {name}"));
        }

        Assert.True(violations.Count == 0, "违规的模块引用：" + string.Join(", ", violations));
    }

    private static IReadOnlyCollection<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

    /// <summary>
    /// 业务模块 = Pzd.* 程序集中除底座工程、Contracts 与测试程序集之外的部分；本轮为空集。
    /// </summary>
    private static IEnumerable<Assembly> ModuleAssemblies()
    {
        var core = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pzd.SharedKernel",
            "Pzd.Infrastructure",
            "Pzd.Api",
            "Pzd.AppHost",
        };

        return Directory
            .GetFiles(AppContext.BaseDirectory, "Pzd.*.dll")
            .Where(path => !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .Select(Assembly.LoadFrom)
            .Where(assembly => assembly.GetName().Name is { } name
                && name.StartsWith("Pzd.", StringComparison.Ordinal)
                && !core.Contains(name)
                && !name.EndsWith(".Contracts", StringComparison.Ordinal)
                && !name.Contains("Tests", StringComparison.Ordinal));
    }
}
