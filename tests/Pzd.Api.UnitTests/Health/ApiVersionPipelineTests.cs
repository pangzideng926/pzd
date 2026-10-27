using System.Text.Json;
using Pzd.Api.Endpoints;
using Pzd.Api.UnitTests.Tenancy;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// F-03-A11：走真实 HTTP 管道（<c>MapGet</c> + <c>Results.Ok</c> + 真实中间件）验两件事——
/// 载荷是票 03 的三字段、归属标记只以响应头出现。
/// <para>
/// 为什么不在这里验「注入了就有值」：<see cref="ApiRunId.Current"/> 是静态只读、取自进程环境变量，
/// 一个测试进程只可能有一种状态。强行验两条分支只能得到一个恒绿的假测试（老登、雨哥都指出了这点），
/// 注入分支由 <see cref="ApiRunIdTests"/> 直接测 <see cref="ApiRunId.Apply"/> 覆盖。
/// 所以这里断言的是**不变式**：响应头存在 ⟺ 本进程注入了 PZD_RUN_ID，且值就是 Current。
/// </para>
/// </summary>
public class ApiVersionPipelineTests
{
    [Fact]
    public async Task Version_endpoint_serves_exactly_the_three_contract_fields()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/version", TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["buildId", "buildTime", "version"], keys);
    }

    [Fact]
    public async Task Attribution_header_exists_exactly_when_a_run_id_was_injected()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        // 这里**故意不写** `Assert.Null(ApiRunId.Current)`：脚本 verify-apphost.ps1:126 会用
        // Process 作用域设 PZD_RUN_ID，只要在同一个终端里先跑过脚本再跑测试，Current 就有值，
        // 那条断言会假红、且红得像实现坏了（老登、雨哥都提过这个环境坑）。
        // 改用不变式后两种环境都成立：头存在 ⟺ 本进程注入过，且值就是 Current。
        // 代价是「注入态」这条分支在这里覆盖不到——它由 ApiRunIdTests 直接测 Apply 覆盖，
        // 不需要为了造注入态去 Environment.SetEnvironmentVariable（那会污染并行跑的用例）。
        var expected = ApiRunId.Current;
        var present = response.Headers.TryGetValues(ApiRunId.HeaderName, out var values);
        var actual = present ? values!.Single() : null;

        Assert.Equal(expected, actual);
        Assert.Equal(expected is not null, present);
    }
}
