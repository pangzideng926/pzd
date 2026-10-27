namespace Pzd.Api.Endpoints;

/// <summary>
/// 本次运行的身份标记（F-03-A11）。由启动方通过 <c>PZD_RUN_ID</c> 注入，API 原样回显。
/// <para>
/// 为什么不用 <c>/api/version</c> 的字段：票 03 验证过的契约就是 version / buildId / buildTime 三项，
/// 加第四项等于改动已验契约（生产环境没注入时还会多出一个无意义的 <c>runId: null</c>）。
/// 所以这里只作为响应头 <c>X-Pzd-Run-Id</c> 输出，且**仅在注入时才输出**，公开端点不多任何默认字段。
/// </para>
/// <para>
/// 为什么不能用 buildId 判归属：它只能证明“这份二进制是我构建的”，同一次构建的残留实例版本完全一样；
/// 今天李总第一次探测打到旧实例，buildId 一字不差。runId 每轮新生成，残留实例拿不到。
/// </para>
/// </summary>
public static class ApiRunId
{
    /// <summary>响应头名，脚本据此判定「应答来自本次启动的进程」。</summary>
    public const string HeaderName = "X-Pzd-Run-Id";

    /// <summary>进程启动时由环境变量确定；未注入即 null（不编造默认值）。</summary>
    public static string? Current { get; } =
        Normalize(Environment.GetEnvironmentVariable("PZD_RUN_ID"));

    /// <summary>空白视为未注入，避免把空串当成有效归属证据。</summary>
    internal static string? Normalize(string? runId) =>
        string.IsNullOrWhiteSpace(runId) ? null : runId;
}
