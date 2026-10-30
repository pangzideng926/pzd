using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 持久化约定的唯一入口（06 号票）：业务模块注入的是这里建出来的客户端的抽象，
/// 不自己写 ConnectionConfig，也就不可能悄悄绕过审计字段、软删除和并发校验。
/// </summary>
public static class PzdPersistence
{
    /// <summary>
    /// 按约定建一个 SqlSugar 客户端。
    /// 07 号票接上租户库切换后，<paramref name="connectionString"/> 由租户解析结果提供，
    /// 本方法签名不变。
    /// </summary>
    public static SqlSugarClient CreateClient(string connectionString, PersistenceOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityNameService = (type, entity) => entity.DbTableName = PersistenceConventions.TableNameFor(type),
                EntityService = PersistenceConventions.ConfigureColumn,
            },
        });

        PersistenceAudit.Attach(db, options, CreateIdGenerator(options));
        PersistenceSoftDelete.RegisterFilters(db, options.SoftDeleteTypes);

        return db;
    }

    /// <summary>
    /// 主键生成器（architecture.md §5.3 雪花）：worker 与机房序号来自部署配置，
    /// 多副本必须给不同的值，否则同一毫秒会生成相同 Id。
    /// </summary>
    public static Func<long> CreateIdGenerator(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return PersistenceIdentity.CreateGenerator(options);
    }
}
