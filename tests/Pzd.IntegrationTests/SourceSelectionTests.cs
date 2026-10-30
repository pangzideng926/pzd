using Pzd.IntegrationTests.Fixtures;

namespace Pzd.IntegrationTests;

/// <summary>
/// 36 号票的来源优先级与不可用指引：AC-04 要求「容器可用时按 `mysql:8.4` 起库，容器不可用才回落外部真库」。
/// 本机没有 Docker，容器分支跑不起来，所以把判定抽成纯函数用用例钉住——
/// 否则「容器路径永远不执行」这种偏差只能等某台有 Docker 的机器才被发现。
/// </summary>
public sealed class SourceSelectionTests
{
    [Fact]
    public void 容器可用时即使配了外部连接串也走容器()
    {
        // AC-04：容器优先。若这里是 External，则任何同时有 Docker 的环境里容器分支都是纸面条款。
        Assert.Equal(
            MySqlSource.Container,
            MySqlServerFixture.ChooseSource(containerAvailable: true, externalConnectionString: "Server=127.0.0.1;"));
    }

    [Fact]
    public void 容器不可用时回落到外部连接串()
    {
        Assert.Equal(
            MySqlSource.External,
            MySqlServerFixture.ChooseSource(containerAvailable: false, externalConnectionString: "Server=127.0.0.1;"));
    }

    [Fact]
    public void 两者都不可用时抛出带中文指引的异常()
    {
        var containerFailure = new InvalidOperationException("Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'.");

        var exception = Assert.Throws<InvalidOperationException>(
            () => MySqlServerFixture.ChooseSource(
                containerAvailable: false,
                externalConnectionString: null,
                containerFailure: containerFailure));

        // 指引必须说清两条出路，且带上容器失败的原始原因——不能只甩一坨 Testcontainers 英文栈。
        Assert.Contains("Docker", exception.Message, StringComparison.Ordinal);
        Assert.Contains(MySqlServerFixture.ExternalConnectionStringVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(MySqlServerFixture.LocalConnectionStringFileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Failed to connect to Docker", exception.Message, StringComparison.Ordinal);
        Assert.Same(containerFailure, exception.InnerException);
    }
}
