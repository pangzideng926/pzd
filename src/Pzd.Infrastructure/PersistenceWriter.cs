using System.Globalization;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 持久化层唯一的写入口（architecture.md §5.3）：乐观并发 + 租户谓词 + 审计字段。
/// 所有 UPDATE 都带 <c>WHERE Id AND Version=旧值 AND TenantId=当前租户</c>；
/// <list type="bullet">
/// <item>版本不匹配 → <c>Persistence.Record.VersionConflict</c>（09 号票映射 409）</item>
/// <item>租户上下文缺失 → 抛 <see cref="InvalidOperationException"/>，绝不按 Id 静默更新（06 审计 high 2）</item>
/// <item>冲突时调用方实体保持原值，可以直接重载重试（06 审计 medium 3）</item>
/// </list>
/// 不用 SqlSugar 的 <c>IsEnableUpdateVersionValidation</c>：实测（SqlSugarCore 5.1.4.221）它在
/// <c>Updateable(entity)</c> 路径上只按主键更新，SET/WHERE 里都没有版本列，冲突根本拦不住。
/// </summary>
public static class PersistenceWriter
{
    /// <summary>
    /// SET 里永不出现的列：主键、版本、租户归属，以及「谁在什么时候创建」这两个不可变事实
    /// （06 审计 high 1：分离实体更新会把它们抹成默认值）。
    /// </summary>
    private static readonly string[] ImmutableColumns =
    [
        PersistenceOptions.Columns.CreatedAt,
        PersistenceOptions.Columns.CreatedBy,
        PersistenceOptions.Columns.TenantId,
    ];

    /// <summary>
    /// 按版本条件更新一条记录：成功返回 <c>null</c>；版本已被别人改掉返回版本冲突。
    /// </summary>
    public static Task<PersistenceFailure?> UpdateAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        CancellationToken cancellationToken = default)
        where T : class
        => WriteAsync(db, entity, options, deleting: false, cancellationToken);

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
        => WriteAsync(db, entity, options, deleting: true, cancellationToken);

    private static async Task<PersistenceFailure?> WriteAsync<T>(
        SqlSugarClient db,
        T entity,
        PersistenceOptions options,
        bool deleting,
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
        var markDeleted = deleting || ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted);

        // 先算，不落实体：冲突时调用方手里的对象要保持加载时的样子。
        var stamp = PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted);

        var parameters = new List<SugarParameter>();
        var assignments = new List<string>();
        foreach (var column in info.Columns)
        {
            if (column == primary || column == version || ImmutableColumns.Contains(column.PropertyName, StringComparer.Ordinal))
            {
                continue;
            }

            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(new SugarParameter(name, ValueFor(column, entity, stamp, deleting)));
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

        // token 直接传给驱动：它真的会取消命令，而不是只放弃等待（06 审计 low 3）。
        var affected = await db.Ado.ExecuteCommandAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
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
        PersistenceAudit.ApplyUpdateStamp(entity, stamp, markDeleted);

        return null;
    }

    /// <summary>审计列用算好的值，其余列用调用方实体上的值。</summary>
    private static object? ValueFor<T>(EntityColumnInfo column, T entity, PersistenceAudit.UpdateStamp stamp, bool deleting)
        where T : class
        => column.PropertyName switch
        {
            PersistenceOptions.Columns.UpdatedAt => stamp.UpdatedAt,
            PersistenceOptions.Columns.UpdatedBy => stamp.UpdatedBy,
            PersistenceOptions.Columns.DeletedToken => stamp.DeletedToken,
            PersistenceOptions.Columns.DeletedAt when deleting => stamp.UpdatedAt,
            PersistenceOptions.Columns.DeletedBy when deleting => stamp.UpdatedBy,
            PersistenceOptions.Columns.IsDeleted when deleting => true,
            _ => column.PropertyInfo.GetValue(entity),
        };

    private static bool ReadBool<T>(EntityInfo info, T entity, string propertyName)
        where T : class
    {
        var column = info.Columns.SingleOrDefault(current => current.PropertyName == propertyName);
        return column?.PropertyInfo.GetValue(entity) is true;
    }
}
