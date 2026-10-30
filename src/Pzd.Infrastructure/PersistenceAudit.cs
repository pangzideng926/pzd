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
        long DeletedToken,
        DateTime? DeletedAt,
        long? DeletedBy);

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
                    OnInsert(entity, options.CurrentUserId(), nextId);
                    break;
                case DataFilterType.UpdateByObject:
                    OnUpdate(entity, options.CurrentUserId());
                    break;
                case DataFilterType.DeleteByObject:
                    // 架构 §5.3 承诺软删除 + 回收站可恢复，物理删掉就永远恢复不了，也没有任何提示。
                    // 这里把「顺手写 db.Deleteable(entity)」变成一眼能看懂的失败（06 审计 high 3）。
                    throw new InvalidOperationException(
                        $"类型「{entity.EntityName}」拒绝物理删除：本仓库的删除一律是软删除，"
                        + "请调用 PersistenceWriter.SoftDeleteAsync（它同时维护 DeletedToken / DeletedAt / DeletedBy）。");
            }
        };
    }

    private static void OnInsert(DataFilterModel entity, long currentUserId, Func<long> nextId)
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
                entity.SetValue(currentUserId);
                break;
            case PersistenceOptions.Columns.DeletedToken:
                entity.SetValue(0L);
                break;
        }
    }

    private static void OnUpdate(DataFilterModel entity, long currentUserId)
    {
        var deleted = ReadBool(entity.EntityValue!, PersistenceOptions.Columns.IsDeleted);

        switch (entity.PropertyName)
        {
            case PersistenceOptions.Columns.UpdatedAt:
                entity.SetValue(UtcNowForDatabase());
                break;
            case PersistenceOptions.Columns.UpdatedBy:
                entity.SetValue(currentUserId);
                break;
            case PersistenceOptions.Columns.DeletedToken:
                // 软删除时把主键写进 DeletedToken：唯一索引 (TenantId, 业务键, DeletedToken) 才能
                // 既挡住活跃记录重名、又放行同名重建。未删除记录这条恒为 0（见 OnInsert）。
                if (deleted)
                {
                    entity.SetValue(ReadLong(entity.EntityValue!, PersistenceOptions.Columns.Id));
                }

                break;
            case PersistenceOptions.Columns.DeletedAt:
                if (deleted)
                {
                    entity.SetValue(UtcNowForDatabase());
                }

                break;
            case PersistenceOptions.Columns.DeletedBy:
                if (deleted)
                {
                    entity.SetValue(currentUserId);
                }

                break;
        }
    }

    /// <summary>
    /// 算一次更新的审计值，**不改实体**：写入口要先拿它拼 SQL，等真的影响 1 行之后再落实体，
    /// 否则版本冲突时调用方手里的实体已经被改脏（06 审计 medium 3）。
    /// 与 <see cref="OnUpdate"/> 用同一套规则，避免两条写路径的时间精度或用户来源不一致。
    /// </summary>
    internal static UpdateStamp ComputeUpdateStamp(object entity, long currentUserId, bool deleting)
    {
        var now = UtcNowForDatabase();
        var id = ReadLong(entity, PersistenceOptions.Columns.Id);

        return new UpdateStamp(
            UpdatedAt: now,
            UpdatedBy: currentUserId,
            DeletedToken: deleting ? id : ReadLong(entity, PersistenceOptions.Columns.DeletedToken),
            DeletedAt: deleting ? now : Read(entity, PersistenceOptions.Columns.DeletedAt) as DateTime?,
            DeletedBy: deleting ? currentUserId : Read(entity, PersistenceOptions.Columns.DeletedBy) as long?);
    }

    /// <summary>SQL 影响 1 行之后，把算好的审计值落实到调用方实体上。</summary>
    internal static void ApplyUpdateStamp(object entity, in UpdateStamp stamp, bool deleting)
    {
        Set(entity, PersistenceOptions.Columns.UpdatedAt, stamp.UpdatedAt);
        Set(entity, PersistenceOptions.Columns.UpdatedBy, stamp.UpdatedBy);

        if (!deleting)
        {
            return;
        }

        Set(entity, PersistenceOptions.Columns.IsDeleted, true);
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
