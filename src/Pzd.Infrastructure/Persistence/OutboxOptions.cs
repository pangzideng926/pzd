using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// Outbox 投递配置（配置节 <c>Outbox</c>）。
/// <para>
/// F-07-5：重试上限**只**从这里读取（默认 5，键 <c>Outbox:MaxAttempts</c>），代码里没有第二处硬编码；
/// 启动期由 <see cref="Load"/> 校验，非法值直接失败而不是静默退回默认值。
/// </para>
/// <para>步骤 3 落库时该配置键与死信/失败表同批对齐（移交清单第 6 项）。</para>
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Outbox";

    /// <summary>配置键：最大投递尝试次数。</summary>
    public const string MaxAttemptsKey = "MaxAttempts";

    /// <summary>默认最大投递尝试次数（票面契约，唯一来源）。</summary>
    public const int DefaultMaxAttempts = 5;

    /// <summary>最大投递尝试次数：达到后消息进入终态「已拒绝」，不再出队。</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>从配置读并校验：键缺省时用 <see cref="DefaultMaxAttempts"/>，非法值抛 <see cref="InvalidOperationException"/>。</summary>
    public static OutboxOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new OutboxOptions();
        var raw = configuration.GetSection(SectionName)[MaxAttemptsKey];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return options;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxAttempts)
            || maxAttempts < 1)
        {
            throw new InvalidOperationException(
                $"配置 {SectionName}:{MaxAttemptsKey} 必须是大于 0 的整数，实际为「{raw}」。");
        }

        options.MaxAttempts = maxAttempts;

        return options;
    }
}
