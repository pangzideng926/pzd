using Microsoft.Extensions.Configuration;
using Pzd.Infrastructure.Persistence;

namespace Pzd.Infrastructure.UnitTests.Persistence;

/// <summary>
/// 票 07 F-07-5：Outbox 重试上限只从配置读（默认 5，键 <c>Outbox:MaxAttempts</c>），
/// 启动期校验，非法值直接失败而不是退回默认值。
/// </summary>
public class OutboxOptionsTests
{
    [Fact]
    public void Max_attempts_is_read_from_the_outbox_section()
    {
        var options = OutboxOptions.Load(BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:MaxAttempts"] = "2",
        }));

        Assert.Equal(2, options.MaxAttempts);
    }

    [Fact]
    public void Max_attempts_defaults_to_five_when_the_key_is_absent()
    {
        // 票面契约：默认 5（唯一来源是 OutboxOptions.DefaultMaxAttempts）。
        var options = OutboxOptions.Load(BuildConfiguration());

        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(OutboxOptions.DefaultMaxAttempts, options.MaxAttempts);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Invalid_max_attempts_fails_fast(string configured)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Outbox:MaxAttempts"] = configured,
        });

        Assert.Throws<InvalidOperationException>(() => OutboxOptions.Load(configuration));
    }

    private static IConfigurationRoot BuildConfiguration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? [])
            .Build();
}
