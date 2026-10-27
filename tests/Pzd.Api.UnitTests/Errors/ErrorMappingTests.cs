using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Pzd.Infrastructure.Errors;
using Pzd.Api.UnitTests.Tenancy;

namespace Pzd.Api.UnitTests.Errors;

/// <summary>
/// 票 06：<c>result.ToHttpResult()</c> 的状态码与 ProblemDetails 映射全表，逐条走 HTTP 接缝。
/// </summary>
public class ErrorMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<string, int, string, string> MappingTable => new()
    {
        { "validation", 400, "Identity.User.NameDuplicated", "https://pzd/errors/identity.user.name-duplicated" },
        { "unauthorized", 401, "Identity.User.NotAuthenticated", "https://pzd/errors/identity.user.not-authenticated" },
        { "forbidden", 403, "Identity.User.Forbidden", "https://pzd/errors/identity.user.forbidden" },
        { "notfound", 404, "Identity.User.NotFound", "https://pzd/errors/identity.user.not-found" },
        { "conflict", 409, "Identity.User.NameDuplicated", "https://pzd/errors/identity.user.name-duplicated" },
        { "businessrule", 422, "Identity.Role.InUse", "https://pzd/errors/identity.role.in-use" },
        { "clienttooold", 426, "Client.TooOld", "https://pzd/errors/client.too-old" },
        { "tenantupgrading", 503, "Tenant.Upgrading", "https://pzd/errors/tenant.upgrading" },
    };

    [Theory]
    [MemberData(nameof(MappingTable))]
    public async Task Result_maps_to_the_expected_status_and_problem_details(
        string scenario,
        int expectedStatus,
        string expectedCode,
        string expectedType)
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await GetScenarioAsync(client, scenario);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(expectedType, root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));

        var traceId = root.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.Equal(traceId, response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    public async Task Validation_failure_turns_PascalCase_keys_into_camel_case()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await GetScenarioAsync(client, "validation");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);

        var errors = json.RootElement.GetProperty("errors");

        Assert.True(errors.TryGetProperty("userName", out _), body);
        Assert.False(errors.TryGetProperty("UserName", out _), body);
        Assert.Equal(["用户名已存在"], errors.GetProperty("userName").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void TraceId_prefers_the_running_activity_id()
    {
        using var activity = new System.Diagnostics.Activity("test").Start();
        var context = new DefaultHttpContext();

        Assert.Equal(activity.Id, TraceIds.Current(context));
    }

    [Fact]
    public void TraceId_falls_back_to_the_request_identifier_without_an_activity()
    {
        var previous = System.Diagnostics.Activity.Current;
        System.Diagnostics.Activity.Current = null;

        try
        {
            var context = new DefaultHttpContext { TraceIdentifier = "fallback-trace" };

            Assert.Equal("fallback-trace", TraceIds.Current(context));
        }
        finally
        {
            System.Diagnostics.Activity.Current = previous;
        }
    }

    [Fact]
    public async Task Successful_non_generic_result_is_204_without_body()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await GetScenarioAsync(client, "success");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Successful_generic_result_is_200_with_the_value()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await GetScenarioAsync(client, "success-value");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"pong\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Framework_404_carries_code_and_traceId()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/missing-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;

        Assert.Equal("Http.NotFound", root.GetProperty("code").GetString());
        Assert.Equal("https://pzd/errors/http.not-found", root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.Equal(root.GetProperty("traceId").GetString(), response.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    public async Task Framework_405_carries_code_and_traceId()
    {
        await using var factory = new PzdApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Post, "/");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = json.RootElement;

        Assert.Equal("Http.MethodNotAllowed", root.GetProperty("code").GetString());
        Assert.Equal("https://pzd/errors/http.method-not-allowed", root.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
    }

    private static Task<HttpResponseMessage> GetScenarioAsync(HttpClient client, string scenario) =>
        SendAsync(client, HttpMethod.Get, $"/__tests__/{scenario}");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Tenant", "acme");

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
