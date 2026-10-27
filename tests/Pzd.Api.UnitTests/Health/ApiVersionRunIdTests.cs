using Pzd.Api.Endpoints;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// F-03-A11：<c>/api/version</c> 的 <c>runId</c> 必须原样回显启动方注入的本次运行标记，
/// 没注入就是 null（不编造默认值）。归属判定只认这个值——<c>buildId</c> 只能证明版本相同，
/// 同 hash 的残留实例照样能应答，所以不能拿它当归属证据。
/// </summary>
public class ApiVersionRunIdTests
{
    [Fact]
    public void Run_id_is_echoed_verbatim_when_provided()
    {
        var version = ApiVersionInfo.Read(typeof(ApiVersionInfo).Assembly, "run-0123456789abcdef");

        Assert.Equal("run-0123456789abcdef", version.RunId);
    }

    [Fact]
    public void Run_id_is_null_when_not_provided()
    {
        var version = ApiVersionInfo.Read(typeof(ApiVersionInfo).Assembly, null);

        Assert.Null(version.RunId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_run_id_is_treated_as_absent(string runId)
    {
        var version = ApiVersionInfo.Read(typeof(ApiVersionInfo).Assembly, runId);

        Assert.Null(version.RunId);
    }
}
