using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>最小内存 sink：只给脱敏策略这类不依赖宿主的用例断言日志事件。</summary>
internal sealed class InMemoryLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyList<LogEvent> Snapshot() => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
