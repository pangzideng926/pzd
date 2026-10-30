using MySqlConnector;
using Pzd.SharedKernel;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>持久化层能识别的失败种类；09 号票按它映射 ProblemDetails。</summary>
public enum PersistenceFailureKind
{
    /// <summary>乐观并发：更新时 Version 不匹配（影响 0 行）。</summary>
    VersionConflict,

    /// <summary>唯一键冲突：MySQL 1062。</summary>
    DuplicateKey,

    /// <summary>其它数据库错误，调用方按 500 处理。</summary>
    Unknown,
}

/// <summary>分类结果：带着可直接放进 <see cref="Result"/> 的领域错误与建议状态码。</summary>
public sealed record PersistenceFailure(PersistenceFailureKind Kind, int HttpStatusCode, Error Error);

/// <summary>
/// 把数据库异常翻译成领域错误（architecture.md §8.3）：
/// 并发冲突与唯一键冲突都是 409，其它走 500。端点层拿到的是 <see cref="Error"/>，不是异常。
/// </summary>
public static class PersistenceErrors
{
    /// <summary>MySQL 唯一键冲突错误号。</summary>
    private const int DuplicateEntryErrorNumber = 1062;

    /// <summary>版本冲突不是异常，而是「影响 0 行」这种结果，单独给一个构造入口。</summary>
    public static PersistenceFailure VersionConflict() => new(
        PersistenceFailureKind.VersionConflict,
        409,
        new Error("Persistence.Record.VersionConflict", "记录已被其他操作修改，请刷新后重试。"));

    public static PersistenceFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var current in Flatten(exception))
        {
            if (current is VersionExceptions)
            {
                return VersionConflict();
            }

            if (current is MySqlException { Number: DuplicateEntryErrorNumber })
            {
                return new PersistenceFailure(
                    PersistenceFailureKind.DuplicateKey,
                    409,
                    new Error("Persistence.Record.DuplicateKey", "同一业务键的记录已存在。"));
            }
        }

        return new PersistenceFailure(
            PersistenceFailureKind.Unknown,
            500,
            new Error("Persistence.Record.Unknown", "数据库操作失败。"));
    }

    /// <summary>异常可能被包在 <see cref="AggregateException"/> 或多层 InnerException 里。</summary>
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                foreach (var nested in Flatten(inner))
                {
                    yield return nested;
                }
            }

            yield break;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
