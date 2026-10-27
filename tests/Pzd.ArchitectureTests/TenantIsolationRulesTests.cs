namespace Pzd.ArchitectureTests;

/// <summary>
/// 租户隔离的代码检查（架构 §6.2 要点）：禁止在共享客户端上 <c>ChangeDatabase</c>，
/// 租户连接只能经 Scoped 的 <c>ITenantDbProvider</c> 获取。扫描范围覆盖 <c>src/Pzd.*</c>（含未来模块）。
/// </summary>
public class TenantIsolationRulesTests
{
    [Fact]
    public void Current_sources_never_call_ChangeDatabase() =>
        Assert.Empty(ArchRules.ChangeDatabaseViolations(ArchRules.FindRepositoryRoot()));

    [Fact]
    public void A_module_source_calling_ChangeDatabase_is_detected()
    {
        var fixtureRoot = CreateTemporaryRepositoryRoot(
            ("src/Pzd.Foo/Pzd.Foo.csproj", "<Project />"),
            ("src/Pzd.Foo/Leak.cs", "public class Leak { public void X(SqlSugar.ISqlSugarClient db) => db.ChangeDatabase(\"pzd_t_other\"); }"));

        try
        {
            var violations = ArchRules.ChangeDatabaseViolations(fixtureRoot);

            Assert.Equal([Path.Combine("src", "Pzd.Foo", "Leak.cs")], violations);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    private static string CreateTemporaryRepositoryRoot(params (string RelativePath, string Content)[] files)
    {
        // Low IL 沙箱只允许写工作区，夹具目录放在测试输出目录下（bin/ 已被 .gitignore 忽略）。
        var root = Path.Combine(AppContext.BaseDirectory, "arch-fixtures", Guid.NewGuid().ToString("N"));

        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return root;
    }
}
