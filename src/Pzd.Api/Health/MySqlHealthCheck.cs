using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pzd.Api.Health;

/// <summary>
/// MySQL 探活：对 <c>ConnectionStrings:mysql</c> 打开一次连接并执行 <c>SELECT 1</c>。
/// 配置缺失 / 连不上 / 认证失败都返回 <see cref="HealthStatus.Degraded"/>（票 03 的降级启动要求）。
/// </summary>
public sealed class MySqlHealthCheck : IHealthCheck
{
    private readonly string? _connectionString;

    public MySqlHealthCheck(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("mysql");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Degraded("mysql 未配置（ConnectionStrings:mysql 为空）。");
        }

        try
        {
            await using var connection = new MySqlConnector.MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1
                ? HealthCheckResult.Healthy("mysql SELECT 1 成功")
                : HealthCheckResult.Degraded("mysql SELECT 1 返回异常结果");
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Degraded($"mysql 不可用：{exception.Message}");
        }
    }
}
