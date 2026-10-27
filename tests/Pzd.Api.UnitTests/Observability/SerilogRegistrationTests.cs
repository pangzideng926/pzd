using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pzd.Api.UnitTests.Observability;

/// <summary>
/// 票 08：Serilog 接管了 <see cref="ILoggerFactory"/>（技术日志唯一出口）。
/// 请求摘要行与降级日志的实际输出用真实启动 API 的 stdout 举证，见交付说明。
/// </summary>
public class SerilogRegistrationTests
{
    [Fact]
    public void Logger_factory_is_backed_by_serilog()
    {
        using var factory = new ObservabilityApiFactory();
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();

        Assert.Equal("Serilog.Extensions.Logging.SerilogLoggerFactory", loggerFactory.GetType().FullName);
    }
}
