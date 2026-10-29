var builder = DistributedApplication.CreateBuilder(args);

// 数据源两种来源，用配置切换（03 号票「容器优先、外部实例回落」）：
//   未配 External:MySql / External:Redis → 起容器（固定镜像 tag）
//   配了连接串 → 直接用外部实例，不再起容器（本机无 Docker 时走这条回落路径）
// 连接串只经 AppHost 注入给资源，不写进任何 appsettings。
var externalMySql = builder.Configuration["External:MySql"];
var externalRedis = builder.Configuration["External:Redis"];

var useContainers = string.IsNullOrWhiteSpace(externalMySql) || string.IsNullOrWhiteSpace(externalRedis);

// 库名只是资源标识，真正的库名以 db/ 下的迁移脚本为准（17、18 号票）
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
