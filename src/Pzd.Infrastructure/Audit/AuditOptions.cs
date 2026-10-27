using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 审计队列配置（配置节 <c>Audit</c>）。容量与批量大小只从这里读；
/// 非法值在启动期失败，而不是静默退回默认值（与 <c>OutboxOptions</c> 同口径）。
/// </summary>
public sealed class AuditOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Audit";

    /// <summary>配置键：有界 Channel 容量。</summary>
    public const string QueueCapacityKey = "QueueCapacity";

    /// <summary>配置键：单批交给写入器的最大条数。</summary>
    public const string BatchSizeKey = "BatchSize";

    /// <summary>默认队列容量：1000 条。</summary>
    public const int DefaultQueueCapacity = 1000;

    /// <summary>默认批量大小：100 条。</summary>
    public const int DefaultBatchSize = 100;

    /// <summary>有界 Channel 容量。</summary>
    public int QueueCapacity { get; init; } = DefaultQueueCapacity;

    /// <summary>单批交给写入器的最大条数。</summary>
    public int BatchSize { get; init; } = DefaultBatchSize;

    /// <summary>从配置读取并校验：缺省用默认值，非法值抛 <see cref="InvalidOperationException"/>。</summary>
    public static AuditOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);

        return new AuditOptions
        {
            QueueCapacity = ReadPositive(section, QueueCapacityKey, DefaultQueueCapacity),
            BatchSize = ReadPositive(section, BatchSizeKey, DefaultBatchSize),
        };
    }

    private static int ReadPositive(IConfigurationSection section, string key, int fallback)
    {
        var raw = section[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new InvalidOperationException(
                $"配置 {SectionName}:{key} 必须是大于 0 的整数，实际为「{raw}」。");
        }

        return value;
    }
}
