using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 本机（受限令牌）写 Windows 事件日志会被拒绝，默认的 EventLog 提供方会让宿主在启动期直接抛异常退出
// （"Hosting failed to start"）。清掉默认提供方只留控制台——与测试宿主 ClearProviders 的处理同源。
builder.Services.AddLogging(logging => logging.ClearProviders());

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
    .WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http");

// 前端 dev server：monorepo 用 pnpm，端口由 vite.config.ts 固定 5199（strictPort），
// 这里不重复指定端口，免得同一事实两处定义；WaitFor 让前端等 API 起来再跑，避免 proxy 先接到 ECONNREFUSED。
builder
    .AddViteApp("web", "../apps/web", "dev")
    .WithPnpm(install: false)
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
