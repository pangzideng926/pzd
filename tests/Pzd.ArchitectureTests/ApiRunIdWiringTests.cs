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
/// </summary>
public class ApiRunIdWiringTests
{
    [Fact]
    public void Version_endpoint_wires_the_run_id_header_through_ApiRunId_Apply()
    {
        var repositoryRoot = ArchRules.FindRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "src", "Pzd.Api", "Program.cs");

        var content = File.ReadAllText(programPath);

        Assert.Contains("ApiRunId.Apply(ApiRunId.Current, context.Response.Headers)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_payload_does_not_carry_the_run_id_field()
    {
        // 反向锁的源码侧：归属标记只能走响应头。载荷一旦重新长出 runId 字段，
        // 票 03 验过的三字段契约就被扩充了——这条与脚本 :428 的反向断言互为冗余，故意重复。
        var repositoryRoot = ArchRules.FindRepositoryRoot();
        var versionPath = Path.Combine(repositoryRoot, "src", "Pzd.Api", "Endpoints", "ApiVersionInfo.cs");

        var content = File.ReadAllText(versionPath);

        Assert.DoesNotContain("RunId", content.Split('\n')
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Aggregate(string.Empty, (all, line) => all + line + "\n"), StringComparison.Ordinal);
    }
}
