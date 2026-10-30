using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 审计字段、雪花主键、版本号初值的自动填充（architecture.md §5.3）。
/// 逐列回调，只有实体上真的存在该属性时属性名才会出现，因此同一套约定可以直接用在
/// 只有部分字段的实体上（例如没有软删除的表就没有 <c>DeletedToken</c>）。
/// </summary>
internal static class PersistenceAudit
{
    /// <summary>
    /// 同一次插入里每列都会触发一次回调，时间戳必须取同一个值，否则 CreatedAt 与 UpdatedAt
    /// 会差几十微秒——那不是「同一时刻」，回收站按时间排序时会出错。
    /// 用弱表按实体实例记住首次取值，实体被回收后自动消失。
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, InsertStampHolder> InsertStamp = new();

    private sealed class InsertStampHolder
    {
        public DateTime Value { get; init; }
    }

    /// <summary>一次更新要写的审计值。先算好、SQL 成功之后才落实体。</summary>
    internal readonly record struct UpdateStamp(
        DateTime UpdatedAt,
        long UpdatedBy,
        bool MarkDeleted,
        long DeletedToken,
        DateTime? DeletedAt,
        long DeletedBy);

    public static void Attach(SqlSugarClient db, PersistenceOptions options, Func<long> nextId)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nextId);

        db.Aop.DataExecuting = (_, entity) =>
        {
            if (entity.EntityValue is null)
            {
                return;
            }

            switch (entity.OperationType)
            {
                case DataFilterType.InsertByObject:
                    OnInsert(entity, options, nextId);
                    break;
                case DataFilterType.UpdateByObject:
                    // 写入口是 db.Ado.ExecuteCommandAsync，**不经过这里**；能走到这儿的只有
                    // db.Updateable(...) 这条旁路。只要 Version 为正就放行的话，「加载→改→保存」
                    // 这种最正常的用法正好能穿过去：整行搬到别的租户、抹掉创建审计（06 三返复验 P5a）。
                    // 所以无条件拒绝，不做「版本对了就放行」的折中。
                    throw new InvalidOperationException(
                        $"类型「{entity.EntityName}」的更新必须走 PersistenceWriter.UpdateAsync / SoftDeleteAsync / RestoreAsync："
                        + "直接 db.Updateable 不带版本条件与租户谓词，会把整行搬到别的租户、抹掉创建审计，并让 Version 失去意义。");
                case DataFilterType.DeleteByObject:
                    // 架构 §5.3 承诺软删除 + 回收站可恢复，物理删掉就永远恢复不了，也没有任何提示。
                    // 这里把「顺手写 db.Deleteable(entity)」变成一眼能看懂的失败（06 审计 high 3）。
                    // 白名单留给 Outbox 已消费清理、审计日志归档这类合法物理删除（06 重审 low 1）。
                    if (IsHardDeleteAllowed(entity, options.HardDeleteTypes))
                    {
                        break;
                    }

                    throw new InvalidOperationException(
                        $"类型「{entity.EntityName}」拒绝物理删除：本仓库的删除一律是软删除，"
                        + "请调用 PersistenceWriter.SoftDeleteAsync（它同时维护 DeletedToken / DeletedAt / DeletedBy）；"
                        + "确需物理清理时在 PersistenceOptions.HardDeleteTypes 里登记。");
            }
        };
    }

    private static void OnInsert(DataFilterModel entity, PersistenceOptions options, Func<long> nextId)
    {
        var now = InsertStamp.GetValue(entity.EntityValue!, _ => new InsertStampHolder { Value = UtcNowForDatabase() }).Value;

        switch (entity.PropertyName)
        {
            case PersistenceOptions.Columns.Id:
                if (ReadLong(entity.EntityValue!, PersistenceOptions.Columns.Id) == 0)
                {
                    entity.SetValue(nextId());
                }

                break;
            case PersistenceOptions.Columns.Version:
                entity.SetValue(1);
                break;
            case PersistenceOptions.Columns.CreatedAt:
            case PersistenceOptions.Columns.UpdatedAt:
                entity.SetValue(now);
                break;
            case PersistenceOptions.Columns.CreatedBy:
            case PersistenceOptions.Columns.UpdatedBy:
                entity.SetValue(options.CurrentUserId());
                break;
            case PersistenceOptions.Columns.DeletedToken:
                entity.SetValue(0L);
                break;
            case PersistenceOptions.Columns.TenantId:
                // 插入侧原来完全敞开：错填写进别家租户，漏填就是 TenantId=0 的幽灵行（06 重审 medium 3）。
                ApplyTenantOnInsert(entity, options);
                break;
        }
    }

    private static void ApplyTenantOnInsert(DataFilterModel entity, PersistenceOptions options)
    {
        var declared = ReadLong(entity.EntityValue!, PersistenceOptions.Columns.TenantId);
        var context = options.CurrentTenantId();

        if (context is null)
        {
            if (declared == 0)
            {
                throw new InvalidOperationException(
                    $"插入「{entity.EntityName}」时既没有租户上下文（PersistenceOptions.CurrentTenantId 返回 null），"
                    + "实体也没带租户号：拒绝写出 TenantId=0 的幽灵记录。");
            }

            return;
        }

        if (declared == 0)
        {
            entity.SetValue(context.Value);
            return;
        }

        if (declared != context.Value)
        {
            throw new InvalidOperationException(
                $"插入「{entity.EntityName}」时实体租户是 {declared}，当前租户上下文是 {context.Value}：拒绝跨租户写入。");
        }
    }

    private static bool IsHardDeleteAllowed(DataFilterModel entity, IReadOnlyList<Type> hardDeleteTypes)
    {
        // EntityValue 在这里不一定是实体本身（SqlSugar 内部可能传集合或包装对象），
        // 用 EntityName 比对类型名，再加一次运行时类型兜底。
        var actual = entity.EntityValue?.GetType();
        foreach (var allowed in hardDeleteTypes)
        {
            if (allowed.Name == entity.EntityName || allowed == actual || allowed.Name == actual?.Name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 算一次更新要写的审计值，**不改实体**：写入口要先拿它拼 SQL，等真的影响 1 行之后再落实体，
    /// 否则版本冲突时调用方手里的实体已经被改脏（06 审计 medium 3）。
    /// <para><paramref name="markDeleted"/>：这次是软删除（写 token / at / by）。</para>
    /// <para><paramref name="clearDeleted"/>：这次是恢复（token 归 0、at 与 by 清空）。</para>
    /// 两个布尔互斥，由写入口按操作语义给出，避免出现「算值用 A、拼 SET 用 B」那种自相矛盾（06 重审 medium 1）。
    /// </summary>
    internal static UpdateStamp ComputeUpdateStamp(object entity, long currentUserId, bool markDeleted, bool clearDeleted)
    {
        var now = UtcNowForDatabase();
        var id = ReadLong(entity, PersistenceOptions.Columns.Id);

        if (markDeleted)
        {
            return new UpdateStamp(now, currentUserId, MarkDeleted: true, DeletedToken: id, DeletedAt: now, DeletedBy: currentUserId);
        }

        if (clearDeleted)
        {
            return new UpdateStamp(now, currentUserId, MarkDeleted: false, DeletedToken: 0L, DeletedAt: null, DeletedBy: 0L);
        }

        return new UpdateStamp(
            now,
            currentUserId,
            MarkDeleted: false,
            DeletedToken: ReadLong(entity, PersistenceOptions.Columns.DeletedToken),
            DeletedAt: Read(entity, PersistenceOptions.Columns.DeletedAt) as DateTime?,
            DeletedBy: Read(entity, PersistenceOptions.Columns.DeletedBy) as long? ?? 0L);
    }

    /// <summary>SQL 影响 1 行之后，把算好的审计值落实到调用方实体上。</summary>
    internal static void ApplyUpdateStamp(object entity, in UpdateStamp stamp)
    {
        Set(entity, PersistenceOptions.Columns.UpdatedAt, stamp.UpdatedAt);
        Set(entity, PersistenceOptions.Columns.UpdatedBy, stamp.UpdatedBy);
        Set(entity, PersistenceOptions.Columns.IsDeleted, stamp.MarkDeleted);
        Set(entity, PersistenceOptions.Columns.DeletedToken, stamp.DeletedToken);
        Set(entity, PersistenceOptions.Columns.DeletedAt, stamp.DeletedAt);
        Set(entity, PersistenceOptions.Columns.DeletedBy, stamp.DeletedBy);
    }

    private static void Set(object entity, string propertyName, object? value) =>
        entity.GetType()
            .GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.SetValue(entity, value);

    /// <summary>
    /// 时间入库前对齐到微秒：MySQL <c>DATETIME(6)</c> 只存到微秒，.NET 的 100ns 精度写下去会被截断，
    /// 读回的值就和写出的值不相等。先在内存里截断，读写两端才是同一个值。
    /// 只截精度、不做时区转换（时区语义由 API 序列化层负责，架构书 §5.3）。
    /// </summary>
    private static DateTime UtcNowForDatabase()
    {
        const long TicksPerMicrosecond = 10;
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TicksPerMicrosecond), DateTimeKind.Utc);
    }

    private static object? Read(object entity, string propertyName) =>
        entity.GetType()
            .GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(entity);

    private static long ReadLong(object entity, string propertyName) => Read(entity, propertyName) switch
    {
        long value => value,
        int value => value,
        _ => 0L,
    };

    private static bool ReadBool(object entity, string propertyName) => Read(entity, propertyName) is true;
}
