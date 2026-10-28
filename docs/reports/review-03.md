# 审计报告：票 03（Aspire 编排 + 免租户健康检查）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec）+ 变异复核 + 真实进程验证
> 被测：首轮 `7c5f411`、终审 **`f778a32`**（见 §8/§9）；审计在独立 worktree 执行（`E:\pzd-aud03` 首轮、`E:\pzd-final03` 终审、`E:\pzd-a9` 收尾复核）
> 基线等价性：`git diff --stat 7c5f411 def4a35 -- . ':(exclude)docs'` 为空（HEAD `def4a35` 只多一笔 docs），按老何检查单第 7 条，结论可挂在 HEAD 上
> 依据：[`tickets.md`](../specs/tickets.md) 票 03（`7c5f411` 附近回填版，含 `9c534ba`/`5ff2ad5` 口径统一）、[`foundation.md`](../specs/foundation.md) §4.3 第 55 条与 §128–130、退出码三档 `756c1fc`
> **首轮结论（`7c5f411`）：需修改。** ⚠️ 本文件是**递增记录**，最终结论在 **§9：「票 03 通过（`f778a32`）」**；§7/§8 为复审与终审过程，其「需修改」均已闭环，阅读时勿把早期小节当现状。
> 首轮主路径（免租户豁免、`/health/live`、`/health/ready` 两种模式、`/api/version` 与 MinVer 一致、降级日志有出处、限额校验启动期失败）经我实跑成立；但 **F-03-A1**（一键脚本会把别人的进程当成被测对象，已实证假绿）与 **F-03-A2**（脚本依赖 `--no-build`，干净副本必失败）必修。另有 5 条 low 登记。
>
> 编号说明：票面已有 `F-03-1`（`WithHttpEndpoint` 缺陷）～`F-03-4`（降级 Warn 刷屏），本报告的审计项统一用 `F-03-A*`，不复用票面号。

---

## 1. 基线（我实跑，独立 worktree）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror` | **0 警告 0 错误**，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 35 + 30 + 86 + **43** + 1 = **195 通过，0 失败** |
| 版本一致性 | `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version` | `0.1.1-alpha.0.104`；`/api/version` 返回 `0.1.1-alpha.0.104`，一致 |
| 工作树 | `git status --porcelain`（worktree） | 干净 |

## 2. 票面验收逐条核对

| # | 验收项（票 03） | 核对结果 |
|---|---|---|
| 1 | 有凭据时 `/health/ready` **Healthy**，明细 mysql/redis 都 Healthy | **通过**：`-Mode api` 实跑 exit 0，根状态 + mysql + redis 三条全 `Healthy` |
| 2 | 实例/凭据缺失时降级启动，`/health/ready` **200 + Degraded**，`/health/live` 200 | **通过**：`-Mode api -NoCredentials` 实跑 exit 0，两项 Degraded 200 |
| 3 | 降级时日志含 `Degraded mode: MySQL/Redis unavailable` | **通过**：同一次实跑 stdout 打出两条原文（`MySQL unavailable（mysql 未配置…）`、`Redis unavailable（redis 未配置…）`） |
| 4 | F-05-4：三个端点免租户返回 200；豁免之外仍 400 `Tenant.NotFound` | **通过**：脚本与 `Pzd.Api.UnitTests` 双侧守护（`/` → 400，带伪 `X-Tenant` 仍 200） |
| 5 | `GET /api/version` 返回 version/buildId/buildTime，与 msbuild 一致 | **通过**（见 §1 版本一致性一行）；buildId = sha 前 7 位，buildTime 由程序集写入时间派生 |
| 6 | 一条命令起 API + 前端 dev server，Ctrl+C 优雅退出 | **部分通过**：aspire 模式的 API 腿我这边通过（§5），**前端腿（5199）未在我这边验成**，原因是审计副本用目录联结复用 `node_modules` 被 pnpm 拒绝（`ERR_PNPM_UNSAFE_TASK_RUN_STATE_PATH`），属我的环境问题，不是产品缺陷；需在无联结的正常副本上补一次全链路 |
| 7 | `verify-apphost.ps1` 退出码 0，并贴出 Healthy / Degraded 证据 | **未通过**：退出码语义本身有假绿风险（F-03-A1），且脚本不能自举（F-03-A2） |

## 3. 我的独立变异复核（都能打红，说明守护不是摆设）

| 变异 | 期望 | 实测 |
|---|---|---|
| ③ 把 `RedisHealthCheck` 的 catch 收窄回 `SocketException or IOException or OperationCanceledException`（旧实现） | `/health/ready` 不能变 503 | **通过（红）**：`host=127.0.0.1;port=70000` → **HTTP 503 / Unhealthy**；还原后 → 200 / Degraded |
| ③' 空 host `host=`（真实进程） | 200 + Degraded | **通过**：200，`redis 连接串没有可用 host` |
| ② 把 `Healthy` 返回值改成 `Degraded`（模拟探活永远降级） | 脚本 Healthy 腿必须红 | **通过（红）**：脚本 exit 1，报「根状态是 Degraded，期望 Healthy」「redis 是 Degraded，期望 Healthy」 |
| ④ 启动期 `LogWarning` 降为 `LogDebug` | 降级路径缺证据必须红 | **通过（红）**：脚本 exit 1，「启动日志里没有 'Degraded mode:' 警告行」 |

结论：②③④ 三条门禁都是**真门禁**，不是装饰。

## 4. 缺陷

### F-03-A1 · **high · 必修** · 一键脚本不校验「5080 是自己起的」，会把别人的进程当成被测对象（已实证假绿）

- **位置**：[`scripts/verify-apphost.ps1:188-217`](../../scripts/verify-apphost.ps1#L188-L217)（启动后只轮询 URL）、[:269-277](../../scripts/verify-apphost.ps1#L269-L277)（后续断言直接用 `$ApiBaseUrl`）
- **问题**：脚本对「5080 谁在监听」零校验。子进程一启动就进 `Wait-ForUrl`，只要 5080 上有**任何**返回 200 的 API，脚本就把它当成本次启动的产物继续断言，子进程是否活着、是否绑定成功都不看。
- **为什么错**：这是**证据链断裂**，不是措辞问题。我实测复现：
  1. 先用正常连接串在 5080 起一个 API（`/health/ready` 全 Healthy）；
  2. 导出 `Outbox__MaxAttempts=0`（票 07 的启动期校验，进程必秒崩），再跑 `-Mode api`；
  3. 脚本自己那个进程输出 **0 字节**（`artifacts/verify-api.log` 与 `.log.err` 都是 0），根本没起来；
  4. 脚本仍然打出「根状态 = Healthy、mysql = Healthy、redis = Healthy、`/api/version` …」，**`exit 0` 验收通过**。
  这也正好解释了两周里「DCP 可启动」的反复：端口被残留 API 顶着时，任何「起不来」的宿主都能被测成「起来了」。票 11 的一键验收要建立在同一脚本上，这条不修就是**把假绿写进最终验收**。
- **修法**（三选一，建议 ①+② 一起）：
  1. 启动前断言端口空闲：`Test-NetConnection`/`Get-NetTCPConnection -LocalPort 5080 -State Listen` 命中即 `throw "5080 已有进程（PID …），先停掉或换端口"`；aspire 模式同时检 5199。
  2. 启动后立刻看子进程：`if ($hostProcess.HasExited) { throw "宿主在启动阶段退出（exit $($hostProcess.ExitCode)），见 $logPath.err" }`，并在 `Wait-ForUrl` 每轮循环里重复检查。
  3. 更彻底：给脚本加 `-Port` 参数，默认取一个临时空闲端口（如 `Get-Random -Minimum 5100 -Maximum 5199`），从根上不可能撞车。

### F-03-A2 · **medium · 必修** · 脚本用 `dotnet run --no-build` 但从不构建

- **位置**：[`scripts/verify-apphost.ps1:192`](../../scripts/verify-apphost.ps1#L192)
- **问题**：干净副本上没有任何 `bin/`，脚本第一次跑必然 `--no-build` 失败（宿主起不来 → 走到 F-03-A1 的假绿分支或超时）。
- **为什么错**：脚本承诺的是「一条命令验收」，实际隐含前置「先手工 `dotnet build`」；更坏的一面是**验到过期二进制**——工作区有未提交改动时，`--no-build` 跑的是上次编译的产物，报告里的 hash 与二进制不对应。我在 worktree 里正是因为先手工 build 过才跑通。
- **修法**：启动前显式 `dotnet build $target -warnaserror`（失败即 exit 1），或直接去掉 `--no-build`；若保留 `--no-build`，至少在日志里打印被验 DLL 的写入时间并断言它晚于最近一次提交时间。

### F-03-A3 · low · `/health/ready` 未认证就把异常原文回给客户端

- **位置**：[`src/Pzd.Api/Health/MySqlHealthCheck.cs:42`](../../src/Pzd.Api/Health/MySqlHealthCheck.cs#L42)、[`RedisHealthCheck.cs:82`](../../src/Pzd.Api/Health/RedisHealthCheck.cs#L82)、[`HealthResponseWriter.cs:23`](../../src/Pzd.Api/Health/HealthResponseWriter.cs#L23)
- **问题**：`description` 直接是 `exception.Message`，MySQL 认证失败时会得到 `Access denied for user 'root'@'127.0.0.1' (using password: YES)` 这类原文；端点免租户、无鉴权，任何能连到 5080 的人都能读到主机名、库名、账号。
- **建议**：对外只给固定摘要（如 `mysql 不可用`），把原文降为服务端 Debug/Warning 日志；或按环境区分（非 Development 脱敏）。

### F-03-A4 · low · `local.env` 的 `PZD_REDIS_PASSWORD` / `PZD_REDIS_DEFAULT_DB` 被脚本丢弃

- **位置**：[`scripts/verify-apphost.ps1:86-90`](../../scripts/verify-apphost.ps1#L86-L90)
- **问题**：只拼 `host:port`，不拼密码。本机 Memurai 无密码所以现在没事；一旦 Redis 开了密码，Healthy 腿会莫名判红，且报错指向「redis 应答异常」而不是「没带密码」。
- **建议**：拼成 `host:port,password=…`（脚本已在内存里，不会回显），或在使用这两个键时打印一条提示。

### F-03-A5 · low · 启动探活超时会被写成「不可用」，措辞误导

- **位置**：[`src/Pzd.Api/Program.cs:93-114`](../../src/Pzd.Api/Program.cs#L93-L114)
- **问题**：启动期 `CheckHealthAsync` 用 10s `CancellationTokenSource`；超时后该项变成 `Unhealthy`，`Description` 是 `A task was canceled.`，于是日志打出 `Degraded mode: MySQL unavailable（A task was canceled.）`——把「慢」说成「不可用」。另外该 CTS 没有 `using`。
- **建议**：超时单独一句（`Degraded mode: MySQL timeout（10s）`），CTS 用 `using var`。

### F-03-A6 · low · Redis 探活只读一次，AUTH 场景可能误判

- **位置**：[`src/Pzd.Api/Health/RedisHealthCheck.cs:59-73`](../../src/Pzd.Api/Health/RedisHealthCheck.cs#L59-L73)
- **问题**：`AUTH` + `PING` 一起写、只 `ReadAsync` 一次；若两次应答分两段到达，首读只有 `+OK`，`Contains("+PONG")` 为假 → 误报 Degraded（健康实例被判降级）。本机无密码路径不受影响。
- **建议**：循环读到 `\r\n` 结束，或读到含 `+PONG` / `-` 开头错误为止。

### F-03-A7 · low · `Remove-StaleDcpState` 会杀掉机器上所有 `dcp` / `Aspire.Dashboard`

- **位置**：[`scripts/verify-apphost.ps1:117-124`](../../scripts/verify-apphost.ps1#L117-L124)
- **问题**：审计/并行开发时，谁的 Aspire 会话开着都会被无差别杀掉；这也是「别人进程顶住 5080」的另一半成因。
- **建议**：只清理本仓库 TEMP 会话目录与端口占用者，或杀之前打印 PID + 命令行并要求 `-Force` 才动手。

## 5. Aspire 模式的状态（我这边的事实）

- `pwsh -NoProfile -File scripts/verify-apphost.ps1 -SkipWeb -TimeoutSeconds 90` → **exit 0**，`/health/live` 200、`/health/ready` 根 + mysql + redis 全 Healthy、`/api/version` 正确、`/` 400。也就是说 **DCP 在我的终端里能起来并拉起 API**，与李总「F-03-1 是端点缺陷」的判断一致。
- AppHost 日志里 **仍然有**旺总报的那条 `Failed to enable orchestration logging … SetSecurityInfo … UnauthorizedAccessException`（DCP 的日志套接字 DACL），但它**没有**演进成 `EnsureKubernetesAsync` 两分钟超时——api 资源照常起来。说明该异常是**噪声级**，不是「受限令牌下 DCP 必然起不来」。
- 前端腿（5199）我这边没验成，原因见 §2 第 6 条（pnpm 拒绝在联结目录上跑），**不记为缺陷**；需在正常副本上补一次全链路。

## 6. 结论与必修清单

**需修改。** 必修两项：

1. **F-03-A1（high）**：脚本加「端口空闲前置 + 子进程存活检查」，杜绝拿别人的进程当被测对象。这是唯一一条会**污染最终验收结论**的缺陷。
2. **F-03-A2（medium）**：脚本自举构建（或去掉 `--no-build` 并断言二进制新于提交）。

其余 5 条 low 可随手修或登记；`/health/ready` 的 `Healthy/Degraded` 三态口径、②③④ 三条门禁、免租户豁免、版本一致性**均判通过**。

前置提醒（给票 11）：这个脚本是票 11 一键验收的底座，A1 不修，票 11 的「通过」同样可能是别人的进程给的。

> 待补证据：aspire 全链路（含 5199 前端腿）在正常副本上跑一次 `pwsh -NoProfile -File scripts/verify-apphost.ps1`，贴完整输出。老卫收口前必须看到这条，或用 `-Mode api` 的结论并注明「aspire 前端腿未验」。

## 7. 复审进度（`d13e58d`，2026-10-27 17:5x，未结）

旺总在 `7c5f411` 之后又落了 `e645123`（前端固定 5199 并关代理）、`f2d49b8`（前端目录改绝对路径）、`d13e58d`（前端失败打诊断）。我按当前 HEAD `d13e58d` 复查 A1/A2：

| 项 | 状态 | 证据 |
|---|---|---|
| **A1** 端口被别人占用 → 假绿 | **主体闭环** | 新增 `Assert-PortFree`（[:191-219](../../scripts/verify-apphost.ps1#L191-L219)）：我占一个 5099 监听后跑 `-ApiBaseUrl http://127.0.0.1:5099`，脚本 `exit 2` 并打出 `端口 5099 已被占用（pid 9636 pwsh）`，没有再拿别人的进程当被测对象 |
| **A1'** 子进程存活检查 | **未做** | `Wait-ForUrl` 循环仍不看 `$hostProcess.HasExited`；子进程在 `--no-build` 失败/端口竞态下退出时，失败表现为 240s 超时而不是「宿主启动阶段已退出」，诊断信息弱（降为建议项，不阻塞） |
| **A2** `--no-build` 不自举 | **未修** | [:225](../../scripts/verify-apphost.ps1#L225) 仍是 `dotnet run … --no-build`，脚本自身不 build；干净副本必失败、脏工作区会验过期二进制 → **仍为必修** |
| 加分项 | — | aspire 模式新增 `apps/web/node_modules` 前置检查（[:208-213](../../scripts/verify-apphost.ps1#L208-L213)），挡住「没装依赖 → 前端秒失败」的假红 |
| **A8（新增 low）** | 待登记 | 脚本已用 `exit 2` 表示「环境/占用问题」、`exit 1` 表示「验收失败」、`exit 0` 表示通过。票 11 的判定按退出码，必须把这三档写进票面与验收脚本，否则 CI 会把环境问题当产品缺陷（或反之） |

**复审结论（暂）：仍为需修改**，唯一硬项是 A2。A1 的假绿通道已封死，我实测复现不了。A2 修好后我做最终复审并出「通过」。

## 8. 终审（`6f18a9f`，2026-10-27 17:20，需修改：只剩 1 项）

`6f18a9f`（`fix(scripts): 票 03 审计 F-03-A1/A2`）在独立 worktree `E:\pzd-final03` 上复核。

| 项 | 命令 / 动作 | 结果 |
|---|---|---|
| 构建 | `dotnet build Pzd.slnx -warnaserror` | 0 警告 0 错误 |
| 测试 | 五个工程逐个跑 | 35 + 30 + 86 + 43 + 10 = **204 条**，0 失败（集成 10 条中 8 条因该 worktree 无凭据 Skip，属预期） |
| `-Mode api` 正常路径 | `-ApiBaseUrl http://127.0.0.1:5099 -SkipWeb` | **exit 0**；根 + mysql + redis 全 Healthy；`/api/version` = `0.1.1-alpha.0.118+6f18a9f`；`/` → 400 |
| `-Mode api -NoCredentials` | 同上加 `-NoCredentials` | **exit 0**；三项 Degraded 200；日志两条原文齐全 |
| **A1** 端口被占 | 用 TcpListener 占住 5099 再跑 | **exit 2**，`[preflight] 端口 5099 已被占用（pid 38340 pwsh）` ✅ |
| **A1'** 宿主秒崩 | `Outbox__MaxAttempts=0` | **exit 1**，`启动的宿主进程已退出（exit code -532462766），http://127.0.0.1:5099/health/live 不是它提供的` ✅ 不再等满超时 |
| **A2** 自举构建 | 观察输出 | 启动前打印 `构建 src/Pzd.Api/Pzd.Api.csproj（-warnaserror）` 并执行，`--no-build` 只用于启动 ✅ |
| low A3/A4/A7 | 读码 | `PZD_REDIS_PASSWORD` 已接入（[:89-90](../../scripts/verify-apphost.ps1#L89-L90)）、`Remove-StaleDcpState` 已按 `CommandLine` 含本仓库路径过滤（[:125](../../scripts/verify-apphost.ps1#L125)） ✅ |

### F-03-A9 · medium · 必修（一行改动）· 构建失败被判成「环境问题」

- **位置**：[`scripts/verify-apphost.ps1:237-241`](../../scripts/verify-apphost.ps1#L237-L241)
- **问题**：显式构建失败后走 `exit 2`，提示前缀是 `[preflight]`（该前缀的含义是「环境/占用问题，不是本票缺陷」）。
- **实测**：我在 worktree 里放一个语法错误文件（`int y = ;`）→ 脚本输出 `[preflight] 构建失败（src/Pzd.Api/Pzd.Api.csproj），先修编译再验票。` 并 **exit 2**。
- **为什么错**：老何已在 `756c1fc` 把三档定死——`1 = 构建、启动或验收执行失败`、`2 = 环境预检受阻`；票 11 按 `1 > 2 > 0` 汇总。现在编译不过会被归成「环境受阻」，CI/交接会把它当机器问题跳过，而它恰恰是最硬的代码缺陷。退出码非零所以不会假绿，但**分诊错了**。
- **修法**：该分支改 `exit 1`，提示改成 `[build] 构建失败（…），先修编译再验票`（不要用 `[preflight]` 前缀）。

**终审结论：需修改，唯一项 F-03-A9。** A1、A2 及 A3/A4/A7 三条 low 均判闭环；A5/A6/A8 仍为登记项。A9 改完（一行）我直接出「通过」，不需要再跑全量。

## 9. 终审通过（`f778a32`，2026-10-27 17:2x）

`f778a32`（`fix(scripts): 构建失败归退出码 1`）+ `ac20713`（监听者血缘核对）在独立 worktree `E:\pzd-a9` 上复核，全部通过。

| 项 | 实测 | 结果 |
|---|---|---|
| 构建 | `dotnet build Pzd.slnx -warnaserror` | 0 警告 0 错误 |
| 测试 | 五个工程逐个跑（该 worktree 已备 `local.env`，集成测试**真跑**） | 35 + 30 + 86 + 43 + **10** = **204 条，0 失败、0 Skip** |
| `-Mode api` 正常路径 | `-ApiBaseUrl http://127.0.0.1:5099 -SkipWeb` | **exit 0**；`[ok] http://127.0.0.1:5099 由本次启动的进程树监听（pid 35500）`；三项 Healthy；`/api/version` = `0.1.1-alpha.0.123+f778a32`；`/` 400 |
| `-Mode api -NoCredentials` | 同上加 `-NoCredentials` | **exit 0**；血缘核对通过；三项 Degraded 200；两条 `Degraded mode:` 原文 |
| **A9** 构建失败 | 放入语法错误文件 `int y = ;` | **exit 1**，`[fail] 构建失败（src/Pzd.Api/Pzd.Api.csproj）：先修编译再验票。` ✅ 不再占环境预检的 2 |
| **A1** 宿主秒崩 | `Outbox__MaxAttempts=0` | **exit 1**，`启动的宿主进程已退出（exit code -532462766），…/health/live 不是它提供的` ✅ |
| **A1** 端口被占 | TcpListener 占住 5099 | **exit 2** ✅ |
| **A1** 正面证据 | 正常路径 | 拿到 200 后沿 `ParentProcessId` 核对监听者血缘，输出 `[ok] … 由本次启动的进程树监听` ✅ |

**结论：票 03 通过（`f778a32`）。** F-03-A1 的正反两面（外人进程 → `exit 2`；宿主崩 → `exit 1`；正常 → 血缘核对 `[ok]`）与 F-03-A2（显式构建 + 构建失败归 1）全部落实，我实测复现原假绿路径已不可达。A5/A6/A8 三条 low 保留登记。

范围外仍需补的一次证据（不计入本票结论文档门槛，但老卫收口时应注明）：**aspire 全链路（含 5199 前端腿）在带 `node_modules` 的正常副本上跑一次**；我的审计副本两次都没走完这一腿（第一次因目录联结被 pnpm 拒绝，第二次为避开端口冲突用了 `-SkipWeb`），不写成已通过。

> **补充（同日）**：李总在 `6f18a9f` 的干净副本上跑通了 aspire 全链路——`/health/live` 200、`/health/ready` 根 + mysql + redis 全 Healthy、`/api/version` buildId `6f18a9f`、`/` 400、**前端 `127.0.0.1:5199` 200 且是 SPA 外壳**、退出码 0，起跑时无 preflight 告警（5080/5199 当时空闲）。`6f18a9f → f778a32` 的脚本差异只有「构建失败 exit 1」一行，不经过 aspire 前端路径，故该结论可沿用。**这条证据属测试方，不是我复跑的**，报告如实区分。
>
> 另登记 **F-03-A10（原文：low；李总实测复现后**升为 medium，票 11 开工前必修**）**：web 腿只断言 `5199 → 200 + lang="zh-CN"`，未像 API 腿那样核对监听者是否属于本次进程树。李总在 `7c5f411` 上实测到完整假绿：他自己的 vite 报 `Port 5199 is already in use` + `ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL` 失败，脚本仍打印 `[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳` 并 **exit 0**——探到的是别人的实例。修法：`$webProcess.HasExited` + `verify-web.log.err` 命中 `already in use`/`ERR_PNPM` 即失败；5199 的占用 pid 必须属于本次 `$webProcess` 子树；或探活带本次运行独有标记。李总报告里编号为 **F-03-A3**，与本条同指一事。
>
> 这一条**不影响票 03 的通过结论**：票 03 的 web 验收证据来自真起 vite + 归属证明（`verify-web.log` 尾部 `Local: http://127.0.0.1:5199/`、err 无冲突、退出后无残留），不是靠脚本那句 `[ok]`；但脚本本身在票 11 复用前必须补齐，否则票 11 的「一键通过」同样可能是别人的进程给的。

---

## 10. F-03-A10 复审补充（2026-10-27 17:40，老登）

被审对象是**工作区未提交版本**（`scripts/verify-apphost.ps1`，HEAD 为 `931d700`），因为 A10 的修正在此之前未入库。`8fbbed9` 版本里 web 腿仍只有 `200 + lang="zh-CN"` 两段断言。

### 10.1 已实测通过：外来监听被正确拒绝

19:40 那次实测（`-Mode api -ApiBaseUrl http://127.0.0.1:5098`，含前端）撞上了真实的外来监听，结果正确：

```
[ok] http://127.0.0.1:5098 由本次启动的进程树监听（pid 124276）
...
验收失败：
 - 前端 127.0.0.1:5199 的监听者 pid 117968 不属于本次启动的进程树（祖先 pid 91036），200 不是本次实例给的
[exit code: 1]
```

定性证据不是脚本的自述，而是它自己的前端进程日志：

```
$ react-router dev
Error: Port 5199 is already in use
[ELIFECYCLE] Command failed with exit code 1.
Error: ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL
```

即本次 `pnpm dev` 根本没起来，5199 上是别人的实例——**脚本没有把别人的 200 当成自己的**。这满足票面 `299421d` 第 ② 条「占住 5199 跑带前端路径必须非 0」。

### 10.2 必修（high）：取不到监听 pid 时反而判通过

```powershell
$webAncestor    = if ($Mode -eq "aspire") { $hostProcess.Id } elseif ($webProcess) { $webProcess.Id } else { $null }
$webListenerPid = Get-PortListenerPid 5199
if ($webAncestor -and $webListenerPid -and -not (Test-ProcessDescendsFrom -ChildPid $webListenerPid -AncestorPid $webAncestor)) {
    $failures.Add("前端 … 的监听者 pid $webListenerPid 不属于本次启动的进程树（祖先 pid $webAncestor），200 不是本次实例给的")
}
else { Write-Host "[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳（监听者 pid $webListenerPid 属本次进程树）" }
```

`$webAncestor` 或 `$webListenerPid` 为 `$null`（`Get-NetTCPConnection` 不可用、查询竞态、前端刚退出、aspire 模式 `$hostProcess` 为空）时，整个 `-and` 短路为假，**直接落到 `else` 打印 `[ok]`**。这正好违反张一一第 ③ 条验收「PID/父链无法确认也必须失败」，而且它是最隐蔽的一种：越是异常状态，越报绿。

修法：把判定反过来，只有三项全部成立且血缘为真才通过，其余一律 `$failures.Add(...)`：

```powershell
if (-not $webAncestor) { $failures.Add("前端归属无法确认：本次前端进程句柄为空（模式 $Mode）") }
elseif (-not $webListenerPid) { $failures.Add("前端归属无法确认：5199 上取不到监听 pid") }
elseif (-not (Test-ProcessDescendsFrom -ChildPid $webListenerPid -AncestorPid $webAncestor)) { $failures.Add(...) }
else { Write-Host "[ok] ..." }
```

### 10.3 未验证（阻塞我判 A10 通过）：api 模式下以 `$webProcess.Id` 作祖先可能误红自己的前端

`$webProcess` 是 `Start-Process pnpm.cmd` 的返回值，即 **cmd.exe 垫片**。Windows 上垫片在 node 起来后可能退出，node 被 reparent，`Test-ProcessDescendsFrom(node, pnpmPid)` 就会返回 false——此时脚本自己起的前端反被判成外来实例，是**误红**（比假绿好，但仍不可接受，票 11 会天天撞）。

本次无法验证：5199 全程被别人的受控反例假服务器占着（见 §10.4），脚本的前端从未成功启动过，正向路径一次都没跑到。

建议同时接受两种判据（李总 §12.3 的口径）：祖先匹配 **或** 监听进程命令行含本次副本的 `apps/web` 绝对路径。后者不受垫片/reparent 影响，且顺带覆盖 aspire 模式。

### 10.4 环境事实

实测期间 5199 被 pid `92300` 占用，命令行是 `node -e "…foreign dev server…foreign listener up on 5199…"`，父进程 `bash.exe` pid 74044——是别人正在跑的 A10 受控反例，**不是我的，我没动它**（票面 第 ② 条明确不许杀外来进程）。

---

## 11. F-03-A11 登记：aspire 模式下 5080 归属断言必然误红（票 11 门禁第 2 项）

### 11.1 现象（依据李总实测，含命令行证据；我未独立复现）

`ac20713` 给 5080 加的归属断言要求「监听 5080 的进程是本次宿主的后代」。但 `-Mode aspire` 下 **5080 由 Aspire 的端口代理 `dcp.exe` 持有**，不是 API 进程自己监听：

```
dcp.exe run-controllers --kubeconfig C:\tmp\pzd-asp4\.aspire-tmp\aspire-dcp*\kubeconfig …
父链：dcp.exe → dcp.exe → 已退出（reparented）
```

父链在第二跳就断了，`Test-ProcessDescendsFrom` 永远返回 false → **aspire 模式必然 `exit 1`**，即使功能和日志全部达标。

### 11.2 定性

**工具误红，不是产品缺陷。** 李总在 `f2d49b8` / `e7200cb` 上两次 `-Mode aspire` 的端点证据（`/health/live` 200、`/health/ready` 三项 Healthy、`/api/version` 回显对应 hash、`/` 400、5199 首屏 SPA + 监听者命令行含本次副本 `apps/web`）都是真的，红的是脚本自己的判据。这与 A10 ③ 恰好相反：**A10 ③ 是该失败时判通过，A11 是该通过时判失败**。

### 11.3 票 11 开工门禁（现在两条，缺一不可）

| 编号 | 缺陷 | 方向 | 证据 |
| --- | --- | --- | --- |
| F-03-A10 | `$webAncestor -and $webListenerPid -and …` 任一为空即短路到 `else` 打印 `[ok]` | 该失败却通过 | 我实测：变异让 `Get-PortListenerPid 5199` 返回 `$null` → `[ok] …（监听者 pid  属本次进程树）`、`EXIT=0` |
| F-03-A11 | aspire 模式 5080 由 `dcp.exe` 代理持有，父链断言必然为假 | 该通过却失败 | 李总实测命令行为证；我未独立复现 |

`299421d` 已把 A10 的四条验收挂到票 11，**A11 尚未入任何票面门禁**，建议一并写入，否则票 11 的「一条命令给出唯一证据」在 aspire 模式下永远非 0。

### 11.4 修法建议：抽成统一的归属判定，别在每个分支里拼 `-and`

两个缺陷同源——**归属判据散落在各自分支里、靠一串 `-and` 短路**，所以一边漏判、一边错判。建议收成一个函数，按模式取不同的正证据，且**取不到证据即失败**：

```
Assert-Owner（模式, 端口, 宿主/前端句柄）:
  api     : 血缘（监听 pid 是 $webProcess / $hostProcess 的后代）
  aspire  : kubeconfig 路径匹配 $repoRoot\.aspire-tmp\aspire-dcp*\kubeconfig
            或 /api/version 的 buildId == 本轮 hash
  任一模式都保留：命令行含本次副本绝对路径（不受垫片/reparent 影响）
  三个证据全取不到 → 失败，不得 [ok]
```

这样 A10 ③ 与 A11 一起闭环，且以后新增模式只需加一条判据。

---

## 12. 状态更新（`398b6e8`）：A10 四条全部闭环，但 API 腿有同款缺陷（F-03-A12）

> 本节覆盖 §11 的 A10 状态。A10 已在 `398b6e8` 闭环；A11 仍开着；另发现 A12。

### 12.1 A10 闭环确认（`398b6e8`，我实测）

`398b6e8` 把前端归属改成三段式，并联查 `.err` 与 `HasExited`。我用同一个变异（让 `Get-PortListenerPid 5199` 返回 `$null`）对照两个版本：

| 版本 | 实测输出 | 退出码 |
| --- | --- | --- |
| `7ccbbcf` | `[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳（监听者 pid  属本次进程树）` | **0** |
| `398b6e8` | `前端 127.0.0.1:5199 取不到监听者 pid，无法确认 200 是否来自本次实例（祖先 pid 35128）` | **1** |

票面 `299421d` 的四条验收现已全部有实测：① 正向 `[ok] …（监听者 pid 28416 属本次进程树）` exit 0；② 外来监听 exit 1（`.err` 有 `Port 5199 is already in use`）；③ pid 取不到 exit 1（上表）；④ 预检后才出现外来监听，即 ② 那次的实际形态。**A10 闭环。**

### 12.2 新发现 F-03-A12（high）：API 腿仍是老写法，pid 取不到即放过

`398b6e8` 只修了 web 腿，5080/api 那条腿没同步：

```powershell
$listenerPid = Get-PortListenerPid ([uri]$ApiBaseUrl).Port
if ($listenerPid -and -not (Test-ProcessDescendsFrom -ChildPid $listenerPid -AncestorPid $hostProcess.Id)) {
    $failures.Add("… 不属于本次启动的进程树 …")
}
else {
    Write-Host "[ok] $ApiBaseUrl 由本次启动的进程树监听（pid $listenerPid）"
}
```

与 A10 ③ 修之前一模一样的短路。变异实测（`Get-PortListenerPid 5096` 返回 `$null`，其余不动，`-SkipWeb`）：

```
[ok] http://127.0.0.1:5096 由本次启动的进程树监听（pid ）
验收通过（/health/ready 期望模式：Healthy）：…
REAL_EXIT=0
```

`pid` 后是两个空格——没查到 pid，却断言"由本次启动的进程树监听"，还宣告验收通过。**这条比 A10 ③ 更危险**：A10 ③ 只影响前端腿，而 API 腿是每一轮 `-Mode api` / `-Mode aspire` 都会走的主判据，也是票 11 唯一的 200 归属证据。

修法：照 web 腿的三段式原样搬过来——`-not $listenerPid` 单独一支失败，`-not (Test-ProcessDescendsFrom …)` 再一支，其余才 `[ok]`。

### 12.3 A11 仍在

5080 的 aspire 分支没改判据，`dcp.exe` 代理持有 5080 的父链断裂问题（§11.1）照旧。**api 腿修 A12 时不要顺手把 aspire 分支一起改错**——两者根因不同：api 是"取不到证据放过了"，aspire 是"证据取错了对象（代理进程不是 API 进程）"。

### 12.4 票 11 开工门禁（更新）

| 编号 | 缺陷 | 方向 | 状态 |
| --- | --- | --- | --- |
| F-03-A10 | web 腿 pid 取不到即 `[ok]` | 该失败却通过 | **已闭环 `398b6e8`** |
| F-03-A12 | API 腿 pid 取不到即 `[ok]` | 该失败却通过 | **已闭环 `562fd88`**（复验：变异后 `exit 1`，报「取不到监听者 pid，无法确认 200 是否来自本次实例」） |
| F-03-A11 | aspire 模式 5080 由 dcp 代理持有 | 该通过却失败 | **未修** |

建议 A12 与 A11 同一批修，并照 §11.4 抽成统一函数，避免第三次漏一支。

### 12.5 A12 闭环复验（`562fd88`）

`562fd88` 把 API 腿改成与 web 腿同口径的三段式。同一个变异（`Get-PortListenerPid 5095` 返回 `$null`，其余不动，`-SkipWeb`）在两版上的对照：

| 版本 | 实测输出 | 退出码 |
| --- | --- | --- |
| `b4b5d5d` | `[ok] http://127.0.0.1:5096 由本次启动的进程树监听（pid ）` | **0** |
| `562fd88` | `http://127.0.0.1:5095 取不到监听者 pid，无法确认 200 是否来自本次实例（宿主 pid 107072）` | **1** |

**A12 闭环。** 票 11 门禁现在只剩一条：**A11**。

### 12.6 A11 的状态在这一版更清楚

`562fd88` 只补了「取不到 pid」那一支，`[:316](E:\pzd\scripts\verify-apphost.ps1#L316)` 仍是 `Test-ProcessDescendsFrom -ChildPid $listenerPid -AncestorPid $hostProcess.Id`。aspire 模式下这个检查**能取到 pid，但对象是 `dcp.exe` 代理**，所以会稳定落进 `:317` 报「不属于本次启动的进程树」——**误红**，不是"取不到"。

所以 A11 的修法是换判据，不是补分支：aspire 分支用 kubeconfig 路径匹配，或 `/api/version` 的 buildId 等于本轮 hash。补分支解决不了它。

> **本节修订（2026-10-28）**：上面这个修法建议**已作废**，两条都被否掉了——kubeconfig 路径匹配挡不住同目录残留实例；`buildId` 只能证明版本相同，排除不了同 hash 的另一个实例。最终采用的是响应头 `X-Pzd-Run-Id`，见 §13.2。

---

## 13. 终审结论（2026-10-28）：票 03 / 08 / 09 全部通过

> 本节是最终结论。§1–§12 是递增的复审过程，其中出现的「需修改」「未修」均已在对应小节标注闭环；阅读时以本节为准。

### 13.1 结论一览

| 票 | 结论 | 锚（代码 + 脚本） | 边界 |
| --- | --- | --- | --- |
| 03 主路径 | 通过 | `f778a32`（§9） | — |
| 03 · F-03-A10 | 闭环 | `398b6e8`（§12.4） | — |
| 03 · F-03-A12 | 闭环 | `562fd88`（§12.5） | — |
| 03 · F-03-A11 | **通过（主仓正向）** | `affad87` + 脚本 sha256 `eff9a210…96db162d`（副本树旧锚 `db17a9b` 并列保留） | 环境绕行，见 13.5 |
| 03 · A5（entry TDZ） | **通过** | `331fac3` | 仅缩短值超时实跑 |
| 08 | 通过 | `e8f2e2d` | 三条覆盖边界，见 13.4 |
| 09 | 通过 | `68f0bef` + 当前脚本 | — |

### 13.2 A5 与 A11

**A5（`apps/web/app/entry.server.tsx`）通过 @ `331fac3`。** 我逐行核过交付文件，不是照转述：

- 三处 `clearTimeout` 覆盖所有出口：`:91` 成功路径 `final`、`:111` `onShellError`、`:129` 同步 catch 兜底；
- 定时器挪到 `renderToPipeableStream` 之后（`:144`），`abort` 提前声明为 `| undefined`（`:74`），同步抛出时定时器压根没挂上——TDZ 不可达；
- 变异残留 `pzd-probe` 计数 **0**；我实建 `Pzd.AppHost.csproj` **0 警告 0 错误**（那行 `GetRequiredService<IHostEnvironment>()` 的 using 没漏）。

**根因封口**，三块证据分工，成品口径：

| 环节 | 证据 | 强度 |
| --- | --- | --- |
| 宿主环境默认 `Production` | `ce08feb` 在正式 `Pzd.AppHost.exe` 上读出 `宿主环境=Production`、两个 `*_ENVIRONMENT` `<unset>` | 运行期读数 |
| `DOTNET_ENVIRONMENT=Development` → `EnvironmentName=Development` | 不启 DCP 的 15 行探针程序 | 实测，不在正式 AppHost 内 |
| 宿主环境 → web 的 `NODE_ENV` | `pzd-envtest`（pin=0 + Development → `development` + `EXIT=0`）／反向 `pzd-fin`（`f6b32412`，`_out:1` = `production`）／第三份 `331fac3-nopin` | 进程级读数各一次 |

链条：宿主环境 `Production` → Aspire JS 托管集成在**AppHost 装配资源模型时（运行期，`CreateBuilder` 之后、DCP 启动之前）**按 `IsDevelopment()` 写 `NODE_ENV=production` → `jsxDEV` 缺失 → 调用点同步抛出 → 默认 entry 的 `abort` 停在 TDZ → 6 秒后 exit 1。

两条已知边界，不往高写：① 写入发生在「装配资源模型期」还是「DCP 运行时」仍未区分；② JS 集成内部那个 `IsDevelopment() ? … : …` 是元数据反推、未反编译 IL。二者都不影响 pin 的必要性。

**`pin`（`37bd872`，`src/Pzd.AppHost/Program.cs:85`）无条件保留。** 三条理由：只影响 `web` 一个资源；宿主环境改成 Development 时确实会绿，但删掉后任何宿主环境为 Production 的场景（CI、容器、别人机器）立刻复发；反对用 `launchSettings.json` 根治（会把整个 AppHost 带进 Development，翻转所有 `IsDevelopment()` 分支）。

**A11 通过（主仓正向）@ `affad87`。** 主仓 `E:\pzd` 上完整验收 `EXIT=0`、**8 条 `[ok]` 全绿**，原文入库 `docs/reports/verify-apphost-main-affad87.log`（21 行，GBK）：`/api/version 0.1.1-alpha.0.237+affad87`、`X-Pzd-Run-Id 与本次运行一致（1a345d7f…）`、前端 `127.0.0.1:5199` 同 runId（pid 3608）且为 SPA 外壳、`/` 无租户仍 400。

升档三条件逐条核过（不是照转述）：

1. 三条判据齐：版本三字段、前端头同 runId、`/` 回 SPA 外壳；
2. 脚本**先入库再引用**：sha256 `eff9a210cb6fb1b17e4e426e4392ebecbaab18fa52b8462349d3e38596db162d`，与 `git show affad87:scripts/verify-apphost.ps1` 及工作区三方逐字节一致；
3. 单变量仍是 exe 位置：同一份 `bin/Debug/net10.0` **原样复制、不重 build**，cwd 仍 `src/Pzd.AppHost`，日志自报「树内 exe 带受限完整性标签 → 树外副本」。

先前锚点 `db17a9b`（副本树，runId `f730b9e2…`、pid `22096`）**不撤**，作为跨树第二份并列保留。后续主仓 aspire 一律走 `affad87` 起的自适应分支（判据：树内 exe 完整性标签非 Medium/High 才走树外副本）；`-Mode api` 路径未变，08/09 证据不受影响。

**文档口径已对齐 @ `9ce3048`**（`9f613b7` 清被取代的两处旧说法、`9ce3048` 改摘要行）。我全量扫过，不是照转述：`README.md` 与 `docs/reports/` 下「等用户拍板 / 待用户批 / 唯一未定的是主仓 / 没有 WRITE_DAC / 改不了自己的 ACL」**0 命中**；`README:26` 与 `handover:19` 现文均为「主仓正向 `affad87` 已拿到、不改权限、去标签只是可选根治（需管理员特权）」。`仍不通过` 仅剩 `handover:249` 一处，带「上一轮快照、已被取代」标记，留作追溯可接受。**至此 `review-03` 与 `handover`、`README` 三处口径一致。**

### 13.3 票 08 / 09

**08 通过 @ `e8f2e2d`**（不是 `79bbe36`——那一轮是旧契约自洽，不能当新契约的通过证据）。229 条全绿、五条安全头齐全、两条降级日志可按资源名 grep、`/` 无租户 400。三条覆盖边界照实登记，其中第二条是交接硬依赖：

1. CSRF 双层 token 只有单测——`src/Pzd.Api` 没有任何写端点，真实 HTTP 面打不出那条 400；
2. **没有任何端点发放 `XSRF-TOKEN` cookie**——登录壳落地时必须同时提供，否则前端所有 POST 一律 400；
3. 审计（`WithAuditLog` / 有界 Channel）同样只有单测。

**09 通过 @ 当前脚本 + `68f0bef`。** 我在当前脚本上复跑：`EXIT=0`、8 条 `[ok]`、`Degraded mode:` **0 条**、`Successfully validated presence of 37 schema objects`、`LocalTransactionJobStore initialized`、`persistence: True, clustered: True`、端口释放。`git diff 6b67c2d..HEAD -- src/Pzd.Api src/Pzd.Infrastructure` 为空，被测代码面未变。

`QRTZ_EXECUTION_HISTORY` 是**可选预留、未接线**：`db/master/quartz_tables.sql:203` 注释写明只有配了 `UsePersistentStore(s => s.UseExecutionHistory())` 才会读写，代码里零命中。所以“建表但 0 行”不是缺陷，写“要用需在 `QuartzRegistration` 加一行”。

### 13.4 四条取证纪律（本轮三次踩坑的产物）

1. **层面要说清**：任何「相同 / 不同 / 有 / 没有」的断言必须点明是**字节**（sha256、`cmp`）、**文本**（`diff --strip-trailing-cr`）还是**语义**（代码是否生效、值是否进了进程）。本轮三个实例全栽在这上面：`grep -c` 数到注释行、日志 GBK/UTF-8 混读、`.bak` 字节不同但文本相同。
2. **全称判断不许用截断结果**：「只有 / 全部 / 唯一 / 0 条」后必须指出它是从哪条完整命令得出的。本轮我自己犯了两次：`grep|head -10` 数出 `jsxDEV` “8 次”（实际 1040）、只看 `_err` 就说“目录里只有一份有内容”（另两个 `_out` 有 10KB / 2.3KB）。
3. **追加写的文件必须按 runId 取**：`artifacts/pzd-web-env.log` 混了 5 轮 10 行，`production` 和 `development` 都在里面，脱离 runId 引用必错。
4. **共享副本的反推禁令**：`mtime` 只记最后一次写入、`git status` 只说改过、`grep -c` 会数到注释——三者都不许反推当轮状态。只能用当轮 `git diff`、或能包住该轮（mtime 区间 + runId + 栈路径 + 单轮）的会话目录；两样都没有才记「不可考」。

第 4 条我本人踩过：曾用 `Program.cs` 的 `mtime 01:47` 反推「00:07 那轮不是注释态」，属同类错误，已收回。

### 13.5 主仓 `E:\pzd` 的环境限制（已定因，绕行已验证）

**根因**：`E:\pzd` 根目录带 `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`——属 **SACL 的强制完整性标签，不是 Deny ACE**——继承给整棵树，树内每个 exe 也带 `(I)(NW)`；**从这类 exe 启动的进程，令牌直接是 Low**。对照：`E:\pzd-webcheck` 与 `E:\` 都无标签行。

后果有三条，**分开记，不合并**：

| 现象 | 原因 | 性质 |
| --- | --- | --- |
| 主仓 aspire 建 DCP 会话目录被拒（`Locations.GetOrCreateDcpSessionDir` → `CreateTempSubdirectory`） | Low 进程写不进 Medium 目录（no write up） | 致命；绕行后已全绿（见 13.2） |
| `dotnet test` 握手失败 `UnauthorizedAccessException @ NamedPipeClientStream.TryConnect`，退出码 `-532462766`（`0xE0434352`） | 同一机制：树内 testhost 是 Low，连 Medium 侧 vstest 的命名管道被拒 | 致命；**成对实验**：同一条命令 `dotnet test tests/Pzd.SharedKernel.UnitTests`，无标签树 `C:\tmp\pzd-db17` → 35/35 通过 `EXIT=0`；`E:\pzd` → 上述失败。唯一变量是树 |
| `SetSecurityInfo` / `CreateSecurityInfo` 被拒 | 令牌缺 `SeSecurityPrivilege` | **非致命**：只是 orchestration logging warning，**Medium 轮里同样出现**，从来不是主因 |

- **不能就地修标签**：`/reset` 删不掉标签；`/setintegritylevel` 要 `SeSecurityPrivilege`，当前令牌没有（`whoami /priv` 查不到）。有效权限实测 `writeDac=true`/`writeOwner=true`，但那只管 DACL，管不到 SACL。备份与回滚就绪（`icacls /save` → `artifacts/acl-backup-pzd.txt`，32,472 项），但**修不修都需要管理员权限**，绕行不需要。
- **绕行（已验证，不动任何权限）**：aspire 把 `bin/Debug/net10.0` 原样复制到树外启动（`d630247` 起自适应）；测试同理，把 `tests/<工程>/bin/Debug/net10.0` 复制到树外跑。
- **我先前那条“`dotnet test` 与会话沙箱有关、与 ACL 无关”已撤回**——它把现象归到了错误的层，现在的 A/B 原文即上表第二行。
- `0xE0434352` 单独出现时只是 .NET 未处理异常的通用退出码，**不单独作因果证据**；因果只挂在上面两个具体调用点上。
- **不影响 §13.1 的四项结论**：08/09 走 `-Mode api`（实测在带标签的树上照常通过，不需要写 Medium 目录、也不需要跨 Medium 侧 IPC）；A5/A11 都在绕行路径上复验过。
