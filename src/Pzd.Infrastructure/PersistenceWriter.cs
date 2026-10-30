using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 乐观并发的写入口（architecture.md §5.3）：<c>WHERE Id AND Version=旧值</c>，同时把 Version 自增。
/// 不用 SqlSugar 的 <c>IsEnableUpdateVersionValidation</c>：实测（SqlSugarCore 5.1.4.221）它在
/// <c>Updateable(entity)</c> 路径上只按主键更新，SET/WHERE 里都没有版本列，冲突根本拦不住。
/// </summary>
public static class PersistenceWriter
{
    /// <summary>
    /// 按版本条件更新一条记录：成功返回 <c>null</c>；版本已被别人改掉返回版本冲突（09 号票映射 409）。
    /// </summary>
    public static async Task<PersistenceFailure?> UpdateAsync<T>(
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
        var primary = info.Columns.SingleOrDefault(column => column.IsPrimarykey)
            ?? throw new InvalidOperationException($"类型「{typeof(T).Name}」没有主键列，无法按版本更新。");
        var version = info.Columns.SingleOrDefault(column => column.PropertyName == PersistenceOptions.Columns.Version)
            ?? throw new InvalidOperationException($"类型「{typeof(T).Name}」没有「{PersistenceOptions.Columns.Version}」列，无法做乐观并发。");

        // 审计字段在这里落内存：本方法是自己拼 SQL，不走 SqlSugar 的 DataExecuting 回调。
        PersistenceAudit.ApplyUpdateStamp(entity, options.CurrentUserId());

        var parameters = new List<SugarParameter>();
        var assignments = new List<string>();
        foreach (var column in info.Columns)
        {
            if (column == primary || column == version)
            {
                continue;
            }

            var name = "@p" + parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            parameters.Add(new SugarParameter(name, column.PropertyInfo.GetValue(entity)));
            assignments.Add($"`{column.DbColumnName}`={name}");
        }

        assignments.Add($"`{version.DbColumnName}`=`{version.DbColumnName}`+1");

        var currentVersion = Convert.ToInt64(version.PropertyInfo.GetValue(entity), System.Globalization.CultureInfo.InvariantCulture);
        parameters.Add(new SugarParameter("@id", primary.PropertyInfo.GetValue(entity)));
        parameters.Add(new SugarParameter("@version", currentVersion));

        var sql = $"UPDATE `{info.DbTableName}` SET {string.Join(",", assignments)} "
            + $"WHERE `{primary.DbColumnName}`=@id AND `{version.DbColumnName}`=@version";

        var affected = await db.Ado.ExecuteCommandAsync(sql, parameters).WaitAsync(cancellationToken);
        if (affected == 0)
        {
            return PersistenceErrors.VersionConflict();
        }

        var next = Convert.ChangeType(
            currentVersion + 1,
            version.PropertyInfo.PropertyType,
            System.Globalization.CultureInfo.InvariantCulture);
        version.PropertyInfo.SetValue(entity, next);

        return null;
    }
}
