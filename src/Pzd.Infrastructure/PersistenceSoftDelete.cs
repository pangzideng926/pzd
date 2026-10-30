using System.Linq.Expressions;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 软删除的全局查询过滤（architecture.md §5.3）：默认查不到已删除记录，
/// 需要时用 <c>db.Queryable&lt;T&gt;().ClearFilter()</c> 显式绕过（回收站）。
/// </summary>
internal static class PersistenceSoftDelete
{
    public static void RegisterFilters(SqlSugarClient db, IReadOnlyList<Type> softDeleteTypes)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(softDeleteTypes);

        foreach (var type in softDeleteTypes)
        {
            var isDeleted = type.GetProperty(PersistenceOptions.Columns.IsDeleted)
                ?? throw new InvalidOperationException(
                    $"类型「{type.Name}」登记了软删除过滤，但没有「{PersistenceOptions.Columns.IsDeleted}」属性。");

            var entity = Expression.Parameter(type, "it");
            var body = Expression.Equal(Expression.Property(entity, isDeleted), Expression.Constant(false));

            // 用非泛型重载：实体类型是运行时登记的，泛型版本要反射拼泛型实参，这里没必要。
            db.QueryFilter.AddTableFilter(
                type,
                Expression.Lambda(body, entity),
                QueryFilterProvider.FilterJoinPosition.Where);
        }
    }
}
