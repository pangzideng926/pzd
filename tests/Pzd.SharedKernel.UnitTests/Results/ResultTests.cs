using Pzd.SharedKernel.Results;

namespace Pzd.SharedKernel.UnitTests.Results;

public class ResultTests
{
    [Fact]
    public void Success_carries_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var error = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Value_of_a_successful_generic_result_is_readable()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Value_of_a_failed_generic_result_throws()
    {
        var result = Result.Failure<int>(new Error("Identity.User.NotFound", ErrorType.NotFound, "用户不存在"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Generic_result_can_be_assigned_from_a_value()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Failure_requires_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Failure<int>(null!));
    }
}
