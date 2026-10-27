using System.Globalization;
using MySqlConnector;

namespace Pzd.IntegrationTests;

/// <summary>
/// 集成测试连接配置：只从进程环境变量或仓库根的 <c>local.env</c>（gitignored）读取。
/// 凭据红线（规格 §4.12）：<see cref="ToString"/> 只输出主机、用户与库名，
/// 任何测试日志、异常消息、断言文本都不得包含密码或完整连接串。
/// </summary>
internal sealed class MySqlTestSettings
{
    private const string LocalEnvFileName = "local.env";

    private MySqlTestSettings(
        MySqlConnectionStringBuilder admin,
        string databaseA,
        string databaseB,
        bool fallbackAConfigured,
        bool fallbackBConfigured)
    {
        AdminConnectionString = admin.ConnectionString;
        DatabaseA = databaseA;
        DatabaseB = databaseB;
        FallbackAConfigured = fallbackAConfigured;
        FallbackBConfigured = fallbackBConfigured;
        Host = admin.Server;
        Port = admin.Port;
        User = admin.UserID;
    }

    /// <summary>管理员连接串（无默认库）；只在建立探测/建库连接时使用，禁止打印。</summary>
    public string AdminConnectionString { get; }

    public string DatabaseA { get; }

    public string DatabaseB { get; }

    public bool FallbackAConfigured { get; }

    public bool FallbackBConfigured { get; }

    public bool HasExplicitFallbackDatabases => FallbackAConfigured && FallbackBConfigured;

    public string Host { get; }

    public uint Port { get; }

    public string User { get; }

    /// <summary>指向指定库的连接串；同样禁止打印。</summary>
    public string ConnectionStringFor(string database)
    {
        var builder = new MySqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = database,
        };

        return builder.ConnectionString;
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"MySQL {Host}:{Port} (user {User}), test dbs {DatabaseA}/{DatabaseB}");

    /// <summary>
    /// 解析顺序：<c>PZD_TEST_MYSQL</c> → <c>ConnectionStrings__mysql</c> → <c>PZD_MYSQL_*</c> 分项。
    /// 一个都没配返回 <c>null</c>（调用方据此动态 Skip，而不是默认连本机）。
    /// </summary>
    public static MySqlTestSettings? TryResolve()
    {
        var env = MySqlEnvironment.Load();

        var raw = FirstNonBlank(env, "PZD_TEST_MYSQL", "ConnectionStrings__mysql");
        MySqlConnectionStringBuilder builder;

        if (raw is not null)
        {
            try
            {
                builder = new MySqlConnectionStringBuilder(raw);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        else
        {
            var host = Value(env, "PZD_MYSQL_HOST");
            var user = Value(env, "PZD_MYSQL_USER");

            if (host is null || user is null)
            {
                return null;
            }

            builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = ParsePort(Value(env, "PZD_MYSQL_PORT")),
                UserID = user,
                Password = Value(env, "PZD_MYSQL_PASSWORD") ?? string.Empty,
            };
        }

        if (string.IsNullOrWhiteSpace(builder.Server) || string.IsNullOrWhiteSpace(builder.UserID))
        {
            return null;
        }

        // 管理员连接不绑定默认库：建库/探测都要能连；每个用例再按库名克隆连接串。
        builder.Database = string.Empty;
        builder.ConnectionTimeout = 5;
        builder.DefaultCommandTimeout = 30;

        var explicitA = Value(env, "PZD_TEST_MYSQL_DB_A");
        var explicitB = Value(env, "PZD_TEST_MYSQL_DB_B");

        return new MySqlTestSettings(
            builder,
            explicitA ?? "pzd_it_a",
            explicitB ?? "pzd_it_b",
            fallbackAConfigured: explicitA is not null,
            fallbackBConfigured: explicitB is not null);
    }

    private static string? FirstNonBlank(IReadOnlyDictionary<string, string> env, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Value(env, key);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Value(IReadOnlyDictionary<string, string> env, string key) =>
        env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static uint ParsePort(string? raw) =>
        uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? port
            : 3306;

    internal static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Pzd.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// 进程环境变量优先；仓库根 <c>local.env</c> 只补缺的键（CI 直接给环境变量即可）。
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Load() => MySqlEnvironment.Load();

    internal static string LocalEnvName => LocalEnvFileName;
}

/// <summary>环境变量 + 仓库根 <c>local.env</c> 的只读合并视图（环境变量优先）。</summary>
internal static class MySqlEnvironment
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Cached = new(LoadCore);

    public static IReadOnlyDictionary<string, string> Load() => Cached.Value;

    private static IReadOnlyDictionary<string, string> LoadCore()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        var root = MySqlTestSettings.FindRepositoryRoot();
        var localEnv = root is null ? null : Path.Combine(root, MySqlTestSettings.LocalEnvName);

        if (localEnv is not null && File.Exists(localEnv))
        {
            foreach (var line in File.ReadAllLines(localEnv))
            {
                var parsed = ParseLine(line);

                if (parsed is { } pair && !values.ContainsKey(pair.Key))
                {
                    values[pair.Key] = pair.Value;
                }
            }
        }

        return values;
    }

    private static KeyValuePair<string, string>? ParseLine(string line)
    {
        var trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return null;
        }

        var separator = trimmed.IndexOf('=');

        if (separator <= 0)
        {
            return null;
        }

        var key = trimmed[..separator].Trim();
        var value = trimmed[(separator + 1)..].Trim();

        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }

        return new KeyValuePair<string, string>(key, value);
    }
}
