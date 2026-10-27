using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pzd.Infrastructure.Persistence;

/// <summary>
/// Outbox 后台投递服务：每轮新建一个 DI 作用域解析 <see cref="OutboxDispatcher"/>（Scoped），
/// 单条消息失败只记录并等待下轮重试，不阻塞主流程；随应用优雅停止。
/// </summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;
    private readonly TimeSpan _interval;

    public OutboxDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherHostedService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();

                await dispatcher.DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox 后台投递失败，将在下一轮重试。");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
