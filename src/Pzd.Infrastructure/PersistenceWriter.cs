using System.Globalization;
using SqlSugar;

namespace Pzd.Infrastructure;

/// <summary>
/// 持久化层唯一的写入口（architecture.md §5.3）：乐观并发 + 租户谓词 + 审计字段。
/// 所有 UPDATE 都带 <c>WHERE Id AND Version=旧值 AND TenantId=当前租户</c>：
/// <list type="bullet">
/// <item>版本不匹配 → <c>Persistence.Record.VersionConflict</c>（09 号票映射 409）</item>
/// <item>本租户下查不到这行 → <c>Persistence.Record.NotFound</c>（404）</item>
/// <item>这行在回收站 → <c>Persistence.Record.InTrash</c>（409；可恢复，所以不是 410）</item>
/// <item>租户上下文缺失 → 抛 <see cref="InvalidOperationException"/>，绝不按 Id 静默更新（06 审计 high 2）</item>
/// <item>唯一键冲突（例如恢复时业务键已被占用）→ <c>Persistence.Record.DuplicateKey</c></item>
/// <item>冲突时调用方实体保持原值，可以直接重载重试（06 审计 medium 3）</item>
/// </list>
/// <para>上面四种业务态**都是返回值**，异常只留给「调用方用错入口」（06 六审 high）。</para>
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
    /// <c>affected == 0</c> 之后查出来的事实：本租户下没这行、有但已删、有且活跃（版本对不上）。
    /// 名字就是 <see cref="ResolveMissAsync"/> 那条 SQL 的返回值（<c>'NoRow'</c>/<c>'Recycled'</c>/<c>'Active'</c>），
    /// 声明顺序不承载任何语义，重排不会改变行为（06 七审 low）。
    /// </summary>
    private enum MissKind
    {
        NoRow,
        Recycled,
        Active,
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
    /// <para>
    /// 能不能恢复由**库**判定（<c>WHERE IsDeleted=1</c>），不看调用方实体上自报的标记：
    /// 07/08 从 HTTP 体转回来的实体 <c>IsDeleted</c> 恒为默认 <c>false</c>，靠它判断会把正常请求抛成 500。
    /// 目标本来就不在回收站时返回 <c>Persistence.Record.NotInTrash</c>（06 七审后 / 张一一 `698e4da`）。
    /// </para>
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
        var isDeletedColumn = info.Columns.SingleOrDefault(
            column => column.PropertyName == PersistenceOptions.Columns.IsDeleted);

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
        // Update 只作用于活跃行（WHERE IsDeleted=0），四列删除审计在库里必然是活跃态常量，
        // 用它算 stamp 才能把实体也规范化成同一组值（06 六审 medium）。
        var stamp = kind == WriteKind.Update
            ? PersistenceAudit.ActiveUpdateStamp(options.CurrentUserId())
            : PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted, clearDeleted);

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

        // 活跃态由**库**判定，不靠调用方实体上的标记：DTO 转回来的实体不带 IsDeleted/DeletedToken，
        // 两道内存守卫都不触发，只有这条条件能挡住「往已删除的行上静默写入」（06 五审 medium / P8）。
        // 恢复反过来要求目标是删除态，否则恢复一条已活跃的记录会白改版本号。
        if (isDeletedColumn is not null)
        {
            where += kind == WriteKind.Restore
                ? $" AND `{isDeletedColumn.DbColumnName}`=1"
                : $" AND `{isDeletedColumn.DbColumnName}`=0";
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
            // 0 行的原因必须分清：三种业务事实各有各的 code（06 六审 high / Q1、Q3、Q4、Q5、Q7）。
            // 一次聚合查询同时拿「本租户下有没有这行」和「这行是不是删除态」，避免两次查询看到不同状态。
            var miss = await ResolveMissAsync(db, info, primary, tenantColumn, isDeletedColumn, entity, currentTenantId, cancellationToken)
                .ConfigureAwait(false);

            if (miss == MissKind.NoRow)
            {
                // 不存在，或存在但不属于当前租户——两者对调用方是同一件事（不泄露别家的存在性）。
                return PersistenceErrors.NotFound();
            }

            // 恢复的目标本来就是删除态：那条行「已删」不是错误，版本对不上才是。
            if (miss == MissKind.Recycled && kind != WriteKind.Restore)
            {
                return PersistenceErrors.RecordInTrash();
            }

            // 反过来：恢复的目标在库里是活跃的，说明它压根不在回收站。
            // 这是业务态、不是并发冲突，更不是用错 API（06 七审后 / 张一一 `698e4da`）。
            if (miss == MissKind.Active && kind == WriteKind.Restore)
            {
                return PersistenceErrors.NotInTrash();
            }

            return PersistenceErrors.VersionConflict();
        }

        // 只有真的写进去了才回写实体。
        var next = Convert.ChangeType(
            currentVersion + 1,
            version.PropertyInfo.PropertyType,
            CultureInfo.InvariantCulture);
        version.PropertyInfo.SetValue(entity, next);

        // 实体与库一致：Update 的 stamp 是活跃态常量，回写后实体上的四个删除列也被规范化。
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

    /// <summary>
    /// <c>affected == 0</c> 时把「为什么一行没改」查清楚：本租户下这行不存在、存在但已删、还是存在且活跃
    /// （那就是版本对不上）。一次查询就返回三态，避免两次查询在并发下看到不同状态。
    /// 用裸 SQL 而不是 <c>Queryable</c>，免得软删除过滤器把要查的那行挡掉。
    /// 返回自描述的字符串、不是数字编码（06 七审 low）：数字编码一旦和枚举声明顺序不一致，
    /// <c>(MissKind)code</c> 这种写法会在没有编译错误的情况下把两态悄悄换位。
    /// </summary>
    private static async Task<MissKind> ResolveMissAsync<T>(
        SqlSugarClient db,
        EntityInfo info,
        EntityColumnInfo primary,
        EntityColumnInfo? tenantColumn,
        EntityColumnInfo? isDeletedColumn,
        T entity,
        long? currentTenantId,
        CancellationToken cancellationToken)
        where T : class
    {
        var deletedSum = isDeletedColumn is null
            ? "0"
            : $"COALESCE(SUM(`{isDeletedColumn.DbColumnName}`), 0)";
        var sql = $"SELECT CASE WHEN COUNT(*)=0 THEN 'NoRow' WHEN {deletedSum}>0 THEN 'Recycled' ELSE 'Active' END"
            + $" FROM `{info.DbTableName}` WHERE `{primary.DbColumnName}`=@missId";
        var parameters = new List<SugarParameter> { new("@missId", primary.PropertyInfo.GetValue(entity)) };

        if (tenantColumn is not null)
        {
            sql += $" AND `{tenantColumn.DbColumnName}`=@missTenantId";
            parameters.Add(new SugarParameter("@missTenantId", currentTenantId));
        }

        var text = await db.Ado.GetStringAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        return Enum.TryParse<MissKind>(text, ignoreCase: false, out var kind)
            ? kind
            : throw new InvalidOperationException($"三态查询返回了未知值「{text}」，三种事实必须由这条 SQL 自己说清。");
    }

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
