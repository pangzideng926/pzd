using MySqlConnector;
using Testcontainers.MySql;

namespace Pzd.IntegrationTests.Fixtures;

/// <summary>MySQL 来源：容器优先，容器不可用时回落外部真库。</summary>
public enum MySqlSource
{
    Container,
    External,
}

/// <summary>
/// 36 号票 S1 seam：对外只暴露「建一个独立库、给出连接串、用完销毁」。
/// 隔离语义是**每用例一个独立库**，不做事务回滚——06 的真提交、08 的 Outbox 回滚语义都建立在这上面。
/// 来源优先容器（票面固定 <c>mysql:8.4</c>）；设置了外部连接串时走外部真库，仍然是真 MySQL，
/// 只是没有容器，剩余风险记在票面「环境与替代方案」。
/// </summary>
public sealed class MySqlServerFixture : IAsyncLifetime
{
    /// <summary>票面固定的镜像 tag；容器可用时必须用它。</summary>
    public const string ImageTag = "mysql:8.4";

    /// <summary>外部真库模式：一个能建库的账号连接串，只从环境变量或本地文件读，不进版本库。</summary>
    public const string ExternalConnectionStringVariable = "PZD_TEST_MYSQL_CONNECTION";

    /// <summary>外部连接串的本地回落文件，放在仓库根被 gitignore 的 <c>.local/</c> 下。</summary>
    public const string LocalConnectionStringFileName = "mysql-test-connection.txt";

    private MySqlContainer? _container;
    private string _serverConnectionString = string.Empty;

    public MySqlSource Source { get; private set; }

    public string ServerVersion { get; private set; } = "unknown";

    public async ValueTask InitializeAsync()
    {
        var external = ReadConfiguredConnectionString();
        if (external is not null)
        {
            Source = MySqlSource.External;
            _serverConnectionString = new MySqlConnectionStringBuilder(external) { Database = string.Empty }.ConnectionString;
        }
        else
        {
            Source = MySqlSource.Container;
            _container = new MySqlBuilder(ImageTag).Build();
            try
            {
                await _container.StartAsync();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"容器不可用（镜像 {ImageTag}）。请安装并启动 Docker，或设置环境变量 "
                    + $"{ExternalConnectionStringVariable}（或写入仓库根 .local/{LocalConnectionStringFileName}）"
                    + "指向一个能建库的真 MySQL。",
                    exception);
            }

            _serverConnectionString = _container.GetConnectionString();
        }

        ServerVersion = await QueryOnServerAsync("SELECT VERSION()") ?? "unknown";
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>建一个属于单个用例的独立库，返回它的句柄；句柄释放时整库删除。</summary>
    public async ValueTask<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = "pzd_test_" + Guid.NewGuid().ToString("N");
        await ExecuteOnServerAsync(
            $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci",
            cancellationToken);

        var builder = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = name };
        return new TestDatabase(name, builder.ConnectionString, this);
    }

    internal async ValueTask DropDatabaseAsync(string name, CancellationToken cancellationToken)
        => await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS `{name}`", cancellationToken);

    private async ValueTask ExecuteOnServerAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<string?> QueryOnServerAsync(string sql)
    {
        await using var connection = new MySqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    /// <summary>外部连接串来源：环境变量优先，其次仓库根 <c>.local/</c> 下的本地文件；都没有返回 null。</summary>
    private static string? ReadConfiguredConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ExternalConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var root = FindRepositoryRoot();
        if (root is null)
        {
            return null;
        }

        var path = Path.Combine(root, ".local", LocalConnectionStringFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var fromFile = File.ReadAllText(path).Trim();
        return string.IsNullOrWhiteSpace(fromFile) ? null : fromFile;
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pzd.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
