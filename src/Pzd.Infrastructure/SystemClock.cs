using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure;

/// <summary>系统时钟（生产实现）；测试用 <see cref="IClock"/> 替身固定时间。</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
