using Pzd.IntegrationTests.Fixtures;

namespace Pzd.IntegrationTests;

/// <summary>
/// 36 号票的 S2 seam：fixture 能起一个真 MySQL 库，走通建表 → 插入 → 查询。
/// 只验证载体本身（驱动、连接串、字符集、每用例独立库），不写业务表语义——
/// 业务断言由 06 及后续票在这个 fixture 上追加。
/// </summary>
public sealed class SmokeTests(MySqlServerFixture fixture) : IClassFixture<MySqlServerFixture>
{
    [Fact]
    public async Task 独立库建表插入后能查回中文原文()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await fixture.CreateDatabaseAsync(ct);

        await db.ExecuteAsync("CREATE TABLE smoke_items (id INT PRIMARY KEY, name VARCHAR(64) NOT NULL)", ct);
        await db.ExecuteAsync("INSERT INTO smoke_items (id, name) VALUES (1, '张三')", ct);

        Assert.Equal("张三", await db.QueryScalarAsync<string>("SELECT name FROM smoke_items WHERE id = 1", ct));
    }

    [Fact]
    public async Task 库默认字符集是utf8mb4()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await fixture.CreateDatabaseAsync(ct);

        Assert.Equal(
            "utf8mb4",
            await db.QueryScalarAsync<string>(
                "SELECT DEFAULT_CHARACTER_SET_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = DATABASE()",
                ct));
    }

    [Fact]
    public async Task 每个用例拿到互不相同的独立库()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var first = await fixture.CreateDatabaseAsync(ct);
        await using var second = await fixture.CreateDatabaseAsync(ct);

        Assert.NotEqual(first.Name, second.Name);

        // 独立库互不可见：第一个库里建的表，第二个库看不到
        await first.ExecuteAsync("CREATE TABLE isolated (id INT PRIMARY KEY)", ct);
        var visibleInSecond = await second.QueryScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'isolated'",
            ct);

        Assert.Equal(0, visibleInSecond);
    }
}
