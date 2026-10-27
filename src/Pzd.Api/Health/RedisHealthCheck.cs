using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pzd.Api.Health;

/// <summary>
/// Redis 探活：连上 <c>ConnectionStrings:redis</c> 指向的实例并发送 <c>PING</c>，期望 <c>+PONG</c>。
/// <para>
/// 直接走 RESP 而不引 StackExchange.Redis：协议级应答比 TCP 连通更能证明实例真的在工作，
/// 又不给探活路径新增依赖（票 03）。
/// </para>
/// <para>
/// 配置缺失或连不上返回 <see cref="HealthStatus.Degraded"/> —— 票 03 明确要求「实例或凭据缺失时降级启动」，
/// 不是 Unhealthy。
/// </para>
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly string? _connectionString;

    public RedisHealthCheck(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("redis");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Degraded("redis 未配置（ConnectionStrings:redis 为空）。");
        }

        var settings = RedisConnectionSettings.Parse(_connectionString);

        try
        {
            using var client = new TcpClient();

            await client.ConnectAsync(settings.Host, settings.Port, cancellationToken).ConfigureAwait(false);

            await using var stream = client.GetStream();

            var command = settings.Password is null
                ? "PING\r\n"
                : $"AUTH {settings.Password}\r\nPING\r\n";

            await stream
                .WriteAsync(Encoding.ASCII.GetBytes(command), cancellationToken)
                .ConfigureAwait(false);

            var buffer = new byte[128];
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            var reply = Encoding.ASCII.GetString(buffer, 0, read);

            return reply.Contains("+PONG", StringComparison.OrdinalIgnoreCase)
                ? HealthCheckResult.Healthy($"redis {settings.Host}:{settings.Port} PONG")
                : HealthCheckResult.Degraded($"redis 应答异常：{reply.Trim()}");
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return HealthCheckResult.Degraded($"redis 不可用：{exception.Message}");
        }
    }

    /// <summary>支持 <c>host:port</c>、<c>host=…;port=…</c>、<c>host:port,password=…</c> 三种常见写法。</summary>
    private readonly record struct RedisConnectionSettings(string Host, int Port, string? Password)
    {
        internal static RedisConnectionSettings Parse(string raw)
        {
            var host = "127.0.0.1";
            var port = 6379;
            string? password = null;

            foreach (var segment in raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = segment.IndexOf('=');

                if (separator > 0)
                {
                    var key = segment[..separator].Trim();
                    var value = segment[(separator + 1)..].Trim();

                    if (key.Equals("host", StringComparison.OrdinalIgnoreCase))
                    {
                        host = value;
                    }
                    else if (key.Equals("port", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, CultureInfo.InvariantCulture, out var configuredPort))
                    {
                        port = configuredPort;
                    }
                    else if (key.Equals("password", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    {
                        password = value;
                    }

                    continue;
                }

                var colon = segment.LastIndexOf(':');

                if (colon > 0)
                {
                    host = segment[..colon];
                    if (int.TryParse(segment[(colon + 1)..], CultureInfo.InvariantCulture, out var inlinePort))
                    {
                        port = inlinePort;
                    }
                }
                else
                {
                    host = segment;
                }
            }

            return new RedisConnectionSettings(host, port, password);
        }
    }
}
