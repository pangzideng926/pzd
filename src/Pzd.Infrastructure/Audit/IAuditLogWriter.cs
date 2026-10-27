namespace Pzd.Infrastructure.Audit;

/// <summary>
/// 审计落库写入器。本票提供 <see cref="NullAuditLogWriter"/> 占位；
/// 步骤 3 接租户库时替换为真实实现（按租户库批量插入审计表）。
/// </summary>
public interface IAuditLogWriter
{
    /// <summary>写入一批审计记录；实现方负责事务与重试。</summary>
    Task WriteAsync(IReadOnlyList<AuditLogEntry> batch, CancellationToken cancellationToken);
}

/// <summary>默认空写入器：本票不落库（步骤 3 接 DB），但后台消费链路保持完整可测。</summary>
public sealed class NullAuditLogWriter : IAuditLogWriter
{
    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<AuditLogEntry> batch, CancellationToken cancellationToken) => Task.CompletedTask;
}
