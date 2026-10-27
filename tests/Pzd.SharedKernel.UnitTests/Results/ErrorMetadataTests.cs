using Pzd.SharedKernel.Results;

namespace Pzd.SharedKernel.UnitTests.Results;

/// <summary>
/// F8 / 票 12：<see cref="Error.Metadata"/> 必须是构造时的只读快照，不随调用方字典变化，
/// 也不能被调用方改写；空元数据保持共享的单例实例。
/// </summary>
public class ErrorMetadataTests
{
    [Fact]
    public void Mutating_the_source_dictionary_after_construction_does_not_affect_metadata()
    {
        var source = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["userName"] = "alice",
            ["attempt"] = 1,
        };

        var error = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在", source);

        source["userName"] = "bob";
        source["extra"] = true;
        source.Remove("attempt");

        Assert.Equal("alice", error.Metadata["userName"]);
        Assert.True(error.Metadata.ContainsKey("attempt"));
        Assert.Equal(1, error.Metadata["attempt"]);
        Assert.False(error.Metadata.ContainsKey("extra"));
        Assert.Equal(2, error.Metadata.Count);
    }

    [Fact]
    public void Metadata_cannot_be_mutated_through_a_dictionary_cast()
    {
        var error = new Error(
            "Identity.User.NameDuplicated",
            ErrorType.Conflict,
            "用户名已存在",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["userName"] = "alice" });

        var metadata = (IDictionary<string, object?>)error.Metadata;

        Assert.Throws<NotSupportedException>(() => metadata["userName"] = "bob");
        Assert.Throws<NotSupportedException>(() => metadata.Add("extra", true));
        Assert.Throws<NotSupportedException>(() => metadata.Remove("userName"));

        Assert.Equal("alice", error.Metadata["userName"]);
        Assert.Single(error.Metadata);
    }

    [Fact]
    public void Metadata_keys_are_compared_ordinally()
    {
        var source = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["userName"] = "alice",
        };

        var error = new Error("Identity.User.NameDuplicated", ErrorType.Conflict, "用户名已存在", source);

        Assert.True(error.Metadata.ContainsKey("userName"));
        Assert.False(error.Metadata.ContainsKey("UserName"));
        Assert.False(error.Metadata.ContainsKey("USERNAME"));
    }

    [Fact]
    public void Empty_metadata_is_a_shared_read_only_instance()
    {
        var withoutMetadata = new Error("Identity.User.NotFound", ErrorType.NotFound, "用户不存在");
        var withEmptyDictionary = new Error(
            "Identity.User.NotFound",
            ErrorType.NotFound,
            "用户不存在",
            new Dictionary<string, object?>(StringComparer.Ordinal));

        Assert.Empty(withoutMetadata.Metadata);
        Assert.Same(withoutMetadata.Metadata, withEmptyDictionary.Metadata);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, object?>)withoutMetadata.Metadata).Add("extra", true));
    }
}
