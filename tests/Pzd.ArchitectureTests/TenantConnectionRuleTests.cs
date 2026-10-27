namespace Pzd.ArchitectureTests;

/// <summary>
/// 票 07 / F-05-8 / F-07-4：后台任务与 Outbox 消费者必须用带 <c>tenantId</c> 的显式重载取连接
/// （先按 store 校验停用/到期），禁止请求路径专用的 <c>GetCurrentConnectionScopeAsync</c>。
/// 规则是<b>标识符级</b>：后台路径里出现该标识符即违规（不看括号、不看点号、不做空白归一化），
/// 因此换行断开、方法组引用、Jobs 目录都拦得住；另保留「零实参 GetConnectionScopeAsync()」这一层，
/// 防止将来重新加回无参重载。
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
    public void A_call_split_by_a_line_break_or_space_is_caught()
    {
        (string Path, string Content)[] sources =
        [
            (
                "src/Pzd.Infrastructure/Persistence/Broken.cs",
                "await _dbProvider\n    .GetCurrentConnectionScopeAsync (ct);"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Single(violations);
        Assert.Equal("src/Pzd.Infrastructure/Persistence/Broken.cs", violations.Single());
    }

    [Fact]
    public void A_method_group_reference_is_caught()
    {
        (string Path, string Content)[] sources =
        [
            (
                "src/Pzd.Infrastructure/Persistence/Group.cs",
                "var factory = _dbProvider.GetCurrentConnectionScopeAsync;"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Single(violations);
        Assert.Equal("src/Pzd.Infrastructure/Persistence/Group.cs", violations.Single());
    }

    [Fact]
    public void A_background_file_under_Jobs_is_caught()
    {
        (string Path, string Content)[] sources =
        [
            (
                "src/Pzd.Infrastructure/Jobs/NightlyJob.cs",
                "await _dbProvider.GetCurrentConnectionScopeAsync();"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Single(violations);
        Assert.Equal("src/Pzd.Infrastructure/Jobs/NightlyJob.cs", violations.Single());
    }

    [Fact]
    public void The_rule_allows_the_explicit_overload_and_request_path_usage_outside_background()
    {
        (string Path, string Content)[] sources =
        [
            (
                "src/Pzd.Infrastructure/Persistence/CallerExplicit.cs",
                "await _dbProvider.GetConnectionScopeAsync(message.TenantId, cancellationToken);"),
            (
                "src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs",
                "Task<ISqlSugarClient> GetCurrentConnectionScopeAsync(CancellationToken cancellationToken = default);"),
            (
                "src/Pzd.Api/Endpoints/Users.cs",
                "await _dbProvider.GetCurrentConnectionScopeAsync(cancellationToken);"),
            // 认形状、不认名字：非后台文件里重新加回无参 GetConnectionScopeAsync() 并调用，也会报。
            (
                "src/Pzd.Api/Endpoints/Legacy.cs",
                "await _dbProvider.GetConnectionScopeAsync();"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Equal(["src/Pzd.Api/Endpoints/Legacy.cs"], violations);
    }

    [Theory]
    [InlineData("src/Pzd.Infrastructure/Persistence/OutboxDispatcher.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Persistence/Any/Deep/File.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Jobs/NightlyJob.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Jobs/Helpers/RetryPolicy.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Jobs/OutboxDispatcherHostedService.cs", true)]
    [InlineData("src/Pzd.Infrastructure/Observability/DomainEventDispatcher.cs", true)]
    [InlineData("src/Pzd.Api/Endpoints/Users.cs", false)]
    [InlineData("src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs", false)]
    public void Background_path_predicate_covers_persistence_jobs_and_hosted_workers(string path, bool expected) =>
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
