using MySqlConnector;

namespace Pzd.IntegrationTests;

internal enum MySqlDatabaseMode
{
    /// <summary>有 CREATE/DROP DATABASE 权限：每次运行自动建/清 <c>pzd_it_a</c>、<c>pzd_it_b</c>。</summary>
    CreateAndDrop,

    /// <summary>无建库权限但显式给了 <c>PZD_TEST_MYSQL_DB_A/B</c>：使用既有库，仍真跑。</summary>
    Existing,
}

internal sealed record MySqlProbe(
    MySqlTestSettings? Settings,
    string? Version,
    MySqlDatabaseMode Mode,
    string? SkipReason)
{
    public bool IsAvailable => SkipReason is null;

    public static MySqlProbe Unavailable(string reason) => new(null, null, MySqlDatabaseMode.CreateAndDrop, reason);
}

/// <summary>
/// 动态可用性探测（票 10）：凭据缺失 / 实例不可达 / 无建库权限且无既有测试库时给出可读 Skip 原因。
/// 结果进程内只探测一次；探测本身不写死 <c>Skip="…"</c>，而是交给 <see cref="IntegrationFactAttribute"/> 使用。
/// </summary>
internal static class MySqlAvailability
{
    /// <summary>票 10 允许的三种 Skip 原因；验收要求原因只能是这三种之一，其他位置一律引用常量，不再手写字面量。</summary>
    public const string CredentialsSkipReason = "MySQL credentials not configured";

    public const string UnreachableSkipReason = "MySQL unreachable";

    public const string PrivilegeSkipReason = "no CREATE DATABASE privilege";

    public static IReadOnlyList<string> SkipReasons { get; } =
        [CredentialsSkipReason, UnreachableSkipReason, PrivilegeSkipReason];

    private const string PrivilegeProbeDatabase = "pzd_it_privprobe";

    private static readonly Lazy<MySqlProbe> CachedProbe = new(ProbeCore, LazyThreadSafetyMode.ExecutionAndPublication);

    public static MySqlProbe Probe() => CachedProbe.Value;

    private static MySqlProbe ProbeCore()
    {
        var settings = MySqlTestSettings.TryResolve();

        if (settings is null)
        {
            return MySqlProbe.Unavailable(CredentialsSkipReason);
        }

        string version;

        try
        {
            version = ReadServerVersion(settings);

            if (CanCreateAndDropDatabase(settings))
            {
                return new MySqlProbe(settings, version, MySqlDatabaseMode.CreateAndDrop, null);
            }
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            return MySqlProbe.Unavailable(UnreachableSkipReason);
        }

        if (settings.HasExplicitFallbackDatabases && BothFallbackDatabasesExist(settings))
        {
            return new MySqlProbe(settings, version, MySqlDatabaseMode.Existing, null);
        }

        return MySqlProbe.Unavailable(PrivilegeSkipReason);
    }

    private static string ReadServerVersion(MySqlTestSettings settings)
    {
        using var connection = new MySqlConnection(settings.AdminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION()";

        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static bool CanCreateAndDropDatabase(MySqlTestSettings settings)
    {
        TestSchema.EnsureSafeDatabaseName(PrivilegeProbeDatabase);

        try
        {
            using var connection = new MySqlConnection(settings.AdminConnectionString);
            connection.Open();

            // 上一次探测若在 DROP 前中断会留下这个库：先清掉，保证本次 CREATE 是真实权限证据。
            Execute(connection, $"DROP DATABASE IF EXISTS `{PrivilegeProbeDatabase}`");
            Execute(connection, $"CREATE DATABASE `{PrivilegeProbeDatabase}`");
            Execute(connection, $"DROP DATABASE `{PrivilegeProbeDatabase}`");

            return true;
        }
        catch (MySqlException exception) when (IsPermissionFailure(exception))
        {
            // 只有「账号缺权限」才归为无建库权限（F-10-A2）；连接断开/超时等故障不吞，
            // 让它冒泡到 ProbeCore 的连通性判断，得到 MySQL unreachable，而不是误导成权限问题。
            TryDropProbeDatabase(settings);
            return false;
        }
    }

    /// <summary>MySQL 权限类错误码：1044 库访问被拒、1045 登录被拒、1142 表权限被拒、1227 特定权限被拒（deny CREATE）。</summary>
    private static bool IsPermissionFailure(MySqlException exception) =>
        (int)exception.ErrorCode is 1044 or 1045 or 1142 or 1227;

    private static void TryDropProbeDatabase(MySqlTestSettings settings)
    {
        try
        {
            using var connection = new MySqlConnection(settings.AdminConnectionString);
            connection.Open();
            Execute(connection, $"DROP DATABASE IF EXISTS `{PrivilegeProbeDatabase}`");
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            // 清理失败不改变「无建库权限」的结论；残留仍是 pzd_it_ 前缀，不影响安全约束。
        }
    }

    private static bool BothFallbackDatabasesExist(MySqlTestSettings settings) =>
        DatabaseExists(settings, settings.DatabaseA) && DatabaseExists(settings, settings.DatabaseB);

    private static bool DatabaseExists(MySqlTestSettings settings, string database)
    {
        TestSchema.EnsureSafeDatabaseName(database);

        try
        {
            using var connection = new MySqlConnection(settings.ConnectionStringFor(database));
            connection.Open();
            return connection.State == System.Data.ConnectionState.Open;
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            return false;
        }
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static bool IsConnectivityFailure(Exception exception) =>
        exception is MySqlException or InvalidOperationException or TimeoutException
            or System.Net.Sockets.SocketException
            or IOException;
}
