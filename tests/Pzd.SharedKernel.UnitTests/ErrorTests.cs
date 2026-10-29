namespace Pzd.SharedKernel.UnitTests;

/// <summary>05 号票 seam S2 的一侧：错误码格式校验，非法时抛 ArgumentException（已定的决定）。</summary>
public sealed class ErrorTests
{
    [Fact]
    public void 合法错误码能构造并保留原文()
    {
        var error = new Error("Identity.User.NotFound", "用户不存在");

        Assert.Equal("Identity.User.NotFound", error.Code);
        Assert.Equal("用户不存在", error.Message);
    }

    [Theory]
    [InlineData("User.NotFound")]
    [InlineData("Identity.NotFound")]
    [InlineData("Identity.User.NotFound.Extra")]
    [InlineData("Identity..NotFound")]
    [InlineData(".User.NotFound")]
    [InlineData("Identity.User.")]
    [InlineData("")]
    public void 非法错误码抛ArgumentException且消息含该码(string invalidCode)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Error(invalidCode, "随便"));

        Assert.Contains(invalidCode, exception.Message, StringComparison.Ordinal);
    }
}
