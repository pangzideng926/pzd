var builder = DistributedApplication.CreateBuilder(args);

// 数据源两种来源，用配置切换（03 号票「容器优先、外部实例回落」）：
//   未配 External:MySql / External:Redis → 起容器（固定镜像 tag）
//   配了连接串 → 直接用外部实例，不再起容器（本机无 Docker 时走这条回落路径）
// 连接串只经 AppHost 注入给资源，不写进任何 appsettings。
var externalMySql = builder.Configuration["External:MySql"];
var externalRedis = builder.Configuration["External:Redis"];

// 回落路径的库名由连接串自己决定，所以必须显式带 Database=。
//   资源名 pzd-master 只是连接串的键名（含下划线会触发 ASPIRE006）；库名一律用下划线，
//   平台库是 pzd_master、租户库是 pzd_t_{code}（architecture.md §6.1）。
//   少了 Database= 驱动就没有默认库，运行期才炸成 ERROR 1049 Unknown database——宁可启动即失败。
if (!string.IsNullOrWhiteSpace(externalMySql)
    && !externalMySql.Contains("Database=", StringComparison.OrdinalIgnoreCase)
    && !externalMySql.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "回落路径的 MySQL 连接串缺少 Database=。资源名是 pzd-master（键名可以有连字符），"
        + "但库名一律下划线：平台库为 pzd_master（architecture.md §6.1）。"
        + "请写成 External__MySql=\"Server=...;Port=...;Database=pzd_master;User ID=...;Password=...\"。");
}

var useContainers = string.IsNullOrWhiteSpace(externalMySql) || string.IsNullOrWhiteSpace(externalRedis);

// 资源名只是连接串的键名，真正的库名看 Database= 或 db/ 下的迁移脚本（12、17、18 号票）
IResourceBuilder<IResourceWithConnectionString> database = string.IsNullOrWhiteSpace(externalMySql)
    ? builder.AddMySql("mysql").WithImageTag("8.4").AddDatabase("pzd-master")
    : builder.AddConnectionString("pzd-master", ReferenceExpression.Create($"{externalMySql}"));

IResourceBuilder<IResourceWithConnectionString> cache = string.IsNullOrWhiteSpace(externalRedis)
    ? builder.AddRedis("redis").WithImageTag("7.4")
    : builder.AddConnectionString("redis", ReferenceExpression.Create($"{externalRedis}"));

var api = builder.AddProject("api", "../../src/Pzd.Api/Pzd.Api.csproj")
    .WithReference(database)
    .WithReference(cache);

if (useContainers)
{
    api.WaitFor(database).WaitFor(cache);
}

builder.AddViteApp("web", "../../apps/web")
    .WithPnpm()
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
