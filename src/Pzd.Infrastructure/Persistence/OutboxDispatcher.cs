using Microsoft.Extensions.Logging;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Infrastructure.Persistence;

/// <summary>Outbox 消费者的最小契约：消息已带租户上下文，处理器只需按业务处理。</summary>
public interface IOutboxMessageHandler
{
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbox 投递器（Scoped）：逐条处理待投递消息。
/// <para>
/// F-05-8：取连接必须走带 <c>tenantId</c> 的显式重载（先校验停用/到期），禁止无参裸取；
/// 停用或过期的租户消息被拒绝并记录理由，不再投递。消费前先 <see cref="ITenantContext.Set"/>。
/// </para>
/// <para>幂等：已处理的消息 Id 记录在存储的已处理集合里，重复投递不再产生副作用。</para>
/// </summary>
public sealed class OutboxDispatcher
{
    /// <summary>单轮最多处理的消息条数。</summary>
    public const int DefaultBatchSize = 50;

    /// <summary>最大投递尝试次数：超过后拒绝该消息，避免坏消息无限重试饿死后续消息。</summary>
    public const int MaxAttempts = 5;

    private readonly IOutboxStore _outbox;
    private readonly ITenantDbProvider _dbProvider;
    private readonly ITenantContext _tenantContext;
    private readonly IReadOnlyList<IOutboxMessageHandler> _handlers;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IOutboxStore outbox,
        ITenantDbProvider dbProvider,
        ITenantContext tenantContext,
        IEnumerable<IOutboxMessageHandler> handlers,
        ILogger<OutboxDispatcher> logger)
    {
        _outbox = outbox;
        _dbProvider = dbProvider;
        _tenantContext = tenantContext;
        _handlers = handlers.ToArray();
        _logger = logger;
    }

    /// <summary>处理一批待投递消息，返回成功投递的条数。</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        var batch = await _outbox
            .DequeueBatchAsync(DefaultBatchSize, cancellationToken)
            .ConfigureAwait(false);

        var processed = 0;

        foreach (var message in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _outbox.IsProcessedAsync(message.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (_handlers.Count == 0 && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "没有注册任何 IOutboxMessageHandler，消息 {MessageId} 将被直接标记为已处理（静默消费）。",
                    message.Id);
            }

            try
            {
                // F-05-8：显式重载——先按 store 校验停用/到期，再取（或建立）该租户连接。
                await _dbProvider
                    .GetConnectionScopeAsync(message.TenantId, cancellationToken)
                    .ConfigureAwait(false);

                _tenantContext.Set(message.TenantId);

                foreach (var handler in _handlers)
                {
                    await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
                }

                await _outbox.MarkProcessedAsync(message.Id, cancellationToken).ConfigureAwait(false);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TenantNotResolvedException exception)
            {
                _logger.LogWarning(
                    "Outbox 消息被拒绝：{MessageId}，租户 {TenantId} 不可用（{Code}）。",
                    message.Id,
                    message.TenantId,
                    exception.Error.Code);

                await _outbox
                    .RejectAsync(message.Id, $"{exception.Error.Code}: {exception.Error.Message}", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (message.Attempts + 1 >= MaxAttempts)
                {
                    _logger.LogError(
                        exception,
                        "Outbox 消息超过最大重试次数（{MaxAttempts}），已拒绝：{MessageId}（租户 {TenantId}）。",
                        MaxAttempts,
                        message.Id,
                        message.TenantId);

                    await _outbox
                        .RejectAsync(
                            message.Id,
                            $"RetryLimitExceeded: {exception.Message}",
                            cancellationToken)
                        .ConfigureAwait(false);

                    continue;
                }

                _logger.LogWarning(
                    exception,
                    "Outbox 消息投递失败，将在下一轮重试：{MessageId}（租户 {TenantId}）。",
                    message.Id,
                    message.TenantId);

                await _outbox
                    .RecordFailureAsync(message.Id, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return processed;
    }
}
