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
