# 测试报告：票 03（Aspire 编排 + 一键验收）

> 被测：**`fd90640`**（`feat(apphost): 票 03 Aspire 编排与本机实例一键验收`）。
> 方式：干净 `git worktree` @ `fd90640`（`C:/tmp/t03`），把不入库的 `local.env` 复制进去（脚本依赖它注入连接串），`dotnet build Pzd.slnx -warnaserror` 后按脚本运行。

## 1. 基线

- `dotnet build Pzd.slnx -warnaserror` → **0 警告 0 错误**。
- 五个测试工程：35 + 30 + 86 + **38** + 1 = **190 条全绿**（`Pzd.Api.UnitTests` 33 → 38，新增 5 条健康检查用例）。

## 2. `-Mode api`（脚本默认）✅

命令：`pwsh -NoProfile -File scripts/verify-apphost.ps1`，**退出码 0**。

| 验收点 | 实测 |
|---|---|
| `/health/live` | 200（免租户） |
| `/health/ready` | 200，`mysql = Healthy`、`redis = Healthy` |
| `/api/version` | `0.1.1-alpha.0.97+fd90640`（`version`/`buildId`/`buildTime` 齐全） |
| `/`（豁免边界） | **400**，无租户不被豁免 |
| 前端 `127.0.0.1:5199` | 200，`lang="zh-CN"` 的 SPA 外壳 |
| 退出后残留 | 5080 / 5199 均无 `LISTENING`，进程树被清掉 |

负向对照：不提供 `local.env` 时脚本**判失败**（`/health/ready 的 mysql 是 'Degraded'`），不会因为缺凭据而假绿。

## 3. 三条必修的实现核对

| 必修 | 代码证据 | 变异/验证 |
|---|---|---|
| `Aspire.Hosting.NodeJs` → `Aspire.Hosting.JavaScript` | `Pzd.AppHost.csproj` 引用 `Aspire.Hosting.JavaScript`；`Program.cs:27` 用 `.AddViteApp("web", "../apps/web", "dev")` | 仓内已无 `NodeJs` 引用 |
| 去掉 EventLog provider（无写权限会抛） | `Pzd.Api/Program.cs:16` `ClearProviders()` + `AddSimpleConsole()`；`Pzd.AppHost/Program.cs:9` 同样清空 | ⚠️ 变异「注释掉 `ClearProviders()`」→ 本机**仍全绿**，无法独立复现该崩溃（当前令牌可写事件日志）。属**防御性修复**，本报告不写「已验证会崩」 |
| MySQL 探活改服务器级 | `MySqlHealthCheck` 构造 `MySqlConnectionStringBuilder{ Database = "" }` 后 `SELECT 1` | 变异「`Database = "pzd_missing_probe"`」→ `/health/ready` mysql **Degraded**、脚本退出失败 ✅ 证明清库名是绿灯的必要条件 |

## 4. 缺资源语义（票面口径）

不注入 mysql/redis 连接串、单起 API（5081）实测：

- `/health/ready` → **HTTP 200**，body `status: Degraded`，`checks.mysql.description = "mysql 未配置…"`、`checks.redis.description = "redis 未配置…"`；
- `/health/live` → 200。

即「实例缺失降级但服务可用、不得 503」成立。

## 5. `-Mode aspire`：❌ 未通过（真缺陷，不是沙箱限制）

### F-03-1 · high · AppHost 端点配置让 DCP 直接抛异常，aspire 模式必失败

实测 `-Mode aspire`：AppHost 启动即退，`/health/live` 60s 超时。`artifacts/verify-apphost.log.err` 首个异常：

```
Unhandled exception. System.AggregateException: (The endpoint 'http' for resource 'api' requested a proxy
(IsProxied is true). Non-container resources cannot be proxied when both TargetPort and Port are specified
with the same value.)
   at Aspire.Hosting.Dcp.DcpModelUtilities.AddServicesProducedInfo...
```

根因在 `src/Pzd.AppHost/Program.cs:22`：

```csharp
.WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http")
```

**一行修法已验证**：改成 `.WithHttpEndpoint(port: 5080, name: "http")`（让 Aspire 自己分配 TargetPort）后重新构建、`-Mode aspire` 重跑，**API 段全部通过**：

```
[ok] /health/live 200（免租户）
[ok] /health/ready mysql = Healthy
[ok] /health/ready redis = Healthy
[ok] /api/version 0.1.1-alpha.0.97+fd90640
[ok] / 无租户仍 400
```

→ DCP 本身能启动、能代理、能编排，票面所谓「DCP 在本沙箱无法启动」不成立，真正阻塞是这行端点配置。

### F-03-2 · medium · 修掉端点上后，`AddViteApp` 的前端资源并未在 5199 起来

同一轮 aspire 运行中，前端等待 90s 超时（`127.0.0.1:5199` 连接被拒）；进程快照里没有 `vite` / `react-router` / `node` 子进程，说明 web 资源根本没被拉起（或拉起后立即退出），不是「起得慢」。

涉及 `Program.cs:27-32`：`.AddViteApp("web", "../apps/web", "dev").WithPnpm(install: false).WithReference(api).WaitFor(api)`。怀疑点（未证实，供开发排查）：Windows 上 DCP 启动 `pnpm` 需要 `.cmd` 垫片；`WithPnpm(install: false)` 在未安装 pnpm 的宿主环境下不会自愈；Vite 端口来自 `vite.config` 而 Aspire 期望通过 `PORT` 注入，二者口径需对齐。

`-Mode api` 下前端（脚本直接用 `pnpm.cmd` 起）是正常的，所以问题范围限于 Aspire 编排路径。

## 6. 结论

- **`-Mode api` 四条验收 + 前端首屏全部通过**，190 条测试全绿，脚本对缺凭据不假绿、退出后无残留监听。
- **`-Mode aspire` 不通过**：F-03-1（high）已定位到具体一行并验证修法有效；F-03-2（medium）前端资源未被拉起，需开发继续。
- **偏差记录需要更正**：foundation §9 写的「Aspire DCP 在本沙箱无法启动（EventLog 无权限为诱因）」与实测不符——DCP 正常；EventLog 修复可以保留（防御性），但不能拿它当 aspire 模式失败的根因。
- 另一个环境坑建议写进 §9：给 **AppHost 进程**设 `ASPNETCORE_URLS=http://…` 会让 Aspire 以「applicationUrl 必须是 https」直接失败（除非同时设 `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`）。现有脚本只对 API 子进程设该变量，所以没踩到。
- 待 F-03-1/F-03-2 修完给 hash，我复测 `-Mode aspire`；`-Mode api` 部分无需重跑。

## 7. 复核 @ `007fe6d`（③④② 三改）

> 干净 worktree @ `007fe6d`，`local.env` 复制进副本。`-Mode api` 一键脚本 **退出码 0**：
> `/health/live` 200、`/health/ready` mysql/redis 均 Healthy、`/api/version 0.1.1-alpha.0.99+007fe6d`、`/` 400、前端 5199 SPA 200。

### 7.1 基线

- `dotnet build Pzd.slnx -warnaserror` → 0 警告 0 错误。
- 五个测试工程：35 + 30 + 86 + **43** + 1 = **195 条全绿**（`Pzd.Api.UnitTests` 38 → 43）。

### 7.2 ③ 缺资源不再 503：三种场景实测

| 场景 | `/health/ready` | 明细 | 启动期日志 |
|---|---|---|---|
| `ConnectionStrings__redis='host='` | **200** | `status: Degraded`；redis `"redis 连接串没有可用 host。"` | `Degraded mode: Redis unavailable（连接串解析出的 host 为空）。` |
| `ConnectionStrings__redis='127.0.0.1:1'` | **200** | `status: Degraded`；redis `"redis 不可用：由于目标计算机积极拒绝，无法连接。"` | 同上格式，含 SocketException(10061) |
| `ConnectionStrings__mysql='…Port=1…'` | **200** | mysql `"mysql 不可用：Unable to connect to any of the specified MySQL hosts."`，redis **Healthy** | `Degraded mode: MySQL unavailable（…）` |

`/health/live` 三种场景都是 200。旧版 `host=` 会 503 `Unhealthy` 的缺陷确已修复。

### 7.3 ② 假绿断言已删

`grep -rn 'Contains(status, ["Healthy", "Degraded"])' tests/` 无命中；`Api.UnitTests` 43 条包含新增的降级用例。

### 7.4 ④ 降级日志有出处，但确有重复（F-03-4，老何已判）

单次启动（2 个资源都不可用）的日志里，每资源出现**两条** `Degraded mode`：

- `<resource>HealthCheck` 类别：`Degraded mode: Redis unavailable（原始异常文本）`
- `Pzd.Api` 类别（启动期 `HealthCheckService` 主动跑一次）：`Degraded mode: Redis unavailable（health check 返回的 description）`

即同一事实由「健康检查类」与「启动期检查」各打一次，文案还不完全一致。另外**每次 `/health/ready` 请求**都会再触发 HealthCheck 类的 Warn 一条——readiness 被轮询时会持续刷日志。建议：健康检查类里降级改用 `LogDebug`（或按状态变化只打一次），启动期那条 Warn 保留为唯一出处。

### 7.5 遗留观察

- redis 不可达时单次 `/health/ready` 耗时约 **2.0s**（连接超时），正常情况 <5ms。票 11 的轮询超时若设得比 2s 还短会误判；建议探针侧加短超时（如 1s）或缓存状态。
- `F-03-1` 在 `007fe6d` **仍未修**：`src/Pzd.AppHost/Program.cs:22` 还是 `WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http")`。旺总工作区里已有未提交的修法，等他提交后我复测 `-Mode aspire`。
- `F-03-2`（`AddViteApp` 前端未在 5199 起来）本轮无新证据，仍在开发侧。

### 7.6 结论

- **③ 闭环**（空 host / redis 不可达 / mysql 不可达三种场景均 200 + Degraded，启动日志有出处）；**② 闭环**（假绿断言已删）。
- **④ 部分闭环**：日志有出处成立，但重复打点未清（F-03-4）；建议按 §7.4 的方案收敛到一处。
- `-Mode api` 全链路通过，195 条测试绿；`-Mode aspire` 仍待 F-03-1（一行）+ F-03-2。

## 8. F-03-4 复测 @ `0746b4a`

> 干净 worktree @ `0746b4a`，`dotnet build src/Pzd.Api -warnaserror` 0/0。
> 只让 redis 降级（`ConnectionStrings__redis='host='`，mysql 用 `local.env` 拼真实连接串），单启一次 API，再连续探测 `/health/ready`。

| 观测 | 结果 |
|---|---|
| `/health/ready` | 200，`status: Degraded`；`mysql: Healthy（mysql SELECT 1 成功）`、`redis: Degraded（redis 连接串没有可用 host。）` |
| 第 1 次探测 + 追加 4 次探测 | 5 次全部 200 |
| `Degraded mode:` 行数 | **恰好 1 条**，且来自 `Pzd.Api` 类别（启动期巡检）：`Degraded mode: Redis unavailable（redis 连接串没有可用 host。）` |
| 探测是否追加该行 | **不追加**（5 次 ready 后仍是 1 条） |

**F-03-4 闭环**：我们自己的重复打点已消除，唯一 Warn 出处固定在启动期巡检。

遗留观察（不阻塞，给票 11 参考）：框架的 `Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService` 仍会**每次探测**打一条 `warn: Health check redis with status Degraded completed…`。这是框架日志、不是我们的打点，但 5 秒轮询时依旧会刷 Warning；若要彻底安静，可在配置里把该类别日志级别设为 Error。另外 redis 不可达时单次 ready ~2s 的连接超时仍在，探针超时别设 2s 以内。

## 9. `-Mode aspire` 复测 @ `7c5f411`（F-03-1 闭环，F-03-2 仍复现）

> 基线：HEAD `7c5f411`，工作区干净（只有本报告未入库）。`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误。
> 命令（票面口径）：`pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 300`，`local.env` 提供真实 MySQL/Memurai 凭据。

### 9.1 后端四条全过 → F-03-1 闭环

| 探测 | 结果 |
|---|---|
| `/health/live` | 200 |
| `/health/ready` | 200；根 `Healthy`、mysql `Healthy`、redis `Healthy` |
| `/api/version` | `0.1.1-alpha.0.104+7c5f411`，`version`/`buildId`/`buildTime` 三字段齐 |
| `/`（无租户） | 400 |

`src/Pzd.AppHost/Program.cs` 已是 `.WithHttpEndpoint(port: 5080, name: "http")`（无 `targetPort`），DCP 正常启动并成功编排 api 资源。**F-03-1 闭环**；§5 里「沙箱中 DCP 起不来」的旧结论作废（老何已在 `2697869` 纠正）。

### 9.2 前端 5199 在 300s 内始终连拒 → F-03-2 仍复现

- 脚本退出码 **1**：`等待 http://127.0.0.1:5199/ 超时（300s），最后一次错误：由于目标计算机积极拒绝，无法连接。`
- 300s / 0.75s 轮询约 400 次探测，全程没有一次 5199 LISTENING。不是「启动慢」，是根本没起来。
- AppHost 日志里没有任何资源事件行，也没有 pnpm/vite 相关错误行。

### 9.3 排除法：不是 `AddViteApp` 相对路径的问题

`src/Pzd.AppHost/Program.cs:40` 的 `.AddViteApp("web", "../apps/web", "dev")` 相对 **AppHost 工程目录**（`src/Pzd.AppHost`）解析，等于 `src/apps/web`——该目录不存在，真实前端在仓库根 `apps/web`。这条本身是缺陷（应当改成 `../../apps/web`），但**不是** F-03-2 的根因，我做了对照：

| 对照组（干净 worktree `C:/tmp/pzd-f032` @ `7c5f411`，未动仓库文件） | 结果 |
|---|---|
| 改 `../../apps/web`，并 `pnpm install --frozen-lockfile` 把依赖装全（`apps/web/node_modules/.bin/react-router` 已存在） | 后端四条同样全过；**5199 在 240s 内仍然连拒**，脚本退出 1 |

所以仅把路径改对不足以让前端起来。另有一条反证：依赖没装的那一轮，日志里出现了 `.aspire-tmp/node-compile-cache/v24.20.0`，说明 node 确实被拉起过（只是跑不了 vite）；依赖装全、路径改对之后 5199 依旧不监听。

### 9.4 取证盲区：aspire 模式下资源日志完全不可见

AppHost 启动期自己连抛两条 ACL 失败：

```
fail: Aspire.Hosting.Backchannel.AuxiliaryBackchannelService[0] Error in auxiliary backchannel service.
      System.UnauthorizedAccessException: Attempted to perform an unauthorized operation.
      at Aspire.Shared.SocketPermissionHelper.CreateWindowsDirectory(...)
fail: Aspire.Hosting.Dcp.DcpHost[0] Failed to enable orchestration logging.（同一处栈）
```

后果：DCP 日志通道没建起来，**资源级 stdout / 状态在 aspire 模式下一个字都看不到**。这不是「没有错误」，是错误被吞掉了。AppHost 日志全文只有 DCP 启动与 dashboard 地址两段，没有任何 `Starting resource web` 之类事件。

### 9.5 交给开发的两条线索（尚未证实，不能当结论）

1. `where pnpm` 首条是 `C:\Users\q1887\AppData\Roaming\npm\pnpm`——**无扩展名的 /bin/sh 脚本**，其后才是 `pnpm.CMD`。Windows 的 `CreateProcess` 遇到无扩展名路径只会补 `.exe` 然后失败。脚本 `-Mode api` 已踩过同一类坑（必须点名 `pnpm.cmd`）；aspire 路径由 Aspire 自己解析 pnpm，很可能命中同一条。
2. 先把 DCP 日志通道打通（或换 ACL 正常的机器 / CI），拿到 `pnpm run dev` 的真实报错再改。现在只看得到「连拒」，看不到原因。

### 9.6 结论

- **F-03-1 闭环**：端点已入版本库，DCP 能启动并编排 api 资源。
- **F-03-2 未关闭**：`7c5f411` 上 aspire 模式仍是后端全通、前端 5199 在 300s 内连拒。**票 03 不能标通过。**
- `d17df6c` 的 `AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.Error)` 已在源码确认（`src/Pzd.Api/Program.cs:24`），但 aspire 模式因 9.4 的日志盲区**无法用日志计数复核**；该条仍以 `-Mode api` 的 §8 结论为准。
- 本条复测未改动仓库任何实现文件；对照实验在仓库外的临时 worktree 完成，用完即删。
- **注意**：本节结论只对 `7c5f411` 这个时点有效。此后旺总又落了 `e645123`（前端固定 5199 并关代理）、`f2d49b8`（前端目录改绝对路径）、`d13e58d`（前端失败打诊断），F-03-2 已在 HEAD 上闭环 → 见 §10。

## 10. `-Mode aspire` 复测 @ `6f18a9f`（全链路通过，F-03-1 / F-03-2 闭环）

> 干净 worktree `C:/tmp/pzd-f033` @ `6f18a9f`（含旺总 `e645123`/`f2d49b8`/`d13e58d` 三个前端修复与老登审计的脚本增强），`pnpm install --frozen-lockfile` 装全依赖。主工作区当时有他人未提交改动，为避免污染结论不使用。
> 命令：`pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 240`，`local.env` 提供真实 MySQL/Memurai 凭据。

| 验收项 | 结果 |
|---|---|
| `/health/live` | 200（免租户） |
| `/health/ready` | 200；根 `Healthy`、mysql `Healthy`、redis `Healthy` |
| `/api/version` | `0.1.1-alpha.0.118+6f18a9f`，`version`/`buildId`/`buildTime` 三字段齐 |
| `/`（无租户、豁免之外） | 400 |
| 前端 `127.0.0.1:5199` | **200 且是 SPA 外壳**（`lang="zh-CN"`） |
| 脚本退出码 | **0** |
| 启动期端口预检 | 未告警（起跑时 5080/5199 均空闲） |

**结论：F-03-1 与 F-03-2 均闭环**，票面口径「一条 `dotnet run --project src/Pzd.AppHost` 拉起 API + 前端」在 `6f18a9f` 上实测成立。我在 §9.3 独立得出的「`../apps/web` 相对 AppHost 工程目录解析会指到不存在的 `src/apps/web`」与旺总 `f2d49b8` 的修法一致（现为按仓库结构算绝对路径）。

### 10.1 未完成的一条：aspire 模式下的降级路径

`-NoCredentials`（无凭据 → 期望 Degraded 且仍 200）这一轮**没跑成**，被端口占用挡住：

```
[preflight] 端口 5199 已被占用（pid 32236 node）。先关掉占用进程再跑，否则验到的是别人实例的结果。
[preflight] 这是环境/占用问题，不是本票缺陷：清掉占用或装好依赖后重跑。
退出码：2
```

> 引文校注（老卫）：本段原先转录成「这不是环境/占用问题，而是本票缺陷」，与脚本原话**语义相反**。脚本自 `8307b4d` 起原文一直是「这是环境/占用问题，不是本票缺陷」（现 `scripts/verify-apphost.ps1:256`），端口占用那句也补全为脚本真实拼接格式。结论不变：这是**环境预检**（`exit 2`），不是本票缺陷。

占用者不是我起的：`pid 32236` 的命令行是 `node "E:\pzd\apps\web\node_modules\.bin\..\@react-router\dev\bin.js" dev`，17:20:44 启动，跑在**主仓库**目录下（我的副本是 `C:\tmp\pzd-f033`）。我没有动它。预检按退出码 2 拒绝执行是正确行为（不是假绿），待占用释放后可补这一条；降级语义本身已在 §8 与 `-Mode api` 覆盖。

### 10.2 一条给脚本的加固建议（假绿风险，非本轮缺陷）

脚本对前端只断言「`127.0.0.1:5199` 返回 200 且含 SPA 标记」，**不校验这个 200 是否来自本次宿主拉起的进程**。如果别人恰好在 5199 上跑着 dev，本脚本可能拿别人的实例当自己的成果（本轮之所以可信，是因为起跑时预检显示 5199 空闲）。建议：探活前记录当前宿主进程树的 pid，或断言占用 5199 的 pid 属于本次 AppHost 的子进程。

## 11. `-Mode api` @ `7c5f411`（前端 5199 首屏：真绿，附归属证明）

> 按调度口径（2026-10-27）：aspire 项记为「待用户终端证明」，前端首屏结论落在 `-Mode api`。
> 干净 worktree `C:/tmp/pzd-api03` @ `7c5f411`；`pnpm install --frozen-lockfile` 成功；`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；起跑前确认 5080/5199 均空闲。

| 验收项 | 结果 |
|---|---|
| `/health/live` | 200（免租户） |
| `/health/ready` | 200；根 `Healthy`、mysql `Healthy`、redis `Healthy` |
| `/api/version` | `0.1.1-alpha.0.104+7c5f411` |
| `/`（无租户） | 400 |
| 前端 `127.0.0.1:5199` | 200 且是 SPA 外壳（`lang="zh-CN"`） |
| 前端是否**本副本自启** | 是：`verify-web.log` 尾部 `➜ Local: http://127.0.0.1:5199/`；`verify-web.log.err` 只有 `$ react-router dev`，**无** `already in use` |
| 脚本退出码 | 0 |
| 退出后监听 | 5080、5199 均无监听（无残留） |

### 11.1 我第一轮跑出过一次假绿（已复现、已留证）— F-03-A3

第一次运行（17:25:32）时 5199 被另一个 dev 实例占着（pid 123312，`E:\pzd\apps\web` 的孤儿 vite）。结果：

- 我自己的 web **启动失败**：`verify-web.log.err` 里是 `Error: Port 5199 is already in use` + `ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL`；
- 但脚本仍然打印 `[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳` 并 **exit 0**——它探到的是别人的实例。

**F-03-A3（blocker 级：验收工具会假绿）**：前端断言不校验 200 的来源，只要本机有别的 dev 占着 5199，「自己前端起不来」就会被报成通过。修法（建议 1+2 都做）：

1. 启动 web 后检查 `$webProcess.HasExited`，并检查 `verify-web.log.err` 是否含 `already in use` / `ERR_PNPM`，命中即判失败；
2. 断言 5199 的 LISTENING 占用 pid 属于本次 `$webProcess` 的子进程树；
3. 探活请求带一个只属于本次运行的标记（自定义 header/path），响应缺标记即失败。

注意：`-Mode api` 下前端腿在**上表这份证据**之前，§2 与 §7 的历史结论没有做归属校验，若当时 5199 上有别人的实例，同样可能是假绿。此后再报前端结论，一律附 `verify-web.log` 的 `Local:` 行或占用 pid 归属。

### 11.2 两次孤儿 dev 实例（环境事实，不是缺陷）

17:20:44、17:24:55 各有一个 `pnpm --filter @pzd/web dev` 在 `E:\pzd` 下启动，两条进程链的链根（pid 37028 / 100796）**均已不存在** → 是被中断的 `-Mode api` 运行留下的孤儿（脚本被强杀时 `Stop-ProcessTree` 没机会跑）。我按「链根已死」逐条确认后清理，并记录在此，避免把在跑的实例当孤儿误杀。

### 11.3 aspire 项状态（按调度口径）

- `6f18a9f` 上我有一次 aspire 全链路通过（§10），**后端证据硬**：`/api/version` 的 `0.1.1-alpha.0.118+6f18a9f` 只能来自该副本的构建。
- 但按 §11.1 的实测，那次**前端腿的归属没有被校验**（脚本当时无 provenance 断言），因此不拿它当「前端腿在 aspire 下成立」的独立证据。
- 结论：**aspire 项按调度口径记为「待用户终端证明」**；F-03-1 代码缺陷已修并入库（`7c5f411`），不再重复判定。

## 12. `-Mode aspire` @ `e7200cb`（功能面全通，脚本归属断言误红 → F-03-A4）

> 干净 worktree `C:/tmp/pzd-asp4` @ `e7200cb`（含 `e645123` 前端端点固定、`ac20713` 归属断言、`f778a32` 退出码三档）。`pnpm install --frozen-lockfile` 成功，起跑前 5080/5199 均空闲。

| 验收项 | 结果 |
|---|---|
| `/health/live` | 200 |
| `/health/ready` | 200；根 `Healthy`、mysql `Healthy`、redis `Healthy` |
| `/`（无租户） | 400 |
| 前端 `127.0.0.1:5199` | 200 且是 SPA 外壳 |
| 脚本退出码 | **1**，唯一失败项是脚本自己的归属断言 |

失败原文：

```
http://127.0.0.1:5080 的监听者 pid 113772 不属于本次启动的进程树（宿主 pid 51280），200 不是本次实例给的
```

### 12.1 直接探针：5080 由 `dcp.exe` 代理持有，父链必然断

在同一次调用内起 AppHost 后抓监听者（`C:\tmp\probe-aspire.ps1`）：

```
api listener pid=117740 name=dcp.exe
  cmd=...\aspire.hosting.orchestration.win-x64\13.6.4\tools\dcp.exe run-controllers
      --kubeconfig C:\tmp\pzd-asp4\.aspire-tmp\aspire-dcprpqulry2.a5g\kubeconfig
      --notification-socket ... --monitor 113316
  ancestor pid=113316 name=dcp.exe cmd=...\dcp.exe start-apiserver --monitor 96532
      --kubeconfig C:\tmp\pzd-asp4\.aspire-tmp\aspire-dcprpqulry2.a5g\kubeconfig ...
  ancestor pid 116240 GONE (reparented)
```

结论：`.WithHttpEndpoint(port: 5080)` 保留代理时，**5080 的监听者是 DCP 的代理进程**，真正的 API 在动态端口上；代理进程的父链是 `dcp.exe → dcp.exe → 已退出`。所以「监听者必须是本次宿主的后代」这条判据在 aspire 模式下**结构上不可能成立**。`-Mode api` 没有代理、5080 由 API 进程直接监听，该判据才成立——这条断言只在 aspire 模式误红。

### 12.2 前端腿的归属：两种结果都抓到了

- 脚本那轮拿到 5199 200 时，监听者是 `node C:\tmp\pzd-asp4\apps\web\node_modules\.bin\..\@react-router\dev\bin.js dev --port 5199` → **确为本副本**，且命令行带 `--port 5199`（`e645123` 的端点固定生效）。
- 探针那轮 5199 被 `E:\pzd\apps\web` 的实例抢占（进程链根 pid 99760 已退出 → 孤儿），我副本的 web 起不来——这正是 F-03-A3 的假绿土壤。而 `ac20713` 的归属断言目前**只覆盖 5080**，5199 仍无归属校验。

### 12.3 建议（给 `scripts/verify-apphost.ps1`）

1. **aspire 模式 5080 归属**：不要走父链。改用 `dcp.exe` 命令行里的**本次运行独有 kubeconfig 路径**（`$repoRoot\.aspire-tmp\aspire-dcp*\kubeconfig`）匹配，或校验 `/api/version` 的 `buildId` 就是本轮 hash。
2. **前端 5199 归属**：校验监听进程命令行包含本次副本的 `apps/web` 绝对路径（DCP 拉起的进程会被重挂父级，路径判据稳定）。
3. 判据写成「父链匹配 **或** 路径/kubeconfig 匹配」：`-Mode api` 走父链即可，aspire 走后者。

### 12.4 结论

- aspire 模式的功能面在 `e7200cb` 上**全部可达**（live / ready 三 Healthy / version / 400 / 前端首屏）。
- 本轮失败是脚本归属断言的结构性误判（**F-03-A4**），属工具缺陷而非产品缺陷；因此**既不判 aspire 通过，也不判产品失败**，等脚本修好后复跑定论。
- **F-03-A3**（前端假绿）仍未关闭：本轮又抓到一次实况——5199 被外部实例抢占。

## 13. `-Mode aspire` 复测 @ `f2d49b8`（两次 exit 0）+ 降级日志计数

> 干净 worktree `C:/tmp/pzd-t03` @ `f2d49b8`（`e645123` + `f2d49b8` 均已含在内）。`pnpm install --frozen-lockfile` 成功，`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；起跑前 5080/5199 均空闲。

| 验收项 | run 1 | run 2 |
|---|---|---|
| `/health/live` | 200 | 200 |
| `/health/ready` | 200；根/mysql/redis 全 `Healthy` | 同左 |
| `/api/version` | `0.1.1-alpha.0.109+f2d49b8` | 同左 |
| `/`（无租户） | 400 | 400 |
| 前端 `127.0.0.1:5199` | 200 且是 SPA 外壳 | 同左 |
| 退出码 | **0** | **0** |

### 13.1 退出清理

- `5080`：两次在脚本退出后**立即**无监听。
- `5199`：脚本退出瞬间还能抓到一次 LISTENING（pid 89128 / 117968），**数秒内自行消失**（我在 17:37:11–17:37:35 连续采样 12 次，全部为 free）。属进程收尾的短暂残留，不是长期占口。
- 对比：今天被我清掉的 6 条孤儿 dev 都是「链根已死、vite 持续监听数分钟」，那是**运行被强杀**（agent 轮次中断）造成的，与正常退出不同。

### 13.2 降级日志计数（`-Mode api`、无凭据）

独立复跑（不依赖开发自述）：

```
启动期 Degraded mode: 2
poll1 HTTP=200 累计=2
poll2 HTTP=200 累计=2
poll3 HTTP=200 累计=2
```

两条原文：`Degraded mode: MySQL unavailable（mysql 未配置（ConnectionStrings:mysql 为空）。）`、`Degraded mode: Redis unavailable（redis 未配置（ConnectionStrings:redis 为空）。）`。**启动恰好 2 条 = 不可用资源数，轮询不增长** → F-03-4 在 `f2d49b8` 上仍成立。

### 13.3 前端腿归属（本 hash 的取证限制）

`f2d49b8` 的脚本**还没有**归属断言（`ac20713` 在其之后）。归属证据来自同一 AppHost/web 代码在 `e7200cb` 上的运行内探针：

```
5199 LISTENING pid=53276 node "C:\tmp\pzd-asp4\apps\web\node_modules\.bin\..\@react-router\dev\bin.js" dev --port 5199
```

即 5199 由**本次副本**的 `apps/web` 进程持有，且命令行带 `--port 5199`（`e645123` 生效）。`f2d49b8 → e7200cb` 之间 `src/Pzd.AppHost`、`src/Pzd.Api`、`apps/web` 零改动（只有脚本、docs 与票 10 测试），故该证据对本 hash 成立。

### 13.4 本轮结论

- **f2d49b8 上 aspire 全链路两次通过（exit 0）**：后端三端点 + 前端 5199 首屏 + 正常退出清理。
- F-03-4 计数独立复现：启动 2 条、轮询不增长。
- 仍开着的只剩 **F-03-A3 / F-03-A4**（脚本归属判定的问题）：`e7200cb` 起新增的 5080 父链断言在 aspire 模式必然误红，5199 一侧则始终没有归属断言。这两条都是验收工具的可信度问题，不是产品行为问题。
- 环境事实：本轮前后共清理 4 条被中断的 `-Mode api` 留下的孤儿 dev 链（进程链根均已不存在），均按「链根已死」逐条确认后才动手。

## 14. `-Mode aspire` @ `d13e58d`：3 次跑，2 通过 1 失败（前端腿间歇性）

> 干净 worktree `C:/tmp/pzd-d13` @ `d13e58d`，`pnpm install --frozen-lockfile`，`dotnet build Pzd.slnx -warnaserror` 0/0。命令固定为 `pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 180~240`。

| 轮次 | 结果 | 备注 |
|---|---|---|
| run 1 | ❌ exit 1 | `/health/ready` 三 Healthy、`/api/version 0.1.1-alpha.0.111+d13e58d`、`/` 400 全过；前端 5199 **240s 连拒** |
| run 2 | ✅ exit 0 | 五项全过；时间线采样见下 |
| run 3 | ✅ exit 0 | 五项全过 |

### 14.1 归属已证实（不再是推测）

run 2 运行期间采样到 5199 的监听者：

```
17:55:xx 5199 LISTENING pid=75532
CommandLine = node "C:\tmp\pzd-d13\apps\web\node_modules\.bin\..\@react-router\dev\bin.js" dev --port 5199
```

即**本次副本**的 `apps/web`、且带 `--port 5199`。**DCP 经 `cmd.exe` 执行 `pnpm.CMD` 是成功的**，不需要改成 `cmd /c pnpm run dev`。

### 14.2 启动初期有一个 ~6s 的 HTTP 500 窗口

同一轮时间线（3s 一次）：

```
17:55:55 pid=75532 http=500
17:55:58 pid=75532 http=500
（其后转 200，脚本判通过）
```

`Wait-ForUrl` 只认 200、所以会继续轮询，这一条没问题；但排查时若只看「起了没」会误以为挂住。

### 14.3 F-03-A5（medium，新）：aspire 前端腿在本机间歇性起不来

同一 hash、同一副本、同一命令，3 次里 1 次前端在 240s 内始终连拒，另外 2 次正常。失败轮里 `d13e58d` 已加的「失败时打印 web/vite/pnpm 行」**没有匹配到任何行**（宿主日志只有 16 行、且 DCP 日志通道被 ACL 挡），所以看不到 pnpm/vite 的报错原文——诊断能力不足，不是没有错误。

建议：① 让脚本在前端失败时把 `verify-apphost.log` 全文尾部 + `.err` 一并打印（现在只打匹配行）；② 连续跑 3 次作为该模式的稳定性口径，单次通过不记为「aspire 稳定通过」。

### 14.4 退出清理

`5080` 每次退出后立即释放。`5199` 有短暂残留（观测到退出后 >4s 仍 LISTENING，pid 88740，其后自行消失）；不是长期占口，但紧跟其后的第二次运行会撞预检 exit 2，脚本的等待/重试需要容忍这一点。

## 15. `-Mode aspire` @ `79bbe36`：前端资源未被 DCP 拉起（F-03-A5 缩小范围）

> 干净 worktree `C:/tmp/pzd-79` @ `79bbe36`（脚本已含 runId 归属、前端按副本路径归属），`pnpm install --frozen-lockfile`，起跑前 5080/5199 空闲、无 dcp 残留。

| 轮次 | 结果 |
|---|---|
| run 1 | exit 1：后端 live/ready/version(runId 比对通过)/400 全过；前端 5199 报「监听进程命令行不含本次副本路径」——抓到的 pid 是外来实例 |
| run 2 | exit 1：后端同左；前端 180s 连拒，无外来实例 |
| 运行内探针（90s，5s 一次） | `node(pzd-79)=0`、`5199_pid=0` **全程为 0** |

### 15.1 关键对照

- **同一副本、同一时刻**手动执行 `pnpm --filter @pzd/web dev`：正常启动，跑到我 30s 超时被杀（退出码 124 是我 timeout 所致，不是 pnpm 失败）。
- **aspire 模式**：DCP 在 90 秒内没有创建任何与 `C:\tmp\pzd-79` 相关的 node 进程，5199 从未被绑定。

所以 F-03-A5 不是「vite 起来了但报错」，也不是「路径/依赖/端口」问题，而是：**这次运行时 web 资源根本没有被 DCP 拉起**。

### 15.2 runId 归属断言已验证有效

`79bbe36` 的脚本用「AppHost 注入 `PZD_RUN_ID` → API 从 `/api/version` 回显 → 脚本比对」判定 5080 归属，实测：

```
[ok] /api/version runId 与本次宿主一致（3d14f427ab9b4927abc6628ae4b90052），应答者来自本次启动的进程
```

这条比父链判定可靠（aspire 下 5080 由 `dcp.exe` 代理持有、父链必断），**F-03-A4 闭环**。

### 15.3 前端归属断言（F-03-A11）也验证有效

run 1 里 5199 被外来实例占着时，脚本判「监听进程 pid 34580 命令行不含本次副本路径 `C:\tmp\pzd-79\apps\web`，200 可能来自别的实例」并判失败——**这正是它该做的事**，假绿通道被封死了。

### 15.4 需要开发侧补的证据（无法从本机日志拿到）

DCP 的日志通道在本机被 ACL 挡住（`Failed to enable orchestration logging`），AppHost 日志里没有任何资源级事件，因此「web 资源为什么没被拉起」看不到原因。建议：

1. 在 AppHost 里订阅资源状态变化，对 `web` 资源打印状态与失败原因（`ResourceNotifications` / `ResourceLoggerService`），让失败可见；
2. 或在 DCP 日志可用的环境复现一次，取 `web` 资源的 `ExecutableReconciler` 报错；
3. 不要把这条再归因到 `.CMD` 垫片：同一副本手动 `pnpm` 可跑，且此前成功轮里 DCP 经 `cmd.exe` 拉起过 web（命令行带 `--port 5199`）。

### 15.5 结论

- 票 03 后端面：`79bbe36` 上全通过，runId 归属成立（F-03-A4 闭环）。
- 前端面：假绿通道已封（F-03-A11 有效），但 **aspire 下 web 资源存在「未被拉起」的偶发失败**（F-03-A5，本 hash 2/2 失败、`d13e58d` 2/3 通过），原因需 AppHost 侧日志才能定位。
- 因此票 03 不能标通过：不是功能一定坏，而是 `-Mode aspire` 的前端腿在本机**不稳定且不可诊断**。

## 16. `-Mode aspire` @ `16bd0f4`（API 腿 A11 实跑通过；web 腿 3 次失败，原因各不相同）

> 干净 worktree `C:/tmp/pzd-asp5` @ `16bd0f4`（含旺总 A10 web 血缘 + F-03-A6 退出清理），`pnpm install --frozen-lockfile` 1.3s，`local.env` 就位。三次运行都用 `-Mode aspire -TimeoutSeconds 240`。

### 16.1 API 腿：A11「实跑」证据补齐（老登此前的未闭环项）

三次运行都拿到同一组：

```
[ok] /health/live 200（免租户）
[note] aspire 模式：5080 由 DCP 代理持有，归属改用 X-Pzd-Run-Id 响应头判定（A11）
[ok] /health/ready 根状态 = Healthy / mysql = Healthy / redis = Healthy
[ok] /api/version 0.1.1-alpha.0.162+16bd0f4
[ok] X-Pzd-Run-Id 与本次运行一致（ee68aa… / 207de1… / b1a728…）：应答来自本次启动的进程
[ok] / 无租户仍 400
```

三次的 runId 互不相同、每次与脚本自报一致 → **aspire 模式下 API 腿的归属判据是活过的，不是只跑过单测**。

### 16.2 web 腿：三次失败，三种原因（都不是「功能面通过」）

| # | 时间 | 5199 的现象 | 我的判断 |
|---|---|---|---|
| 1 | 18:23 | 200，但 `X-Pzd-Run-Id: 170e0807…` ≠ 本次 `ee68aa…` | **外来实例**。血缘断言正确抓住（`5199 不是本次实例`），A10 反向证据在真实环境里成立 |
| 2 | 18:26 | 240s 全程连接被拒 | 自己的 web 资源**根本没起来**（F-03-A5 在 `16bd0f4` 上仍复现），exit 1 |
| 3 | 18:30 | 200，但 `X-Pzd-Run-Id: probe-web-abc123` | 别人正在跑的**受控探针**占着 5199；断言再次抓住，exit 1 |

第 3 次我做了根因对照：把 PATH 前置成一个只含 `pnpm.CMD` 的垫片目录（`where pnpm` 首条变成垫片、无扩展名那个排在后面），结果**垫片一次都没被调用**（`invoked.log` 为空），而 5199 上仍是探针进程。

### 16.3 两条结论边界

- **`.CMD` 垫片不是已证根因**：无扩展名 `pnpm` 的 `Process.Start` 实测报 `not a valid application`（`CreateProcess` 不补 `.CMD`），这是真的；但第 3 次垫片未被调用，说明那一次 Aspire 根本没走到「调用 pnpm」这一步，**没有证据把 A5 钉在它身上**。
- **本机 5199 在 18:20–18:35 被多人反复占用**（探针 `probe-web-abc123`、`E:\pzd` 自己的 dev server pid 21460 等）。所以 web 腿的成败在这段时间**不能单独归因于代码**；要判定 A5 必须有一个独占 5199 的窗口，并且 AppHost 侧打出 `web` 资源状态（`ResourceNotifications`）。

### 16.4 A5 仍是票 03 唯一未闭环项

`-NoCredentials` 的 aspire 降级这轮没跑（同端口争用，跑了也只会红在 web 腿）。A5 需要的证据没变：AppHost 里对 `web` 资源打印状态/失败原因，或换一台 DCP 日志可用的机器复现。

## 17. `16bd0f4`/`6b67c2d`：A5 根因找到了——**web 资源不是没被拉起，是起来后自己 crash（RRD TDZ）**

> `6b67c2d` 新加的 `ResourceNotifications` 诊断把状态打了出来；再用一个记录调用的 `pnpm.CMD` wrapper 抓住真实命令行与 stderr。干净副本 `C:/tmp/pzd-a5`。

### 17.1 诊断输出（old 结论被推翻）

```
[pzd-diag] resource=web state=(unknown) ×3
[pzd-diag] resource=web state=Starting
[pzd-diag] resource=web state=Waiting ×2
[pzd-diag] resource=web state=Running ×4
[pzd-diag] resource=web state=Finished exitCode=1      ← 起来过，然后退出
（重复一轮）
```

此前 §15/§16 写的「web 资源根本没被 DCP 拉起」**不准确，撤回**：DCP 确实拉起了它，进程启动后约几秒以 `exit 1` 终止。

### 17.2 wrapper 抓到的真实调用与报错

Aspire 调用的命令（cwd 与参数都对）：

```
[wrap] cwd=C:\tmp\pzd-a5\apps\web args=run dev --port 5199
[wrap] exit=1
```

stderr：

```
C:\...\@react-router\dev\dist\config\defaults\entry.server.node.tsx:30
      () => abort(),
            ^
ReferenceError: Cannot access 'abort' before initialization
    at Timeout.eval [as _onTimeout] (…entry.server.node.tsx:30:13)
    at listOnTimeout (node:internal/timers:685:17)
[ELIFECYCLE] Command failed with exit code 1.
```

对应源码（React Router `7.18.4` 默认 server entry）：

```ts
let timeoutId = setTimeout(() => abort(), streamTimeout + 1000);   // ← :28-31，6 秒
// …之后才 let abort = () => {…}
```

即：请求处理里先挂 6 秒定时器，`abort` 还没赋值；一旦某处抛出/卡住使它没被赋值，定时器到点就 `ReferenceError`，dev server 直接退出 1。这就是「同一 hash 有时绿有时红」的**间歇性来源**。

### 17.3 对照：手动跑同一条命令是好的

同一副本、同一条 `pnpm run dev --port 5199`：

- 起服务 5 次，**5/5 正常**（`Local: http://127.0.0.1:5199/`，被超时杀掉）；
- 起服务后真发 3 个请求，**3/3 200**，不 crash。

所以触发条件与 **Aspire 运行时注入的环境/时序**有关，与「DCP 拉不起进程」「`.CMD` 垫片」都无关——wrapper 本身就证明 Aspire 成功执行了 `pnpm.CMD`。

### 17.4 结论与移交

- **A5 属前端（`apps/web` + React Router 7.18.4 默认 entry）问题，不是 Aspire/DCP/脚本问题**；修法方向：给 `apps/web` 提供自定义 `entry.server`（或升级/降级 react-router 版本），避免默认 entry 的 6 秒 `abort` 定时器在异常路径上炸掉进程。责任人：锋哥（`apps/web`）。
- 脚本侧不需要再改；`-Mode aspire` 的 web 腿只要前端能稳定起住就能验。
- 我这一轮在 `6b67c2d` 上同时再次确认 API 腿：`/health/ready` 三项 Healthy、`/api/version` = `0.1.1-alpha.0.168+6b67c2d`、`X-Pzd-Run-Id` 与本次一致、`/` 400。

## 18. A5 的 TDZ 机制：本地最小复现（脱离 Aspire 也能跑红）

张一一排除了「过早请求」（启动 1s 内连打 8 次 `/`，8/8 = 200），我在默认 entry 源码上把机制钉死。

### 18.1 源码事实（不是推断）

`apps/web` 当前**没有** `app/entry.server.tsx`，用 RRD 默认的那份；实装版本 `react-router` / `@react-router/dev` 都是 **7.18.4**（`package.json` 写 `^7.8.0`）。

`node_modules/@react-router/dev/dist/config/defaults/entry.server.node.tsx`（88 行）：

- `:10` `export const streamTimeout = 5_000;`
- `:42-45` `let timeoutId = setTimeout(() => abort(), streamTimeout + 1000);` → **6000ms**
- `:47` `const { pipe, abort } = renderToPipeableStream(...)` —— **没有 try/catch**
- `:55` `clearTimeout(timeoutId)` **只在** `PassThrough` 的 `final(callback)` 里（成功渲染路径）
- `:73-75` `onShellError(error) { reject(error); }` —— **不清定时器**

> 更正 §17.2：我当时把定时器标成 `:28-31`（凭观察推断），实际是 `:42-45`；`abort` 不是「之后才赋值」，而是同一行解构赋值的产物，解构没完成即处于 TDZ。机制结论不变，行号以此处为准。

### 18.2 最小复现（`C:\tmp\rrd-tdz\repro3.mjs`，只用 apps/web 已装的 react-dom）

逐行同形复刻「先挂 6s 定时器、再解构 `{pipe, abort}`、无 try」，让渲染调用**同步抛出**：

```
[   1ms] promise rejected（真错误的第一次机会）: TypeError: simulated: sync throw at the call site
[6004ms] setTimeout 触发，访问 abort
[6005ms] UNCAUGHT ReferenceError: Cannot access 'abort' before initialization
REAL_EXIT=1
```

三点结论：

1. **错误字符串与线上栈一字不差**（`Cannot access 'abort' before initialization`），所以 §17 抓到的栈就是这条链路，不是别的错。
2. **真实错误发生在第 1 毫秒**，那一毫秒里它就已经被 reject 了；是 6 秒后的 TDZ 把它盖住并把进程带成 exit 1。日志里只看得到后者，前者要靠 reject 链路（或自定义 entry）才露得出来。
3. **6000ms = `streamTimeout + 1000`，是固定值不是随机值**。所以「Running 几秒后 exit 1」的「几秒」应当可量化为 ≈6s——拿到 web 资源 `Running`/`Finished` 时间戳的人可以用这个数校验是否同一条链路。

### 18.3 反向控制：不同步抛出就不会炸

同一形状下传 `renderToPipeableStream(undefined, {})`（不抛，返回对象、错误走异步）→ 6000ms 定时器触发时 `abort` 已有值，**进程存活，exit 0**。

所以 TDZ 的触发条件是**调用点同步抛出**这一种，不是「渲染出错」的泛指。对锋哥的修复有直接影响：自定义 entry 若只是把错误 `console.error` 出来、却仍在失败路径上留着未被清理的定时器，**6 秒后照样 exit 1**。

### 18.4 移交锋哥的验收判据（两条，缺一不可）

1. **真错误必须在不晚于首个请求的日志里出现**——即 `renderToPipeableStream` 的抛出点要有 try/catch 或前置校验，把原始错误打出来，不能等 6 秒后由 TDZ 代为报信。
2. **失败路径必须清理定时器**——`onShellError` / 抛出分支都要 `clearTimeout(timeoutId)`。只做第 1 条不做第 2 条，A5 在外观上会变成「错误日志有了，进程仍然 6 秒后退出 1」。

这条判据不依赖 Aspire：用一条**故意让首请求进入失败路径**的调用即可在本地跑红/跑绿（18.2 的脚本就是最小形态），可以做成回归测试。范围：只碰 `apps/web/**`，我不改实现，等锋哥落笔后再验。

## 19. A5 的验收判据（按老何三条硬约束落成可执行形式）

老何本轮定的验收面是三条：原始异常可见且脱敏、失败路径不遗留定时器造成二次崩溃、SSR 超时保护仍有效。加上他「白名单输出、连接串/密码/令牌不得入日志」，这里把它们转成可以判红判绿的形式。

### 19.1 我的证据边界（先收紧，避免重复上一轮的过度推断）

我 §18 证明的只有两件事：

- **机制**：`Cannot access 'abort' before initialization` 只能是「`renderToPipeableStream(...)` 调用点**同步**抛出、`const {pipe, abort}` 解构未完成」这一条路径（18.3 的反向控制：非法输入也不同步抛，定时器到点照样 exit 0）；
- **时间常数**：从请求到进程退出是 `streamTimeout + 1000` = **6000ms**，固定值（18.2 实测 6004ms）。

我**没有**证明根因是什么。老何那句「只能排除已测条件，不能把根因锁死为环境变量或 stdio」成立，张一一 8/8 = 200 与旺总 30/30 = 200 排除的是「请求时机」，我这边排除的是「普通渲染错误」。能在调用点同步抛出的只剩调用之前或调用自身的环节（模块入口解析、版本/导出条件、`renderToPipeableStream` 缺失），具体是哪一环仍待定——环境变量是候选之一，不是结论。

### 19.2 三条判据的具体形态

**① 原始异常可见**：首个请求的日志里必须在 **≪6 秒**时就出现原始错误（不是 `ReferenceError: abort`），且带类型与消息。判红形态就是 §18.2 的输出——`[1ms]` 只有 promise rejected、日志里看得见的却是 `[6005ms] UNCAUGHT ReferenceError`。

**② 失败路径不遗留定时器**：错误发生到进程结束之间，不出现任何因 `abort` 未初始化而抛的二次异常。判红形态：错误已经打出来了，进程仍在 ~6 秒后 exit 1。当前默认 entry 的缺口在 `:73-75`（`onShellError` 只 reject）与 `:55`（`clearTimeout` 只在成功路径的 `final()` 里）。

**③ SSR 超时保护仍有效**：这条是**反方向**的，专门拦「为了消掉 TDZ 把定时器删了/关掉」的修法。期望行为：流已经建好但迟迟不 ready 时，`streamTimeout`（5000ms）+1s 之后仍要触发 abort 并让请求结束，**不能永久挂住**。判红形态：卡死的渲染一直不返回，进程既不退出也不响应。

② 和 ③ 必须同时成立，这是一对互相拉扯的约束：只满足 ②（失败就清定时器、永不 abort）会让卡死的请求挂到进程被杀；只满足 ③（定时器照挂不判空）就是现在这个 TDZ。

### 19.3 日志脱敏（可机械检查）

旺总打印 DCP 注入环境时按白名单来。可机械判的几条：输出中不得出现 `Password=`、`Pwd=`、`User ID=`、`sk-`、`Bearer `、`--password`；`local.env` 里的 `PZD_MYSQL_PASSWORD` 值不得出现在任何一行 stdout/stderr。PATH 只记诊断相关片段（例如是否含 `node_modules/.bin`），不整段打印。

这条我给个自查命令（在跑完 aspire 后对日志文件执行）：把 `local.env` 的密码读进环境而不是命令行，然后 `grep -F -c "$MYSQL_PWD" <日志>` 必须为 0。

### 19.4 谁来验

①②③ 都可脱离 Aspire 在本地跑（我 §18 的脚本就是骨架），aspire 全链路只是最后一道「DCP 真实环境」确认。锋哥落笔后我按这四条给结论；在此之前票 11 不放行，和老的裁决一致。

## 20. A5 修复件（`apps/web/app/entry.server.tsx`）验收 @ 工作区快照（无 hash）

### 20.1 先说输入边界

这不是 hash 锚定的结论。验证期间主仓这个文件**至少改过三版**（115 行带 try/catch → 91 行「默认 entry 拷贝 + 临时 throw」→ 118 行自定义版 + throw），所以我把 118 行那一版连同当时的 `package.json` **冻结到干净副本** `C:/tmp/pzd-a5v`（`git worktree add --detach` 自 `87fcf5d`）再验，结论只对这份快照成立。副本已删，端口已空。

### 20.2 首要前提：这份 entry 到底有没有被 RRD 采用（决定性实验）

如果不被采用，`/` 照样 200、归属头照样有，整台修复看起来全是绿的其实没生效——所以先证这一条，不证它后面都不用看。

方法：副本里保留那处 try 内 `throw new Error("pzd-probe: …")` 作为判别器，起 dev（端口 5202）。

```
[+0s] GET / -> HTTP '500'
[ok] 日志命中 pzd-probe -> 自定义 entry 确实被 RRD 采用并执行
[ok] 无 abort TDZ
[ok] / 回了 '500' -> 失败路径确实走到了
RESULT=PASS
```

栈里还给出了 `/` 的完整调用链，这条对 A11 也有用：

```
at handleRequest (apps/web/app/entry.server.tsx:48:10)
at renderHtml            (react-router@7.18.4 .../chunk-H4DAEOV7.mjs:1557:22)
at handleDocumentRequest (.../chunk-H4DAEOV7.mjs:1453:16)
at requestHandler       (.../chunk-H4DAEOV7.mjs:1296:18)
```

**结论：`/` 走 `handleDocumentRequest → renderHtml → entry.server`，自定义 entry 在 `/` 的路径上。**（张一一说的「`/` 由文档中间件应答」和这条不冲突：中间件在链上，但它最终调的就是 entry。）

### 20.3 判据 ① 真错误可见：PASS（两条失败路径都验了）

| 场景 | 首个请求的日志 | 判定 |
|---|---|---|
| 调用点**同步**抛出 | `[pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）： Error: pzd-probe…` | PASS，错误在 1s 内出现，6 秒后无 TDZ |
| 渲染期抛出（**异步**走 `onShellError`） | `[pzd/entry.server] 文档外壳渲染失败（这是真错误，不是 abort TDZ）： Error: pzd-probe-shell-error…` | PASS |

异步那条是副本里把渲染目标换成 `const ProbeThrows = (): never => { throw … }` 造出来的，`/` 回 500、错误立刻打出来。

### 20.4 判据 ② 失败路径不遗留定时器：同步分支 PASS；`onShellError` 有缺口，但**实测良性**

代码事实（118 行快照）：`clearTimeout` 只有一处（`:75`，`PassThrough` 的 `final()`），`onShellError`（`:92-93`）只 `reject`、不清定时器；而定时器在 `:118` 渲染成功后才挂。

- **同步抛出分支**：定时器根本没挂上 → 无遗留，无 TDZ。实测 PASS。
- **`onShellError` 分支**：定时器已挂上且不会被清 → 6 秒后 `abort?.()` 仍会执行。

**我上一轮在群里说这条会「二次崩溃」，实测不成立，按实测修正：**

```
逐个秒观察 14 秒：监听=yes 全程，日志行数恒为 57（+6s 时无任何新增行）
第二次 GET / -> HTTP '500'（服务仍可用）
grep "Cannot access 'abort'" / "Cannot pipe" -> (无)
```

因为进入 `onShellError` 时 `abort` **已经初始化**（`abort = stream.abort` 在 `:114`），`abort?.()` 调一个已失败的流是静默 no-op。所以实际后果只有「每个失败请求多持一个 6 秒定时器句柄」，**不崩、不影响可用性**，定级 **low（资源卫生）**，不是 high。

建议仍然补上 `onShellError` 里的 `clearTimeout(timeoutId)`——一行，消除句柄持有，也让「失败路径不留定时器」这条判据变成无条件成立，省得下次再为它做一次实测。

### 20.5 判据 ③ SSR 超时保护仍有效：PASS（保留判定，卡死场景未实跑）

`:118` 的 `setTimeout(() => abort?.(), streamTimeout + 1_000)` 保留了，`abort` 判空，没有「为了消 TDZ 把定时器删掉」。**但「流永不 ready」的真卡死场景我没有实跑**，只做了代码判定——这一条我给的是 PASS（代码层），不是实跑结论。谁要写成「已实跑验证超时保护」，得先补这个用例。

### 20.6 正常路径回归：PASS

主仓当前代码、端口 5199、注入 `PZD_RUN_ID=probe-li-1793115053-a1b2c3`：

```
[ok] 第 2s 起 / 回 200
  [1] HTTP/1.1 200   x-pzd-run-id=probe-li-1793115053-a1b2c3  content-type=text/html
  [2] HTTP/1.1 200   x-pzd-run-id=probe-li-1793115053-a1b2c3  content-type=text/html
  [3] HTTP/1.1 200   x-pzd-run-id=probe-li-1793115053-a1b2c3  content-type=text/html
[ok] 响应体含 lang="zh-CN"（是我们的 SPA 外壳）
[ok] 日志无 abort TDZ / 无 entry.server 报错
pnpm --filter @pzd/web typecheck -> EXIT=0
```

即：自定义 entry 没有把正常 `/` 搞坏，`X-Pzd-Run-Id` 仍然出在 `/` 上（A11 web 腿的写头部分在本机成立）。

### 20.7 结论与两条必须先处理的提醒

- **A5 修复件在「①②③ + 正常路径」上按上述边界算通过**（②按 low 记，③是代码判定）。aspire 全链路仍待跑——那要等 5080/5199 空出来，且属于「DCP 是否真把 `PZD_RUN_ID` 传进去」的另一件事，不在本节的结论范围内。
- **【必须先处理】工作区的 `apps/web/app/entry.server.tsx` 当前带临时变异**（`grep -c pzd-probe` = 1）。提交前必须删掉并复查：这段 throw 一旦入库，生产 `/` 会全部 500。建议提交前统一跑一次 `grep -c pzd-probe apps/web/app/entry.server.tsx` 必须为 0。
- **最终验收要按提交 hash 重跑**：本节锚的是工作区快照（当时 118 行），主仓 HEAD 已从 `87fcf5d` 推进到 `ddb8b84`。锋哥提交后我按 hash 复跑 20.2/20.6 两组，再给可入库的结论。

## 21. aspire 全链路在干净副本上通过 @ `5682cd0`（A5 修复 + A11 web 腿正向证据 + DCP 环境快照）

### 21.1 主仓跑不了，副本能跑——这个差异本身是环境阻塞

在主仓 `E:\pzd` 连跑三次 `-Mode aspire` 全部失败：

| 次 | 现象 |
|---|---|
| 23:34 | `CreateDcpObjectsAsync` 抛 `UnauthorizedAccessException`（`SocketPermissionHelper.CreateWindowsDirectory` → `SetSecurityInfo` 写 SACL），退出码 `-532462766` |
| 23:40 | 同上；`dotnet build-server shutdown` 后重试仍失败 |
| 23:42 | 形态变了：`Failed to enable orchestration logging` 消失（我用 `icacls /remove:d "Everyone"` 移掉了 `.aspire-tmp` 上 Aspire 自己写的 `Deny Everyone DeleteSubdirectoriesAndFiles`，改成同样生效），改为 `Polly.Timeout.TimeoutRejectedException: The operation didn't complete within the allowed timeout of '00:02:00'` |

**判断依据（不是猜）**：同一份代码、同一个 HEAD，在**全新副本**里第一次跑就 `EXIT=0`。副本的 `.aspire-tmp` 是首次创建，主仓的 `.aspire-tmp` 带着 18:18/18:36 那几轮留下的受限 ACE。`Get-Acl` 实测主仓该目录含 `Deny Everyone DeleteSubdirectoriesAndFiles` 与一个 AppContainer SID `S-1-4-679037864-1046299354`；当前进程 `Elevated=False`、无 `SeSecurityPrivilege`，所以 `repairExisting` 改不动它。

**这是一个影响全体成员的环境阻塞**：只要还在主仓跑 aspire，谁都会撞这条。清理它需要管理员（子项被 Deny 且被 MSBuild 占用，我这边 `Remove-Item -Recurse` 与 `Rename-Item` 都被拒）。这不是代码问题，别记成 A5 或脚本缺陷。

### 21.2 全链路结果：`EXIT=0`

副本 `C:/tmp/pzd-asp`（`git worktree` 自 `5682cd0`，已删），`pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 300`：

```
[ok] /health/live 200（免租户）
[ok] /health/ready 根状态 = Healthy
[ok] /health/ready mysql = Healthy
[ok] /health/ready redis = Healthy
[ok] /api/version 0.1.1-alpha.0.180+5682cd0
[ok] X-Pzd-Run-Id 与本次运行一致（9ef57153c7174b0481330bd0a187e302）：应答来自本次启动的进程
[ok] / 无租户仍 400
[ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 58668），且是 SPA 外壳：由本次运行拉起
REAL_EXIT=0
```

**其中最后一条是 A11 web 腿一直缺的正向证据**：aspire/DCP 拉起的前端回出了与本轮 runId 一致的 `X-Pzd-Run-Id`，并且脚本判定了归属。这和 §16 那三次「web 腿三种原因全红」形成对照——修 A5 之前 web 根本活不到回包。

### 21.3 DCP 环境快照：旺总要的三项全部拿到

```
[pzd-diag] 已订阅资源状态流：api, web
[pzd-diag] resource=web state=Running exitCode=-
[pzd-diag] resource=web 环境变量共 103 项（下表仅白名单取值）
[pzd-diag] env NODE_ENV=development
[pzd-diag] env PATH=C:\Users\q1887\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe;C:\Users\q1887\.dsh\profiles\desktop\node_mo…
[pzd-diag] env PORT=5199
[pzd-diag] env PZD_RUN_ID=9ef57153c7174b0481330bd0a187e302
```

逐项回答：

1. **`PORT` 在不在** —— 在，`5199`，与手动 shell 一致。
2. **`NODE_ENV` 与手动 shell 有无差别** —— `development`，与手动 `pnpm run dev` 一致，无差别。
3. **`PZD_RUN_ID` 有没有落到 vite 进程** —— **落了**，值 `9ef57153c7174b0481330bd0a187e302` 与脚本本轮生成的 runId **逐字符一致**。这条同时回答了雨哥担心的「`WithEnvironment` 只挂在资源定义上、没进真实进程」：**进了真实进程**，而且不只是 diag 读到了它，`/` 的响应头也是从进程里读出来回的（21.2 最后一条），两条独立证据互相印证。

脱敏也按老何的白名单执行了：只出现 `NODE_ENV`/`PATH`/`PORT`/`PZD_RUN_ID` 四个白名单名，`PATH` 单值截断，未见任何密码/连接串/令牌行。

### 21.4 一个必须记住的边界：环境变量只在 `Running` 之后才有

日志里 `环境变量共 0 项` 出现了 5 次，`103 项` 只出现 1 次，位置很关键：

```
state=(unknown) → 0 项
state=Starting  → 0 项
state=Waiting   → 0 项
state=Running   → 103 项   ← 只有到这里才有
```

也就是说：**资源没进 `Running` 之前，快照里拿不到环境变量**。这解释了为什么修 A5 之前这个诊断看不到任何 env——旧行为是 `Running` 之后紧接着 `Finished exitCode=1`，而那份快照可能根本赶不上。凡是拿这份 diag 判「环境变量有没有传进去」的人，必须确认 web 状态到过 `Running`，否则 `0 项` 是「时序没到」，不是「没传」。

### 21.5 A5 修复的运行侧确认：web 不再 `Finished exitCode=1`

§17 抓到的旧形态是 `resource=web state=Running ×4 → Finished exitCode=1`。这次整份日志 44 行里：**`web` 的 `Finished` 出现 0 次、`exitCode` 非 `-` 出现 0 次**，`state=Running` 稳定出现在最后三条。加上 21.2 的 `/` 回包，可以判断 A5 在运行侧也过了。

本次**未**覆盖：`onShellError` 分支在 DCP 下是否会出现（这轮 web 全程正常，没触发失败路径）。

### 21.6 结论

- **A5**：修后 aspire 下 web 稳定 `Running`、`/` 正常回包、无 TDZ、无 `Finished exitCode=1`。结合 §20 的本地①②③，A5 按「已修复」收。
- **A11 web 腿**：正向证据本轮拿到（runId 匹配 + 由本次运行拉起）。老何要求的外来实例反例仍按原计划单独验，不在本节。
- 证据文件：`C:/tmp/aspire-diag-5682cd0.log`（AppHost 原日志）、`C:/tmp/aspire-copy-5682cd0.log`（脚本输出）。
- 本节锚 `5682cd0`，但**跑的是副本**：主仓 `e834342` 因 21.1 的 `.aspire-tmp` ACL 问题跑不了 aspire。等主仓那个目录被清理掉，需要在主仓复跑一次才能把这条写成「主仓绿」。

## 22. vite **进程**真实环境 @ `e834342`：`NODE_OPTIONS` 是 null

### 22.1 为什么要做这一轮：快照 ≠ 进程环境

张一一在 `e834342` 的 `vite.config.ts` 注释里点出一件我 §21.3 说得不够准的事：`[pzd-diag] env` 读的是 `snapshot.EnvironmentVariables`，那是**资源声明**的环境，supervisor 在运行期塞进去的值不会出现在快照里。所以「快照里没有某项」**不能反证**「进程里没有」。

按这条，我 §21.3 里「diag 读到 `PZD_RUN_ID`」只能算「资源声明里配了」，真正证明它进了进程的是 `/` 的响应头（那条证据是硬的，结论不变，只是**证据等级要写准**）。

这一轮专门取进程真实值：在副本的 `apps/web/vite.config.ts` 里加一个**写文件**探针（只写单值、不含秘密），绕开 DCP 的资源 stdout 通道——实测那条通道读不到，`[pzd-web]`/`[pzd-probe]` 的 `console.log` 一条都没进 AppHost 日志。

### 22.2 结果（`C:/tmp/pzd-env-probe-e834342.json`）

```json
{
  "NODE_ENV": "development",
  "NODE_OPTIONS": null,
  "PZD_RUN_ID": "003505dd9345465f95d1a110c2c55060",
  "PORT": "5199",
  "GLOBAL_PID": 87816
}
```

同一次运行 `EXIT=0`，脚本侧：

```
本次运行标记 X-Pzd-Run-Id=003505dd9345465f95d1a110c2c55060
[ok] /api/version 0.1.1-alpha.0.182+e834342
[ok] X-Pzd-Run-Id 与本次运行一致（003505dd9345465f95d1a110c2c55060）
[ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 87816），且是 SPA 外壳
```

**三重一致**：探针文件里的 `PZD_RUN_ID` = 脚本本轮 runId = `/` 的响应头；探针文件里的 `GLOBAL_PID` 87816 = 脚本判定的监听 pid。这条把 A11 web 腿的归属彻底钉住了——不是快照里配了，是**那个 pid 的进程里真的有这个值，并且用它回了头**。

### 22.3 对 A5 的判决：`NODE_OPTIONS` 这条线索排除

**`NODE_OPTIONS` 在 vite 进程里是 `null`**，而快照里也没有这一项。所以：

- `--conditions=react-server`、`--require`、`--loader` 这些东西**根本不在 DCP 拉起的 vite 进程里**；
- 旺总实测过 `react-server` 条件会让进程**在监听之前**就崩（`React2.createContext is not a function`），而 A5 是「Running 几秒后 exit 1」——形态本来就不合；现在又有了进程侧的 `null`，这条可以从 A5 的候选里划掉；
- 配合旺总那两条 Vite 侧排除（RRD 没有把 `react-dom` 塞进 SSR bundle、`.vite` 下没有 `deps_ssr`，所以走 `externalize → node require`），「Vite 优化器/npm 条件把 browser 构建塞给 SSR」整条也不成立。

A5 剩下的候选只能在**应用自己的模块图**里，根因仍标待定位。**§18.3 的反向控制仍然有效**（普通渲染错误炸不出 TDZ），所以范围是「entry 加载期或调用点附近的同步抛」，而不是渲染行为。

### 22.4 本轮其他事实

- **预检是对的**：第一次跑 `exit 2`（`端口 5199 已被占用（pid 66568 node）`），没有误杀、没有把占用当成票的缺陷。占用者是 `E:\pzd-a5pre\apps\web`，别人的 A5 验证副本，我等它释放（5s）后重跑。
- **探针踩了两个坑**，记下来省得复现：① `require` 在 ESM 的 `vite.config.ts` 里不可用，异常被 `try/catch` 吞掉，表现是「文件没生成」而不是报错；改成顶层 `import { writeFileSync } from "node:fs"` 才行。② DCP 下**资源 stdout 拿不到**，`console.log` 探针这条路在本机不可用，要取证只能写文件。
- 探针与副本已全部清理，`C:/tmp/pzd-asp2` 已删，端口空闲。**主仓未改一行**：所有探针都在副本里，主仓 `apps/web/vite.config.ts` 保持 `e834342` 原样。
- 主仓 `.aspire-tmp` 的 ACL 阻塞（§21.1）依然存在，本节同样是在**副本**里跑的。

## 23. 锚点可见性与 `onShellError` 状态码 @ `db17a9b` 副本（端口 5211，不碰 5080/5199）

### 23.1 诊断锚点在本地是好的——所以 aspire 下看不到是通道问题

`4485ca0` 之后 entry 里有两个 `console.log` 锚点（`:45` 模块加载期、`:80` 调用前）。在副本上单独起 dev（5211，注入 `PZD_RUN_ID=anchor-check-815`）：

```
[pzd-web] entry loaded（NODE_ENV=development）          ← :45 命中
[pzd-web] 首次文档渲染：即将调用 renderToPipeableStream   ← :80 命中
[pzd/entry.server]                                       ← 未命中（正常路径本就不该有）
GET / -> 200   X-Pzd-Run-Id=anchor-check-815 == 注入值
```

**结论：锚点代码正确、本地能打。** 那么 aspire 下取不到它，原因只能是 §22.4 那条：DCP 拿不到 web 资源 stdout。这条很重要——否则「aspire 跑完没有任何 `[pzd-web]` 行」会被误读成「模块图加载失败」，而实际上可能是通道丢了。

顺带一个判读细节：`[pzd-web] NODE_ENV=development` 会**出现两次**（vite 配置加载一次、SSR 模块重载一次），同一行出现两遍不代表请求执行了两遍。

### 23.2 `onShellError` 路径：`/` 是 **500**，不是 200

张一一在 `bf08bd4` + 未提交变异上看到 `/` 返回 **200**，而老何验收第①条要的是「显式 500 + 脱敏日志」。我在 `db17a9b` 副本上造同形状变异（把渲染目标换成 `ProbeThrows`，走异步 `onShellError`）：

```
[1] HTTP '500'
[2] HTTP '500'
[3] HTTP '500'

HTTP/1.1 500
Vary: Origin
X-Pzd-Run-Id: shell-err-952        ← 500 响应上归属头仍在（中间件对错误响应也生效）
content-type: text/plain

[+3s] 监听=yes  [+6s] 监听=yes  [+9s] 监听=yes
[pzd/entry.server] 文档外壳渲染失败（这是真错误，不是 abort TDZ）： Error: pzd-probe: 外壳渲染失败（onShellError 路径）
无 abort TDZ / 无超时兜底触发
```

**三点**：

1. `db17a9b` 上 `onShellError → reject → 500`，**老何第①条（显式 500）是满足的**；
2. 500 响应上 `X-Pzd-Run-Id` 还在，说明归属头不因错误响应丢失；
3. 进程存活、无 TDZ、超时兜底没被触发（因为 `onShellError` 已经先 reject 了）。

**差异待张一一确认**：他测的是 `bf08bd4`（`onShellError` 里还没有 `clearTimeout`，也没有超时兜底 reject），我测的是 `db17a9b`（含 `4485ca0` + `331fac3`）。200 与 500 的差别如果确实由这两笔引起，那 `4485ca0` 顺带修掉了「失败却回 200」；如果不是，那他那次变异的形状可能不是 `onShellError` 路径（例如外壳已渲染后的 `onError`，响应头早已发出，状态码改不动）。**结论按 hash 分家，别把 `bf08bd4` 的行为记到 `db17a9b` 头上。**

### 23.3 端口与环境

- 本轮全程没碰 5080/5199：`5080` 被 pid 49688（`dcp.exe`，kubeconfig 指向 `E:\pzd-a5pre\.aspire-tmp`）占、`5199` 被 pid 56392（node，`E:\pzd-a5pre\apps\web`）占，两个父进程都在、是**活实例**，23:52:24 起持续占用。不属我的清理范围，未杀。
- 因此 **aspire 全链路本轮仍未在 `db17a9b` 上跑**（预检正确判 `exit 2`）。副本 `C:/tmp/pzd-final`（`db17a9b`）已还原为干净状态、5211 已释放，端口一空即可跑。
- 船上所有变异都在副本里做、做完 `git checkout` 还原；**主仓未改一行**。

## 24. aspire 全链路通过 @ `db17a9b`，并**证伪**「没有 entry 行 ⇒ 错误在入口之前」

### 24.1 全链路（端口终于空出来，第一次在含 `4485ca0`+`331fac3` 的版本上跑通 aspire）

副本 `C:/tmp/pzd-final`（`git worktree` 自 `db17a9b`，已删），`-Mode aspire -TimeoutSeconds 300`，**`REAL_EXIT=0`**：

```
本次运行标记 X-Pzd-Run-Id=3d53956e60ab4a95ab85216e81f505f0
[ok] /health/live 200（免租户）
[ok] /health/ready 根状态 = Healthy
[ok] /health/ready mysql = Healthy
[ok] /health/ready redis = Healthy
[ok] /api/version 0.1.1-alpha.0.186+db17a9b
[ok] X-Pzd-Run-Id 与本次运行一致（3d53956e60ab4a95ab85216e81f505f0）
[ok] / 无租户仍 400
[ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 111232），且是 SPA 外壳：由本次运行拉起
```

### 24.2 判据被证伪：**web 完全正常时，`[pzd-web]` / `[pzd/entry.server]` 同样是 0 行**

同一份 `artifacts/verify-apphost.log`（44 行）里：

| 前缀 | 命中行数 |
|---|---|
| `[pzd-diag] env` | 4（`NODE_ENV` / `PATH` / `PORT` / `PZD_RUN_ID=3d53956e…`） |
| `[pzd-diag] resource=web state=Running` | 4 |
| **`[pzd-web]`（`:45`、`:80` 两个锚点）** | **0** |
| **`[pzd/entry.server]`** | **0** |

而这次运行是**成功**的：web 全程 `Running`、`/` 由 pid 111232 正确回包、runId 一致、`EXIT=0`。也就是说 entry 必然被加载、`handleRequest` 必然被调用、`:80` 那个锚点必然执行过——**但一行都没进 AppHost 日志**。

**所以旺总那条三分法的第二层不成立**：

> 「只有 `[pzd-diag] env`、没有 entry 行 → 真错误在入口之前（模块加载期）」

不成立。**「没有 entry 行」在完全成功的运行里同样出现**，它对「错误发生在入口之前还是之后」**没有任何区分力**。当前 DCP 通道下，`[pzd-web]` 和 `[pzd/entry.server]` 这两类信号在 AppHost 日志里根本不存在，拿它做判据会把「一切正常」误判成「模块加载期失败」。

三分法现在只有两层可用：

- `[pzd-diag] env` 有 + **web `state=Running`** → DCP 确实把 web 拉起来并给了环境（本条本轮成立）；
- 两个都没有 / web 从未 `Running` → DCP 没把 web 拉起，第三类故障，与 A5 不同一条（本轮 §21.1 的 ACL 问题就属于这类，但表现是宿主直接退出）；
- **「有没有 entry 行」这一层，在把锚点改成写文件之前，不可用。**

### 24.3 要用这条判据，先解决可见性

两个锚点要变成 aspiration 下可读的信号，只有两条路（§23.1 已证锚点代码本身是对的）：

1. 锚点改成 `writeFileSync` 写到固定路径——我 §22 用这个方法拿到过 vite 进程真实环境，可行；
2. 回到 `pnpm.CMD` wrapper 抓 stderr——§17 的 TDZ 栈就是这么抓到的。

任选其一之前，**`[pzd-web]` / `[pzd/entry.server]` 不能写进任何验收判据或交接**，否则下一个人会拿「没看到」当成证据。

### 24.4 累计状态

- `db17a9b` 上 aspire 全链路通过（第 2 次 aspire 绿，第 1 次是 `5682cd0`；两次都在**副本**里，主仓 `.aspire-tmp` 的 ACL 阻塞仍在）。
- A5 在运行侧持续正常：web `Running`、无 `Finished exitCode=1`、`/` 正常回包。
- A11 web 腿正向证据在 `db17a9b` 上再次成立（runId 一致 + 由本次运行拉起）。
- 主仓 `apps/web/`、`src/` 我一行未改；副本与端口均已清理。

## 25. A5「那行真错误」在 aspire 下不存在——**三个错误出口一次都没被触发** @ `9e92728`

### 25.1 做法：把 entry 的错误出口全部落盘（绕开 stdout）

既然 DCP 拿不到资源 stdout（§24.2），「等 aspire 里出现 `[pzd/entry.server]`」这条路本身就走不通。我反过来做：在副本 entry 里给**三个错误出口**各装一个 `appendFileSync` 探针（定义 + `onShellError` + `onError` 的 shellRendered 分支 + 同步抛出 catch），然后跑 aspire。

- 文件生成 → 有错误发生，里面就是真错误与栈；
- 文件不生成 → **三个出口都没被触发**。

### 25.2 结果：`EXIT=0`，错误文件**没有生成**

```
REAL_EXIT=0
(未生成 → 三个错误出口都没被触发)
```

全链路同前两轮：`/health/ready` 三项 Healthy、`/api/version` 对应当前 hash、`/` 回包、前端 runId 一致。

### 25.3 先验探针本身，不让「没生成」变成假阴性

不验证探针就可能把「探针坏了」读成「没有错误」。所以在副本里加了一条**条件式强制同步抛出**（只在请求带 `x-pzd-force: 1` 时抛），再打一次：

```
GET / (带 x-pzd-force: 1) -> HTTP '500'

=== 探针文件 ===
[有效] 文件已生成，探针工作正常：
[2026-10-27T16:00:45.475Z] sync-throw: pzd-probe-validate: 强制同步抛出
Error: pzd-probe-validate: 强制同步抛出
    at eval (…/apps/web/app/entry.server.tsx:54:61)
    at new Promise (<anonymous>)
    at handleRequest (…/apps/web/app/entry.server.tsx:42:10)
    at renderHtml (…react-router…/chunk-H4DAEOV7.mjs:1510:20)
```

探针工作正常（写文件、带栈、`500`）。**先验探针，再信结论**——这一步不能省。

### 25.4 结论：根因**当前不复现**，不是「已找到」

- A5 的**症状**已经消失：aspire 下 web `Running` 稳定、`/` 回包正常、无 `Finished exitCode=1`、无 TDZ；
- 但**根因没有被抓到**——三个错误出口一次都没触发，说明 `9e92728` 上那次运行里 `renderToPipeableStream` **根本没有同步抛出**，外壳渲染也没有失败；
- 所以「entry 修好了根因」这个说法**不成立**：entry 只是让同类失败不再变成「6 秒后 TDZ 崩进程」，而这次连失败本身都没有发生。

按老何的口径，根因继续标**待定位**。同时提醒全群：**不要再安排「等 aspire 里出现 `[pzd/entry.server]` 那行」的任务**——当前通道不会产出它，而落在 `9e92728` 上它压根不会发生。要重新抓根因，得先让触发条件复现（例如回到能稳定复现的 `6b67c2d` 形态），而不是继续在同一套判据上等。

### 25.5 三条判据的最终状态

| 判据 | 状态 |
|---|---|
| ① 真错误可见 | **PASS**（§20.3 / §23.2：两条失败分支都能立刻打出原对象） |
| ② 失败路径不留定时器 | **PASS**（`4485ca0`：三出口清；不补也良性，见 §20.4） |
| ③ SSR 超时保护仍在 | **PASS**（代码判定 + `4485ca0` 超时兜底 `reject`；「流永不 ready」的实跑仍由锋哥的 `streamTimeout=200` 变异覆盖，我没独立复跑） |
| 根因定位 | **未完成 / 当前不复现**（§25.4） |

主仓未改一行；副本与端口已清理；证据 `C:/tmp/aspire-verify-9e92728.log`、`C:/tmp/pzd-entry-errors-validate.json`。

## 26. 证伪「DCP 给 web 注入 `NODE_ENV=production`」@ `7f8b11a`

> **⚠️ 本节结论已被 §27 推翻，原文保留作为方法教训。**
> 失败原因是本节三次取样（`e834342` / `db17a9b` / `9e92728`）**全都已经带着 `WithEnvironment("NODE_ENV","development")`**，
> 读到的 `development` 是我们自己钉进去的值，不是 DCP 的原值。§26.4 要求的那条「DCP 侧读到 production 的实证」已在 §27 给出。

交接里（`handover-01-04.md:147`）把这个写成了「**根因已定位**：DCP 给 web 注入 `NODE_ENV=production` → `jsxDEV is not a function` 在调用点同步抛出」。机制部分我复现了，但**归因部分被实测否掉**。

### 26.1 先证明我的探针可信（否则反证无效）

我此前在 DCP 下读到 `NODE_ENV=development`，但如果 vite 会在加载配置前把父进程的值覆盖掉，那个读数就不能用来反证 DCP 传了什么。所以先做对照（副本 `7f8b11a`，端口 5211，`vite.config.ts:31` 的探针原样使用）：

```
=== [prod] 父进程 NODE_ENV=production ===
  GET / -> HTTP '500'
  [pzd-web] NODE_ENV=production          ← 读到 production，vite 没有覆盖
  [pzd/entry.server] renderToPipeableStream 同步抛出： TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
  无外壳失败

=== [unset] ===
  GET / -> HTTP '200'
  [pzd-web] NODE_ENV=development
  无同步抛出
```

**两点结论**：

1. **vite 不覆盖父进程传入的 `NODE_ENV`**（传 production 就读到 production）→ 我在 DCP 下读到的 `development` **是真实值**，探针可信。
2. **锋哥那条机制我独立复现了**：`NODE_ENV=production` 下 `jsxDEV is not a function` 在调用点同步抛出、`/` 回 500。机制成立。

### 26.2 但 DCP 下不是 production

- 进程级探针（`e834342`，`process.env` 直接落盘）：`NODE_ENV: "development"`；
- 三份 `[pzd-diag] env`（`e834342`/`db17a9b`/`9e92728`）：`NODE_ENV=development`。

**两路独立证据都指向 development，且 §26.1 已证明这个读数不会被 vite 覆盖掉。** 所以「DCP 给 web 注入 `NODE_ENV=production`」**没有 DCP 侧实证**——把手工 `NODE_ENV=production` 能复现，当成 DCP 真的这么干，是把「可复现的机制」偷换成「实际发生的原因」。

### 26.3 正确的分层表述

| 层 | 状态 | 依据 |
|---|---|---|
| **机制** | 已定位 | `NODE_ENV=production` → `jsxDEV is not a function` 在调用点同步抛出 → `abort` 停 TDZ → 6000ms 后 `ReferenceError` 带崩进程。锋哥与我都复现了 |
| **DCP 下的触发条件** | **未证实 / 实测相反** | DCP 拉起的 vite 是 `development`，这条链在 DCP 下**没有走过** |
| **`6b67c2d` 那次真实崩溃的根因** | **仍未定位** | 若 DCP 一直是 development，那次崩溃就来自另一个原因；§25.4 的「当前不复现」也由此得到解释 |

**这条改正很重要**：写「根因已定位 = DCP 注入 production」会让接手的人去查一个不存在的注入，而真正的原因（`6b67c2d` 那次为什么崩）仍然悬着。建议交接改成：「机制已定位（production 下 `jsxDEV` 缺失导致调用点同步抛出）；DCP 下实测为 development，触发条件未证实，`6b67c2d` 的真实根因待定位。」

### 26.4 边界

- 我能证明的是**我跑的那三次 DCP 运行**里是 `development`；如果存在某种让 DCP 注入 production 的配置路径，那它在我这三次里没有生效。要推翻这节，需要给出一条 DCP 侧读到 production 的实证（在 entry 内或 vite 进程内读 `process.env.NODE_ENV` 并落盘），不能用手工设置代替。
- 证据：`C:/tmp/nodeenv-prod-proof.log`、`C:/tmp/nodeenv-unset-proof.log`；副本已删，主仓未改。

## 27. A5 结案：删掉钉住即复现 500（反做对照，进程级证据）@ `62b80ed`

> **标题口径已修订（2026-10-28，见 §28）**：本节初版标题与 §27.3 结论写的是「DCP 确实给 web 注入 `NODE_ENV=production`」，**这个归因已撤回**。§28 的单变量实验证明：这个值随 **AppHost 的宿主环境**改变（`DOTNET_ENVIRONMENT=Development` → web 读到 `development` 且全绿）。所以能写的是「未钉住时 web 进程读到 `production`」，**不能写「DCP 注入」**；产生位置（Aspire JS 集成装配资源模型时 / DCP 应用环境块时）仍未区分。本节其余数据（runId、探针原文、时序、`_out` 原文）不变，仍有效。

### 27.1 为什么 §26 被推翻

§26 用来反证的三次取样（`e834342`、`db17a9b`、`9e92728`）**每一棵树上都已经有 `WithEnvironment("NODE_ENV","development")`**：

```powershell
git show e834342:src/Pzd.AppHost/Program.cs | Select-String 'WithEnvironment\("NODE_ENV"\)'  → 命中 1
git show db17a9b:src/Pzd.AppHost/Program.cs → 命中 1
git show 9e92728:src/Pzd.AppHost/Program.cs → 命中 1
```

所以那三次读到的 `development` 是我们自己钉进去的值，**不是 DCP 的原值**；拿它去反证「DCP 注入 production」，等于用被测修复去证明缺陷不存在。唯一没有钉住的 `6b67c2d` 连「环境变量共 N 项」这段快照代码都没有（命中 0），所以「钉住之前的 aspire 现场」在存档里一次都不存在，也不可靠重跑 HEAD 补出来——只能**反做**。

### 27.2 实验设计（唯一变量：那一行钉住）

- 副本：`git worktree add --detach C:/tmp/pzd-fin 62b80ed`，`cp local.env`，`pnpm install --no-frozen-lockfile`；
- **A 组**：不动，保留 `src/Pzd.AppHost/Program.cs:85` `web = web.WithEnvironment("NODE_ENV", "development");`
- **B 组**：只把 `:85` 注释掉，其余文件、命令、参数完全一致；
- 读数取**进程级**真值：`apps/web/vite.config.ts:31-49` 的落盘探针把 `process.env.NODE_ENV` 写进 `artifacts/pzd-web-env.log`（当时误以为「aspire 下 web 的 stdout 不落任何日志」，见 §24 与 §27.10 的更正：它落在 DCP 会话目录 `<TEMP>\aspire-dcp<id>\<uuid>_out`。落盘探针仍然保留，理由是它**带 runId、可按轮切分**，而会话目录会在正常退出后被清理）；
- 每轮完整跑 `pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 300`。

### 27.3 结果：两组由 runId 分开，变量就是 `NODE_ENV`

| 组 | `PZD_RUN_ID` | vite 进程真值 | `GET /` | 验收 |
|---|---|---|---|---|
| A（钉住） | `e8c2d03f41a04ed09c9b0b281cf21a17` | **`NODE_ENV=development`** | **200**，`lang="zh-CN"` 的 SPA 外壳 | 8 条 `[ok]` 全过，`EXIT=0` |
| B（删钉住） | `f6b32412c3464c40a99c0cdf802bb9a6` | **`NODE_ENV=production`** | **500**，等满 300s 超时 | `/` 腿失败，`EXIT=1` |

```
# artifacts/pzd-web-env.log（原样，按 runId 分）
2026-10-27T16:06:04.632Z cwd=C:\tmp\pzd-fin\apps\web NODE_ENV=development PZD_RUN_ID=e8c2d03f41a04ed09c9b0b281cf21a17
2026-10-27T16:07:27.712Z cwd=C:\tmp\pzd-fin\apps\web NODE_ENV=production  PZD_RUN_ID=f6b32412c3464c40a99c0cdf802bb9a6
```

两侧都是 **`[pzd-diag] apphost 自身 NODE_ENV=<unset>`**，即宿主进程本身没有这个变量，仓库里除白名单常量外也没有任何地方设置它——所以 `production` 是在 AppHost 与 web 资源之间被注入的，不是继承、不是我们代码写的。

B 组的崩溃形态（对照 §17 的 `6b67c2d`）：

- web 进程**没有退出**：`Finished`、`exitCode=1`、`Cannot access 'abort'` 命中 **0**；
- 说明自定义 entry 的同步 `catch` 把这次抛出兜成了 500、进程存活（锋哥 ②③ 在真实 DCP 环境成立）；
- 而 `6b67c2d` 那次的「`Running` → `Finished exitCode=1`」正是**同一个 production 注入 + 默认 entry**：同步抛出 → `abort` 停 TDZ → 6000ms 后带崩。

→ **A5 根因确认：钉住被删掉后，web 进程读到的是 `NODE_ENV=production`**（进程真值，stdout 与会话目录两路互证），`37bd872` 的显式钉住是修复而非无害声明。**归因措辞按 §28：不写「DCP 注入」**——这个值由宿主环境决定，产生位置尚未区分。

**补一条：A 组那一轮「当时有没有 pin」不必靠文件状态回答。** 该轮（16:07:27，runId `f6b32412…`）**没有留当轮的 `git diff`**，事后按现查状态或 `mtime` 反推历史的做法一律不算数（这条纪律见 §28.3）。但同一份探针文件里，同一棵树相隔 **83 秒**的两轮读数是互斥的：

```
16:06:04.632Z NODE_ENV=development PZD_RUN_ID=e8c2d03f41a04ed09c9b0b281cf21a17   ← A 组（pin 在）
16:07:27.712Z NODE_ENV=production  PZD_RUN_ID=f6b32412c3464c40a99c0cdf802bb9a6   ← B 组（反做）
```

而 `62b80ed:85` 本身就是**活代码 pin**，「pin 生效 ⇒ web 读到 `development`」不是假设——A 组这一轮自己就是证据（§28.2 的 Development 宿主轮、§28.1 的 `db17a9b` 同一结论）。因此 B 组读到 `production`，只能意味着**当轮 pin 未生效**。这是「两轮进程读数互斥」，不是「用现状反推历史」。

旁证（不依赖文件状态）：`pzd-fin/.aspire-tmp/` 里**只剩失败那一轮**的会话目录（`aspire-dcpi4124qsy.vok`，web `_out` 三条 16:07 读数），成功那一轮（16:06）的会话目录已在正常退出时被清理——两轮确实是两次独立运行，与 §28.2 边界 2 一致。

**归档取舍（老登 2026-10-28 核后定稿，我采纳）**：A 组**保留为主反向证据**，不换成 `331fac3-nopin`——理由是它比那份更严格：除 `_out` 读 `production` + `_err` 抛 `jsxDEV` 之外，还多了**同毫秒双通道对齐**与**目录 mtime 包住该轮**：

```
.aspire-tmp/aspire-dcpi4124qsy.vok/   目录 mtime 00:07:26.950（该轮启动）
  d973dad1-…_out  2106 B  mtime 00:07:29.244
    :1   16:07:27.712Z [pzd-web] NODE_ENV=production
    :20  16:07:29.151Z [pzd-web] entry loaded（NODE_ENV=production）
    :21  16:07:29.244Z [pzd-web] 首次文档渲染
  d973dad1-…_err  2496416 B  mtime 00:12:28
    :2   16:07:29.247Z [pzd/entry.server] 同步抛出：… jsxDEV is not a function
    :3   at eval (C:/tmp/pzd-fin/apps/web/app/entry.server.tsx:39:113)
```

三条锁：`_err:2` 的栈路径把该轮锁到 `C:/tmp/pzd-fin` 这棵树；目录到 `_out` 的 mtime 区间包住整轮；**三个 uuid 是同一轮的三个资源，不是三轮**——由各自 `_out` 首行判定身份，时间戳全落在 `16:07:26.880 ~ 16:07:29.244` 同一窗口、同属一个 dcp 会话目录。引用时写「该轮文件状态不可考，本行不依赖它」。`331fac3-nopin`（有当轮 `git diff`，2 增 1 删）并列作**第三份独立反向证据**。

**本条初版写错过，照实记下（旺总、张一一、老登先后指出，已按全量输出核对）**：初版写「目录内只有 `d973dad1` 有内容（另两个 uuid 的 `_err` 均 0 字节）」。后半句数字是对的（`95ef2ce1_err`、`c27d2d80_err` 确实都是 **0 B**），但**前半句错**：那两个 `_out` 分别有 **10309 B**（api 的 Quartz 日志）与 **2354 B**（Aspire dashboard），一查就露。所以「单轮」不能靠「只有一份有内容」这种弱条件立，只能靠**资源维度 + 时间窗**——改完之后这条锁定反而更强了。全量输出：

```
95ef2ce1-…_out  10309 B  00:12:00   :1 16:07:27.922Z [00:07:27 INF] Quartz SchedulerSignaller   ← api
95ef2ce1-…_err      0 B  00:07:26
c27d2d80-…_out   2354 B  00:07:27   :1 16:07:26.880Z Aspire.Dashboard.DashboardWebApplication  ← dashboard
c27d2d80-…_err      0 B  00:07:26
d973dad1-…_out   2106 B  00:07:29   :1 16:07:27.712Z [pzd-web] NODE_ENV=production              ← web
d973dad1-…_err 2496416 B  00:12:28   :1 16:07:27.100Z $ react-router dev --port 5199
```


补充时序细节：`_err` 的 mtime `00:12:28` 比 `_out` 晚 5 分钟——那一轮 web 进程活到 00:12 还在反复抛 `jsxDEV`（`_err` 2.4 MB 的来源），进程未崩。不影响结论，但把该轮时间跨度补全了。



### 27.4 注入源定位到托管库，不是 DCP 二进制

.NET 程序集的用户字符串是 **UTF-16LE**，用 ASCII 搜会得到假阴性：

| 文件 | `NODE_ENV` 命中 | 同区字面量 |
|---|---|---|
| `aspire.hosting.javascript/13.6.4/lib/net8.0/Aspire.Hosting.JavaScript.dll` | UTF-16 = **1**（ASCII = 0） | `production` / `development` 各 1 |
| `aspire.hosting/13.6.4/lib/net8.0/Aspire.Hosting.dll` | UTF-16 = 0 | — |
| `aspire.hosting.orchestration.win-x64/13.6.4/tools/dcp.exe` | ASCII/UTF-16 = **0** | `production` 1、`development` 5（与 NODE_ENV 无关） |

→ 字面量在 `Aspire.Hosting.JavaScript`（`AddViteApp` 一侧），**不是** `dcp.exe`，也不是项目代码。**边界**：这是字符串/元数据层面的定位，没反编译 IL；`dcp.exe` 命中 0 也不排除它只是运行期转发环境块。§28 的实测把上游收敛到「宿主环境」，产生位置仍未区分。

### 27.5 A11 web 腿正向判据：三次实证（此前记的「未出现」应作废）

| 运行 | 本次 runId | 5199 pid | 结果行 |
|---|---|---|---|
| `5682cd0` | `9ef57153c7174b0481330bd0a187e302` | 58668 | `[ok]` 前端 `X-Pzd-Run-Id` 与本次运行一致，SPA 外壳 |
| `db17a9b` | `3d53956e60ab4a95ab85216e81f505f0` | 111232 | 同上 |
| `62b80ed` | `e8c2d03f41a04ed09c9b0b281cf21a17` | 42816 | 同上 |

判据是「脚本本次生成、只有本次进程拿得到的 runId」在 `5199` 响应头原样回显，且 pid 为本次拉起、`/` 是 SPA 外壳（不是错误页）。

**但必须带前提写**：这三次都在 `NODE_ENV` 钉住之后；同一副本删掉 `:85` 的 B 组，前端腿直接失败（`/` 全 500、300s 超时）。所以准确表述是「**A11 web 腿在 `NODE_ENV` 钉住后成立，三次实证**」，不是无条件通过。

### 27.6 边界与遗留

1. **注入的触发条件未验**：`Aspire.Hosting.JavaScript` 里 `NODE_ENV` 与 `production`/`development` 三者在同一段字符串区，符合「按宿主环境模式选值」（`builder.Environment.IsDevelopment()`）。要锁死需补一条对照：`DOTNET_ENVIRONMENT=Development` 起 AppHost 且**保留**钉住，若 web 进程真值仍为 `development`，说明两条各自独立生效；若需删钉住才变，则说明钉住覆盖了库值。**本轮未跑，不写成结论。**
2. **快照 vs 进程**：本实验两路读数一致（快照 `production`、进程 `production`），但这只说明**这一例**一致，不能推广成「快照永远可信」；旺总那条「`snapshot.EnvironmentVariables` 是资源声明」的限制仍然成立，写进 §27.3 的仍是进程级读数。
3. **超时保护未按真实值实跑**：锋哥那次是 `streamTimeout` 临时改 200ms，证明「到点显式中止而不是挂死」，**不是**按真实 6.2 秒验证过。
4. **DCP 可用性实测是不稳定的，不能按「主仓/副本」简单二分**（本节据 00:22–00:26 的四次运行更正上一版写法）：
   - 副本 `C:/tmp/pzd-fin`（新版脚本 + `-TempRoot C:/tmp/pzd-temproot`）→ `EXIT=0`，8 条 `[ok]`，`s+62b80ed`；
   - 主仓 `E:\pzd`（**同一份脚本、同一个 TempRoot、`git diff 62b80ed 82654d9 -- src/ apps/` 为空即代码一致**）连跑两次 → 均 `EXIT=1`，卡在 `Aspire.Hosting.Dcp.Locations.GetOrCreateDcpSessionDir` → `Directory.CreateTempSubdirectoryCore` 的 `UnauthorizedAccessException`（`Access to the path '…\aspire-dcp….cnx' is denied`），宿主 exit `-532462766`；
   - 为排除脚本因素，主仓**手动**起宿主（`TEMP/TMP` 显式指向新建空目录，不经脚本）→ 复现同一异常，且先出现 `AuxiliaryBackchannelService` 的 `Win32.SetSecurityInfo` 拒绝；
   - 因此上一版「残留目录清掉后主仓可复跑」**已被证伪**：残留清掉后主仓仍 2/2 失败。共同点是**写安全描述符的操作（SACL / 临时目录）被拒**，与仓库位置、脚本版本、TempRoot 盘符都无关，属环境层不稳定，机制仍待定位。
   - 结论边界：**「副本绿」不能升级成「主仓绿」**，A11 的「主仓复跑」这条门禁本会话内没有拿到。

### 27.7 证据文件

`C:/tmp/aspire-fin-62b80ed.log`（A 组脚本输出）、`C:/tmp/aspire-nopin.log`（B 组脚本输出）、`C:/tmp/pzd-fin/artifacts/verify-apphost.log`（B 组宿主日志）、`C:/tmp/pzd-fin/artifacts/verify-apphost-pinned-62b80ed.log`（A 组宿主日志）、`C:/tmp/pzd-fin/artifacts/pzd-web-env.log`（两组进程级 `NODE_ENV` 原值）。副本 `C:/tmp/pzd-fin` 保留，`src/Pzd.AppHost/Program.cs` 已 `git checkout` 还原、工作区 0 改动。

### 27.8 独立复核：真实 pre-pin 提交 `90ee1b2` + 签名原文（不改代码，只加探针）

§27.2 是「从当前树里删掉钉住那一行」的反做；为排除「改一行引入的偏差」，再用**真实历史提交**复核一次：

- 树：`git worktree add --detach C:/tmp/pzd-pre 90ee1b2`（`WithEnvironment("NODE_ENV")` 命中 **0**，且树内**没有任何** `NODE_ENV` 锚点）；
- 唯一改动：`apps/web/vite.config.ts` 加 20 行落盘探针，写**绝对路径** `C:/tmp/pzd-pre-env.log`（不依赖 `cwd`，不依赖 stdout）；
- 命令与 A/B 完全相同：`pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire -TimeoutSeconds 300`。

结果：

```
# 探针（vite 进程真实值）
2026-10-27T16:16:34.020Z NODE_ENV=production PZD_RUN_ID=0e0d94ca8e08485886997cf666631c73

# 快照同值
[pzd-diag] env NODE_ENV=production

# 验收
[ok] /api/version 0.1.1-alpha.0.176+90ee1b2        ← 确实跑的是这棵树
[ok] X-Pzd-Run-Id 与本次运行一致（0e0d94ca…）/ /health/ready 根、mysql、redis 均 Healthy
验收失败：等待 http://127.0.0.1:5199/ 超时（300s），最后一次错误：HTTP 500
EXIT=1
```

再在同一棵树上手工以 `production` 起 dev（`5216`），拿错误原文，补上「签名级」这一格：

```
HTTP=500
[pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）：
  TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
```

→ **签名级**（`jsxDEV is not a function` 原文）与**进程级**（`NODE_ENV=production`）在同一棵 pre-pin 树上同时成立。

三种状态汇总（互不重叠）：

| 状态 | vite 进程真值 | `GET /` | 错误原文 |
|---|---|---|---|
| pre-pin `90ee1b2`，DCP 拉起 | `production` | 500 | —（stdout 被 DCP 接管，落不到日志） |
| pre-pin `90ee1b2`，本地 `NODE_ENV=production` | `production` | 500 | `jsxDEV is not a function` |
| pinned `62b80ed` / `5682cd0` / `db17a9b`，DCP 拉起 | `development` | 200（SPA 外壳） | 无 |

两次复核方法互相独立：§27.2 改代码反做，§27.8 不改代码、用真实历史提交，结论一致。

### 27.9 A11 反例：同 hash 外来实例（5199）不会被当成本次运行

判据要能被反例打穿才算判据，所以补一条负向控制：

- 位置与 hash：`C:/tmp/pzd-fin`（`62b80ed`，与正向证据同一个 hash 家族），占用 5199；
- 外来说明：以 `PZD_RUN_ID=probe-foreign-abc123` 起 `pnpm --filter @pzd/web dev --port 5199`，pid **95796**，响应头实测 `X-Pzd-Run-Id: probe-foreign-abc123`；
- 在同一目录跑 `verify-apphost.ps1 -Mode aspire`，脚本本次生成的 runId 为 `7f4b32da78204aa48861df099ac3b0a6`。

结果：

```
[preflight] 端口 5199 已被占用（pid 95796 node）。先关掉占用进程再跑，否则验到的是别人实例的结果。
[preflight] 这是环境/占用问题，不是本票缺陷：清掉占用或装好依赖后重跑。
EXIT=2
```

两层都成立：

1. **预检先挡住**，退出码 `EXIT=2` 与老何定的三档（`0` 通过 / `1` 验收失败 / `2` 环境预检受阻）一致——不把外来实例的结果记成本次通过；
2. **即便绕过预检，两条判据也能区分**：响应头 `probe-foreign-abc123` ≠ 本次 `7f4b32da…`；pid `95796` ≠ 本次拉起的 web pid。所以「runId 相等 + pid 相符」不是自证，外来实例两个都不满足。

**A11 剩余缺口因此收敛为两条**：主仓复跑（被 `E:\pzd` 根目录 ACL 堵住，见 §27.6 第 4 条，等权限修复）、老登复审。前端腿的正向与反向两侧测试侧证据都已具备。

### 27.10 端到端原文：DCP 采集的 web stdout/stderr（在会话目录里，不在 AppHost 日志里）

位置规律：`<TempRoot>\<repoRuntimeTemp>\aspire-dcp<id>\<uuid>_out` = web 进程 stdout，`<uuid>_err` = stderr。§27.2 与 §27.8 两轮的会话目录都还在，逐字如下（时间戳为 DCP 加的）。

**A. 预钉真实提交 `90ee1b2`（`C:/tmp/pzd-pre`，runId `0e0d94ca…`）**

```
16:16:34.148Z    ➜  Local:   http://127.0.0.1:5199/
16:16:34.783Z [pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）：
                 TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
16:16:34.783Z    at eval (C:/tmp/pzd-pre/apps/web/app/entry.server.tsx:33:113)
16:16:34.783Z    at handleRequest (C:/tmp/pzd-pre/apps/web/app/entry.server.tsx:25:10)
16:16:34.789Z TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
```

**B. 删掉钉住的 `62b80ed`（`C:/tmp/pzd-fin`，runId `f6b32412…`）——同一轮里五个来源同秒互证**

```
16:07:27.712Z [pzd-web] NODE_ENV=production                          ← vite 进程 stdout（_out）
16:07:27.712Z cwd=…\apps\web NODE_ENV=production PZD_RUN_ID=f6b32412… ← 落盘探针
16:07:27.831Z [pzd-diag] env NODE_ENV=production                      ← AppHost 侧快照（Running，103 项）
16:07:29.151Z [pzd-web] entry loaded（NODE_ENV=production）           ← entry 模块加载
16:07:29.244Z [pzd-web] 首次文档渲染：即将调用 renderToPipeableStream
16:07:29.247Z [pzd/entry.server] …同步抛出：TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
             at handleRequest (C:/tmp/pzd-fin/apps/web/app/entry.server.tsx:27:10)
```

关键时序：`首次文档渲染` → **3 毫秒后**同步抛出。即第一个文档请求就在 `renderToPipeableStream` **调用点**炸掉，不是渲染中途、不是流式阶段。

→ 至此 A5 的证据链在同一轮运行内闭合：**进程真值 production（stdout + 落盘两路）→ 快照同为 production → entry 加载时读到 production → 首个文档渲染 → 3ms 后 `jsxDEV` 同步抛出 → `/` 500**。两轮（真实 pre-pin 提交、删钉住）都复现同一形态。

**方法教训（我自己犯的）**：我曾据此断言「DCP 不采集 web stdout，`[pzd-web]` 永远是 0 行，别指望日志能读到」。这个结论只对「AppHost 日志」那一处成立，对**会话目录**不成立——判「有没有落盘」必须去看会话目录，而不是看宿主日志。

**归档（防清理丢失）**：上列原文已复制进 `artifacts/a5-evidence/`（`artifacts/` 已 gitignore），两轮各一个子目录，`README.txt` 里含 hash、runId、全部 `[pzd-web]` 行与首条错误原文，可直接被交接文档引用。

```
artifacts/a5-evidence/README.txt                       ← 索引 + 原文 + 方法约束
artifacts/a5-evidence/nopin-62b80ed/web-stdout.log     ← DCP 会话 <uuid>_out
artifacts/a5-evidence/nopin-62b80ed/web-stderr.log     ← DCP 会话 <uuid>_err（2.4MB）
artifacts/a5-evidence/nopin-62b80ed/pzd-web-env.log    ← 进程级 NODE_ENV 探针
artifacts/a5-evidence/prepin-90ee1b2/web-{stdout,stderr}.log
```

**编码更正**：这些 `_out`/`_err` 是 **UTF-8**，不是 GBK。我最初按 GBK 读，得到的是乱码（`鍚屾鎶涘嚭`），一度以为文件损坏；按 UTF-8 读即得上面的中文原文。判编码一律先看字节，不要凭印象。

**更正（本节第一版写错过）**：第一版写的「DCP 那次的 500 响应体没有留下、只能靠本地反推」是错的。DCP 确实采集 web 进程的 stdout/stderr，只是**不写进 AppHost 日志**，而是落到会话目录 `<TEMP>\aspire-dcp<id>\<uuid>_out` 与 `_err` 里。按这个路径回头去翻，两轮都拿到了端到端原文，见 §27.10。**「AppHost 日志里没有」不等于「没有落盘」**，这条对以后所有人都是硬约束。

---

## 28. 通道在成功轮同样成立 + 宿主环境是上游开关（单变量）@ `db17a9b` / `62b80ed`

### 28.1 成功轮也有 `_out` 锚点——「AppHost 日志里没有」零判别力

此前只有一个未决点：**成功那轮的会话目录已经删了，无法证明成功时 `_out` 里也有 `entry loaded`**。这次新开副本 `C:/tmp/pzd-db17`（`db17a9b`，pin 在）跑 `-Mode aspire`，**`EXIT=0`**，8 条 `[ok]`；会话目录 `.aspire-tmp/aspire-dcpjeoost0x.scs/` 里 web 那一路（`a9c3ab6e-…_out`，1997 B）：

```
 1  16:32:51.586Z [pzd-web] NODE_ENV=development        ← vite 配置加载时打印
17  16:32:51.683Z [pzd-web] NODE_ENV=development        ← SSR 模块加载/重载再打印（同一次运行打两遍，不能当请求数）
18  16:32:51.712Z   ➜  Local:   http://127.0.0.1:5199/
19  16:32:52.496Z [pzd-web] entry loaded（NODE_ENV=development）
20  16:32:52.593Z [pzd-web] 首次文档渲染：即将调用 renderToPipeableStream
（20 行到此为止，无异常栈；_err 全文 58 B，只有一行 `$ react-router dev --port 5199`）
```

**与落盘探针同毫秒互证**：探针 `16:32:51.585 / .683`，`_out` 的 `:1 / :17` 是 `.586 / .683`，runId 都是 `f730b9e225ee42df95721933c9ab4893` → 两条通道记录的是同一进程、同一次运行。

同一轮 `artifacts/verify-apphost.log` 里 `pzd-web|pzd/entry.server` 命中 **0**，而 `_out` 命中 **4**。

→ **判读纪律（升级版）**：`[pzd-diag] env NODE_ENV=` 有值且 web 到过 `state=Running` = DCP 确实拉起过 web；至于 entry 有没有跑，**只能看会话目录**。而「AppHost 日志里没有锚点」在**成功轮同样成立**，所以它**零判别力**，不许拿来推断「失败在入口之前」。

其余同轮 A11 正向（第 4 次）：`X-Pzd-Run-Id` 与 `/api/version` 同 runId、前端 pid **22096** 头一致且返回 SPA 外壳、`/api/version 0.1.1-alpha.0.186+db17a9b`。

### 28.2 宿主环境是上游开关：同一棵 un-pin 树，只改 `DOTNET_ENVIRONMENT`

树 `C:/tmp/pzd-fin`（`62b80ed`），`src/Pzd.AppHost/Program.cs:85` 的钉住**整行注释掉**；经实测确认这不影响结论：相对 `62b80ed` 的精确改动只有 `@@ -85 +85 @@` 这一行，**未注释的有效赋值 0 处**。

对照组（16:07，runId `f6b32412…`）不设任何变量；实验组（17:48，runId `968c1848…`）只加 `DOTNET_ENVIRONMENT=Development`：

| | 无 pin，不设 `DOTNET_ENVIRONMENT` | 无 pin + `DOTNET_ENVIRONMENT=Development` |
|---|---|---|
| AppHost 自报 | `<unset>` | `<unset>` |
| 资源快照 `NODE_ENV` | production（103 项） | **development（109 项）** |
| vite 进程内探针 | production | **development** |
| `/` | 500 + `jsxDEV` 同步抛出 | **200 + SPA 外壳**（pid 111888） |
| 验收 | `EXIT=1` | **`EXIT=0`**，8 条 `[ok]`，`/api/version 0.1.1-alpha.0.199+62b80ed` |

→ **结论**：这个值**随宿主环境改变**，所以「宿主环境 → Aspire JS 集成写入 web 的 `NODE_ENV`」这一环有实测，不再是元数据反推。

**三条边界（引用时必须带）**：

1. 只证明「上游开关是宿主环境」，**没区分**产生位置（装配资源模型时写入 / DCP 应用环境块时转发）——仍待构建期 dump。
2. 实验组的 DCP 会话目录**在正常退出后被清理**，所以该轮进程级读数只有落盘探针；对照组才有 `_out`。两条通道分属两次运行，**不要混引**。
3. 两棵树都停在 `62b80ed`，早于加上「宿主环境」诊断的提交，所以该轮「宿主环境=Development」是**继承链推断**（脚本 `Start-Process` 未加 `-UseNewEnvironment`、也不清环境），**不是日志里读出来的**。

另：`C:/tmp/pzd-envtest`（同一基座、pin 整行删除、`cf7b5542…`）是一次**独立的第二棵树**，结论相同（`development` + `EXIT=0` + `/` SPA 外壳）。两份分属不同树、不同 runId，互为交叉验证，不是同一份数据的两次引用。

### 28.3 取证判据修订（本轮踩过的四个坑，逐条作废旧写法）

| 旧写法 | 为什么错 | 改成 |
|---|---|---|
| `grep -c 'WithEnvironment("NODE_ENV"'` 判有无 pin | **注释掉的行照样命中**，正好把有无 pin 判反（本轮多人中招） | `grep -n '^\s*web = web\.WithEnvironment("NODE_ENV"'` 数**活代码**；再配基座 hash + `git status --porcelain` |
| 引用 `artifacts/pzd-web-env.log` 里的一行 | 该文件**追加写**，同文件里 `production` 与 `development` 都有（`pzd-fin` 那份混了 4 轮以上） | 必须 `runId + 会话目录名` 一起引；我自己第一版 grep 就抓到了 16:07 的旧 `production` |
| 只看扩展名/印象定编码 | 同一台机器多种编码：`artifacts/verify-apphost.log` 与 DCP `_out` 是 **UTF-8**，脚本自己的 `[ok]` 输出是 **GBK** | 先看字节再读；按文件判，不按印象判 |
| 探针文件没生成 → 某条结论 | 文件没生成只说明 web 没走到写文件那一步 | **本轮无效**，既不是正证也不是反证 |
| 「相同 / 不同」不加限定就说 | 同一份内容在字节、文本、语义三个层面可以同时「不同又相同」。本轮三次实例：`grep -c` 数到注释行、`[ok]` 行 GBK 与 `.aspire-tmp` UTF-8 混读、`.bak` 与 git blob 差 313 字节但去 CR 后 sha256 一致 | 断言必须写清层面：**字节 / 文本 / 语义**。「逐字节相同」只在 `cmp` 通过时才写 |
| 「这一轮当时是什么状态」凭现查文件或 `mtime` 反推 | `mtime` 只记最后一次写入；共享副本无版本控制、无锁，任何人都能改。本轮就有人据此把「A 组是活代码 pin」判反 | **当轮 `git diff` 或当轮会话目录/探针记录，二者任一即可**；会话目录还必须能证明**包住该轮**（目录与 `_out` 的 mtime 区间 + runId + 栈路径指向该树 + 目录内只有该轮有内容）；两样都没有才记「不可考」 |
| 全称判断（「只有 / 全部 / 唯一 / 0 条 / 都」）用 `head`/`tail`/单列的截断结果下 | 本轮同一天抓出三次：`grep \| head -10` 把 `jsxDEV` 数成 8 次（实际 1040）、`tail -8` 把探针读成「8 条」（实际 10 行 5 轮）、只查 `_err` 字节数就说「只有一份有内容」（两个 `_out` 各有 10 KB / 2.3 KB） | 全称判断**必须贴完整命令与输出范围**，不许用 `head`/`tail` 的截断结果；每个全称句后面要能指出它出自哪条完整命令 |

### 28.4 归档

```
artifacts/a5-evidence/db17a9b-success/   ← 28.1：web-stdout.log(1997B)/web-stderr.log(58B)/pzd-web-env.log/host.log + README.txt
artifacts/a5-evidence/devenv-62b80ed/    ← 28.2：host.log/pzd-web-env.log/script.log/Program.cs.orig.bak + README.txt
```

`artifacts/` 被 `.gitignore:4` 忽略，**这些文件只在工作区磁盘上、没进 git**；报告里已抄入关键原文，需要长留的部分由文档负责。`Program.cs.orig.bak`（`C:\tmp\Program.cs.pzd-fin.bak` 的原样副本）用于复核「树上是注释、不是删行」。

**行尾更正（张一一 2026-10-28 指出，已核）**：那份 `.bak` 与 `62b80ed` 的 git blob 是**内容相同、行尾不同**——工作区是 CRLF、blob 是 LF：`313` 个 CR、`15480` vs `15167` 字节（差 `313`），去 CR 后 sha256 一致（`bf857287…`）。所以不能写「与原件逐字节相同」，`cmp` 一对就会起疑。这一条与本报告 §27.10 的编码更正、以及脚本 BOM 那次是同一类坑：**行尾/编码/字节数这些“看起来无关”的差异，最终都会变成别人复核时的假阳性。**

### 28.5 双锚：代码 hash + 脚本 hash

按交接新立的通则（验收结论锚「代码 hash + 脚本 hash」两个坐标），本节两轮补齐脚本坐标（`sha256` 取前 16 位）：

| 小节 | 代码 | 脚本 | 说明 |
|---|---|---|---|
| 28.1 | `db17a9b` | `b25088dfa9bd501c` | 与 `62b80ed` 自带脚本**一致、未打补丁**（`git diff 62b80ed -- scripts/verify-apphost.ps1` 为空）。所以 TEMP 落在 `<repo>/.aspire-tmp`，会话目录能原地 grep 到（这是 28.1 能拿到 `_out` 的原因） |
| 28.2 | `62b80ed` | `ce094d7dbc803443` | 带 `-TempRoot` 补丁版（+33/−6，计数与张一一的 +34/−7 只是 `--ignore-cr-at-eol` 口径差异，**同一份文件同一个 hash**）。该版默认 TEMP 在仓库外，正常退出时会话目录随之被清理——这正是 28.2 边界 2 的成因，不是数据缺失 |

→ 因此 28.2 与张一一 `cf7b5542…` 那轮**可并排引用**：同代码、同脚本、同变量，只有树与 runId 不同。


