using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

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
var web = builder
    .AddViteApp("web", webDirectory, "dev")
    .WithPnpm(install: false)
    .WithReference(api)
    .WaitFor(api);

// F-03-A11：aspire 模式下 5199 的归属也靠本次运行标记。DCP reparent 后父链到不了本宿主
// （dcp.exe → dcp.exe → 已退出），命令行路径匹配又排除不了同目录的残留实例（路径一模一样），
// 只有脚本本轮新生成、残留实例拿不到的值能证明「这个 200 是本次启动的实例给的」。
// 必须传给 **vite 进程本身**（不是传给 AppHost）：vite.config.ts 读的是自己的 process.env。
if (!string.IsNullOrWhiteSpace(runId))
{
    web = web.WithEnvironment("PZD_RUN_ID", runId);
}

web
    .WithHttpEndpoint(port: 5199, name: "http")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 5199;
        endpoint.TargetPort = 5199;
    });

var app = builder.Build();

// 诊断（李总 F-03-A5）：本机 DCP 的编排日志通道被 ACL 挡住（"Failed to enable orchestration logging"），
// AppHost 输出里没有任何资源级事件，于是「web 资源压根没被 DCP 拉起」在运行期完全不可见——只能等
// 脚本 240 秒超时，事后也无法判断是「没拉起」还是「起来了又挂」。这里直接订阅资源状态流，
// 把 api/web 每次状态变化打到 stdout：失败时能看到 State（如 FailedToStart/Exited）、ExitCode 和
// 资源快照里的错误文本，让这条腿的失败可诊断。
// 只订阅、不改资源定义。DistributedApplication 上没有 Lifetime 属性，所以这里不用
// ApplicationStarted 回调：`app.Run()` 会阻塞到宿主退出，直接把订阅任务挂在 Build() 之后即可。
_ = Task.Run(async () =>
{
    var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
    var watched = new[] { "api", "web" };

    try
    {
        await foreach (var resourceEvent in notifications.WatchAsync())
        {
            var name = resourceEvent.Resource.Name;
            if (!watched.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var snapshot = resourceEvent.Snapshot;
            var state = snapshot.State?.Text ?? "(unknown)";
            var exitCode = snapshot.ExitCode?.ToString() ?? "-";

            Console.WriteLine($"[pzd-diag] resource={name} state={state} exitCode={exitCode}");

            if (!string.IsNullOrWhiteSpace(snapshot.State?.Text) &&
                snapshot.State.Text.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[pzd-diag] resource={name} 状态文本含 error：{snapshot.State.Text}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // 宿主退出：正常收尾，不打印噪声。
    }
});

app.Run();
