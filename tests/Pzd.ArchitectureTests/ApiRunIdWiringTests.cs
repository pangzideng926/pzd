namespace Pzd.ArchitectureTests;

/// <summary>
/// F-03-A11：`X-Pzd-Run-Id` 的**端点接线**守卫。
/// <para>
/// 为什么需要这一条：<c>ApiRunId.Apply</c> 的单测只覆盖「给定 runId 与 headers，写不写」，
/// 管不到 <c>/api/version</c> 有没有真的调用它。把 <c>Program.cs</c> 里那行调用删掉时，
/// 所有单测与「未注入时无头」的 HTTP 用例**全绿**——因为未注入时本来就不该有头，
/// 而注入态的 HTTP 断言绕不过 <c>ApiRunId.Current</c> 的静态只读语义（一个进程只有一种状态）。
/// 于是这条接线此前只由 api 模式的验收脚本兜底，没有自动化回归保护。
/// </para>
/// <para>
/// 这里用源码断言补上，和 <see cref="TenantConnectionRuleTests"/> 检查
/// <c>OutboxDispatcher</c> 必须传 <c>TenantId</c> 是同一个套路：**断言调用点存在**，
/// 而不是断言行为——行为已经由外层的单元测试与实跑分别覆盖。
/// </para>
/// <para>
/// 两条已知边界，别高估它（复审 2026-10-27 提出）：
/// ① 它是文本断言，**区分大小写**：有人加 <c>[JsonPropertyName("runId")]</c> 或小写属性，
///    载荷那条才管得住（那里用 OrdinalIgnoreCase），这里管的是调用点。
/// ② 格式化改动（换行、参数对齐）会误报。这是有意的取舍：误报好过误放行。
/// </para>
/// </summary>
public class ApiRunIdWiringTests
{
    /// <summary>端点里的调用点原文。改签名就要同步改这里——那是刻意的摩擦。</summary>
    private const string ApplyCall = "ApiRunId.Apply(ApiRunId.Current, context.Response.Headers)";

    [Fact]
    public void Version_endpoint_wires_the_run_id_header_through_ApiRunId_Apply()
    {
        var programPath = Path.Combine(ArchRules.FindRepositoryRoot(), "src", "Pzd.Api", "Program.cs");

        var content = File.ReadAllText(programPath);

        // ① 全文**恰好一处**。只断言「字符串存在」时，Program.cs 里多出第二个调用点
        //    而 /api/version 那处被删掉，守卫照样绿——这是必须封掉的假绿路径。
        Assert.Equal(1, CountOccurrences(content, ApplyCall));

        // ② 而且这一处必须落在 /api/version 的端点体内，不是别的端点顺手写的头。
        var endpointBody = EndpointBody(content, "/api/version");

        Assert.Contains(ApplyCall, endpointBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_payload_does_not_carry_the_run_id_field()
    {
        // 反向锁的源码侧：归属标记只能走响应头。载荷一旦重新长出 runId 字段，
        // 票 03 验过的三字段契约就被扩充了——这条与脚本 :428 的反向断言互为冗余，故意重复。
        //
        // 用 OrdinalIgnoreCase 比对：C# 属性会写成 RunId，而 JSON 契约里是小写 runId，
        // 两种写法都必须被咬住（复审 2026-10-27 第 2 条）。
        var versionPath = Path.Combine(
            ArchRules.FindRepositoryRoot(), "src", "Pzd.Api", "Endpoints", "ApiVersionInfo.cs");

        var codeOnly = File.ReadAllText(versionPath).Split('\n')
            .Where(line => !IsComment(line))
            .Aggregate(string.Empty, (all, line) => all + line + "\n");

        Assert.DoesNotContain("runid", codeOnly, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string content, string value) =>
        content.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>
    /// 截出 `MapGet("&lt;route&gt;"` 到下一个 `app.Map` 之间的片段。
    /// 这样端点重排、在它后面追加端点都不影响判定，只有「调用挪到别的端点」才会红。
    /// </summary>
    private static string EndpointBody(string content, string route)
    {
        var start = content.IndexOf($"MapGet(\"{route}\"", StringComparison.Ordinal);

        Assert.True(
            start >= 0,
            $"Program.cs 里找不到 MapGet(\"{route}\"：端点被改名或删除了（守卫随之失效，必须先修守卫）");

        var next = content.IndexOf("app.Map", start + 1, StringComparison.Ordinal);

        return next < 0 ? content[start..] : content[start..next];
    }
}
