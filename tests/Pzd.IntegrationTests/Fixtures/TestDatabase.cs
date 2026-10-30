using System.Globalization;
using MySqlConnector;

namespace Pzd.IntegrationTests.Fixtures;

/// <summary>一个用例独占的独立库；释放时整库删除，失败不留残留。</summary>
public sealed class TestDatabase(string name, string connectionString, MySqlServerFixture owner) : IAsyncDisposable
{
    public string Name { get; } = name;

    public string ConnectionString { get; } = connectionString;

    public async ValueTask ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<T> QueryScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"查询没有返回任何值：{sql}");

        if (value is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync() => await owner.DropDatabaseAsync(Name, CancellationToken.None);
}
