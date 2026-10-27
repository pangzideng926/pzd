using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 本机（受限令牌）写 Windows 事件日志会被拒绝，默认的 EventLog 提供方会让宿主在启动期直接抛异常退出
// （"Hosting failed to start"）。因此换掉默认提供方、**只保留控制台**——既避开 EventLog，
// 又不至于把资源日志一起吞掉（ClearProviders() 不补加控制台时，Dev 环境下一个字都看不到）。
builder.Services.AddLogging(logging => logging
    .ClearProviders()
    .AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

// 票 03：只引用本机已有实例，不创建容器（本机无 Docker，容器编排不在本轮验收）。
// 连接串从环境变量 / local.env 注入（ConnectionStrings__mysql、ConnectionStrings__redis），
// 缺省时保持为空 —— API 以 Degraded 模式启动并打印 Warning，CI 与演示环境不至于起不来。
var mysql = builder.AddConnectionString("mysql");
var redis = builder.AddConnectionString("redis");

// API 固定 http://127.0.0.1:5080：前端 dev proxy 与 scripts/verify-apphost.ps1 都按这个地址探测。
var api = builder
    .AddProject<Projects.Pzd_Api>("api")
    .WithReference(mysql)
    .WithReference(redis)
    // 只固定对外端口：targetPort 交给 DCP 动态分配并由代理转发。
    // 同时写 port 与相同的 targetPort 会被 Aspire 拒绝（"Non-container resources cannot be proxied
    // when both TargetPort and Port are specified with the same value."）。
    .WithHttpEndpoint(port: 5080, name: "http");

// F-03-A11：把「本次运行唯一标记」透传给 API 资源。aspire 模式下 5080 由 DCP 的代理持有，
// 父链到不了本次宿主（dcp.exe → dcp.exe → 已退出），所以归属不能靠进程树判；
// 由启动方注入 PZD_RUN_ID，API 原样从 /api/version 回显，脚本比对即可证明「应答的是本次启动的进程」。
// 同 hash 的残留实例拿不到这个值，所以它比 buildId 更硬（buildId 只能证明版本相同）。
var runId = Environment.GetEnvironmentVariable("PZD_RUN_ID");

if (!string.IsNullOrWhiteSpace(runId))
{
    api = api.WithEnvironment("PZD_RUN_ID", runId);
}

// 前端目录按仓库结构算绝对路径：AddViteApp 的相对路径是相对 **AppHost 工程目录**（src/Pzd.AppHost）
// 解析的，写 "../apps/web" 会指到 src/apps/web（不存在），于是 web 资源瞬间失败、连 node 进程都不产生
// （F-03-2）。用绝对路径 + 启动期校验，路径错了立刻报得出来，而不是等到 90 秒超时。
var webDirectory = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "..", "apps", "web"));

if (!Directory.Exists(webDirectory))
{
    throw new DirectoryNotFoundException($"找不到前端目录：{webDirectory}");
}

// 前端 dev server：monorepo 用 pnpm，端口由 vite.config.ts 固定 5199（strictPort）。
// 这里只声明「对外就是 5199」并关掉代理：不关的话 Aspire 会给它另分配一个动态端口再转发，
// 5199 就永远没有监听者，脚本与票面探测点全部失效；关掉代理后 Vite 自己按 vite.config.ts
// 绑 127.0.0.1:5199，端口仍然只有一处定义。
// WaitFor 让前端等 API 起来再跑，避免 proxy 先接到 ECONNREFUSED。
builder
    .AddViteApp("web", webDirectory, "dev")
    .WithPnpm(install: false)
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5199, name: "http")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 5199;
        endpoint.TargetPort = 5199;
    });

builder.Build().Run();
