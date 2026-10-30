using Pzd.IntegrationTests.Fixtures;

namespace Pzd.IntegrationTests;

/// <summary>
/// 36 号票的护栏用例：`pzd_test` 账号带全局 `DROP`（要建随机库名，MySQL 没法只授给不存在的库），
/// 所以「删的必须是测试库」这条防线只能写在代码里。06–32 全部复用这套 fixture，一次传错库名
/// 就能静默删掉平台库，护栏必须由用例钉住，不能只靠「名字都是内部生成的」这个假设。
/// fixture 是程序集级的（见 <c>AssemblyFixtures.cs</c>）。
/// </summary>
public sealed class FixtureGuardTests(MySqlServerFixture fixture)
{
    [Fact]
    public void 删库护栏只认测试库名前缀()
    {
        // 合法：fixture 自己生成的名字
        MySqlServerFixture.EnsureTestDatabaseName("pzd_test_0123456789abcdef");

        // 非法：平台库、租户库、空名、只差一个前缀的近似名
        foreach (var rejected in new[] { "pzd_master", "pzd_t_tenant1", string.Empty, "pzd_test", "xpzd_test_abc" })
        {
            Assert.Throws<ArgumentException>(() => MySqlServerFixture.EnsureTestDatabaseName(rejected));
        }
    }

    [Fact]
    public async Task 误传库名时释放不会执行删除()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var real = await fixture.CreateDatabaseAsync(ct);

        // 这个库不存在，也不允许被建；护栏若失效，DROP DATABASE IF EXISTS 会静默成功，
        // 等于「错误被吞掉」，所以断言的是抛异常，不是「库还在」。
        var rogue = new TestDatabase("pzd_guard_should_not_drop", real.ConnectionString, fixture);

        await Assert.ThrowsAsync<ArgumentException>(() => rogue.DisposeAsync().AsTask());
    }
}
