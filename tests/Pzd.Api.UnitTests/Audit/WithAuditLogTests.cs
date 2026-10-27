using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pzd.Api.UnitTests.Tenancy;
using Pzd.Infrastructure.Audit;
using Pzd.Infrastructure.Tenancy;
using Pzd.SharedKernel.Abstractions;

namespace Pzd.Api.UnitTests.Audit;

/// <summary>
/// 票 08：标了 <c>WithAuditLog(模块, 动作)</c> 的写端点，调用后审计条目进入 Channel 并被后台消费；
/// 走真实 HTTP 管道（TestServer），断言操作人、租户、模块、动作、结果、耗时、IP、UA。
/// </summary>
public class WithAuditLogTests
{
    [Fact]
    public async Task Audited_endpoint_enqueues_entry_with_all_required_fields()
    {
        var options = new AuditOptions { QueueCapacity = 16, BatchSize = 10 };
        var channel = new AuditLogChannel(options, NullLogger<AuditLogChannel>.Instance);
        var writer = new RecordingAuditLogWriter();
        var clock = new PzdApiFactory.FixedClock(new DateTimeOffset(2026, 10, 27, 3, 0, 0, TimeSpan.Zero));

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(channel);
        builder.Services.AddSingleton<IAuditLogWriter>(writer);
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddHostedService<AuditLogBackgroundService>();

        await using var app = builder.Build();

        app.Use(async (context, next) =>
        {
            // 真实管道里由租户解析中间件写入；这里只需一个已解析的租户。
            context.RequestServices.GetRequiredService<ITenantContext>().Set(7);
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "42")],
                authenticationType: "Test"));
            await next(context);
        });

        app.MapPost("/api/users", () => Results.Ok(new { id = 1 })).WithAuditLog("用户管理", "新增用户");

        await app.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            using var client = app.GetTestServer().CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
            request.Headers.Add("User-Agent", "pzd-test-agent");
            request.Headers.Add("X-Forwarded-For", "203.0.113.5");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await writer.WaitForCountAsync(1, TimeSpan.FromSeconds(10));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }

        var entry = Assert.Single(writer.Batches.SelectMany(batch => batch));

        Assert.Equal("用户管理", entry.Module);
        Assert.Equal("新增用户", entry.Action);
        Assert.Equal(7, entry.TenantId);
        Assert.Equal("42", entry.UserId);
        Assert.Equal(200, entry.StatusCode);
        Assert.True(entry.Succeeded);
        Assert.InRange(entry.ElapsedMilliseconds, 0, 5_000);
        Assert.Equal("203.0.113.5", entry.ClientIp);
        Assert.Equal("pzd-test-agent", entry.UserAgent);
        Assert.Equal(clock.UtcNow, entry.OccurredAt);
    }
}
