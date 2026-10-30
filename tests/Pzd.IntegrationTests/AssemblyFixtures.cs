using Pzd.IntegrationTests.Fixtures;
using Xunit;

// 36 号票约定（06–32 一律照此写，别再各猜一套）：
// 集成测试用**程序集级** fixture，不用 IClassFixture。IClassFixture 是每个测试类一个实例，
// 容器模式下等于每个类各起一个 MySQL 容器，几十个类会线性放大；程序集级只起一个，
// 每个用例仍然通过 CreateDatabaseAsync 拿自己独立的库。
[assembly: AssemblyFixture(typeof(MySqlServerFixture))]
