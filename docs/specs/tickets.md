# PZD 底座 — 票（§18 步骤 1–2）

> 规格：[`docs/specs/foundation.md`](./foundation.md)
> 状态：执行中（2026-10-27）｜共 11 张｜先做无阻塞的 01、04，02/03 可并行
> 进度：01 ✅ 已交付（`e0ec679`，待测）｜04 ✅ 已交付（`50ac4ec`，待测）｜其余 ready-for-agent
> 交接：开发（旺总）→ 测试（李总）→ 审计（老登）→ 文档（老卫）
> 规则：每张票完成后必须贴出「验证命令 + 实际输出摘要」；失败不许标完成。测试先行的票在票内注明。

> **环境适配（2026-10-27 实测，票 01/04 回填）**
> ① 测试运行器改为 `xunit.v3.mtp-v2`（Microsoft.Testing.Platform），`global.json` 含 `"test": { "runner": "Microsoft.Testing.Platform" }`，测试工程统一在 `tests/Directory.Build.props` 配置，不再引用 `Microsoft.NET.Test.Sdk`（xUnit v3 + VSTest 在 .NET 10 SDK 已不兼容）。
> ② `E:\pzd` 被标了 Low Mandatory Level，MTP `dotnet test` 的命名管道握手失败（报 handshake failure，非测试失败）：**工作区内**用 `dotnet run --project tests/<工程>/<工程>.csproj`（同一批测试、同一退出码）；普通目录 / CI 用 `dotnet test Pzd.slnx` 等价（`E:\pzd-copy` 实测 39 passed、退出码 0）。
> ③ 版本号查询必须带 Build target：`dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version` → `0.1.0`；不加 `-t:Build` 只得 MSBuild 求值默认值 `1.0.0`。
> 下文各票里的 `dotnet test` 命令，在工作区内一律按 ② 替换为等价 `dotnet run --project` 形式；这不降低验收标准。

## 依赖图

```
01 仓库与 .NET 骨架
├─ 04 SharedKernel + 架构测试基线
│  ├─ 05 多租户内核 + 单测 ──┬─ 07 UoW + Outbox + 领域事件
│  ├─ 06 异常处理 + Result→HTTP ─┘  08 可观测性 + 审计 + 安全横切（←06）
│  ├─ 09 Quartz + 健康检查 + 版本
│  └─ 10 Testcontainers 优雅跳过 + 租户隔离集成测试
├─ 02 前端 monorepo 骨架
└─ 03 Aspire 编排与一键启动（←01,02）
11 CLAUDE.md + 一键验收脚本（←02,03,05,06,07,08,09,10）
```

| # | 票 | Blocked by | 验证命令（根目录执行） |
|---|---|---|---|
| 01 | 仓库与 .NET 骨架 | 无 | `dotnet build Pzd.slnx -warnaserror` |
| 02 | 前端 monorepo 骨架 | 01 | `pnpm install --frozen-lockfile && pnpm -w build` |
| 03 | Aspire 编排与一键启动 | 01, 02 | `pwsh -NoProfile -File scripts/verify-apphost.ps1` |
| 04 | SharedKernel + 架构测试基线 | 01 | `dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj` |
| 05 | 多租户内核 + 单测 | 04 | `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj` |
| 06 | 异常处理 + Result→HTTP | 04 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` |
| 07 | UoW + Outbox + 领域事件 | 05, 06 | `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj` |
| 08 | 可观测性 + 审计 + 安全横切 | 06 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` |
| 09 | Quartz + 健康检查 + 版本 | 04 | `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj` |
| 10 | Testcontainers 跳过 + 租户隔离集成测试 | 05 | `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj` |
| 11 | CLAUDE.md + 一键验收脚本 | 02,03,05,06,07,08,09,10 | `pwsh -NoProfile -File scripts/verify.ps1` |

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

**Status:** ready-for-agent

- [ ] `git init` 完成，初始提交存在，工作区无未跟踪的 `bin/obj`
- [ ] `git tag` 含 `v0.1.0`（MinVer 可产出 `0.1.0`）
- [ ] `dotnet build Pzd.slnx -warnaserror` 退出码 0，无警告
- [ ] 5 个测试工程各自 `dotnet run --project tests/<工程>/<工程>.csproj` 退出码 0（5 个 smoke test 全绿）；普通目录 `dotnet test Pzd.slnx` 等价通过（工作区 Low IL 限制见顶部环境适配 ②）
- [ ] `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version` 输出 `0.1.0`（必须带 `-t:Build`，否则得 `1.0.0`）
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

# 03: Aspire 编排与一键启动

**What to build:** `dotnet run --project src/Pzd.AppHost` 一条命令拉起 API 与前端 dev server；无 Docker / 无 MySQL / 无 Redis 时 API 以降级模式启动并打印明确 Warning，`/health`、`/api/version` 可访问，Ctrl+C 优雅退出；有 Docker 时额外拉起 MySQL 与 Redis。

**文件路径**

- `src/Pzd.AppHost/Pzd.AppHost.csproj`（Aspire AppHost SDK + `Aspire.Hosting.AppHost`）
- `src/Pzd.AppHost/Program.cs`、`appsettings.json`、`appsettings.Development.json`、`Properties/launchSettings.json`
- `src/Pzd.Api/appsettings.Development.json`（`ConnectionStrings:master` / `redis` 缺省为空）
- `src/Pzd.Api/Program.cs`（接受配置注入；**本票不实现**租户/异常/日志，仅启动骨架）
- `scripts/verify-apphost.ps1`（后台启动 AppHost → 轮询 `/health` → 断言 200 → 关闭进程）

**Blocked by:** 01, 02

**Status:** ready-for-agent

- [ ] API 在 AppHost 中固定 `http://127.0.0.1:5080`（便于前端 proxy 与脚本验证）
- [ ] 无 Docker 且无连接串：启动成功，日志含 `Degraded mode: MySQL/Redis unavailable`，`/health` 返回 200 且 readiness 明细标 Degraded
- [ ] 提供 `ConnectionStrings__mysql` / `ConnectionStrings__redis` 时：注册为外部资源并等待可用，不因缺 Docker 失败
- [ ] Docker 可用时：`AddMySql` / `AddRedis` 容器资源被拉入并注入连接串（本机无 Docker，允许以「配置分支 + 单元测试/日志断言」验证，实际容器启动记入交接说明）
- [ ] `GET /api/version` 返回 `version`、`buildId`、`buildTime`
- [ ] `scripts/verify-apphost.ps1` 在无 Docker 机器上退出码 0
- [ ] Ctrl+C / 停止父进程后子进程退出，无残留监听端口

**验证命令**

```powershell
pwsh -NoProfile -File scripts/verify-apphost.ps1
```

---

# 04: SharedKernel + 架构测试基线

**What to build:** 领域层最小原语可用（Entity / AggregateRoot / ValueObject / DomainEvent / Result / Error），且架构测试能自动拦截分层违规并带一条反向用例证明规则有效；后续所有模块直接复用。

**文件路径**

- `src/Pzd.SharedKernel/Domain/Entity.cs`、`AggregateRoot.cs`、`ValueObject.cs`、`IDomainEvent.cs`、`IDomainEventCollector.cs`
- `src/Pzd.SharedKernel/Results/Result.cs`、`Result{T}.cs`、`Error.cs`、`ErrorType.cs`
- `src/Pzd.SharedKernel/Abstractions/IClock.cs`、`ICurrentUser.cs`、`ITenantContext.cs`（接口放 SharedKernel，实现放 05）
- `tests/Pzd.SharedKernel.UnitTests/Results/*.cs`、`Domain/*.cs`
- `tests/Pzd.ArchitectureTests/DependencyRulesTests.cs`、`ReverseCaseTests.cs`、`ArchitectureTestAssembly.cs`（反向用例用的违规类型）、`Pzd.ArchitectureTests.csproj`（反向用例对 `SqlSugarCore` 的真实引用；显式提版 `Pkcs 10.0.14` 以过 `-warnaserror`）

**Blocked by:** 01

**Status:** ready-for-agent（建议测试先行：先写 Result / 架构规则测试再补实现）

- [ ] `Result.Success` / `Result.Failure(Error)` 不可同时成立；`Result<T>` 泛型转换正确
- [ ] `Error` 按 Code 相等，含 `Type` / `Metadata`；错误码格式 `模块.实体.原因` 有测试
- [ ] `AggregateRoot` 收集领域事件且 `ClearEvents()` 后为空；`ValueObject` 按值相等
- [ ] `ITenantContext.TenantId` 为 `long`（与雪花主键一致）；`IClock`、`ICurrentUser` 接口就位
- [ ] 架构规则 4 条：① SharedKernel 不引用 Infrastructure/Api；② Infrastructure 不引用 Api；③ `*.Domain` 命名空间不引用 SqlSugar / ASP.NET Core；④ 模块间只能引用 `*.Contracts`（模块为空时允许空集）
- [ ] 反向用例：测试程序集内构造 `Pzd.SharedKernel.Domain.Fake` 类型引用 `SqlSugar`，规则必须报违规（测试断言"检测到 1 处违规"），证明规则非空跑
- [ ] `dotnet run --project tests/Pzd.SharedKernel.UnitTests/...`（29 passed）与 `tests/Pzd.ArchitectureTests/...`（7 passed）均退出码 0
- [ ] 若 NetArchTest 对 net10 不兼容，改用 ArchUnitNET 并保持规则语义与反向用例

**验证命令**

```powershell
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
```

---

# 05: 多租户内核 + 单测

**What to build:** 请求进入后能解析出可信租户上下文（登录 claim 优先，匿名头/子域次之），停用/过期立即 403；业务代码通过 Scoped 的 `ITenantDbProvider` 按需取租户连接，绝不串库。全部用单测证明，不依赖数据库。

**文件路径**

- `src/Pzd.SharedKernel/Abstractions/ITenantContext.cs`（若 04 未建）
- `src/Pzd.Infrastructure/Tenancy/TenantContext.cs`、`TenantResolutionMiddleware.cs`、`ITenantStore.cs`、`InMemoryTenantStore.cs`、`TenantInfo.cs`、`ITenantDbProvider.cs`、`TenantDbProvider.cs`、`TenantErrors.cs`
- `src/Pzd.Api/Program.cs`（注册 `UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization`）
- `src/Pzd.Api/appsettings.json`（`Tenants:*` 测试用租户配置，或内存 Store 由 DI 注入）
- `tests/Pzd.Infrastructure.UnitTests/Tenancy/*.cs`
- `tests/Pzd.Api.UnitTests/Tenancy/*.cs`（WebApplicationFactory，走 HTTP 接缝）

**Blocked by:** 04

**Status:** ready-for-agent（测试先行）

- [ ] 已登录且 claim `tid` 存在：路由/端点拿到该租户，`X-Tenant` 头被忽略（有测试证明"忽略"）
- [ ] 匿名 + `X-Tenant`：解析成功；匿名 + 子域名：解析成功；两者同时存在时子域名优先
- [ ] 无任何租户来源且端点要求租户：400/403 ProblemDetails，错误码 `Tenant.NotFound`
- [ ] 租户停用 → 403 + `code = Tenant.Disabled`；租户过期 → 403 + `code = Tenant.Expired`（精确断言响应体）
- [ ] 每个请求都重新校验状态（`ITenantStore` 被再次调用，缓存不跨请求）
- [ ] 同一 `ITenantContext` 不跨请求泄漏：两个并发/顺序 scope 各自 `Set` 后互不影响
- [ ] `TenantDbProvider`：不存在连接时加锁双检、只 `AddConnection` 一次、返回 `GetConnectionScope(tid)`；无租户上下文取连接抛明确异常（错误码 `Tenant.NotResolved`）
- [ ] 全局单例上不出现 `ChangeDatabase`（架构测试或代码检查断言）
- [ ] 上述全部为单测/HTTP 接缝测试，测试命令（工作区 `dotnet run --project`，见顶部环境适配 ②）无 Docker 也绿

**验证命令**

```powershell
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

---

# 06: 异常处理 + Result→HTTP

**What to build:** 端点只需 `return result.ToHttpResult()` 就得到准确状态码与统一 ProblemDetails（`type` / `title` / `status` / `code` / `traceId` / `errors`）；意外异常统一走 `IExceptionHandler`，生产不泄堆栈。

**文件路径**

- `src/Pzd.SharedKernel/Results/ErrorType.cs`（与 04 对齐，必要时补 `ClientTooOld`、`TenantUpgrading`）
- `src/Pzd.Api/Errors/ResultHttpExtensions.cs`（`ToHttpResult()`）、`ProblemDetailsCustomization.cs`、`GlobalExceptionHandler.cs`、`ProblemDetailsSetup.cs`
- `src/Pzd.Api/Program.cs`（`AddProblemDetails` + `IExceptionHandler` 链 + 中间件顺序）
- `tests/Pzd.Api.UnitTests/Errors/ErrorMappingTests.cs`、`UnexpectedExceptionTests.cs`
- 测试辅助：`tests/Pzd.Api.UnitTests/TestEndpoints.cs`（仅测试用最小端点，生产代码不加调试端点）

**Blocked by:** 04

**Status:** ready-for-agent（测试先行，§8.3 映射表逐条先写）

- [ ] 映射全表通过 HTTP 接缝逐条断言：400（含 `errors` 字典）、401、403、404、409（乐观锁/唯一键语义）、422、426、503
- [ ] `type` = `https://pzd/errors/{kebab-code}`；`code` 与 `Error.Code` 一致；`traceId` 与响应头/`Activity.Id` 一致
- [ ] 未知异常 → 500，响应体无堆栈、无异常类型名，含 `traceId`；开发环境与生产环境行为差异有测试或明确断言
- [ ] FluentValidation 失败 → 400，`errors` 键为 camelCase，与前端字段名一致
- [ ] 404/405 等框架生成的问题响应同样带 `code`/`traceId`（自定义 ProblemDetails 覆盖到框架路径）
- [ ] 不使用「永远 200 + {code,msg,data}」包装（代码审查 + 测试断言）

**验证命令**

```powershell
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

---

# 07: UoW + Outbox + 领域事件

**What to build:** 一次 Command 提交时，聚合收集的领域事件先分发、再与 Outbox 一起提交；失败整体回滚；Outbox 由后台幂等投递。UoW 与 Outbox 存储可替换（本轮内存实现，步骤 3 换落库）。

**文件路径**

- `src/Pzd.SharedKernel/Abstractions/IUnitOfWork.cs`
- `src/Pzd.Infrastructure/Persistence/UnitOfWork.cs`、`IOutboxStore.cs`、`InMemoryOutboxStore.cs`、`OutboxMessage.cs`、`OutboxDispatcher.cs`、`OutboxDispatcherHostedService.cs`、`DomainEventDispatcher.cs`
- `src/Pzd.Api/Program.cs`（注册 UoW 与后台投递）
- `tests/Pzd.Infrastructure.UnitTests/Persistence/UnitOfWorkTests.cs`、`OutboxTests.cs`

**Blocked by:** 05, 06

**Status:** ready-for-agent（测试先行）

- [ ] 提交顺序被断言：领域事件分发 → 写 Outbox → 事务提交（用假 store / 假发布器记录调用序列）
- [ ] 领域事件处理器抛异常 → UoW 不提交、Outbox 无残留消息
- [ ] 聚合事件分发后从聚合清除，不重复分发
- [ ] Outbox 消息带 `TenantId`；消费前 `ITenantContext.Set` 被调用（假消费者断言）
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

# 09: Quartz + 健康检查 + 版本端点

**What to build:** 调度器以内存存储先跑通一个示例任务（可停用、有下次执行时间），后续换 MySQL AdoJobStore 只改注册；`/health` 分离存活/就绪且无 DB 时降级而非不健康；`/api/version` 返回 MinVer 版本、buildId、构建时间。

**文件路径**

- `src/Pzd.Infrastructure/Jobs/QuartzRegistration.cs`、`HeartbeatJob.cs`、`JobDefinitionAttribute.cs`、`JobExecutionLogger.cs`
- `src/Pzd.Api/Endpoints/VersionEndpoint.cs`、`HealthSetup.cs`
- `src/Pzd.Api/Program.cs`（Quartz / health / version 注册）
- `src/Pzd.Api/Properties/AssemblyInfo.cs` 或 props（`buildId` / `buildTime` 由构建注入）
- `tests/Pzd.Api.UnitTests/Endpoints/VersionEndpointTests.cs`、`HealthEndpointTests.cs`、`tests/Pzd.Infrastructure.UnitTests/Jobs/QuartzRegistrationTests.cs`

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Quartz 以内存存储启动，`HeartbeatJob` 按注册 cron（`Asia/Shanghai`）存在，`[DisallowConcurrentExecution]`
- [ ] 提供 master 连接串时切到 MySQL AdoJobStore + 集群配置（本机无 DB，允许以单元测试断言配置分支，实跑记入交接）
- [ ] `/health/live` 永远 200；`/health/ready` 无 DB/Redis 时 200 + `Degraded` + 每项检查明细；不因缺资源变 Unhealthy
- [ ] `/api/version` 返回 `version`（MinVer）、`buildId`（git sha）、`buildTime`（ISO 8601），字段名与架构 §15.1 一致
- [ ] 版本号与 `dotnet msbuild ... -getProperty:Version` 一致（有测试断言或启动验证）
- [ ] 本票不做 `version.json`、前端刷新提示、`X-Min-Client-Build`

**验证命令**

```powershell
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project src/Pzd.AppHost
# 另开终端：curl http://127.0.0.1:5080/health/live ; curl http://127.0.0.1:5080/api/version
```

---

# 10: Testcontainers 优雅跳过 + 租户隔离集成测试

**What to build:** 无 Docker 的机器上集成测试明确 Skipped 且测试命令退出码 0，不阻塞验收；有 Docker 时用两个真实 MySQL 租户库验证跨租户隔离、QueryFilter 与停用租户 403。

**文件路径**

- `tests/Pzd.IntegrationTests/DockerFactAttribute.cs`（探测 Docker 端点，缺失时 `Skip = "Docker unavailable: …"`）
- `tests/Pzd.IntegrationTests/MySqlFixture.cs`（Testcontainers `MySqlBuilder`，单次容器、两库）
- `tests/Pzd.IntegrationTests/TenantIsolationTests.cs`、`TenantStatusTests.cs`
- `tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（`Testcontainers.MySql`）
- `src/Pzd.Infrastructure/Tenancy/TenantDbProvider.cs`（如需最小支撑改动）

**Blocked by:** 05

**Status:** ready-for-agent

- [ ] 无 Docker：`dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj` 退出码 0，输出中含 Skip 与原因（`Docker unavailable`），测试报告状态为 Skipped 而非 Passed
- [ ] 跳过机制是**动态探测**（`DockerFactAttribute`），不是写死 `Skip="…"`
- [ ] 有 Docker：创建 `pzd_t_a` / `pzd_t_b` 两库 → 各插一行同结构数据 → A 租户查询只返回 A 数据，B 同样；`QueryFilter` 生效
- [ ] 有 Docker：租户停用后请求得到 403 `Tenant.Disabled`（走 HTTP 接缝）
- [ ] 集成测试套件不依赖具体端口/机器路径，容器自动清理
- [ ] 不把集成测试加入 `scripts/verify.ps1` 的必过集合（只作为可选阶段，见 11）

**验证命令**

```powershell
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
# 期望本机：退出码 0，Skipped ≥ 2，原因含 Docker unavailable
# 普通目录（非 Low IL）等价：dotnet test tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
```

---

# 11: CLAUDE.md + 一键验收脚本 + 收尾

**What to build:** 一条命令给出「底座完成」的唯一证据；AI 协作者拿到约定即按统一风格写代码；规格状态回填。

**文件路径**

- `CLAUDE.md`（命名、分层、错误码、权限码、测试、Conventional Commits、AI 协作规则）
- `scripts/verify.ps1`（build → architecture tests → 单测 → 集成测试（允许全 Skip）→ 汇总；任一段失败即非 0；运行器按顶部环境适配 ①② 选择：工作区内逐工程 `dotnet run --project`，普通目录/CI 用 `dotnet test Pzd.slnx`）
- `README.md`（如何起、如何验、环境前提：.NET 10 / Node 24 / pnpm、Docker 可选）
- `docs/specs/foundation.md`（状态改为「已验收（日期）」+ 偏差回填）
- `.gitignore`（补齐 `node_modules/`、`.turbo/`、`*.user`、`artifacts/`）

**Blocked by:** 02, 03, 05, 06, 07, 08, 09, 10

**Status:** ready-for-agent

- [ ] `pwsh -NoProfile -File scripts/verify.ps1` 在无 Docker 机器上退出码 0，打印 4 段结果与耗时
- [ ] 脚本按环境适配 ①② 选运行器：工作区内逐工程 `dotnet run --project`，普通目录/CI 用 `dotnet test Pzd.slnx`；不因 MTP 命名管道失败
- [ ] 脚本任一段失败时退出码非 0，且失败段号可定位
- [ ] `CLAUDE.md` 覆盖：工程结构、分层依赖、命名、Result/ProblemDetails、权限码格式、测试分层与 Skip 规则、提交规范
- [ ] `README.md` 的一键命令与脚本实际一致（经实测）
- [ ] 规格 §9 记录本轮偏差（如未做 Aspire 容器实跑、NetArchTest 替换、shadcn 退化）
- [ ] 无业务模块、无登录实现（审计确认范围守卫）

**验证命令**

```powershell
pwsh -NoProfile -File scripts/verify.ps1
```

---

## 交接检查单（每张票完成时贴出）

1. 验证命令 + 真实输出摘要（不是"应该通过"）
2. 测试类票贴 `=== TEST EXECUTION SUMMARY ===` 行（`dotnet run --project` 形式）；若用 `dotnet test`，注明运行目录不是 Low IL（如 `E:\pzd-copy`）
3. 改动文件清单（与票内路径对照，超出范围需说明）
4. 未完成项/偏离项（若无写「无」）
5. 下一票可开始（依赖已满足）
