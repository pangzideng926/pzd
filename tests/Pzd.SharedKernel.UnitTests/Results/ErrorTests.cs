using Pzd.SharedKernel.Results;

namespace Pzd.SharedKernel.UnitTests.Results;

public class ErrorTests
{
    [Theory]
    [InlineData("Identity.User.NameDuplicated")]
    [InlineData("Organization.Dept.CircularParent")]
    [InlineData("Tenant.NotFound")]
    [InlineData("Tenant.Disabled")]
    public void Well_formed_codes_are_accepted(string code) => Assert.True(Error.IsValidCode(code));

    [Theory]
    [InlineData("")]
    [InlineData("Identity")]
    [InlineData("identity.user.nameDuplicated")]
    [InlineData("Identity..NameDuplicated")]
    [InlineData("Identity.User.Name.Duplicated")]
    [InlineData("Identity.User.User-Name")]
    public void Malformed_codes_are_rejected(string code) => Assert.False(Error.IsValidCode(code));

    [Fact]
    public void Constructor_rejects_malformed_code() =>
        Assert.Throws<ArgumentException>(() =>
            new Error("identity.user.name", ErrorType.Validation, "用户名不合法"));

    [Fact]
    public void Errors_are_equal_by_code_ignoring_type_and_message()
    {
        var first = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在");
        var second = new Error("Identity.User.NameDuplicated", ErrorType.Validation, "另一个文案");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Errors_with_different_codes_are_not_equal()
    {
        var first = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在");
        var second = new Error("Identity.User.NotFound", ErrorType.Conflict, "用户名已存在");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Metadata_is_preserved()
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["userName"] = "alice",
        };

        var error = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在", metadata);

        Assert.Equal("alice", error.Metadata["userName"]);
    }

    [Fact]
    public void Metadata_defaults_to_empty()
    {
        var error = new Error("Identity.User.NotFound", ErrorType.NotFound, "用户不存在");

        Assert.Empty(error.Metadata);
    }
}
