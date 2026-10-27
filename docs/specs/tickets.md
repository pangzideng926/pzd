# PZD 底座 — 票（§18 步骤 1–2）

> 规格：[`docs/specs/foundation.md`](./foundation.md)
> 状态：执行中（2026-10-27 第二轮）｜共 12 张
> 进度：01 ✅ 审计通过｜04 ✅ 审计通过（返工 F1/F2/F5/F4/F7，[`review-01-04-final.md`](../reports/review-01-04-final.md)）｜12 ✅ 审计通过（[`review-12.md`](../reports/review-12.md)）｜05 ✅ 审计通过（`e593225`，[`review-05.md`](../reports/review-05.md)，116 条）｜06 🔧 返工中（预审需修改，[`review-06.md`](../reports/review-06.md)）｜02/03 未开工｜07–10 ready-for-agent
> 交接：开发（旺总）→ 测试（李总）→ 审计（老登）→ 文档（老卫）
> 规则：每张票完成后必须贴出「验证命令 + 实际输出摘要」；失败不许标完成。测试先行的票在票内注明。

> **环境适配（2026-10-27 实测，票 01/04 回填）**
> ① 测试运行器改为 `xunit.v3.mtp-v2`（Microsoft.Testing.Platform），`global.json` 含 `"test": { "runner": "Microsoft.Testing.Platform" }`，测试工程统一在 `tests/Directory.Build.props` 配置，不再引用 `Microsoft.NET.Test.Sdk`（xUnit v3 + VSTest 在 .NET 10 SDK 已不兼容）。
> ② `E:\pzd` 被标了 Low Mandatory Level，MTP `dotnet test` 的命名管道握手失败（报 handshake failure，非测试失败）：**工作区内**用 `dotnet run --project tests/<工程>/<工程>.csproj`（同一批测试、同一退出码）；普通目录 / CI 用 `dotnet test Pzd.slnx` 等价（`E:\pzd-copy` 实测 39 passed、退出码 0）。
> ③ 版本号查询必须带 Build target：`dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version`；不加 `-t:Build` 只得 MSBuild 求值默认值 `1.0.0`。
> ④ **本机实例可用**：MySQL80 `8.0.46` @ `127.0.0.1:3306`（root 需密码）、Redis/Memurai @ `6379`（无密码，PONG），无 Docker。票 03/09/10 的主路径改为连接本机实例，容器编排退出本轮验收。
> ⑤ **凭据红线**：密码/连接串只经环境变量（`ConnectionStrings__mysql`、`ConnectionStrings__redis`、`PZD_TEST_MYSQL`）或 `local.env`（必须被 `.gitignore` 忽略）注入；仓库、日志、测试报告、群消息零密码（规格 §4.12）。
> ⑥ **版本口径（F6）**：期望值以 git tag 为基准，不硬编码 `0.1.0`——tag 提交上是 `0.1.0`，tag 之后是 `0.1.1-alpha.0.<height>+<sha>`（HEAD `1bdc680` 实测 `0.1.1-alpha.0.1`）；`/api/version` 与 msbuild 版本一致即可。
> 下文各票里的 `dotnet test` 命令，在工作区内一律按 ② 替换为等价 `dotnet run --project` 形式；这不降低验收标准。

## 依赖图

```
01 仓库与 .NET 骨架
├─ 04 SharedKernel + 架构测试基线
│  ├─ 05 多租户内核 + 单测 ──┬─ 07 UoW + Outbox + 领域事件
│  ├─ 06 异常处理 + Result→HTTP ─┘  08 可观测性 + 审计 + 安全横切（←06）
│  ├─ 09 Quartz + 健康检查 + 版本
│  ├─ 10 租户隔离集成测试（本机 MySQL 真跑，缺失则 Skip）
│  └─ 12 Error.Metadata 只读快照（F8）
├─ 02 前端 monorepo 骨架
└─ 03 Aspire 编排与一键启动（引用本机实例，←01,02）
11 CLAUDE.md + 一键验收脚本（←02,03,05,06,07,08,09,10）
```

| # | 票 | Blocked by | 验证命令（根目录执行） |
|---|---|---|---|
| 01 | 仓库与 .NET 骨架 | 无 | `dotnet build Pzd.slnx -warnaserror` |
| 02 | 前端 monorepo 骨架 | 01 | `pnpm install --frozen-lockfile && pnpm -w build` |
| 03 | Aspire 编排与一键启动（引用本机实例） | 01, 02, 05（F-05-4） | `pwsh -NoProfile -File scripts/verify-apphost.ps1`（需 env 连接串） |
| 04 | SharedKernel + 架构测试基线 | 01 | `dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj` |
| 05 | 多租户内核 + 单测 | 04 | `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj` |
| 06 | 异常处理 + Result→HTTP | 04 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` |
| 07 | UoW + Outbox + 领域事件 | 05, 06 | `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj` |
| 08 | 可观测性 + 审计 + 安全横切 | 06 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` |
| 09 | Quartz + 健康检查 + 版本 | 03, 04 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` + 启动验证 |
| 10 | 租户隔离集成测试（本机 MySQL 真跑，缺失则 Skip） | 05 | `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj` |
| 11 | CLAUDE.md + 一键验收脚本 | 02,03,05,06,07,08,09,10,12 | `pwsh -NoProfile -File scripts/verify.ps1` |
| 12 | Error.Metadata 只读快照（F8） | 04 | `dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj` |

---

# 01: 仓库与 .NET 骨架

**What to build:** 任何人 checkout 后，一条 `dotnet build Pzd.slnx` 能编译整个后端解决方案；SDK 锁定、中央包管理、MinVer 版本号、测试工程骨架全部就位，仓库从「只有文档」变为「可编译、可测试、有版本号」。

**文件路径**

- `.gitignore`、`.editorconfig`、`global.json`（锁 `10.0.4xx`，含 `"test": { "runner": "Microsoft.Testing.Platform" }`）、`Directory.Build.props`（Nullable、ImplicitUsings、TreatWarningsAsErrors、MinVer）、`Directory.Packages.props`（NuGet 中央包管理）、`NuGet.config`（`<clear />` + `packageSourceMapping`，规避 CPM 的 NU1507 并保证 restore 可复现）
- `Pzd.slnx`（解决方案文件夹 `src` / `tests`）
- `src/Pzd.SharedKernel/Pzd.SharedKernel.csproj`（classlib，零依赖）
- `src/Pzd.Infrastructure/Pzd.Infrastructure.csproj`（引用 SharedKernel）
- `src/Pzd.Api/Pzd.Api.csproj`（Microsoft.NET.Sdk.Web，引用 Infrastructure）
- `tests/Directory.Build.props`（统一测试工程配置：`IsTestProject`、`OutputType=Exe`、`TestingPlatformDotnetTestSupport`、`xunit.v3.mtp-v2`）
- `tests/Pzd.ArchitectureTests/`、`tests/Pzd.SharedKernel.UnitTests/`、`tests/Pzd.Infrastructure.UnitTests/`、`tests/Pzd.Api.UnitTests/`、`tests/Pzd.IntegrationTests/`（各含 `*.csproj` + 1 个 smoke test；**`xunit.v3.mtp-v2`，不引用 `Microsoft.NET.Test.Sdk`**）
- 测试模板：优先 `dotnet new install xunit.v3.templates` 后 `dotnet new xunit3`；不可用则手写 csproj（验收只看 csproj 里的包与测试执行结果）
- `docs/specs/tickets.md` 已存在，本票只读

**Status:** ✅ 通过（审计：[`review-01-04-final.md`](../reports/review-01-04-final.md) §1，commit `e0ec679`）

- [ ] `git init` 完成，初始提交存在，工作区无未跟踪的 `bin/obj`
- [ ] `git tag` 含 `v0.1.0`（MinVer 可产出 `0.1.0`）
- [ ] `dotnet build Pzd.slnx -warnaserror` 退出码 0，无警告
- [ ] 5 个测试工程各自 `dotnet run --project tests/<工程>/<工程>.csproj` 退出码 0（5 个 smoke test 全绿）；普通目录 `dotnet test Pzd.slnx` 等价通过（工作区 Low IL 限制见顶部环境适配 ②）
- [ ] 版本以 git tag 为基准（F6）：`v0.1.0` tag 提交上 `dotnet msbuild ... -t:Build -getProperty:Version` = `0.1.0`；tag 之后的 HEAD 为 `0.1.1-alpha.0.<height>+<sha>`（实测 `1bdc680` → `0.1.1-alpha.0.1`）；命令必须带 `-t:Build`，票面不把单一版本值写成唯一期望
- [ ] 所有 NuGet 版本只出现在 `Directory.Packages.props`，csproj 用 `Version` 省略写法
- [ ] 本票不含 AppHost / 前端 / 任何业务类型

**验证命令**

```powershell
dotnet build Pzd.slnx -warnaserror
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version
git log --oneline -1; git tag
```

---

# 02: 前端 monorepo 骨架

**What to build:** `pnpm install` + `pnpm -w build` 一次通过，`apps/web` 能起 dev server 并渲染一个占位路由；`packages/ui|api|realtime` 目录与包元数据就位，为步骤 4 的登录外壳留好位置。

**文件路径**

- `package.json`（根脚本：`dev` / `build` / `lint` / `typecheck`）、`pnpm-workspace.yaml`、`turbo.json`、`pnpm-lock.yaml`
- `apps/web/`（React Router v7 framework mode，`ssr: false`，路由骨架 + 空白页；**不含**登录页、侧边栏、多页签）
- `packages/ui/`（shadcn 组件与主题，preset `b2oDW9T8C`）、`packages/api/`（orval 占位 + README）、`packages/realtime/`（SignalR 封装占位 + README）
- `apps/web/vite.config.ts`（dev proxy `/api`、`/hubs` → `http://127.0.0.1:5080`）
- `apps/web/version.json` 相关逻辑**不做**（步骤 4）

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] `pnpm install --frozen-lockfile` 退出码 0
- [ ] `pnpm -w build` 退出码 0，`apps/web/build/client` 产出
- [ ] `pnpm -w typecheck` 退出码 0，TS strict
- [ ] `pnpm --filter web dev` 起服务后 `GET /` 返回 200 且不是错误页
- [ ] 工作区包关系正确（web 依赖 ui/api/realtime 中实际需要的包），lockfile 提交
- [ ] 若 shadcn preset 拉取失败：退化为最小 React Router SPA，并在 `docs/specs/foundation.md` §9 记录偏差与剩余项

**验证命令**

```powershell
pnpm install --frozen-lockfile
pnpm -w build
pnpm -w typecheck
```

---

# 03: Aspire 编排与一键启动（引用本机实例）

**What to build:** `dotnet run --project src/Pzd.AppHost` 一条命令拉起 API 与前端 dev server，并连上本机 MySQL 8.0.46 与 Redis(Memurai)；`/health/ready` 为 Healthy 且 mysql/redis 明细 Up，`/api/version` 可访问，Ctrl+C 优雅退出。凭据只从环境变量 / `local.env` 注入，仓库零密码；实例或凭据缺失时降级启动并打印 Warning（CI 兜底）。容器编排退出本轮验收。**开工硬前置 F-05-4**：`/health/live`、`/health/ready`、`/api/version` 必须免租户，无 claim / `X-Tenant` / 子域时也返回 200，否则脚本轮询与版本端点会被票 05 的 `Tenant.NotFound` 400 拦掉。

**文件路径**

- `src/Pzd.AppHost/Pzd.AppHost.csproj`（Aspire AppHost SDK + `Aspire.Hosting.AppHost`）
- `src/Pzd.AppHost/Program.cs`（以 `AddConnectionString` 引用本机实例并注入 API，**不建容器**）、`appsettings.json`、`Properties/launchSettings.json`
- `src/Pzd.Api/appsettings.Development.json`（`ConnectionStrings:master` / `redis` 键缺省为空，不写真实值）
- `src/Pzd.Api/Program.cs`（配置注入 + 给 `/health/*`、`/api/version` 标记 `AllowAnonymousTenant`）
- `src/Pzd.Infrastructure/Tenancy/TenantResolutionMiddleware.cs`（识别豁免 metadata，跳过租户解析与状态校验）
- `scripts/verify-apphost.ps1`（读 env 连接串 → 后台启动 AppHost → 轮询 `/health/ready` 断言 Healthy → 取 `/api/version` → 关闭进程；无 env 时断言降级 200）

**Blocked by:** 01, 02, 05（F-05-4 需在 05 的中间件上加豁免）

**Status:** ready-for-agent

- [ ] API 在 AppHost 中固定 `http://127.0.0.1:5080`（便于前端 proxy 与脚本验证）
- [ ] 提供 `ConnectionStrings__mysql`（本机 3306）与 `ConnectionStrings__redis`（6379）时：启动成功，`/health/ready` **Healthy**，明细 mysql=Up、redis=Up
- [ ] 凭据只来自环境变量 / `local.env`：仓库内 `git grep` 无密码，`local.env` 已被 `.gitignore` 忽略
- [ ] 实例或凭据缺失：降级启动，日志含 `Degraded mode: MySQL/Redis unavailable`，`/health/ready` 200 + Degraded，`/health/live` 200
- [ ] **F-05-4（硬前置）**：无 claim / `X-Tenant` / 子域时，`/health/live`、`/health/ready`、`/api/version` 均返回 **200**；豁免端点之外的请求仍返回 400 `Tenant.NotFound`
- [ ] `GET /api/version` 返回 `version`、`buildId`、`buildTime`，version 与 `dotnet msbuild -t:Build -getProperty:Version` 一致（以 tag 为基准，F6）
- [ ] 不注册 MySQL / Redis 容器资源（无 Docker，容器编排不在本轮验收）
- [ ] `scripts/verify-apphost.ps1` 在本机退出码 0，并贴出 Healthy（有 env）/ Degraded（无 env）证据
- [ ] Ctrl+C / 停止父进程后子进程退出，无残留监听端口

**验证命令**

```powershell
# 连接串由 local.env（gitignored）或当前会话环境变量提供；仓库与命令示例中不出现真实密码
pwsh -NoProfile -File scripts/verify-apphost.ps1
```

---

# 04: SharedKernel + 架构测试基线

**What to build:** 领域层最小原语可用（Entity / AggregateRoot / ValueObject / DomainEvent / Result / Error），且架构测试能自动拦截分层违规（**规则③有反向用例、规则④有单元级反例**，① ② 是对真实程序集的直接断言）；后续所有模块直接复用。本轮按审计报告 §4 返工：F1 四条规则抽成共享断言方法（生产测试与反向用例跑同一方法，断言恰好 1 条违规）、F2 模块发现改读 `src/Pzd.*/*.csproj` 的 `ProjectReference`、F5 规则④允许集加入「其他模块的 `*.Contracts`」并补正向用例、F7 规则③禁止列表并入 `Pzd.Infrastructure` / `Pzd.Api`、F4 补跨类型同 Id 用例（`Order`/`Invoice` 同 Id 不相等）。**只改测试资产，不动实现**。返工结果已由 [`review-01-04-final.md`](../reports/review-01-04-final.md) 复核通过。

**文件路径**

- `src/Pzd.SharedKernel/Domain/Entity.cs`、`AggregateRoot.cs`、`ValueObject.cs`、`IDomainEvent.cs`、`IDomainEventCollector.cs`
- `src/Pzd.SharedKernel/Results/Result.cs`、`Result{T}.cs`、`Error.cs`、`ErrorType.cs`
- `src/Pzd.SharedKernel/Abstractions/IClock.cs`、`ICurrentUser.cs`、`ITenantContext.cs`（接口放 SharedKernel，实现放 05）
- `tests/Pzd.SharedKernel.UnitTests/Results/*.cs`、`Domain/*.cs`（F4 的 `Order`/`Invoice` 同 Id 用例）
- `tests/Pzd.ArchitectureTests/ArchRules.cs`（四条规则的共享断言方法，F1）、`DependencyRulesTests.cs`、`ReverseCaseTests.cs`、`ArchitectureTestAssembly.cs`（反向用例用的违规类型）、`Pzd.ArchitectureTests.csproj`（反向用例对 `SqlSugarCore` 的真实引用；显式提版 `Pkcs 10.0.14` 以过 `-warnaserror`）

**Blocked by:** 01

**Status:** ✅ 通过（返工 F1/F2/F5/F4/F7 已复核，审计 [`review-01-04-final.md`](../reports/review-01-04-final.md)；代码 commit `49ee09a`）

- [ ] `Result.Success` / `Result.Failure(Error)` 不可同时成立；`Result<T>` 泛型转换正确
- [ ] `Error` 按 Code 相等，含 `Type` / `Metadata`；错误码格式 `模块.实体.原因` 有测试
- [ ] `AggregateRoot` 收集领域事件且 `ClearEvents()` 后为空；`ValueObject` 按值相等；**F4**：不同实体类型同 Id 不相等（`Order`/`Invoice`）
- [ ] `ITenantContext.TenantId` 为 `long`（与雪花主键一致）；`IClock`、`ICurrentUser` 接口就位
- [ ] 架构规则 4 条，均通过**共享断言方法**（F1）；证据口径 = **规则③反向用例 + 规则④单元级反例**，① ② 是对真实程序集的直接断言：① SharedKernel 不引用 Infrastructure/Api；② Infrastructure 不引用 Api；③ `*.Domain` 不引用 `SqlSugar` / `Microsoft.AspNetCore` / `Pzd.Infrastructure` / `Pzd.Api`（F7）；④ 模块间只能引用对方 `*.Contracts`，允许集包含「其他模块的 `*.Contracts`」（F5，模块为空时允许空集）
- [ ] **F2**：模块发现遍历 `src/Pzd.*/*.csproj` 的 `ProjectReference`，不扫输出目录 DLL；两模块未接测试引用时也能检出违规
- [ ] **F5 正向用例**：`Pzd.Platform -> Pzd.Identity.Contracts` 判为合法（不误报）；反向用例仍断言恰好 1 条违规
- [ ] **规则③反向用例**：测试程序集内构造 `Pzd.SharedKernel.Domain.Fake` 引用 `SqlSugar`，规则必须报违规且恰好 1 条；**规则④单元级反例**：`Modules_must_not_reference_other_modules_directly` 证明允许集不是全放行；**F7 待补测（票 05/06 触发）**：`Pzd.Infrastructure` / `Pzd.Api` 有公共类型后，在测试程序集 `Pzd.SharedKernel.Domain` 下加 `FakeInfrastructureDependency` / `FakeApiDependency` 并断言恰好 2 / 3 条违规
- [ ] `dotnet run --project tests/Pzd.SharedKernel.UnitTests/...` 与 `tests/Pzd.ArchitectureTests/...` 均退出码 0，贴出 `TEST EXECUTION SUMMARY`
- [ ] 若 NetArchTest 对 net10 不兼容，改用 ArchUnitNET 并保持规则语义与反向用例

**验证命令**

```powershell
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
```

---

# 05: 多租户内核 + 单测

**What to build:** 请求进入后能解析出可信租户上下文（登录 claim 优先，匿名头/子域次之），停用/过期立即 403；业务代码通过 Scoped 的 `ITenantDbProvider` 按需取租户连接，**每租户独立 `SqlSugarScope` + 双检缓存**，绝不串库。全部用单测证明，不依赖数据库。

**实现偏差（票 05 实测回填）**：`SqlSugarScope.AddConnection` 在 async 上下文里是**上下文私有**的（加完回到调用方即丢，`Task.Yield()` 即复现），因此原票面「单例 scope + `AddConnection(tid)` 只加一次 / `GetConnectionScope(tid)`」不可实现。改为**每租户懒建一个独立 `SqlSugarScope`**（构造期配置跨上下文可见）+ 双检缓存：每租户只建一次、**连接创建只查一次 `ITenantStore`（显式重载按次校验查库）**、不 `ChangeDatabase`、不跨租户。另：新增 `TenantConnectionCache.cs`、`TenantConfiguration.cs`、`SystemClock.cs`、`appsettings.json`（无真实凭据）；provider 为 async API；本轮所有端点都要求租户（登录类 `AllowAnonymousTenant` 留步骤 3）。

**文件路径**

- `src/Pzd.SharedKernel/Abstractions/ITenantContext.cs`（若 04 未建）
- `src/Pzd.Infrastructure/Tenancy/TenantContext.cs`、`TenantResolutionMiddleware.cs`、`ITenantStore.cs`、`InMemoryTenantStore.cs`、`TenantInfo.cs`、`ITenantDbProvider.cs`、`TenantDbProvider.cs`、`TenantErrors.cs`、`TenantConnectionCache.cs`、`TenantConfiguration.cs`、`src/Pzd.Infrastructure/SystemClock.cs`
- `src/Pzd.Api/Program.cs`（注册 `UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization`）
- `src/Pzd.Api/appsettings.json`（`Tenants:*` 测试用租户配置，或内存 Store 由 DI 注入）
- `tests/Pzd.Infrastructure.UnitTests/Tenancy/*.cs`
- `tests/Pzd.Api.UnitTests/Tenancy/*.cs`（WebApplicationFactory，走 HTTP 接缝）

**Blocked by:** 04

**Status:** ✅ 通过（终审：[`review-05.md`](../reports/review-05.md)，实现 `e593225`，全仓 **116** 条全绿：35+16+54+10+1）

- [x] 已登录且 claim `tid` 存在：路由/端点拿到该租户，`X-Tenant` 头被忽略（有测试证明"忽略"）
- [x] 匿名 + `X-Tenant`：解析成功；匿名 + 子域名：解析成功；两者同时存在时子域名优先
- [x] 无任何租户来源且端点要求租户：400/403 ProblemDetails，错误码 `Tenant.NotFound`
- [x] 租户停用 → 403 + `code = Tenant.Disabled`；租户过期 → 403 + `code = Tenant.Expired`（精确断言响应体）
- [x] 每个请求都重新校验状态（`ITenantStore` 被再次调用，缓存不跨请求）
- [x] 同一 `ITenantContext` 不跨请求泄漏：两个并发/顺序 scope 各自 `Set` 后互不影响
- [x] `TenantDbProvider`：每租户懒建**独立 `SqlSugarScope`**（构造期配置跨 async 上下文可见）+ 双检缓存——同一租户只建一次、**连接创建只查一次 `ITenantStore`**；禁止全局单例 `AddConnection(tid)` / `ChangeDatabase`；无租户上下文取连接抛明确异常（错误码 `Tenant.NotResolved`）
- [x] 全局单例上不出现 `ChangeDatabase`（架构测试或代码检查断言）
- [x] 架构规则③的检查范围纳入真实模块程序集（复用 `ArchRules.DiscoverModules()` 结果 + 底座程序集），不再只硬编码 3 个底座程序集（交接记录「规则③检查范围」项；本票搭好机制，模块出现后自动生效）
- [x] F7 补测条件：本票给 `Pzd.Infrastructure` 加公共类型后，在测试程序集 `Pzd.SharedKernel.Domain` 下加 `FakeInfrastructureDependency` 引用它，反向用例断言**恰好 2 条违规**（交接记录「F7 补测条件」项）
- [x] 上述全部为单测/HTTP 接缝测试，测试命令（工作区 `dotnet run --project`，见顶部环境适配 ②）在无实例/无凭据时也绿（单测不依赖数据库）

**口径回填（审计 §5.2 措辞项）**

- 「只查一次 `ITenantStore`」精确化为：**连接创建只查一次；显式重载 `GetConnectionScopeAsync(tenantId)` 为校验状态按次查库**（实测同租户两次调用 `FindByIdCalls=2`）。
- `Tenants` 为空：允许启动，请求返回 400（不在启动期 fail-fast）；`Tenants:0:ExpiresAt` 为 `null` = 永不过期，为空字符串 = 配置错误、启动失败。此口径写进票面，README 由老卫同步。
- 本票遗留的前置项不属 05 未完成：**F-05-4** 已落票 03/09 + foundation §4.3；**F-05-8** 已落票 07 验收。
- 跨票补丁（F-06-2，traceId）：租户 400/403 的 `traceId` / `X-Trace-Id` 与 kebab 去重由**票 06 收口**，票 05 不重开。
- 编号口径以终审报告 [`review-06.md`](../reports/review-06.md) 为准：**F-06-1** = errors camelCase、**F-06-2** = 租户 traceId、**F-06-3** = 框架 401/403。

**验证命令**

```powershell
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

---

# 06: 异常处理 + Result→HTTP

**What to build:** 端点只需 `return result.ToHttpResult()` 就得到准确状态码与统一 ProblemDetails（`type` / `title` / `status` / `code` / `traceId` / `errors`）；意外异常统一走 `IExceptionHandler`，生产不泄堆栈。**本轮收口（终审口径，编号以 [`review-06.md`](../reports/review-06.md) 为准）**：**F-06-1** `errors` 的 PascalCase key 转 camelCase，并补一条**独立 PascalCase 入参**用例；**F-06-2** 租户 400/403 手写 ProblemDetails 补 `traceId` + 响应头 `X-Trace-Id`，两处 `ToKebabCase` 收成一份；**F-06-3** 框架裸 401/403（status code pages）映射为 `Auth.Unauthorized` / `Auth.Forbidden`，不得落成 `System.Unexpected` /「服务器内部错误」（建议项，本轮一并修）。FluentValidation 管道**不在本票**（见文末「步骤 3 移交清单」）。

**文件路径**

- `src/Pzd.SharedKernel/Results/ErrorType.cs`（与 04 对齐，必要时补 `ClientTooOld`、`TenantUpgrading`）
- `src/Pzd.Api/Errors/ResultHttpExtensions.cs`（`ToHttpResult()`）、`ProblemDetailsCustomization.cs`、`GlobalExceptionHandler.cs`、`ProblemDetailsSetup.cs`
- `src/Pzd.Infrastructure/Tenancy/TenantResolutionMiddleware.cs`（补 `traceId` / `X-Trace-Id`，复用统一 kebab 转换）
- `src/Pzd.Api/Program.cs`（`AddProblemDetails` + `IExceptionHandler` 链 + status code pages + 中间件顺序）
- `tests/Pzd.Api.UnitTests/Errors/ErrorMappingTests.cs`、`UnexpectedExceptionTests.cs`
- 测试辅助：`tests/Pzd.Api.UnitTests/TestEndpoints.cs`（仅测试用最小端点，生产代码不加调试端点）

**Blocked by:** 04

**Status:** 🔧 返工中（终审需修改：[`review-06.md`](../reports/review-06.md)；F-06-1/2 必修、F-06-3 顺带）

- [ ] 映射全表通过 HTTP 接缝逐条断言：400（含 `errors` 字典）、401、403、404、409（乐观锁/唯一键语义）、422、426、503
- [ ] `type` = `https://pzd/errors/{kebab-code}`；`code` 与 `Error.Code` 一致；`traceId` 与响应头 `X-Trace-Id` 同源
- [ ] 未知异常 → 500，响应体无堆栈、无异常类型名，含 `traceId`；开发环境与生产环境行为差异有测试或明确断言
- [ ] **F-06-1 · errors 透传 + camelCase**：Validation 类型 Result（本轮由测试端点手工构造）→ 400，`errors` 原样透传并把 key 转 camelCase；**用例必须以 PascalCase 键入参**并断言响应里出现 `userName`，不得把 `UserName` 原样透传。**本轮不验证 FluentValidation 管道**（见文末「步骤 3 移交清单」）
- [ ] **F-06-2** 租户 400/403（[`TenantResolutionMiddleware`](src/Pzd.Infrastructure/Tenancy/TenantResolutionMiddleware.cs#L121) 手写路径）同样带 `traceId` 与响应头 `X-Trace-Id`；kebab 转换只有一份实现（中间件与 `ToHttpResult` 共用）；403 用例断言 `traceId` 非空
- [ ] **F-06-3** 框架裸 401/403（status code pages 路径）返回 ProblemDetails：`code = Auth.Unauthorized` / `Auth.Forbidden`、`status` 401/403、带 `traceId`，**不得**出现 `System.Unexpected` /「服务器内部错误」
- [ ] 404/405 等框架生成的问题响应同样带 `code`/`traceId`（自定义 ProblemDetails 覆盖到框架路径）
- [ ] 不使用「永远 200 + {code,msg,data}」包装（代码审查 + 测试断言）
- [ ] F7 补测条件：本票给 `Pzd.Api` 加公共类型（如 `ResultHttpExtensions`）后，在测试程序集 `Pzd.SharedKernel.Domain` 下加 `FakeApiDependency` 引用它，反向用例断言**恰好 3 条违规**（交接记录「F7 补测条件」项）

**验证命令**

```powershell
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

---

# 07: UoW + Outbox + 领域事件

**What to build:** 一次 Command 提交时，聚合收集的领域事件先分发、再与 Outbox 一起提交；失败整体回滚；Outbox 由后台幂等投递。UoW 与 Outbox 存储可替换（本轮内存实现，步骤 3 换落库）。**F-05-8 约束**：后台任务 / Outbox 消费者取连接必须走带状态校验的显式重载 `GetConnectionScopeAsync(tenantId)`（先查 store 校验停用/到期再走缓存），**禁止**无参 `GetConnectionScopeAsync()` 裸取——后者只信任 `ITenantContext`，手工 `Set(停用租户)` 后仍会拿到连接，绕过状态校验。

**文件路径**

- `src/Pzd.SharedKernel/Abstractions/IUnitOfWork.cs`、`IDomainEventDispatcher.cs`
- `src/Pzd.Infrastructure/Persistence/UnitOfWork.cs`（含 `IUnitOfWorkTransactionFactory` / `InMemoryUnitOfWorkTransactionFactory`）、`IOutboxStore.cs`、`InMemoryOutboxStore.cs`、`OutboxMessage.cs`、`OutboxDispatcher.cs`（含 `IOutboxMessageHandler`）、`OutboxDispatcherHostedService.cs`、`DomainEventDispatcher.cs`
- `src/Pzd.Api/Program.cs`（注册 UoW 与后台投递）
- `tests/Pzd.Infrastructure.UnitTests/Persistence/UnitOfWorkTests.cs`、`OutboxTests.cs`
- `tests/Pzd.ArchitectureTests/ArchRules.cs`（F-05-8 规则）、`TenantConnectionRuleTests.cs`

**Blocked by:** 05, 06

**Status:** 🔧 已实现待复测（`d9919ab`，152 条全绿：35 + 71 + 26 + 19 + 1）

**实现偏差与口径（2026-10-27，`d9919ab`，已由老何回填）**

- 事务工厂 `IUnitOfWorkTransactionFactory` 与内存实现、消费者抽象 `IOutboxMessageHandler` **内联在** `UnitOfWork.cs` / `OutboxDispatcher.cs`，未单开文件；文件名差异不影响契约。
- F-05-8 的代码检查落在 `ArchRules.cs`（`ParameterlessTenantConnectionViolations` 扫 `src/Pzd.*`），守护用例在新增的 `TenantConnectionRuleTests.cs`，用合成源码证明规则有效——票面原清单未列，属必要补充。
- 本轮**不注册任何 `IOutboxMessageHandler`**（真实消费者随 Platform/Identity 模块来）；`OutboxDispatcher` 为 Scoped，由后台服务每轮建作用域解析。
- 本轮事务**不真正回滚**，用补偿删除模拟；落库事务留步骤 3（与「Outbox/审计/Quartz 落库表」同批）。
- 全内存，不引入 MySQL 依赖、无跨库事务。

- [ ] 提交顺序被断言：领域事件分发 → 写 Outbox → 事务提交（用假 store / 假发布器记录调用序列）
- [ ] 领域事件处理器抛异常 → UoW 不提交、Outbox 无残留消息
- [ ] 聚合事件分发后从聚合清除，不重复分发
- [ ] Outbox 消息带 `TenantId`；消费前 `ITenantContext.Set` 被调用（假消费者断言）
- [ ] **F-05-8**：后台任务 / 消费者用带 `tenantId` 的显式重载取连接（先校验停用/到期），停用或过期租户的消息被拒绝处理并记录理由；**不得**出现无参 `GetConnectionScopeAsync()` 裸取（代码检查 + 用例守护）
- [ ] 投递幂等：同一消息重复投递不产生重复副作用（消息 Id + 已处理集合）
- [ ] 投递失败重试并记 Warning/Error，不阻塞主流程；后台服务随应用优雅停止
- [ ] 无跨库事务（master 与租户库不共享事务）；本票内存实现不引入 MySQL 依赖

**验证命令**

```powershell
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
```

---

# 08: 可观测性 + 审计 + 安全横切

**What to build:** 每个请求一行结构化日志并自动带 `TenantId`/`UserId`/`TraceId`/`ClientIp`；OTel traces/metrics 已接入；关键写端点标注 `WithAuditLog` 后异步批量采集审计；写请求默认 Antiforgery 校验，Data Protection 密钥可持久化，安全响应头就位。

**文件路径**

- `src/Pzd.Infrastructure/Observability/SerilogRegistration.cs`、`OpenTelemetryRegistration.cs`、`LogContextMiddleware.cs`、`SensitiveDataDestructuring.cs`
- `src/Pzd.Infrastructure/Audit/WithAuditLogExtensions.cs`、`AuditLogFilter.cs`、`AuditLogEntry.cs`、`AuditLogChannel.cs`、`AuditLogBackgroundService.cs`
- `src/Pzd.Infrastructure/Security/DataProtectionSetup.cs`、`SecurityHeadersMiddleware.cs`
- `src/Pzd.Api/Program.cs`（Serilog / OTel / Antiforgery / 审计过滤 / 响应头）
- `src/Pzd.Api/appsettings.json`（`Serilog`、`OpenTelemetry:Endpoint`、`DataProtection:KeyPath`、`Audit:QueueCapacity`）
- `tests/Pzd.Api.UnitTests/Observability/*.cs`、`Audit/*.cs`、`Security/*.cs`

**Blocked by:** 06

**Status:** ready-for-agent

- [ ] `UseSerilogRequestLogging` 生效：一次请求恰好一行摘要，`TenantId`/`UserId`/`TraceId`/`ClientIp` 出现在日志属性中（用测试 sink 断言）
- [ ] OTel 已注册 service name 与 ASP.NET Core / HttpClient instrumentation；未配置端点时不导出、不报错
- [ ] 日志脱敏：password / token / 手机号 / 身份证号被替换（destructuring policy 测试）
- [ ] `WithAuditLog(模块, 动作)` 的端点被调用后，审计条目进入有界 Channel 并被后台批量消费（假消费者断言：操作人、租户、模块、动作、结果、耗时、IP、UA）
- [ ] Channel 满时按策略丢弃并计数告警，绝不阻塞请求（有测试）
- [ ] 非安全方法（POST/PUT/PATCH/DELETE）缺 `X-XSRF-TOKEN` → 400；带正确 token → 放行
- [ ] 响应头含 CSP、`X-Frame-Options`、`X-Content-Type-Options`，非开发环境含 HSTS
- [ ] Data Protection 密钥环路径来自配置，重启后密钥可复用（本地路径测试）
- [ ] 审计落库与 Data Protection 接 Redis 不在本票（步骤 3/有 DB 时）

**验证命令**

```powershell
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

---

# 09: Quartz + 健康检查 + 版本端点（接本机 MySQL）

**What to build:** Quartz 主路径用 **MySQL AdoJobStore**（表在 `pzd_master`），`HeartbeatJob` 注册后可在库中查到、重启后仍在；`/health` 分离存活/就绪，本机实例可用时 readiness 为 Healthy（mysql/redis 明细 Up）；`/api/version` 返回以 git tag 为基准的版本、buildId、构建时间。**F-05-4（硬前置，与票 03 共用豁免）**：`/health/live`、`/health/ready`、`/api/version` 无 claim / `X-Tenant` / 子域时也必须 200，不得被 `Tenant.NotFound` 拦截。

**文件路径**

- `src/Pzd.Infrastructure/Jobs/QuartzRegistration.cs`、`HeartbeatJob.cs`、`JobDefinitionAttribute.cs`、`JobExecutionLogger.cs`
- `db/master/quartz_tables.sql`（Quartz 官方 `tables_mysql_innodb.sql`，`pzd_master` 内建 `qrtz_*` 表）
- `scripts/init-master-db.ps1`（读 env 凭据 → 幂等创建 `pzd_master` → 执行 `quartz_tables.sql`）
- `src/Pzd.Api/Endpoints/VersionEndpoint.cs`、`HealthSetup.cs`
- `src/Pzd.Api/Program.cs`（Quartz / health / version 注册；豁免 metadata；连接串来自 env）
- `src/Pzd.Api/Properties/AssemblyInfo.cs` 或 props（`buildId` / `buildTime` 由构建注入）
- `tests/Pzd.Api.UnitTests/Endpoints/VersionEndpointTests.cs`、`HealthEndpointTests.cs`、`tests/Pzd.Infrastructure.UnitTests/Jobs/QuartzRegistrationTests.cs`

**Blocked by:** 03, 04（健康检查与版本端点的豁免与配置来自票 03）

**Status:** ready-for-agent

- [ ] **F-05-4**：无 claim / `X-Tenant` / 子域时，`/health/live`、`/health/ready`、`/api/version` 均返回 200；豁免端点之外仍返回 400 `Tenant.NotFound`
- [ ] 提供 master 连接串：启动 **MySQL AdoJobStore**（`SchedulerName = "pzd"` + 集群配置），`pzd_master` 内 `qrtz_*` 表存在
- [ ] `HeartbeatJob` 按注册 cron（`Asia/Shanghai`，`[DisallowConcurrentExecution]`）注册，下次执行时间可查
- [ ] **重启 API 后任务仍在**（查 `qrtz_job_details` 或 scheduler API，贴输出作为持久化证据）
- [ ] 无连接串：内存存储 + Warning（CI 兜底），启动不失败
- [ ] `/health/live` 永远 200；`/health/ready` 本机实例可用时 Healthy，明细 mysql=Up、redis=Up；无实例时 200 + Degraded，不因缺资源变 Unhealthy
- [ ] `/api/version` 返回 `version`（MinVer，tag 基准，不硬编码）、`buildId`（git sha）、`buildTime`（ISO 8601），字段与架构 §15.1 一致
- [ ] version 与 `dotnet msbuild ... -t:Build -getProperty:Version` 一致；以 tag 为基准（tag 提交 `0.1.0`，HEAD 形如 `0.1.1-alpha.0.<height>+<sha>`），票面不写死具体值
- [ ] `scripts/init-master-db.ps1` 幂等，只创建 `pzd_master` 与 `qrtz_*` 表，不碰其他库；密码不出现在脚本与输出
- [ ] 本票不做 `version.json`、前端刷新提示、`X-Min-Client-Build`

**验证命令**

```powershell
# 凭据来自 local.env / 环境变量，命令与输出不出现密码
pwsh -NoProfile -File scripts/init-master-db.ps1
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project src/Pzd.AppHost
# 另开终端：curl http://127.0.0.1:5080/health/live ; /health/ready ; /api/version
# 重启 AppHost 后再查 qrtz_job_details，确认 HeartbeatJob 仍在
```

---

# 10: 租户隔离集成测试（本机 MySQL 真跑，缺失则 Skip）

**What to build:** 连本机 MySQL 8.0.46，用 `pzd_it_a` / `pzd_it_b` 两个真实租户库验证跨租户隔离、QueryFilter 与停用租户 403；凭据/实例不可用时动态 Skip 且退出码 0（CI 兜底）。Docker / Testcontainers 不再是主路径。

**文件路径**

- `tests/Pzd.IntegrationTests/MySqlFixture.cs`（读 `PZD_TEST_MYSQL` / `ConnectionStrings__mysql`，探测 `SELECT VERSION()`、`CREATE/DROP DATABASE` 权限）
- `tests/Pzd.IntegrationTests/IntegrationFactAttribute.cs`（动态 Skip 原因：`MySQL credentials not configured` / `MySQL unreachable` / `no CREATE DATABASE privilege`）
- `tests/Pzd.IntegrationTests/TestSchema.cs`（测试用最小表结构，只作用于 `pzd_it_*` 前缀库）
- `tests/Pzd.IntegrationTests/TenantIsolationTests.cs`、`TenantStatusTests.cs`
- `tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（`MySqlConnector`；Testcontainers 可从主路径移除）
- `src/Pzd.Infrastructure/Tenancy/TenantDbProvider.cs`（如需最小支撑改动）

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] 本机凭据可用：真跑并全绿，贴 `TEST EXECUTION SUMMARY` 与 `SELECT VERSION()`（期望 `8.0.46`）；自动建/清 `pzd_it_a`、`pzd_it_b`
- [ ] A/B 两库插同结构数据 → A 租户查询只返回 A，B 只返回 B；`QueryFilter` 生效
- [ ] 停用租户经 HTTP 接缝返回 403 `Tenant.Disabled`
- [ ] 无 `CREATE/DROP DATABASE` 权限：改用 `PZD_TEST_MYSQL_DB_A/B` 指定的既有库并**仍真跑**；两者都不可用时 Skip 原因必须为 `no CREATE DATABASE privilege`
- [ ] 无凭据 / MySQL 不可达：动态 Skip（原因明确），退出码 0，状态 **Skipped 而非 Passed**；跳过机制是动态探测，不写死 `Skip="…"`
- [ ] 幂等与安全：重复运行不残留库/数据；只操作 `pzd_it_` 前缀库，断言不触碰 `pzd_master` 等非测试库
- [ ] 密码不落日志、测试报告、异常消息（`git grep` 无残留）
- [ ] 本机实例可用时，集成测试是 `scripts/verify.ps1` 的必过段（不再"允许全 Skip"）；CI 无实例才允许 Skip 通过

**验证命令**

```powershell
# 凭据来自 local.env / 环境变量，不贴群
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
# 期望本机：Total ≥ 3、Failed 0；无凭据时 Skipped ≥ 2、退出码 0
# 普通目录（非 Low IL）等价：dotnet test tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
```

---

# 11: CLAUDE.md + 一键验收脚本 + 收尾

**What to build:** 一条命令给出「底座完成」的唯一证据；AI 协作者拿到约定即按统一风格写代码；凭据与推送安全有检查；规格状态回填。

**文件路径**

- `CLAUDE.md`（命名、分层、错误码、权限码、测试、Conventional Commits、AI 协作规则）
- `scripts/verify.ps1`（build → architecture tests → 单测 → 集成测试 → 汇总；任一段失败即非 0；运行器按顶部环境适配 ①② 选择：工作区内逐工程 `dotnet run --project`，普通目录/CI 用 `dotnet test Pzd.slnx`；集成测试段在本机实例可用时必须真跑，无凭据时 Skip 兜底）
- `README.md`（如何起、如何验、环境前提：.NET 10 / Node 24 / pnpm / 本机 MySQL 8.0.46 与 Redis 6379；凭据用 `local.env`（gitignored）或环境变量，示例不含真实密码）
- `.gitignore`（补齐 `node_modules/`、`.turbo/`、`*.user`、`artifacts/`、**`local.env`**）
- `docs/specs/foundation.md`（状态改为「已验收（日期）」+ 偏差回填）

**Blocked by:** 02, 03, 05, 06, 07, 08, 09, 10, 12

**Status:** ready-for-agent

- [ ] `pwsh -NoProfile -File scripts/verify.ps1` 在**本机实例可用**时退出码 0，打印 4 段结果与耗时；集成测试段真跑（Total ≥ 3、Failed 0），不是全 Skip
- [ ] 无凭据时同一脚本退出码 0，集成测试段打印 Skip 原因（CI 兜底），其余段照常必过
- [ ] 脚本按环境适配 ①② 选运行器：工作区内逐工程 `dotnet run --project`，普通目录/CI 用 `dotnet test Pzd.slnx`；不因 MTP 命名管道失败
- [ ] 脚本任一段失败时退出码非 0，且失败段号可定位
- [ ] `CLAUDE.md` 覆盖：工程结构、分层依赖、命名、Result/ProblemDetails、权限码格式、测试分层与 Skip 规则、提交规范、凭据红线（§4.12）
- [ ] `README.md` 的一键命令与脚本实际一致（经实测）；环境前提与 `local.env` 变量名写清楚，不含真实密码
- [ ] `.gitignore` 含 `local.env`；推送到 `github.com/pangzideng926/pzd` 前 `git grep` 无密码/连接串残留，`docs/reports/` 报告不含凭据
- [ ] 规格 §9 记录本轮偏差（如容器编排移出本轮、NetArchTest 替换、shadcn 退化、版本口径 F6）
- [ ] 无业务模块、无登录实现（审计确认范围守卫）

**验证命令**

```powershell
pwsh -NoProfile -File scripts/verify.ps1
```

---

# 12: Error.Metadata 只读快照（F8）

**What to build:** `Error.Metadata` 不再直接持有调用方传入的字典（[`Error.cs:33`](../../src/Pzd.SharedKernel/Results/Error.cs#L33)），构造时复制成只读快照（`ReadOnlyDictionary`，`StringComparer.Ordinal`）；构造后修改原字典、或尝试写入 `Metadata`，都不改变已创建的 `Error`；共享的 `EmptyMetadata` 不能被强转改写。只改 SharedKernel 这一处实现 + 用例，不动架构测试。

**文件路径**

- `src/Pzd.SharedKernel/Results/Error.cs`
- `tests/Pzd.SharedKernel.UnitTests/Results/ErrorTests.cs`（或新增 `ErrorMetadataTests.cs`）

**Blocked by:** 04

**Status:** ✅ 通过（审计：[`review-12.md`](../reports/review-12.md)；实现 commit `1463e9e`，51 条测试全绿）

- [x] 构造后修改传入字典（增 / 改 / 删键）不影响 `Error.Metadata`
- [x] `Error.Metadata` 是只读快照：底层为 `ReadOnlyDictionary<string, object?>`（或等价的只读实现），强转写入抛 `NotSupportedException`
- [x] 键比较为 `StringComparer.Ordinal`（大小写不同的键视为不同键）
- [x] `EmptyMetadata` 不可被改写（强转写入抛异常），且保持单例、不因空元数据重复分配
- [x] 现有 SharedKernel 单测全绿（当前 35 条）+ 新增用例，贴 `=== TEST EXECUTION SUMMARY ===`
- [x] 不改变 `Error.Equals` / `Code` / `ErrorType` 语义与错误码校验
- [x] 不改架构测试与其它工程

**边界说明（low，审计 §2.4，不阻塞）**

- 只做**字典结构快照**：metadata 的值若是可变对象（`List<T>`、嵌套字典），仍会随外部修改变化；`Metadata` 的 XML 注释需写明「仅结构快照，值请使用不可变类型」（旺总随票 05 顺手补，不做 `object?` 深拷贝）。
- 可选补测：`Clear()` 与 `ICollection<KeyValuePair>.Add` 两路读写守卫；`ReadOnlyDictionary` 同样抛 `NotSupportedException`，不影响当前正确性。

**验证命令**

```powershell
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
```

---

## 步骤 3 移交清单（不在本轮 DoD，另开票）

本轮为避免「假绿」主动下沉的事项，步骤 3 首次开模块 / 登录时逐条开票：

1. **FluentValidation 管道 + Application 层校验**（票 06 F-06-1 的生成端）：pipeline behavior、Validation 类型 Result 生成、`errors` 键 camelCase 的端到端断言；票 06 只保证出口转换与透传，不声称管道已验证。
2. **`*.Contracts` 自身引用约束**（规则④扩展）：Contracts 只能引用 SharedKernel / 其他 Contracts，堵住绕过模块隔离的后门。
3. **登录类端点租户策略**：`AllowAnonymousTenant` 的完整匿名解析（子域 / `X-Tenant`）、登录端点豁免与限流。
4. **权限码与授权**：`IAuthorizationPolicyProvider`、`.RequirePermission`、401/403 由授权中间件产生（依赖 F-06-3 已修）。
5. **Platform / Identity 模块**：租户开通流程、登录、`/api/me`（§18 步骤 3）。
6. **Outbox / 审计 / Quartz 的落库表**：`db/tenant` 迁移执行器与 `schema_versions`（假设 A6 的延后项）。

---

## 交接检查单（每张票完成时贴出）

1. 验证命令 + 真实输出摘要（不是"应该通过"）
2. 测试类票贴 `=== TEST EXECUTION SUMMARY ===` 行（`dotnet run --project` 形式）；若用 `dotnet test`，注明运行目录不是 Low IL（如 `E:\pzd-copy`）
3. 涉及真实实例的票（03/09/10）贴 MySQL `SELECT VERSION()`、用到的库名、Healthy / Skip 原因；输出与报告不得含密码
4. 改动文件清单（与票内路径对照，超出范围需说明）
5. 未完成项/偏离项（若无写「无」）
6. 下一票可开始（依赖已满足）
