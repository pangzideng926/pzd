using Pzd.Api.Endpoints;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// F-03-A11：本次运行标记（<c>PZD_RUN_ID</c>）只作为响应头 <c>X-Pzd-Run-Id</c> 输出，
/// 且仅在注入时有值——<c>/api/version</c> 的载荷必须保持票 03 验证过的三字段，
/// 不能因为归属判定多出一个 <c>runId: null</c> 的公开字段。
/// </summary>
public class ApiRunIdTests
{
    [Fact]
    public void Run_id_is_returned_verbatim_when_provided()
    {
        Assert.Equal("run-0123456789abcdef", ApiRunId.Normalize("run-0123456789abcdef"));
    }

    [Fact]
    public void Run_id_is_null_when_not_provided()
    {
        Assert.Null(ApiRunId.Normalize(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_run_id_is_treated_as_absent(string runId)
    {
        // 空白值不能当成有效归属证据：取不到就判失败，而不是比对一个空串。
        Assert.Null(ApiRunId.Normalize(runId));
    }

    [Fact]
    public void Header_name_is_fixed_for_the_verification_script()
    {
        // 脚本按名字取响应头，改名就是破坏契约。
        Assert.Equal("X-Pzd-Run-Id", ApiRunId.HeaderName);
    }
}
