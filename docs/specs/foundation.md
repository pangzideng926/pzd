# PZD 工程底座与横切能力 — 规格（§18 步骤 1–2）

> 状态：执行中（2026-10-27）｜假设 A1–A8 待确认｜票 01/04/05/12 审计通过；票 06 返工中（预审 F-06 三项）；票 03/09/10 口径已按本机实例升格（见 §4.6 / §4.8 / §4.12 / §9 修订记录）
> 上游：[`docs/architecture.md`](../architecture.md)（已确认），本规格只覆盖 §18 步骤 1「工程底座」与步骤 2「横切能力」
> 票：[`docs/specs/tickets.md`](./tickets.md)
> 依据方法论：to-spec / to-tickets（Matt Pocock），测试接缝取最高层

---

## 1. Problem Statement

仓库 `E:\pzd` 目前只有一份已确认的架构文档，没有任何可编译、可测试、可启动的代码。团队要在它上面按 §18 的路线逐步实现一个模块化单体后台系统，但第一步缺少「地基」：

- 没有解决方案、中央包管理、SDK 锁定与版本号来源（MinVer 依赖 git tag，而当前目录还不是 git 仓库）；
- 没有前后端 monorepo 骨架，也没有 Aspire 编排；
- 架构中最关键的横切约束——多租户上下文、统一异常处理（Result → ProblemDetails）、UoW + Outbox、可观测性、安全基线——都还没有任何可执行的证据，后续模块只能靠口头约定；
- 机器上没有 Docker（但有本机 MySQL 8.0.46 与 Redis/Memurai 实例，root 需密码）：既不能把容器当主路径，也不能让「必须起数据库」成为卡死流水线的前提，需要一套「本机实例为主、缺失时降级/Skip」的明确口径。

**对使用者的意义**：如果这一步不落地，后续每一个模块（Platform、Identity、Organization…）都会各自发明一套错误处理、租户取值和日志写法，架构文档里的分层规则无法被自动拦截，返工成本随模块数量线性增长。

## 2. Solution

在 `E:\pzd` 建成一个**最小但完整**的工程底座，并用**一条命令**验收：

1. 根目录扁平结构：`Pzd.slnx` + `global.json` + `Directory.Build.props` + `Directory.Packages.props` + pnpm workspace + Turborepo，与架构 §2 一致；
2. 前后端骨架可编译：后端 4 个工程（SharedKernel / Infrastructure / Api / AppHost）+ 5 个测试工程，前端空壳 SPA（无登录页、无业务页面）；
3. 横切能力具备可执行证据：租户上下文与解析中间件、租户连接工厂（按需加连接）、UoW + Outbox、Result→ProblemDetails 映射、全局异常处理链、Serilog + OpenTelemetry、审计日志通道、Antiforgery、Data Protection、Quartz 基础、健康检查与 `/api/version`；
4. 测试分层落地：架构测试（NetArchTest）可自动拦截分层违规、单元测试覆盖租户隔离与异常映射、集成测试**连本机 MySQL 真跑跨库隔离**（凭据/实例缺失时动态 Skip 且不阻塞验收）；
5. 验收命令 `pwsh -NoProfile -File scripts/verify.ps1` 在**本机实例可用**的当前机器上退出码 0（集成测试真跑；CI 无实例时 Skip 兜底）。

**不包含**：登录、会话、`/api/me`、权限码、菜单、前端登录外壳与布局（§18 步骤 3–4，下一轮），也不创建任何业务模块。

## 3. User Stories

1. As a 后端开发者, I want 一条命令编译整个解决方案并锁定 SDK 版本, so that 任何人 checkout 后都不会因 SDK 差异编译失败
2. As a 后端开发者, I want 中央包管理集中声明 NuGet 版本, so that 各工程不会各自漂移版本
3. As a 后端开发者, I want 版本号来自 git tag 且构建产物带版本, so that `/api/version` 与发布记录能对上
4. As a 开发者, I want 解决方案内工程归属由 `.slnx` 解决方案文件夹表达, so that 根目录保持扁平且不出现 `src/` 之外的分组嵌套
5. As a 前端开发者, I want `pnpm install` + `pnpm -w build` 一次通过, so that 前后端可以在同一仓库同一命令下构建
6. As an 运维/开发者, I want `dotnet run --project src/Pzd.AppHost` 一条命令拉起 API 与前端 dev server, so that 本地环境不靠手写多窗口启动
7. As a 开发者, I want 本机实例可用时 AppHost 一条命令连上真实 MySQL/Redis 并启动, so that 本地环境与验收环境一致
8. As a 开发者, I want 数据库与缓存凭据只从环境变量/本地 env 注入而绝不进仓库, so that 团队共享同一套启动方式又不泄露密码
9. As a 开发者, I want 请求进入后能从已登录 claim 或匿名头/子域解析出租户, so that 业务代码永远拿到可信的租户上下文
10. As a 开发者, I want 租户停用或过期时请求直接得到 403 ProblemDetails（`Tenant.Disabled` / `Tenant.Expired`）, so that 前端能按错误码统一处理
11. As a 开发者, I want 租户连接按需加入且只在 Scoped 内获取, so that 租户连接不会串库、也不会拖慢启动
12. As a 开发者, I want 领域层返回 `Result` 而端点自动映射为准确状态码的 ProblemDetails, so that 业务代码不写 HTTP 细节
13. As a 开发者, I want 未知异常返回 500 且不泄露堆栈、响应带 traceId, so that 线上排障有据且安全
14. As a 开发者, I want 一次 Command 对应一次 UoW 提交并在提交前分发领域事件、写 Outbox, so that 跨模块最终一致有统一出口
15. As a 开发者, I want 每个请求一行 Serilog 摘要并自动带 TenantId / UserId / TraceId, so that 日志可按租户与请求关联
16. As a 开发者, I want OpenTelemetry 的 traces / metrics 已注册并可导出 OTLP, so that 后续接 Seq / Aspire Dashboard 不用改业务代码
17. As a 租户管理员（后续模块的读者）, I want 关键写操作能通过 `WithAuditLog` 采集审计并异步批量落库, so that 审计不阻塞请求
18. As a 安全负责人, I want 写请求默认校验 Antiforgery、Cookie 与安全响应头按 §7.3 配置, so that 同源 Cookie 方案具备基本防护
19. As an 运维, I want Data Protection 密钥可持久化配置, so that 多节点部署或重启后 Cookie 不会全部失效
20. As a 开发者, I want Quartz 以内存存储先跑通一个示例任务, so that 后续换 MySQL AdoJobStore 只改注册配置
21. As an 运维, I want `/health` 与 `/api/version` 无需数据库即可返回, so that 存活探针与发布核对不依赖 DB
22. As a 测试/审计, I want 架构测试能拦下「Domain 引用 SqlSugar」「模块引用非 Contracts」等违规, so that 分层规则不靠人工 review
23. As a 测试/审计, I want 架构测试自身有反向用例（故意构造违规类型必须被判失败）, so that 规则不是空跑
24. As a 测试, I want 多租户与异常处理的单元测试不依赖数据库, so that 没有实例也能验证核心行为
25. As a 测试, I want 凭据或本机实例不可用时集成测试明确 Skip 并给出原因, so that CI 与新机器上验收流水线不会红
26. As a 测试, I want 本机 MySQL 以两个真实租户库验证跨租户隔离, so that 独立库方案得到真实验证而不是替身
27. As a 审计, I want 一条 `scripts/verify.ps1` 汇总编译 / 架构测试 / 单测 / 集成测试(skip) 结果, so that 「底座完成」有唯一可复现证据
28. As an AI 协作者, I want `CLAUDE.md` 记录命名、分层、错误码、权限码、测试与提交约定, so that 后续代码风格一致

## 4. Implementation Decisions

### 4.1 仓库与工程

- 采用架构 §2 的**根目录扁平**结构；`.slnx` 使用解决方案文件夹（`src` / `tests`）。
- 本轮创建：`src/Pzd.SharedKernel`、`src/Pzd.Infrastructure`、`src/Pzd.Api`、`src/Pzd.AppHost`；`tests/Pzd.ArchitectureTests`、`tests/Pzd.SharedKernel.UnitTests`、`tests/Pzd.Infrastructure.UnitTests`、`tests/Pzd.Api.UnitTests`、`tests/Pzd.IntegrationTests`。
- 本轮**不创建** `Pzd.Platform / Pzd.Identity / Pzd.Organization / Pzd.System / Pzd.Workflow / Pzd.Realtime` 及其 Contracts（步骤 3+）。
- `global.json` 锁 `10.0.4xx`；`Directory.Build.props` 开启 `Nullable`、`TreatWarningsAsErrors`、`ImplicitUsings`，接入 MinVer；`Directory.Packages.props` 做 NuGet 中央包管理。
- 仓库初始化：`git init` + 初始提交 + `v0.1.0` tag（MinVer 需要 tag 才能产出可预测版本）；`.gitignore` 排除 `bin/ obj/ node_modules/ .turbo/ *.user` 等。
- 包版本：实施时取各包**当时最新稳定版**并记录于 `Directory.Packages.props`，规格不锁具体版本号（避免过期）。

### 4.2 测试接缝（Seams）

按 to-spec 要求取最高接缝，按优先级：

1. **HTTP 接缝**（首选，唯一主要接缝）：`WebApplicationFactory<Program>` / `TestServer` 覆盖中间件顺序、ProblemDetails 映射、`/health`、`/api/version`、Antiforgery 行为。该接缝不依赖数据库。
2. **DI/Scoped 接缝**：`ITenantContext`、租户解析中间件、`ITenantDbProvider`、`ITenantStore` 以内存实现单测；验证请求间隔离用两个独立 scope。
3. **端口接缝**：UoW 用假 `IOutboxWriter` / 假事件发布器；审计日志用假有界 Channel 消费者；时钟用 `IClock`。
4. **本机实例接缝**（最低优先）：本机 MySQL 8.0.46 / Redis(Memurai) 真实实例验证跨库隔离、QueryFilter、AdoJobStore 与 readiness；实例或凭据不可用时动态 Skip（CI 兜底），不阻塞验收。

不为测试在生产代码里新增开关；唯一例外是 AppHost 的「外部依赖可选」由配置驱动（见 4.6）。

### 4.3 多租户内核

- `ITenantContext`（放 SharedKernel 抽象）：`TenantId`（**`long`**，与雪花主键一致）、`HasTenant`、`Set(tid)`、`Clear()`；实现 Scoped，禁止静态单例保存租户。`X-Tenant` 头的租户 code → id 解析由 `ITenantStore` 负责（票 05）。
- 解析中间件顺序：`UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization`。
  - 已登录：claim `tid` 优先，忽略 `X-Tenant` 头。
  - 匿名：子域名或 `X-Tenant` 头（仅匿名端点可用），二者同时存在时子域名优先。
  - 无法解析且端点要求租户 → 400/403 ProblemDetails（错误码 `Tenant.NotFound`）。
  - `ITenantStore` 校验状态与到期：停用 → 403 `Tenant.Disabled`，过期 → 403 `Tenant.Expired`；每次请求都校验（不缓存「已通过」结论超过请求生命周期）。
- **免租户豁免（F-05-4）**：`/health/live`、`/health/ready`、`/api/version`（以及后续登录类端点）标记 `AllowAnonymousTenant` metadata，中间件跳过租户解析与状态校验；这些端点无 claim / `X-Tenant` / 子域时返回 200。豁免端点之外，缺租户仍返回 400 `Tenant.NotFound`。**票 03 开工硬前置**，由票 03 / 09 实现。
- `ITenantDbProvider`：按 `TenantId` **每租户懒建一个独立 `SqlSugarScope`**（连接配置在构造期注入，AOP / 过滤器随配置挂载）+ 双检缓存；同一租户只建一次、**连接创建只查一次 `ITenantStore`（显式重载为校验状态按次查库）**；禁止全局单例 `AddConnection(tid)` 与 `ChangeDatabase`。原因：实测 `SqlSugarScope.AddConnection` 在 async 上下文里是上下文私有的，方法返回后对调用方不可见（票 05 偏差回填，见 §9）。租户连接串来自 `ITenantStore`（本轮内存实现 / 配置注入，加密与 KMS 属步骤 3）。
- `master` 连接始终存在（`ConfigId = "master"`）；租户库表仍保留 `TenantId` 与全局 QueryFilter（纵深防御）。

### 4.4 异常处理与 Result

- `Result` / `Result<T>` / `Error`（`Code`、`Type`、`Message`、`Metadata`）与 `ErrorType`（Validation、Unauthorized、Forbidden、NotFound、Conflict、BusinessRule、ClientTooOld、TenantUpgrading、Unexpected）位于 SharedKernel。
- `Error.Metadata` 是**构造时复制的只读快照**（`ReadOnlyDictionary`，`StringComparer.Ordinal`），不直接持有调用方字典；`EmptyMetadata` 不可被强转改写（F8，票 12）。
- 端点统一 `result.ToHttpResult()`，映射严格按架构 §8.3：400/401/403/404/409/422/426/503；`type` = `https://pzd/errors/{kebab-code}`。
- 全局 `IExceptionHandler` 链只处理意外异常：500、生产不返回堆栈、`traceId` 取 `Activity.Current?.Id`；`AddProblemDetails(o => o.CustomizeProblemDetails = ...)` 统一补 `traceId`、`code`。
- 租户中间件手写的 400/403（`Tenant.NotFound` / `Tenant.Disabled` / `Tenant.Expired`）必须与 `ToHttpResult()` 共用同一出口：响应体带 `traceId`、响应头带 `X-Trace-Id`、kebab 转换只有一份实现（F-06-2，票 06 收口）。
- 框架裸 401/403（status code pages 路径）必须映射为 ProblemDetails：`Auth.Unauthorized` / `Auth.Forbidden`，不得落成 `System.Unexpected` /「服务器内部错误」（F-06-3，本轮修）。
- `errors` 键统一 camelCase（`userName`），由 Result→ProblemDetails 出口转换；本轮**没有** Application 层与 FluentValidation，Validation 错误由测试手工构造，**管道与校验规则属步骤 3 新票**（tickets.md「步骤 3 移交清单」第 1 项），上线后保持同一行为与断言。
- 前端侧错误拦截（orval mutator）属步骤 4，本轮只留 `packages/api` 目录与 README 占位。

### 4.5 UoW + Outbox + 领域事件

- 一个 Command = 一次 UoW 提交；UoW 在提交前：收集聚合领域事件 → 进程内分发 → 写 Outbox（同事务）→ 提交。
- Outbox 投递由 `BackgroundService` 轮询有界队列/表（本轮先实现内存 Outbox + 可替换 `IOutboxStore` 抽象，真实落库随步骤 3 的租户库 SQL 一起接入）；投递幂等，失败重试并记 Warning/Error。
- 禁止跨库事务（master 与租户库）；跨库流程拆为可重试步骤。
- 消息携带 `TenantId`，消费前 `ITenantContext.Set`。

### 4.6 Aspire 编排（引用本机实例，2026-10-27 修订）

- `Pzd.AppHost` 使用 .NET Aspire AppHost；资源：`Pzd.Api` + Vite dev server（`apps/web`）。
- MySQL / Redis **不建容器**，改为引用本机实例（本机已确认：MySQL80 8.0.46 在 `127.0.0.1:3306`，root 需密码；Memurai 在 `127.0.0.1:6379`，无密码）：
  - 连接串来自环境变量 / 用户机密（`ConnectionStrings__mysql`、`ConnectionStrings__redis`），AppHost 以 `AddConnectionString` 注册并把引用注入 API；仓库内 `appsettings` **不得**出现真实密码。
  - 连接串可用且实例可达 → API 依赖实例启动，`/health/ready` 为 Healthy（明细 mysql/redis Up），**这是本轮主路径**；
  - 实例或凭据缺失（CI / 新机器）→ 不阻塞启动，API 以降级模式运行，日志输出一条 `Degraded mode: MySQL/Redis unavailable` 的 Warning，`/health/ready` 返回 Degraded（HTTP 200 + 明细）。
- 本机实例可用时，「一条命令启动全部服务」（API + 前端 + 已连真实 MySQL/Redis）成立；降级路径仅作兜底，不再是主验收路径。
- Docker 容器编排从本轮验收中移除（无 Docker，且本机实例已覆盖需求）；未来需要时另开票。

### 4.7 可观测性与安全横切

- Serilog：控制台 + `UseSerilogRequestLogging`，每请求一行摘要；`LogContext` 推入 `TenantId`、`UserId`、`TraceId`、`ClientIp`。
- OpenTelemetry：traces + metrics + OTLP exporter（端点由配置提供，缺省不导出），ASP.NET Core / HttpClient / 运行时 instrumentation。
- 审计日志：端点 `WithAuditLog(模块, 动作)` metadata → Endpoint Filter 采集 → 有界 `Channel` → `BackgroundService` 批量写；有 DB 时落 `audit_logs`（建表 SQL 随步骤 3），无 DB 时计数并 Warning（本轮不静默丢弃且不影响请求）。
- Antiforgery：非安全方法（POST/PUT/PATCH/DELETE）校验 `X-XSRF-TOKEN`；Cookie 策略 `HttpOnly` + `SameSite=Lax` + `Secure`（开发环境例外）。
- Data Protection：密钥环持久化路径可配置（默认 `%LOCALAPPDATA%\Pzd\dp-keys` 或配置项），生产接 Redis / master 库留待步骤 3。
- 安全响应头：CSP、`X-Frame-Options`、`X-Content-Type-Options`、HSTS（非开发环境）。

### 4.8 Quartz / 健康检查 / 版本（2026-10-27 修订：接真实实例）

- Quartz：主路径用 **MySQL AdoJobStore**，Quartz 表放 master 库（`db/master/quartz_tables.sql`，取自 Quartz 官方 `tables_mysql_innodb.sql`），`SchedulerName = "pzd"` + 集群配置；示例任务 `HeartbeatJob`（`[DisallowConcurrentExecution]`，`Asia/Shanghai`），注册后可在库中查到，重启后仍在（持久化证据）。实例或凭据不可用时降级为内存存储并打印 Warning（CI 兜底）。
- `/health`：liveness 永远 200；readiness 真实检查 master（MySQL）/ Redis：实例可用时 Healthy + 每项 Up；缺失资源标记 Degraded 而非 Unhealthy（保留无实例机器可开发）。
- `/api/version`：返回 `version`（MinVer，来自 git tag）、`buildId`（git sha）、`buildTime`；**不硬编码具体版本值（F6）**——tag 提交上为 `0.1.0`，tag 之后的提交为 `0.1.1-alpha.0.<height>+<sha>`（HEAD `1bdc680` 实测 `0.1.1-alpha.0.1`）。
- 前端版本检测（version.json / buildId / 刷新提示）属步骤 4，本轮不做。

### 4.9 前端骨架

- 用架构指定的 shadcn preset 初始化并按 §2 扁平化为 `apps/web` + `packages/ui|api|realtime`；`apps/web` 仅保留路由骨架与空白页，`ssr: false`，不含登录页与布局。
- 根 `package.json` 脚本：`build`、`dev`、`lint`、`typecheck`；`turbo.json` 任务依赖 `api#generate → web#build`（本轮 `api#generate` 为占位，读取 `openapi.json` 时若缺失则输出提示并跳过，不阻塞 `web#build`）。
- 若 shadcn preset 拉取失败（网络/沙箱），退化为**最小 React Router v7 SPA + 空 `packages/*`**，并在本规格「Further Notes」记录偏差待下一轮补齐（假设 A3）。

### 4.10 测试运行器与执行方式（2026-10-27 实测修订）

实施中发现原定的 xUnit v3 + `Microsoft.NET.Test.Sdk`（VSTest）组合在 .NET 10 SDK 上不兼容（VSTest 目标被禁用），按规格 §9 风险④走替代方案，验收标准不变：

- 测试包改为 **`xunit.v3.mtp-v2`**（Microsoft.Testing.Platform，MTP），`global.json` 增加 `"test": { "runner": "Microsoft.Testing.Platform" }`；测试工程统一在 `tests/Directory.Build.props` 配置（`IsTestProject`、`OutputType=Exe`、`TestingPlatformDotnetTestSupport`），不再引用 `Microsoft.NET.Test.Sdk`。
- 当前工作区 `E:\pzd` 被 DSH 打了 Low Mandatory Level 完整性标签，MTP 的 `dotnet test` 走命名管道时子进程连不上（handshake failure，非测试失败）。因此：
  - **工作区内**统一用 `dotnet run --project tests/<工程>/<工程>.csproj`（xunit 进程内 runner，同一批测试、同一退出码与汇总输出）；
  - **普通目录 / CI** 用 `dotnet test Pzd.slnx`（已在 `E:\pzd-copy` 验证 39 passed、退出码 0，等价）。
- 版本号查询必须带 Build target：`dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version`；不加 `-t:Build` 只会拿到 MSBuild 求值默认值 `1.0.0`（MinVer 在 target 内计算）。
- 以上三条属于票 01 的环境适配，已回填票 01 / 04 及全部票的验证命令；**不属于验收降低**。

### 4.11 允许的环境适配产物

票 01/04 允许且已在交付中出现的额外文件（审计不应判为超范围）：

- `NuGet.config`：仓库级源映射。CPM 遇到机器上多个 NuGet 源会报 NU1507，显式 `<clear />` + `packageSourceMapping` 是 CPM 的必需配置，同时保证 restore 可复现。
- `tests/Directory.Build.props`：统一测试工程配置（见 4.10），避免 5 个 csproj 复制粘贴。
- `tests/Pzd.ArchitectureTests` 对 `SqlSugarCore` 的真实引用，以及显式提版 `Pkcs 10.0.14`：反向用例必须引用真实 SqlSugar 类型；Pkcs 是其漏洞版传递依赖，不提版过不了 `-warnaserror`。

### 4.12 本机实例与凭据（2026-10-27 新增）

- **实例事实**：MySQL80 `8.0.46` @ `127.0.0.1:3306`（root 需密码，空密码不可用）；Redis 为 Memurai @ `127.0.0.1:6379`（无密码，`PING` → `PONG`）。Docker 不存在，容器不是路径。
- **凭据来源**（唯一合法来源，按优先级）：
  1. 进程环境变量：`ConnectionStrings__mysql`、`ConnectionStrings__redis`（.NET 配置双下划线约定）；
  2. 仓库根 `local.env`（由 ops 写入，`.gitignore` 必须包含 `local.env`，仓库内**不放**任何真实密码）；
  3. `dotnet user-secrets`（开发机可选）。
- **红线**：密码 / 连接串秘密**不得**出现在仓库文件、`appsettings*.json`、代码常量、日志、测试报告、ProblemDetails、群消息（Chief 已明确）。规格与票只写变量名。
- **集成测试凭据**：`tests/Pzd.IntegrationTests` 读取同一组环境变量（允许 `PZD_TEST_MYSQL` 覆盖）；无凭据 / 连不上 → 动态 Skip 并给出 `MySQL credentials not configured` 或 `MySQL unreachable` 原因，退出码 0。
- **建库权限探测**：fixture 先执行 `SELECT VERSION()` 与 `CREATE DATABASE` 探测；有权限 → 自动建/删 `pzd_it_a`、`pzd_it_b`；无 `CREATE/DROP DATABASE` 权限 → 使用 `PZD_TEST_MYSQL_DB_A/B` 指定的既有库（只读写该前缀库，禁止触碰 `pzd_master` 等非测试库）；两者都没有 → Skip 并说明缺权限。
- **推送安全**：仓库推送到 `github.com/pangzideng926/pzd` 前，确认 `.gitignore` 含 `local.env`、`git grep` 无密码残留、测试报告无凭据（票 11 验收项）。

## 5. 验收标准（本轮 Definition of Done）

- [ ] **AC1** `pwsh -NoProfile -File scripts/verify.ps1` 在本机（MySQL 8.0.46 / Redis Memurai 可用）退出码 0，并打印 4 段结果：build / architecture tests / unit tests / integration tests（连真实 MySQL 运行；凭据缺失时 Skip 也视为通过）
- [ ] **AC2** `dotnet build Pzd.slnx -warnaserror` 通过（含 `TreatWarningsAsErrors`），MinVer 版本写入程序集与 `/api/version`；**版本以 git tag 为基准**（tag 提交 `0.1.0`，之后为 `0.1.1-alpha.0.<height>`），不在票/测试里硬编码具体版本值（F6）
- [ ] **AC3** 架构测试通过；证据口径 = **规则③反向用例**（故意构造的 Domain→SqlSugar 违规类型必须让规则失败）+ **规则④单元级反例**（证明允许集非全放行）；① ② 是对真实程序集的直接断言，不声称「四条都有反向用例」（交接记录「票面措辞」项，已于 `8e1a1a3` 修正）
- [ ] **AC4** 多租户单元测试通过：claim `tid` 优先、匿名 `X-Tenant`/子域、停用 403 `Tenant.Disabled`、过期 403 `Tenant.Expired`、请求间上下文隔离、无上下文取连接报明确错误；显式重载对停用/过期租户拒绝取连接（F-05-8）；`/health/*`、`/api/version` 无租户返回 200（F-05-4）
- [ ] **AC5** 异常处理单元测试通过：§8.3 全部状态码映射逐条断言；未知异常 500 不含堆栈且含 `traceId`；租户 400/403 亦带 `traceId` / `X-Trace-Id`；框架裸 401/403 映射为 `Auth.Unauthorized` / `Auth.Forbidden`（不落 `System.Unexpected`）；`errors` 键 camelCase（PascalCase 入参断言 `userName`）
- [ ] **AC6** `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（普通目录等价 `dotnet test`）在**本机 MySQL 可用**时真跑：自动建/清 `pzd_it_a`、`pzd_it_b`，A/B 两库数据互不可见、QueryFilter 生效、停用租户 403 `Tenant.Disabled`；无凭据 / 实例不可达时动态 Skip 且 exit 0（CI 兜底）
- [ ] **AC7** `dotnet run --project src/Pzd.AppHost` 一条命令启动 API + 前端并连本机 MySQL/Redis，`/health/ready` 为 Healthy（明细 mysql/redis Up），`/api/version` 可访问，Ctrl+C 优雅退出；实例缺失时降级 200 Degraded（兜底路径，非主验收）
- [ ] **AC8** `pnpm install` + `pnpm -w build` 通过（空壳 SPA，无登录页）
- [ ] **AC9** Serilog 请求摘要、OTel 注册、审计日志通道、Antiforgery、Data Protection、Quartz **AdoJobStore 落库与重启持久化**、`/health/ready` 真实 Up、`/api/version` 均有对应单测或启动验证（写入票内验证命令与证据）
- [ ] **AC10** `CLAUDE.md` 落地：命名、分层、错误码、权限码、测试、Conventional Commits 约定
- [ ] **AC11** 本轮无业务模块、无登录/会话/权限实现（范围守卫，审计确认）
- [x] **AC12** `Error.Metadata` 为只读快照（F8）：构造后修改原字典不影响 `Error`，强转写入抛异常；票 12 用例通过（`1463e9e`，审计 [`review-12.md`](../reports/review-12.md)）

## 6. 假设（需确认）

| # | 假设 | 影响 | 若否定的替代方案 |
|---|---|---|---|
| A1 | 本机 MySQL 8.0.46 / Redis Memurai 作为**主路径**被 Aspire 引用（非容器）；实例或凭据缺失时降级启动、集成测试 Skip，仅作 CI/新机器兜底 | §18 步骤 1「一条命令启动全部服务」= API + 前端 + 真实 MySQL/Redis | 若必须容器化，需先装 Docker，票 03 恢复容器分支，本轮阻塞 |
| A2 | 集成测试**主路径连本机 MySQL 真跑**；动态 Skip 仅保留为凭据/实例缺失时的兜底；建库权限不足时使用 `PZD_TEST_MYSQL_DB_A/B` 指定的既有库 | 验收能拿到真实跨库隔离证据 | 只靠 Skip 则 AC6 退化为「优雅跳过」，与本次「先试试」意图不符 |
| A3 | shadcn preset 初始化按架构命令执行；若网络受限则退化为最小 React Router SPA 并记录偏差 | 前端骨架完整度 | 阻塞等网络，或本轮不做前端（与步骤 1 不符） |
| A4 | `git init` + 初始提交 + `v0.1.0` tag 属于本轮工作 | MinVer 版本可预测 | 不建 tag 时 MinVer 产出 `0.0.0-alpha.0.N`，可用但不可读 |
| A5 | Aspire / xUnit v3 模板未安装，允许通过 `dotnet new install` 或手写 csproj + 包引用实现 | 实施方式 | 只用现有 MSTest 模板（与架构 §16 冲突，不采纳） |
| A6 | 本轮只新增 `db/master/quartz_tables.sql`（Quartz 官方建表脚本，AdoJobStore 必需）；业务表与 `schema_versions` 迁移执行器仍留步骤 3 | Quartz 持久化可验证 | 连 Quartz 表也推迟则票 09 只能内存存储，达不到真实持久化证据 |
| A7 | 前端错误拦截 mutator、`/api/me`、登录页、权限菜单属步骤 3–4 | 范围边界 | 本轮做则范围翻倍 |
| A8 | 凭据只经环境变量 / `local.env`（gitignored）/ user-secrets 注入，仓库、日志、测试报告零秘密 | 安全红线 | 若允许写进 appsettings 则违反 Chief 要求，不采纳 |

## 7. Testing Decisions

- **好测试的定义**：只测外部可观察行为（HTTP 状态码/响应体、DI 解析结果、跳过原因），不测私有方法与内部调用次数；每个测试自带 setup，不共享可变状态。
- **架构测试**（`tests/Pzd.ArchitectureTests`）：NetArchTest 断言 ① SharedKernel 不引用 Infrastructure / Api；② Infrastructure 不引用 Api；③ `*.Domain` 命名空间不引用 `Pzd.Infrastructure`、`Pzd.Api`、`SqlSugar`、`Microsoft.AspNetCore`（F7）；④ 模块间只能引用对方 `.Contracts`，允许集必须包含「其他模块的 `*.Contracts`」（F5），模块为空时允许空集。四条规则抽成共享断言方法，生产测试与反向用例跑同一方法并断言恰好 1 条违规（F1）；模块发现读 `src/Pzd.*/*.csproj` 的 `ProjectReference`，不扫输出目录 DLL（F2）。附 1 条反向用例（测试程序集内构造违规类型，断言规则报出违规）。规则③的扫描范围自票 05 起扩展为「底座程序集 + `DiscoverModules()` 结果」，模块 Domain 自动纳入（交接记录「规则③检查范围」项）；`*.Contracts` 工程自身暂不受规则④约束，步骤 3 补「Contracts 只能引用 SharedKernel / 其他 Contracts」（交接记录「Contracts 约束」项）。F7 的 `Pzd.Infrastructure` / `Pzd.Api` 两条禁依赖断言当前不可证伪，补测条件见票 05 / 06（`FakeInfrastructureDependency` 恰好 2 条违规、`FakeApiDependency` 恰好 3 条）（交接记录「F7 补测条件」项）。
  - 风险：NetArchTest.Rules 对 net10.0 的兼容性；若不兼容改用 `TngTech.ArchUnitNET`，验收标准不变。
- **单元测试**：`Pzd.SharedKernel.UnitTests`（Result/Error/值对象/聚合事件收集）、`Pzd.Infrastructure.UnitTests`（租户上下文、解析策略、TenantDbProvider 连接选择与缓存、UoW 事件分发与 Outbox 写入、审计通道）、`Pzd.Api.UnitTests`（WebApplicationFactory：中间件顺序、ProblemDetails 映射全表、Antiforgery、`/health`、`/api/version`）。
- **集成测试**：`Pzd.IntegrationTests` 连**本机 MySQL**（Testcontainers 容器方式从主路径移除）；fixture 探测 `SELECT VERSION()` 与 `CREATE DATABASE` 权限，按 §4.12 决定「自动建库 / 用既有库前缀 / 动态 Skip」；真跑时验证 A/B 两库互不可见、QueryFilter 生效、停用租户 403。无凭据或实例不可达时 Skip 且退出码 0；**本机实例可用时必须真跑**，AC1 的集成测试段允许 Skip 仅限兜底场景。
- **无 Mock 数据库**：不用 EF InMemory / SQLite 冒充 MySQL；租户隔离的真实性只由本机 MySQL 实例用例证明，其余用端口替身证明逻辑。
- **验证命令**（每票内重复，便于直接执行；工作区内按 4.10 用 `dotnet run --project`）：
  - `dotnet build Pzd.slnx -warnaserror`
  - `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version`（期望值以 git tag 为基准，不硬编码：tag 提交 `0.1.0`，之后 `0.1.1-alpha.0.<height>`）
  - `dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj`
  - `dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（本机 MySQL 可用：真跑；无凭据：Skipped，退出码 0）
  - `pnpm install --frozen-lockfile`、`pnpm -w build`
  - `pwsh -NoProfile -File scripts/verify.ps1`（内部按 4.10 选择运行器）

## 8. Out of Scope

- 登录 / 登出 / 会话 / 强制下线 / Cookie 认证实现、`/api/me`、权限码与授权策略、菜单
- Platform / Identity / Organization / System / Workflow 任何模块与 Contracts
- 前端登录页、侧边栏 + 多页签布局、路由守卫、`<Can>`、orval 生成的真实客户端、SignalR 客户端
- 容器化编排（Docker / Testcontainers 作为主路径；本机实例已覆盖本轮需求，未来需要时另开票）、Outbox 落库、审计日志落库、Data Protection 接 Redis
- `db/tenant` 业务表 SQL 与迁移执行器（本轮只允许 `db/master/quartz_tables.sql`，见 A6）
- CI 流水线（GitHub Actions / GitLab CI）、Docker 镜像、E2E / Playwright、性能测试
- 对象存储、邮件 / 短信 / 企业微信通知、Excel 导入导出、编号规则

## 9. Further Notes

- 范围守卫：本轮任何票都不应修改 `docs/architecture.md`；架构文档是已确认输入，发现矛盾走本规格的假设表升级，而不是静默改架构。
- 风险清单：① NetArchTest 与 net10 兼容性；② Aspire 13 SDK + AppHost 引用本机实例的组合；③ shadcn preset 为在线交互命令；④ xUnit v3 与 `Microsoft.NET.Test.Sdk` 组合（已发生，见 4.10）；⑤ MinVer 在无 tag / shallow clone 时的版本；⑥ 本机 MySQL root 密码与建库权限未确认（票 10 需探测，见 §4.12）。实施中遇到风险按「票内验证命令失败 → 最小替代方案 → 记录到本文件 Further Notes」处理，不得静默降低验收标准。
- **实测记录（2026-10-27，票 01/04 交付时回填）**：
  - ④ 已发生：xUnit v3 + VSTest 在 .NET 10 SDK 不兼容 → 改用 `xunit.v3.mtp-v2` + `global.json` 的 `test.runner=Microsoft.Testing.Platform`（见 4.10）。验收标准未变，测试数量 39（SharedKernel 29 + Architecture 7 + Infrastructure/Api/Integration 各 1）全绿。
  - 新增环境限制：工作区 `E:\pzd` 为 Low Mandatory Level，MTP `dotnet test` 的命名管道握手失败；工作区内改用 `dotnet run --project tests/<工程>` 执行（同一批测试、退出码等价），`dotnet test` 在普通目录 `E:\pzd-copy` 验证通过（39 passed）。此限制不影响代码与验收结论。
  - ⑤ 已处理：`git init` + 初始提交 + `v0.1.0` tag 落地（假设 A4 成立），`Pzd.Api` ProductVersion = `0.1.0+50ac4ec`。
  - 架构反向用例已实测：测试程序集构造 `Pzd.SharedKernel.Domain.Fake` 引用 SqlSugar，4 条规则断言到 1 处违规，规则非空跑（AC3 满足）。
  - CPM 与机器多 NuGet 源冲突 → 新增仓库级 `NuGet.config`（源映射），详见 4.11。
- **口径修订（2026-10-27 第二轮，本机实例可用后）**：
  - 本机实例探测：MySQL80 `8.0.46` @ 3306（root 需密码）、Memurai @ 6379（无密码，PONG）；Docker 仍不存在。票 03（Aspire 引用本机实例）、票 09（AdoJobStore 真实落库 + readiness 真实 Up）、票 10（连本机 MySQL 真跑隔离）按此升格主路径，容器编排退出本轮（§4.6 / §4.8 / §4.12、A1/A2）。
  - **F6 版本口径**：HEAD `1bdc680` 实测 `0.1.1-alpha.0.1+1bdc680`；票 01 / 票 09 的版本期望改为**以 git tag 为基准**，不硬编码 `0.1.0`（tag 提交上才是 `0.1.0`）。
  - **F7 归属决定**：`Domain → Infrastructure` 规则并入票 04 的规则 ③ 禁止列表（不单开票、不延后到步骤 3）；旺总本轮返工范围 = F1 / F2 / F5 / F4 + F7。
  - 凭据红线见 §4.12；仓库推送到 `github.com/pangzideng926/pzd` 前按 §4.12 做推送安全检查。
  - `docs/reports/` 已随 `2c2f58c` 入库（测试/审计/交接报告）；后续报告提交同样不得含密码或连接串秘密。
- **收口记录（2026-10-27，票 01/04 审计通过后）**：
  - 票 01 / 票 04 审计结论均为**通过**（[`review-01-04-final.md`](../reports/review-01-04-final.md)），票面状态与顶部进度已回填；返工范围 F1/F2/F5/F4/F7 全部经变异复核。
  - **F8 落票**：新增票 12「`Error.Metadata` 只读快照」（`Error.cs:33`），要求构造时复制 `ReadOnlyDictionary`（Ordinal）并补「构造后改原字典不影响 Error」用例；`EmptyMetadata` 不可强转改写。
  - **F7 补测条件**：`*.Domain` 禁依赖 `Pzd.Infrastructure` / `Pzd.Api` 当前不可证伪（无公共类型可引用），已写入票 05（`FakeInfrastructureDependency`，恰好 2 条违规）与票 06（`FakeApiDependency`，恰好 3 条违规）。
  - **规则③范围（交接记录「规则③检查范围」项）**：票 05 起改为「底座程序集 + `DiscoverModules()`」，模块 Domain 自动纳入；**`*.Contracts` 自身约束（交接记录「Contracts 约束」项）**本轮无 Contracts 工程，留步骤 3 首次出现模块时补。
  - **票面措辞（交接记录「票面措辞」项）**：tickets.md 原「四条规则都有反向用例」已改为「规则③反向用例 + 规则④单元级反例」，① ② 明确为对真实程序集的直接断言。
- **票 12 收尾（2026-10-27）**：
  - 票 12（F8）审计**通过**（[`review-12.md`](../reports/review-12.md)，实现 `1463e9e`）；基线 51 条测试全绿（35 + 13 + 1 + 1 + 1），四个变异各自独立变红，F8 关闭。
  - 遗留 low（不阻塞）：`Error.Metadata` 只做**结构快照**，值为可变对象时仍会跟随变化——`Metadata` 的 XML 注释补「仅结构快照，值请使用不可变类型」（旺总随票 05 补）；可选补 `Clear()` / `ICollection.Add` 两路守卫。
  - 规则③的模块程序集范围（交接记录「规则③检查范围」项）确认**由票 05 承接**：`ProductAssemblies` 改为「底座程序集 + `DiscoverModules()`」，并在票 05 补 `FakeInfrastructureDependency` 反例（恰好 2 条违规）；票 06 补 `FakeApiDependency`（恰好 3 条）。
- **票 05 实现偏差（2026-10-27，`057f64a`）**：
  - `SqlSugarScope.AddConnection` 在 async 上下文里**上下文私有**（加完回到调用方即丢，`Task.Yield()` 可复现），原设计「单例 scope + `AddConnection(tid)` 只加一次 / `GetConnectionScope(tid)`」不可实现。改为**每租户懒建独立 `SqlSugarScope`**（构造期配置跨上下文可见）+ 双检缓存：只建一次、**连接创建只查一次 store（显式重载按次校验查库）**、不 `ChangeDatabase`、不跨租户；`ITenantDbProvider` 为 async API。票 05 票面与 §4.3 已回填。
  - 票 05 新增文件：`TenantConnectionCache.cs`、`TenantConfiguration.cs`、`SystemClock.cs`、`src/Pzd.Api/appsettings.json`（无真实密码）；Infrastructure 加 `FrameworkReference Microsoft.AspNetCore.App`、`SqlSugarCore`、`InternalsVisibleTo`；测试包加 `Microsoft.AspNetCore.Mvc.Testing`。均属实现必需，审计不应判超范围。
  - 本轮**所有端点都要求租户**；登录类端点（`AllowAnonymousTenant`）与匿名租户解析的完整策略留步骤 3。
  - 票 05 交付时全仓 90 条全绿（35 + 15 + 30 + 9 + 1）；返工后 **`e593225` 终审通过**，116 条全绿（35 + 16 + 54 + 10 + 1），见 [`review-05.md`](../reports/review-05.md)。票面状态、测试数与「连接创建只查一次；显式重载按次校验查库」措辞已回填。
- **票 05 终审与跨票前置（2026-10-27，`e593225`）**：
  - **F-05-4**（`/health`、`/api/version` 免租户）→ 已写入 §4.3、票 03（开工硬前置）与票 09；实现为 `AllowAnonymousTenant` metadata，验收要求无租户访问三端点均 200。
  - **F-05-8**（后台任务取连接）→ 已写入票 07 验收：必须用带状态校验的显式重载 `GetConnectionScopeAsync(tenantId)`，禁止无参裸取；停用/过期租户消息拒绝处理并记录理由。
  - 票 05 口径：`Tenants` 为空允许启动但请求 400（不做启动期 fail-fast）；`ExpiresAt = null` 视为永不过期，空字符串为配置错误、启动失败。
- **票 06 预审与口径决策（2026-10-27，`dee779d`）**：
  - 基线 132 条全绿（35 + 54 + 26 + 16 + 1）；映射主路径、`type` kebab、404/405、生产不泄漏、F7「恰好 3 条」成立，判需修改的点在下面三条。
  - **F-06-2 traceId（跨票 05）** → 纳入票 06 返工：租户 400/403 补 `traceId` + 响应头 `X-Trace-Id`，kebab 转换去重为一份实现；**票 05 不重开**，票面加跨票补丁说明。
  - **F-06-1 errors camelCase** → 仍要求实现：出口统一转 camelCase，用例必须以 PascalCase 键入参并断言 `userName`（防止再出现「用例自己喂期望输入」）；本轮无 FluentValidation / Application 层，**管道与校验规则移交步骤 3**（tickets.md「步骤 3 移交清单」第 1 项），交付报告不得写成「管道已验证」。
  - **框架裸 401/403（老登 F-06-3）** → 纳入本轮票 06：status code pages 映射为 `Auth.Unauthorized` / `Auth.Forbidden` ProblemDetails 并带 `traceId`，补用例；避免步骤 3 一上授权就出现 `System.Unexpected`。
  - 编号以终审报告 [`review-06.md`](../reports/review-06.md) 为准：**F-06-1** = errors camelCase、**F-06-2** = 租户 traceId、**F-06-3** = 框架 401/403；F-06-4/5/6 为 low。
- 文档交付：本轮结束时更新本文件状态为「已验收（日期）」并回填 `CLAUDE.md` 路径。
