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
[preflight] 端口 5199 已被占用：pid 32236 node。先关掉占用进程再验本脚本。
[preflight] 这不是环境/占用问题，而是本票缺陷：请占用用户安装的…
退出码：2
```

占用者不是我起的：`pid 32236` 的命令行是 `node "E:\pzd\apps\web\node_modules\.bin\..\@react-router\dev\bin.js" dev`，17:20:44 启动，跑在**主仓库**目录下（我的副本是 `C:\tmp\pzd-f033`）。我没有动它。预检按退出码 2 拒绝执行是正确行为（不是假绿），待占用释放后可补这一条；降级语义本身已在 §8 与 `-Mode api` 覆盖。

### 10.2 一条给脚本的加固建议（假绿风险，非本轮缺陷）

脚本对前端只断言「`127.0.0.1:5199` 返回 200 且含 SPA 标记」，**不校验这个 200 是否来自本次宿主拉起的进程**。如果别人恰好在 5199 上跑着 dev，本脚本可能拿别人的实例当自己的成果（本轮之所以可信，是因为起跑时预检显示 5199 空闲）。建议：探活前记录当前宿主进程树的 pid，或断言占用 5199 的 pid 属于本次 AppHost 的子进程。
