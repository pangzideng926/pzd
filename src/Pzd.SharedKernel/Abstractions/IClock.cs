namespace Pzd.SharedKernel.Abstractions;

/// <summary>
/// 可替换的时钟：业务与基础设施不直接读系统时间，便于测试与审计。
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
