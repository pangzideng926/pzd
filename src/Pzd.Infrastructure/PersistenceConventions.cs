using System.Reflection;
using System.Text;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 映射约定集中处（architecture.md §5.2）：Domain 里零 ORM 特性，表名列名的规则全在这里。
/// </summary>
public static class PersistenceConventions
{
    /// <summary>
    /// 表名 = 实体类型名的 snake_case：<c>TestUser → test_user</c>、<c>PermissionCode → permission_code</c>。
    /// 只做命名转换，不做复数化——将来 db/ 下的版本化 SQL 是唯一权威，改表名就在这里改。
    /// </summary>
    public static string TableNameFor(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var name = entityType.Name;
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (char.IsUpper(current) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    /// <summary>列名与属性名一致；<c>Version</c> 挂乐观并发校验，<c>Id</c> 声明为主键。</summary>
    public static void ConfigureColumn(PropertyInfo property, EntityColumnInfo column)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(column);

        column.DbColumnName = property.Name;

        if (property.Name == PersistenceOptions.Columns.Id)
        {
            column.IsPrimarykey = true;
        }
    }
}
