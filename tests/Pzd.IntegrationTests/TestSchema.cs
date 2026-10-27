using System.Text.RegularExpressions;
using MySqlConnector;

namespace Pzd.IntegrationTests;

/// <summary>
/// 测试库/表的最小结构。所有建库、删库、建表都先过 <see cref="EnsureSafeDatabaseName"/>：
/// 只允许 <c>pzd_it_*</c> 前缀，防止误碰 <c>pzd_master</c> 或任何非测试库（票 10 安全约束）。
/// </summary>
internal static partial class TestSchema
{
    public const string TableName = "it_widgets";

    private const string CreateTableSql = $"""
        CREATE TABLE `{TableName}` (
            `Id` BIGINT NOT NULL,
            `TenantId` BIGINT NOT NULL,
            `Name` VARCHAR(64) NOT NULL,
            PRIMARY KEY (`Id`),
            KEY `IX_{TableName}_TenantId` (`TenantId`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
        """;

    /// <summary>只接受 <c>pzd_it_</c> 前缀 + 字母数字下划线，长度不超过 MySQL 的 64 字符上限。</summary>
    public static void EnsureSafeDatabaseName(string database)
    {
        if (string.IsNullOrWhiteSpace(database) || !SafeDatabaseName().IsMatch(database))
        {
            throw new InvalidOperationException(
                $"拒绝操作非测试库 '{database}'：票 10 只允许 pzd_it_ 前缀的库（字母/数字/下划线）。");
        }
    }

    public static async Task CreateDatabaseAsync(
        string adminConnectionString,
        string database,
        CancellationToken cancellationToken)
    {
        EnsureSafeDatabaseName(database);

        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;",
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task DropDatabaseAsync(
        string adminConnectionString,
        string database,
        CancellationToken cancellationToken)
    {
        EnsureSafeDatabaseName(database);

        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS `{database}`;", cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<bool> DatabaseExistsAsync(
        string adminConnectionString,
        string database,
        CancellationToken cancellationToken)
    {
        EnsureSafeDatabaseName(database);

        await using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name;";
        command.Parameters.AddWithValue("@name", database);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    /// <summary>建表前先删表：重复运行不依赖上一次的残留结构（幂等）。</summary>
    public static async Task RecreateSchemaAsync(string databaseConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"DROP TABLE IF EXISTS `{TableName}`;", cancellationToken)
            .ConfigureAwait(false);
        await ExecuteAsync(connection, CreateTableSql, cancellationToken).ConfigureAwait(false);
    }

    public static async Task DropSchemaAsync(string databaseConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"DROP TABLE IF EXISTS `{TableName}`;", cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task TruncateAsync(string databaseConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"TRUNCATE TABLE `{TableName}`;", cancellationToken).ConfigureAwait(false);
    }

    public static async Task InsertAsync(
        string databaseConnectionString,
        long id,
        long tenantId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO `{TableName}` (`Id`, `TenantId`, `Name`) VALUES (@id, @tenantId, @name);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        command.Parameters.AddWithValue("@name", name);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<long> CountAllRowsAsync(string databaseConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{TableName}`;";

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public static async Task<bool> TableExistsAsync(string databaseConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(databaseConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @name;
            """;
        command.Parameters.AddWithValue("@name", TableName);

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    public static IReadOnlyList<string> ListTestDatabases(string adminConnectionString, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(adminConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME LIKE 'pzd_it_%';";
        command.CommandTimeout = 30;

        var databases = new List<string>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            databases.Add(reader.GetString(0));
        }

        return databases;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"\Apzd_it_[A-Za-z0-9_]{1,57}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabaseName();
}
