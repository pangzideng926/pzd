using System.Globalization;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 持久化层唯一的写入口（architecture.md §5.3）：乐观并发 + 租户谓词 + 审计字段。
/// 所有 UPDATE 都带 <c>WHERE Id AND Version=旧值 AND TenantId=当前租户</c>：
/// <list type="bullet">
/// <item>版本不匹配 → <c>Persistence.Record.VersionConflict</c>（09 号票映射 409）</item>
/// <item>租户上下文缺失 → 抛 <see cref="InvalidOperationException"/>，绝不按 Id 静默更新（06 审计 high 2）</item>
/// <item>唯一键冲突（例如恢复时业务键已被占用）→ <c>Persistence.Record.DuplicateKey</c></item>
/// <item>冲突时调用方实体保持原值，可以直接重载重试（06 审计 medium 3）</item>
/// </list>
/// 不用 SqlSugar 的 <c>IsEnableUpdateVersionValidation</c>：实测（SqlSugarCore 5.1.4.221）它在
/// <c>Updateable(entity)</c> 路径上只按主键更新，SET/WHERE 里都没有版本列，冲突根本拦不住。
/// </summary>
public static class PersistenceWriter
{
    /// <summary>SET 里永不出现的列：主键、版本、租户归属，以及「谁在什么时候创建」这两个不可变事实。</summary>
    private static readonly string[] ImmutableColumns =
    [
        PersistenceOptions.Columns.CreatedAt,
        PersistenceOptions.Columns.CreatedBy,
        PersistenceOptions.Columns.TenantId,
    ];

    /// <summary>
    /// 删除态的四个列。<see cref="WriteKind.Update"/> 一个都不写：删除态的翻转只由
    /// <see cref="SoftDeleteAsync"/> 与 <see cref="RestoreAsync"/> 负责，否则 Update 既能造出
    /// 「删除态但 DeletedToken=0」的幽灵行，也会把回收站的 DeletedAt/DeletedBy 刷成当前时间，
    /// 让回收站排序失真（06 四审 medium 2 / C9、C10）。
    /// </summary>
    private static readonly string[] DeleteColumns =
    [
        PersistenceOptions.Columns.IsDeleted,
        PersistenceOptions.Columns.DeletedToken,
        PersistenceOptions.Columns.DeletedAt,
        PersistenceOptions.Columns.DeletedBy,
    ];

    private enum WriteKind
    {
        Update,
        SoftDelete,
        Restore,
    }

    /// <summary>
    /// 插入的唯一入口（06 重审 high 5）：业务代码不直接写 <c>db.Insertable</c>。
    /// 审计四列、雪花主键、<c>Version=1</c> 与租户校验都由背后的 AOP 强制完成，
    /// 唯一键冲突翻译成 <c>Persistence.Record.DuplicateKey</c>（409）。
    /// </summary>
    public static async Task<PersistenceFailure?> InsertAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            await db.Insertable(entity).ExecuteCommandAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            var failure = PersistenceErrors.Classify(exception);
            if (failure.Kind != PersistenceFailureKind.Unknown)
            {
                return failure;
            }

            throw;
        }
    }

    /// <summary>
    /// 按版本条件更新一条活跃记录：成功返回 <c>null</c>；版本已被别人改掉返回版本冲突。
    /// 删除态的四个列一个都不碰，也不接受删除态或回收站里的实体——翻转请走
    /// <see cref="SoftDeleteAsync"/> / <see cref="RestoreAsync"/>（06 四审 medium 2）。
    /// </summary>
    public static Task<PersistenceFailure?> UpdateAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        CancellationToken cancellationToken = default)
        where T : class
        => WriteAsync(db, entity, options, WriteKind.Update, cancellationToken);

    /// <summary>
    /// 软删除的唯一入口（06 审计 high 3）：一次写好 <c>IsDeleted</c>、<c>DeletedToken</c>、
    /// <c>DeletedAt</c>、<c>DeletedBy</c>，并同样带版本与租户条件。
    /// </summary>
    public static Task<PersistenceFailure?> SoftDeleteAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        CancellationToken cancellationToken = default)
        where T : class
        => WriteAsync(db, entity, options, WriteKind.SoftDelete, cancellationToken);

    /// <summary>
    /// 回收站恢复（06 重审 medium 2）：把 <c>DeletedToken</c> 归 0、清空 <c>DeletedAt/DeletedBy</c>。
    /// 业务键已被活跃记录占用时由唯一索引拦下，返回 <c>Persistence.Record.DuplicateKey</c>，
    /// 不会出现两条活跃同名记录。
    /// </summary>
    public static Task<PersistenceFailure?> RestoreAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        var info = db.EntityMaintenance.GetEntityInfo<T>();
        if (!ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted))
        {
            throw new InvalidOperationException(
                $"类型「{typeof(T).Name}」的这条记录没有处于软删除状态，无法恢复。");
        }

        return WriteAsync(db, entity, options, WriteKind.Restore, cancellationToken);
    }

    private static async Task<PersistenceFailure?> WriteAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        WriteKind kind,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        var info = db.EntityMaintenance.GetEntityInfo<T>();
        var primary = info.Columns.SingleOrDefault(column => column.IsPrimarykey)
            ?? throw new InvalidOperationException($"类型「{typeof(T).Name}」没有主键列，无法按版本更新。");
        var version = info.Columns.SingleOrDefault(column => column.PropertyName == PersistenceOptions.Columns.Version)
            ?? throw new InvalidOperationException($"类型「{typeof(T).Name}」没有「{PersistenceOptions.Columns.Version}」列，无法做乐观并发。");

        var tenantColumn = info.Columns.SingleOrDefault(
            column => column.PropertyName == PersistenceOptions.Columns.TenantId);

        // 有租户列就必须知道当前租户：不知道就只能按 Id 改，那是写穿别家记录的路。
        var currentTenantId = tenantColumn is null ? (long?)null : options.CurrentTenantId()
            ?? throw new InvalidOperationException(
                $"类型「{typeof(T).Name}」有「{PersistenceOptions.Columns.TenantId}」列，但没有租户上下文"
                + "（PersistenceOptions.CurrentTenantId 返回 null）。写入口拒绝在没有租户上下文时按主键更新。");

        var currentVersion = Convert.ToInt64(version.PropertyInfo.GetValue(entity), CultureInfo.InvariantCulture);

        // 一个语义只留一个布尔：markDeleted 决定「这次是不是软删除」，clearDeleted 决定「这次是不是恢复」。
        var markDeleted = kind == WriteKind.SoftDelete;
        var clearDeleted = kind == WriteKind.Restore;

        // 删除态的翻转只由 SoftDeleteAsync / RestoreAsync 负责，Update 一律拒绝删除态实体：
        // 放行的话它要么把回收站记录改成「活跃但留着旧 token」，要么复活一条 token=0 的幽灵行
        // （06 三返复验 P6、06 四审 medium 2 / C9）。
        if (kind == WriteKind.Update)
        {
            if (ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted))
            {
                throw new InvalidOperationException(
                    $"类型「{typeof(T).Name}」的这条记录处于删除态，不能用 UpdateAsync 更新："
                    + "删除态只能经 PersistenceWriter.SoftDeleteAsync 标记、经 PersistenceWriter.RestoreAsync 恢复。");
            }

            if (ReadLong(info, entity, PersistenceOptions.Columns.DeletedToken) != 0)
            {
                throw new InvalidOperationException(
                    $"类型「{typeof(T).Name}」的这条记录仍在回收站里（{PersistenceOptions.Columns.DeletedToken} 不为 0），"
                    + "不能通过 UpdateAsync 改回活跃；请调用 PersistenceWriter.RestoreAsync。");
            }
        }

        // 先算，不落实体：冲突时调用方手里的对象要保持加载时的样子。
        var stamp = PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted, clearDeleted);

        var parameters = new List<SugarParameter>();
        var assignments = new List<string>();
        foreach (var column in info.Columns)
        {
            if (column == primary || column == version || ImmutableColumns.Contains(column.PropertyName, StringComparer.Ordinal))
            {
                continue;
            }

            // Update 一个删除列都不写：它们只能由软删除 / 恢复入口改写（06 四审 medium 2 / C10）。
            if (kind == WriteKind.Update && DeleteColumns.Contains(column.PropertyName, StringComparer.Ordinal))
            {
                continue;
            }

            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(new SugarParameter(name, ValueFor(column, entity, stamp)));
            assignments.Add($"`{column.DbColumnName}`={name}");
        }

        assignments.Add($"`{version.DbColumnName}`=`{version.DbColumnName}`+1");

        parameters.Add(new SugarParameter("@id", primary.PropertyInfo.GetValue(entity)));
        parameters.Add(new SugarParameter("@version", currentVersion));

        var where = $"`{primary.DbColumnName}`=@id AND `{version.DbColumnName}`=@version";
        if (tenantColumn is not null)
        {
            parameters.Add(new SugarParameter("@tenantId", currentTenantId));
            where += $" AND `{tenantColumn.DbColumnName}`=@tenantId";
        }

        var sql = $"UPDATE `{info.DbTableName}` SET {string.Join(",", assignments)} WHERE {where}";

        int affected;
        try
        {
            // token 直接传给驱动：它真的会取消命令，而不是只放弃等待（06 审计 low 3）。
            affected = await db.Ado.ExecuteCommandAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 恢复时业务键被活跃记录占着，撞的就是这里：翻译成 409 交给调用方，不往外抛。
            var failure = PersistenceErrors.Classify(exception);
            if (failure.Kind != PersistenceFailureKind.Unknown)
            {
                return failure;
            }

            throw;
        }

        if (affected == 0)
        {
            return PersistenceErrors.VersionConflict();
        }

        // 只有真的写进去了才回写实体。
        var next = Convert.ChangeType(
            currentVersion + 1,
            version.PropertyInfo.PropertyType,
            CultureInfo.InvariantCulture);
        version.PropertyInfo.SetValue(entity, next);
        PersistenceAudit.ApplyUpdateStamp(entity, stamp);

        return null;
    }

    /// <summary>审计列一律用算好的值（恢复时就是清空后的值），其余列用调用方实体上的值。</summary>
    private static object? ValueFor<T>(EntityColumnInfo column, T entity, PersistenceAudit.UpdateStamp stamp)
        where T : class
        => column.PropertyName switch
        {
            PersistenceOptions.Columns.UpdatedAt => stamp.UpdatedAt,
            PersistenceOptions.Columns.UpdatedBy => stamp.UpdatedBy,
            PersistenceOptions.Columns.IsDeleted => stamp.MarkDeleted,
            PersistenceOptions.Columns.DeletedToken => stamp.DeletedToken,
            PersistenceOptions.Columns.DeletedAt => stamp.DeletedAt,
            PersistenceOptions.Columns.DeletedBy => stamp.DeletedBy,
            _ => column.PropertyInfo.GetValue(entity),
        };

    private static bool ReadBool<T>(EntityInfo info, T entity, string propertyName)
        where T : class
        => info.Columns.SingleOrDefault(current => current.PropertyName == propertyName)
            ?.PropertyInfo.GetValue(entity) is true;

    private static long ReadLong<T>(EntityInfo info, T entity, string propertyName)
        where T : class
        => info.Columns.SingleOrDefault(current => current.PropertyName == propertyName)
            ?.PropertyInfo.GetValue(entity) switch
        {
            long value => value,
            int value => value,
            _ => 0L,
        };
}
