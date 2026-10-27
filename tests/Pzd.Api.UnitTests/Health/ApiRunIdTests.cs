using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pzd.Api.Endpoints;

namespace Pzd.Api.UnitTests.Health;

/// <summary>
/// F-03-A11：本次运行标记（<c>PZD_RUN_ID</c>）只作为响应头 <c>X-Pzd-Run-Id</c> 输出，
/// 且仅在注入时有值——<c>/api/version</c> 的载荷必须保持票 03 验证过的三字段，
/// 不能因为归属判定多出一个 <c>runId: null</c> 的公开字段。
/// </summary>
public class ApiRunIdTests
{
    [Fact]
    public void Run_id_is_returned_verbatim_when_provided()
    {
        Assert.Equal("run-0123456789abcdef", ApiRunId.Normalize("run-0123456789abcdef"));
    }

    [Fact]
    public void Run_id_is_null_when_not_provided()
    {
        Assert.Null(ApiRunId.Normalize(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_run_id_is_treated_as_absent(string runId)
    {
        // 空白值不能当成有效归属证据：取不到就判失败，而不是比对一个空串。
        Assert.Null(ApiRunId.Normalize(runId));
    }

    [Fact]
    public void Header_name_is_fixed_for_the_verification_script()
    {
        // 脚本按名字取响应头，改名就是破坏契约。
        Assert.Equal("X-Pzd-Run-Id", ApiRunId.HeaderName);
    }

    [Fact]
    public void Header_is_written_verbatim_when_injected()
    {
        var headers = new HeaderDictionary();

        ApiRunId.Apply("run-0123456789abcdef", headers);

        Assert.Equal("run-0123456789abcdef", headers[ApiRunId.HeaderName].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Header_is_omitted_when_not_injected(string? runId)
    {
        // 这是老登 F-03-A11 ① 的序列化层要求：不是“值为 null 的字段”，是**头本身不该出现**。
        var headers = new HeaderDictionary();

        ApiRunId.Apply(runId, headers);

        Assert.False(headers.ContainsKey(ApiRunId.HeaderName));
        Assert.Empty(headers);
    }

    [Fact]
    public void Version_payload_keeps_the_three_field_contract()
    {
        // 断言的是**HTTP 载荷**，不是 CLR 属性：上一版 bug 正是「属性上没值、JSON 里多了 runId:null」，
        // 对象级断言全绿也发现不了。少一个字段、多一个字段、多一个 null 字段，这里都会红。
        var json = JsonSerializer.Serialize(ApiVersionInfo.Current, JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["buildId", "buildTime", "version"], keys);
    }
}
