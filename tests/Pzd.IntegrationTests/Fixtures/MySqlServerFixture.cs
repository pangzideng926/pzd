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

    /// <summary>测试库名前缀：建库时用它，删库前必须校验它。</summary>
    public const string TestDatabaseNamePrefix = "pzd_test_";

    private MySqlContainer? _container;
    private Exception? _containerFailure;
    private string _serverConnectionString = string.Empty;

    public MySqlSource Source { get; private set; }

    public string ServerVersion { get; private set; } = "unknown";

    public async ValueTask InitializeAsync()
    {
        var container = await TryStartContainerAsync();
        var external = container is null ? ReadConfiguredConnectionString() : null;

        Source = ChooseSource(container is not null, external, _containerFailure);

        if (container is not null)
        {
            _container = container;
            _serverConnectionString = container.GetConnectionString();
        }
        else
        {
            // 把 Database 清空是必须的，不是冗余：服务器级操作要建库/删库，
            // 外部串若带着 Database=pzd_master 而那个库不存在，连接会直接 1049。
            _serverConnectionString = new MySqlConnectionStringBuilder(external!) { Database = string.Empty }.ConnectionString;
        }

        ServerVersion = await QueryOnServerAsync("SELECT VERSION()") ?? "unknown";
    }

    /// <summary>
    /// 来源优先级（AC-04、spec A1）：容器优先，容器不可用才回落外部真库。
    /// 抽成纯函数是因为本机没有 Docker，容器分支跑不起来；判定本身必须能被用例钉住，
    /// 否则「容器路径永远不执行」要等到某台有 Docker 的机器才会暴露。
    /// </summary>
    internal static MySqlSource ChooseSource(
        bool containerAvailable,
        string? externalConnectionString,
        Exception? containerFailure = null)
    {
        if (containerAvailable)
        {
            return MySqlSource.Container;
        }

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            return MySqlSource.External;
        }

        var reason = containerFailure is null ? "未捕获到具体原因" : containerFailure.Message.Trim();
        throw new InvalidOperationException(
            $"容器不可用（镜像 {ImageTag}）：{reason}。请安装并启动 Docker，"
            + $"或设置环境变量 {ExternalConnectionStringVariable}（或写入仓库根 .local/{LocalConnectionStringFileName}）"
            + "指向一个能建库的真 MySQL。",
            containerFailure);
    }

    private async ValueTask<MySqlContainer?> TryStartContainerAsync()
    {
        MySqlContainer? container = null;
        try
        {
            // Build() 内部就会探测 Docker 端点，容器不可用时在这一行就抛 DockerUnavailableException，
            // 所以它必须留在 try 里：放到 try 外，下面的中文指引永远不会生效。
            container = new MySqlBuilder(ImageTag).Build();
            await container.StartAsync();
            return container;
        }
        catch (Exception exception)
        {
            _containerFailure = exception;
            if (container is not null)
            {
                try
                {
                    await container.DisposeAsync();
                }
                catch (Exception)
                {
                    // 起了一半的容器清理失败不该盖住真正的原因（Docker 不可用），忽略即可。
                }
            }

            return null;
        }
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
        var name = TestDatabaseNamePrefix + Guid.NewGuid().ToString("N");
        await ExecuteOnServerAsync(
            $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci",
            cancellationToken);

        var builder = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = name };
        return new TestDatabase(name, builder.ConnectionString, this);
    }

    /// <summary>
    /// 删库护栏：只允许删 fixture 自己生成的测试库。
    /// 账号带全局 <c>DROP</c>（MySQL 没法把权限授给还不存在的库，只能靠建随机库名绕），
    /// 所以「删的必须是测试库」这条防线只能落在代码里——06–32 全复用这套 fixture，
    /// 一次传错库名就能静默删掉平台库。
    /// </summary>
    internal static void EnsureTestDatabaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith(TestDatabaseNamePrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"拒绝操作非测试库：库名必须以「{TestDatabaseNamePrefix}」开头，实际传的是「{name}」。",
                nameof(name));
        }
    }

    internal async ValueTask DropDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        EnsureTestDatabaseName(name);
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS `{name}`", cancellationToken);
    }

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
