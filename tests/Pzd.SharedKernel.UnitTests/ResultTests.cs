namespace Pzd.SharedKernel.UnitTests;

/// <summary>05 号票 seam S2 的另一侧：Result / Result&lt;T&gt; 的成功与失败分支。</summary>
public sealed class ResultTests
{
    [Fact]
    public void 成功结果没有错误()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void 失败结果带着传入的错误()
    {
        var error = new Error("Identity.User.NotFound", "用户不存在");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void 带值的成功结果能取回原值()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void 带值的失败结果取值抛异常且带错误码()
    {
        var error = new Error("Identity.User.NotFound", "用户不存在");

        var result = Result<int>.Failure(error);

        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(error.Code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 空错误不能构成失败结果()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
    }
}
