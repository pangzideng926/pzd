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

// F-03-A5 根因候选（锋哥 2026-10-27 复现）：`NODE_ENV=production` 撞上 `react-router dev` 时，
// Vite 的 SSR 变换仍生成 `jsxDEV` 调用，而 production 的 `react/jsx-dev-runtime` 没有 `jsxDEV`，
// 于是在 `renderToPipeableStream` 调用点**同步抛出**——默认 entry 的 `abort` 停在 TDZ，6 秒后
// 进程以 exit 1 退出，5199 从头到尾没人监听，和 aspire 现场完全同形。
//
// 这里把 web 的 `NODE_ENV` 显式钉成 development：这个资源跑的是 package.json 的 `dev` 脚本，
// 语义上本来就该是 development，钉住之后不吃 supervisor 传下来的任何值。没被注入时它是个无害的
// 显式声明；被注入 production 时它就是修复。
web = web.WithEnvironment("NODE_ENV", "development");

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
    var seenStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    try
    {
        // 先打一行「订阅已建立」：这样「零输出」能立刻分成两类——连这行都没有说明 AppHost
        // 根本没走到订阅点（多半死在 app.Run() 的启动阶段），有这行却没有资源事件，
        // 才是「资源没被创建」或「事件没来」。
        Console.WriteLine($"[pzd-diag] 已订阅资源状态流：{string.Join(", ", watched)}");

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

            // F-03-A5：手动 shell 能起、DCP 起就崩，唯一的差异面在进程环境。这里在 web 每次
            // 进入新状态时打一份**白名单**环境快照——只列已知会分叉的几项，PATH 只留前三段，
            // 连接串/密码/令牌一律不输出（老何的硬约束：不整包打印）。
            //
            // 张一一 2026-10-27 指出：初始快照是 (unknown)、环境变量 0 项，若此时就打印
            // 「共 0 项」，后来人会读成「web 进程没有环境变量」——比不打更糟。所以空快照
            // 只留一行说明「等非空再打」，绝不当作证据。
            EnvironmentDiagnostics.Report(name, state, snapshot.EnvironmentVariables, seenStates);

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
    catch (Exception exception)
    {
        // 订阅自身抛错如果被静默吞掉，「一行都没有」就会和「资源真的没创建」混成同一个现象
        // （雨哥 2026-10-27 指出）。这里必须留下痕迹，否则下一个人还要在零输出上卡一次。
        Console.WriteLine($"[pzd-diag] 订阅中断：{exception.GetType().Name}: {exception.Message}");
    }
});

app.Run();

/// <summary>
/// F-03-A5 诊断用的环境变量**白名单**渲染。唯一出口是 <see cref="Describe"/>：
/// 名字不在白名单里一律返回 <c>null</c>，所以即使 DCP 快照里带了连接串，也流不到 stdout。
/// </summary>
internal static class EnvironmentDiagnostics
{
    // 只观察「手动 shell 能起、DCP 起就崩」最可能分叉的几项。新增名字前先想清它会不会携带凭据。
    private static readonly string[] Watched =
    [
        "PORT",
        "NODE_ENV",
        "NODE_OPTIONS",
        "CI",
        "TZ",
        "PZD_RUN_ID",
        "PATH",
    ];

    // 兜底闸门：将来谁往白名单里加了带这些词的名字，也不会输出值。
    private static readonly string[] SensitiveMarkers =
    [
        "PASSWORD",
        "SECRET",
        "TOKEN",
        "CONNECTIONSTRING",
        "CONNECTION_STRING",
        "CREDENTIAL",
        "SALT",
    ];

    /// <summary>
    /// 打印一份白名单环境快照。**空快照绝不当作证据**：初始事件是 <c>(unknown)</c> 且 0 项，
    /// 那时打印「共 0 项」会被读成「web 进程没有环境变量」。空快照只留一行说明并退回，
    /// 等第一个非空快照再打。
    /// </summary>
    public static void Report(
        string resourceName,
        string state,
        IEnumerable<EnvironmentVariableSnapshot> variables,
        Dictionary<string, string> seenStates)
    {
        // 只看 web：api 腿一直正常，多打一份只会淹没证据。
        if (!string.Equals(resourceName, "web", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var all = variables as IReadOnlyCollection<EnvironmentVariableSnapshot> ?? variables.ToArray();

        if (all.Count == 0)
        {
            // 同一状态只提醒一次，否则 WatchAsync 的重复回调会刷屏。
            var emptyKey = $"empty:{state}";
            if (seenStates.TryGetValue(resourceName, out var warned) && warned == emptyKey)
            {
                return;
            }

            seenStates[resourceName] = emptyKey;
            Console.WriteLine(
                $"[pzd-diag] resource={resourceName} state={state} 环境变量快照为 0 项：" +
                "这不代表 web 进程没有环境变量，只是该状态的快照还没填充，等非空快照再打");
            return;
        }

        // WatchAsync 对 URL、健康等变化也会回调，按状态文本去重，避免刷屏。
        if (seenStates.TryGetValue(resourceName, out var previous) && previous == state)
        {
            return;
        }

        seenStates[resourceName] = state;
        Console.WriteLine($"[pzd-diag] resource={resourceName} state={state} 环境变量共 {all.Count} 项（下表仅白名单取值）");

        foreach (var variable in all.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            var rendered = Describe(variable.Name, variable.Value);
            if (rendered is null)
            {
                continue;
            }

            Console.WriteLine($"[pzd-diag] env {variable.Name}={rendered}");
        }
    }

    /// <summary>白名单 + 脱敏后的渲染；不需要或不该输出的一律返回 <c>null</c>。</summary>
    public static string? Describe(string? name, string? value)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Watched.Contains(name, StringComparer.Ordinal))
        {
            return null;
        }

        if (SensitiveMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return name switch
        {
            "PATH" => DescribePath(value),
            "NODE_OPTIONS" => DescribeNodeOptions(value),
            _ => Truncate(value),
        };
    }

    /// <summary>
    /// NODE_OPTIONS 不能按整串截断：要查的 `--conditions`、`--require`、`--loader` 完全可能落在尾部，
    /// 被裁掉就等于白测。按空白切成 token 逐个渲染，只有单个 token 过长时才截断。
    /// </summary>
    private static string DescribeNodeOptions(string options)
    {
        var tokens = options.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "（空）";
        }

        var rendered = string.Join(" | ", tokens.Select(static token => Truncate(token)));
        return $"{rendered}（共 {tokens.Length} 个 token）";
    }

    private static string DescribePath(string path)
    {
        var segments = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "（空）";
        }

        // PATH 动辄几十段，全打印既没用又可能带出用户目录；只留前三段 + 总段数。
        var head = string.Join(Path.PathSeparator, segments.Take(3));
        return $"{head}{Path.PathSeparator}…（共 {segments.Length} 段）";
    }

    private static string Truncate(string value) =>
        value.Length <= 96 ? value : $"{value.AsSpan(0, 96)}…（共 {value.Length} 字符，已截断）";
}
