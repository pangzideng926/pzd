namespace Pzd.ArchitectureTests;

/// <summary>
/// 票 07 / F-05-8：后台任务与 Outbox 消费者必须用带 <c>tenantId</c> 的显式重载取连接
/// （先校验停用/到期），禁止无参裸取。生产源码扫描 + 合成源码反向验证规则本身有效。
/// </summary>
public class TenantConnectionRuleTests
{
    [Fact]
    public void Product_sources_never_call_the_parameterless_tenant_connection_overload()
    {
        var violations = ArchRules.ParameterlessTenantConnectionViolations(ArchRules.FindRepositoryRoot());

        Assert.Empty(violations);
    }

    [Fact]
    public void The_rule_flags_the_parameterless_overload_but_allows_the_explicit_one()
    {
        (string Path, string Content)[] sources =
        [
            ("src/Pzd.Infrastructure/Persistence/Caller.cs", "await _dbProvider.GetConnectionScopeAsync();"),
            ("src/Pzd.Infrastructure/Persistence/CallerCt.cs", "await _dbProvider.GetConnectionScopeAsync(cancellationToken);"),
            ("src/Pzd.Infrastructure/Persistence/CallerExplicit.cs", "await _dbProvider.GetConnectionScopeAsync(message.TenantId, cancellationToken);"),
            ("src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs", "Task<ISqlSugarClient> GetConnectionScopeAsync(CancellationToken cancellationToken = default);"),
        ];

        var violations = ArchRules.ParameterlessTenantConnectionViolationsFromSource(sources);

        Assert.Equal(
            ["src/Pzd.Infrastructure/Persistence/Caller.cs", "src/Pzd.Infrastructure/Persistence/CallerCt.cs"],
            violations);
    }

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
