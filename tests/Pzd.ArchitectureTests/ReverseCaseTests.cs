using NetArchTest.Rules;
using Pzd.SharedKernel.Domain;

namespace Pzd.ArchitectureTests;

/// <summary>
/// 反向用例：规则必须能抓到故意构造的违规类型，否则“全绿”没有意义。
/// </summary>
public class ReverseCaseTests
{
    [Fact]
    public void Domain_dependency_rule_detects_a_deliberate_sqlsugar_reference()
    {
        var result = Types.InAssembly(typeof(Fake).Assembly)
            .That()
            .HaveName(nameof(Fake))
            .Should()
            .NotHaveDependencyOn("SqlSugar")
            .GetResult();

        Assert.False(result.IsSuccessful);
        Assert.Single(result.FailingTypes!);
    }
}
