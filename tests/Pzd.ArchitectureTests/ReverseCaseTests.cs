using Pzd.SharedKernel.Domain;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 反向用例：复用生产规则（<see cref="ArchRules.DomainDependencyViolations"/>），把故意违规的
/// <see cref="Fake"/> 所在程序集并入输入，断言规则恰好报出 1 条违规。
/// 生产谓词若被改坏（例如命名空间选择器写错），这里会拿到 0 条违规而失败（F1）。
/// </summary>
public class ReverseCaseTests
{
    [Fact]
    public void Domain_dependency_rule_detects_the_deliberate_sqlsugar_reference()
    {
        var assembliesIncludingViolation = ArchRules.ProductAssemblies.Append(typeof(Fake).Assembly);

        var result = ArchRules.DomainDependencyViolations(
            assembliesIncludingViolation,
            ArchRules.SqlSugarDependency);

        Assert.False(result.IsSuccessful);
        Assert.Equal(["Pzd.SharedKernel.Domain.Fake"], ArchRules.FailingTypeNames(result));
    }
}
