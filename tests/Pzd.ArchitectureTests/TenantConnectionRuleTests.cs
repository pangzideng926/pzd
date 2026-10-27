namespace Pzd.ArchitectureTests;

/// <summary>
/// 票 07 / F-05-8：后台任务与 Outbox 消费者必须用带 <c>tenantId</c> 的显式重载取连接
/// （先按 store 校验停用/到期），禁止请求路径专用的 <c>GetCurrentConnectionScopeAsync</c>。
/// 生产源码扫描 + 合成源码反向验证：换名 token 也绕不过（F-07-1）。
/// </summary>
public class TenantConnectionRuleTests
{
    [Fact]
    public void Product_sources_never_use_the_request_path_connection_api_in_background_code()
    {
        var violations = ArchRules.ParameterlessTenantConnectionViolations(ArchRules.FindRepositoryRoot());

        Assert.Empty(violations);
    }

    [Fact]
    public void The_rule_flags_the_request_path_api_including_a_renamed_token_but_allows_the_explicit_overload()
    {
        (string Path, string Content)[] sources =
        [
            ("src/Pzd.Infrastructure/Persistence/Caller.cs", "await _dbProvider.GetCurrentConnectionScopeAsync();"),
            (
                "src/Pzd.Infrastructure/Persistence/CallerRenamedToken.cs",
                "var ct2 = cancellationToken;\nawait _dbProvider.GetCurrentConnectionScopeAsync(ct2);"),
            (
                "src/Pzd.Infrastructure/Persistence/CallerExplicit.cs",
                "await _dbProvider.GetConnectionScopeAsync(message.TenantId, cancellationToken);"),
            (
                "src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs",
                "Task<ISqlSugarClient> GetCurrentConnectionScopeAsync(CancellationToken cancellationToken = default);"),
            (
                "src/Pzd.Api/Endpoints/Users.cs",
                "await _dbProvider.GetCurrentConnectionScopeAsync(cancellationToken);"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Equal(
            [
                "src/Pzd.Infrastructure/Persistence/Caller.cs",
                "src/Pzd.Infrastructure/Persistence/CallerRenamedToken.cs",
            ],
            violations);
    }

    [Theory]
    [InlineData("src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Persistence/Any/Deep/File.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Jobs/OutboxDispatcherHostedService.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Observability/DomainEventDispatcher.cs", true)]
    [InlineData("src/Pzd.Api/Endpoints/Users.cs", false)]
    [InlineData("src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs", false)]
    public void Background_path_predicate_covers_persistence_and_hosted_workers(string path, bool expected) =>
        Assert.Equal(expected, ArchRules.IsBackgroundPath(path));

    [Fact]
    public void The_outbox_dispatcher_itself_passes_the_rule()
    {
        var repositoryRoot = ArchRules.FindRepositoryRoot();
        var dispatcherPath = Path.Combine(
            repositoryRoot,
            "src",
            "Pzd.Infrastructure",
            "Persistence",
            "OutboxDispatcher.cs");

        var content = File.ReadAllText(dispatcherPath);

        Assert.Contains("GetConnectionScopeAsync(message.TenantId", content, StringComparison.Ordinal);
        Assert.Empty(ArchRules.ParameterlessTenantConnectionViolationsFromSource([("OutboxDispatcher.cs", content)]));
    }
}
