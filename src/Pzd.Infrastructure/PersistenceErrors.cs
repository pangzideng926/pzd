using MySqlConnector;
using Pzd.SharedKernel;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>持久化层能识别的失败种类；09 号票按它映射 ProblemDetails。</summary>
public enum PersistenceFailureKind
{
    /// <summary>乐观并发：更新时 Version 不匹配（影响 0 行）。</summary>
    VersionConflict,

    /// <summary>记录不存在，或存在但不属于当前租户（影响 0 行且查不到本租户的行）。</summary>
    NotFound,

    /// <summary>记录在回收站里（<c>IsDeleted=1</c>）：能恢复，所以不是 404 也不是并发冲突。</summary>
    RecordInTrash,

    /// <summary>恢复的目标不在回收站（库里那行是活跃的）：能恢复的才是 <see cref="RecordInTrash"/>，这条反过来。</summary>
    NotInTrash,

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

    /// <summary>
    /// 记录不存在，或存在但不属于当前租户。和版本冲突分开：一个是「查错地方」，一个是「同时被人改」，
    /// 压成同一个 409 会让排查方向全错（06 六审 high / Q4、Q7）。
    /// </summary>
    public static PersistenceFailure NotFound() => new(
        PersistenceFailureKind.NotFound,
        404,
        new Error("Persistence.Record.NotFound", "记录不存在，或不属于当前租户。"));

    /// <summary>
    /// 记录在回收站里。这是**数据状态**、不是用错 API，所以走返回值；能恢复，所以用 409 而不是 410
    /// （410 的语义是永久消失）。<c>code</c> 与 09 号票的 <c>Persistence.Record.InTrash</c> 对齐。
    /// </summary>
    public static PersistenceFailure RecordInTrash() => new(
        PersistenceFailureKind.RecordInTrash,
        409,
        new Error("Persistence.Record.InTrash", "这条记录已被删除，如需修改请先恢复（RestoreAsync）。"));

    /// <summary>
    /// 恢复的目标不在回收站（库里那行是活跃的）。这是**数据状态**，走返回值：
    /// 端点层若把它当异常，一次正常的「重复恢复」请求会冒成 500。
    /// 与 <see cref="RecordInTrash"/> 正好相反，两个 <c>code</c> 不能合并（06 七审后 / 张一一 `698e4da`）。
    /// </summary>
    public static PersistenceFailure NotInTrash() => new(
        PersistenceFailureKind.NotInTrash,
        409,
        new Error("Persistence.Record.NotInTrash", "这条记录不在回收站，无需恢复。"));

    public static PersistenceFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var current in Flatten(exception))
        {
            // 仅防御**外部**写路径：本仓库的 PersistenceWriter 用「影响 0 行」表达冲突，
            // 不抛这个异常。留着是因为别人用 SqlSugar 自带的版本校验更新时仍会走到这里。
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
