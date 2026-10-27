using Pzd.Infrastructure.Observability;
using Serilog;
using Serilog.Events;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>
/// 票 08：destructuring policy 脱敏——password / token / 手机号 / 身份证号被替换，
/// 普通字段保持原样。期望值是独立字面量，不复算实现。
/// </summary>
public class SensitiveDataDestructuringTests
{
    [Fact]
    public void Sensitive_properties_are_masked_in_structured_objects()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With(new SensitiveDataDestructuring())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information(
            "创建用户 {@User}",
            new
            {
                UserName = "alice",
                Password = "p@ssw0rd",
                Token = "jwt-token-value",
                Phone = "13800138000",
                IdCard = "110101199001011234",
            });

        var entry = Assert.Single(sink.Snapshot());
        var user = Assert.IsType<StructureValue>(entry.Properties["User"]);

        Assert.Equal("alice", Value(user, "UserName"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(user, "Password"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(user, "Token"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(user, "Phone"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(user, "IdCard"));
    }

    [Fact]
    public void Phone_and_id_card_values_are_masked_even_when_the_property_name_is_innocuous()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration()
            .Destructure.With(new SensitiveDataDestructuring())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information(
            "联系人 {@Contact}",
            new
            {
                Nickname = "bob",
                Contact = "13800138000",
                Document = "110101199001011234",
            });

        var entry = Assert.Single(sink.Snapshot());
        var contact = Assert.IsType<StructureValue>(entry.Properties["Contact"]);

        Assert.Equal("bob", Value(contact, "Nickname"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(contact, "Contact"));
        Assert.Equal(SensitiveDataDestructuring.Mask, Value(contact, "Document"));
    }

    private static object? Value(StructureValue structure, string propertyName) =>
        structure.Properties.Single(property => property.Name == propertyName).Value is ScalarValue scalar
            ? scalar.Value
            : null;
}
