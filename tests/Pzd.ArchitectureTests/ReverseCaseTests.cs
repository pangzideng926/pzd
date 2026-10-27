using Pzd.SharedKernel.Domain;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 反向用例：复用生产规则（<see cref="ArchRules.DomainDependencyViolations"/>），把故意违规的
/// <see cref="Fake"/>（依赖 SqlSugar）与 <see cref="FakeInfrastructureDependency"/>（依赖 Pzd.Infrastructure）
/// 所在程序集并入输入，断言规则恰好报出 2 条违规。
/// 生产谓词若被改坏（命名空间选择器、禁止项），这里会拿到 0 或不同条数而失败（F1）。
/// </summary>
public class ReverseCaseTests
{
    [Fact]
    public void Domain_dependency_rule_detects_both_deliberate_violations()
    {
        var assembliesIncludingViolations = ArchRules.DomainScanAssemblies(ArchRules.FindRepositoryRoot())
            .Append(typeof(Fake).Assembly);

        var result = ArchRules.DomainDependencyViolations(
            assembliesIncludingViolations,
            ArchRules.SqlSugarDependency,
            ArchRules.InfrastructureProject);

        Assert.False(result.IsSuccessful);
        Assert.Equal(
            ["Pzd.SharedKernel.Domain.Fake", "Pzd.SharedKernel.Domain.FakeInfrastructureDependency"],
            ArchRules.FailingTypeNames(result).Order(StringComparer.Ordinal));
    }
}
