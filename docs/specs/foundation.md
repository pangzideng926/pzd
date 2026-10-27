# PZD 工程底座与横切能力 — 规格（§18 步骤 1–2）

> 状态：执行中（2026-10-27）｜假设 A1–A7 待确认｜票 01/04 已交付待测，环境适配见 §4.10 / §9 实测记录
> 上游：[`docs/architecture.md`](../architecture.md)（已确认），本规格只覆盖 §18 步骤 1「工程底座」与步骤 2「横切能力」
> 票：[`docs/specs/tickets.md`](./tickets.md)
> 依据方法论：to-spec / to-tickets（Matt Pocock），测试接缝取最高层

---

## 1. Problem Statement

仓库 `E:\pzd` 目前只有一份已确认的架构文档，没有任何可编译、可测试、可启动的代码。团队要在它上面按 §18 的路线逐步实现一个模块化单体后台系统，但第一步缺少「地基」：

- 没有解决方案、中央包管理、SDK 锁定与版本号来源（MinVer 依赖 git tag，而当前目录还不是 git 仓库）；
- 没有前后端 monorepo 骨架，也没有 Aspire 编排；
- 架构中最关键的横切约束——多租户上下文、统一异常处理（Result → ProblemDetails）、UoW + Outbox、可观测性、安全基线——都还没有任何可执行的证据，后续模块只能靠口头约定；
- 机器上没有 Docker，也没有本地 MySQL / Redis，因此任何以「必须起数据库」为前提的验收都会让整条流水线卡死。

**对使用者的意义**：如果这一步不落地，后续每一个模块（Platform、Identity、Organization…）都会各自发明一套错误处理、租户取值和日志写法，架构文档里的分层规则无法被自动拦截，返工成本随模块数量线性增长。

## 2. Solution

在 `E:\pzd` 建成一个**最小但完整**的工程底座，并用**一条命令**验收：

1. 根目录扁平结构：`Pzd.slnx` + `global.json` + `Directory.Build.props` + `Directory.Packages.props` + pnpm workspace + Turborepo，与架构 §2 一致；
2. 前后端骨架可编译：后端 4 个工程（SharedKernel / Infrastructure / Api / AppHost）+ 5 个测试工程，前端空壳 SPA（无登录页、无业务页面）；
3. 横切能力具备可执行证据：租户上下文与解析中间件、租户连接工厂（按需加连接）、UoW + Outbox、Result→ProblemDetails 映射、全局异常处理链、Serilog + OpenTelemetry、审计日志通道、Antiforgery、Data Protection、Quartz 基础、健康检查与 `/api/version`；
4. 测试分层落地：架构测试（NetArchTest）可自动拦截分层违规、单元测试覆盖租户隔离与异常映射、Testcontainers 集成测试在无 Docker 时**动态 Skip 且不阻塞验收**；
5. 验收命令 `pwsh -NoProfile -File scripts/verify.ps1` 在**无 Docker** 的当前机器上退出码 0。

**不包含**：登录、会话、`/api/me`、权限码、菜单、前端登录外壳与布局（§18 步骤 3–4，下一轮），也不创建任何业务模块。

## 3. User Stories

1. As a 后端开发者, I want 一条命令编译整个解决方案并锁定 SDK 版本, so that 任何人 checkout 后都不会因 SDK 差异编译失败
2. As a 后端开发者, I want 中央包管理集中声明 NuGet 版本, so that 各工程不会各自漂移版本
3. As a 后端开发者, I want 版本号来自 git tag 且构建产物带版本, so that `/api/version` 与发布记录能对上
4. As a 开发者, I want 解决方案内工程归属由 `.slnx` 解决方案文件夹表达, so that 根目录保持扁平且不出现 `src/` 之外的分组嵌套
5. As a 前端开发者, I want `pnpm install` + `pnpm -w build` 一次通过, so that 前后端可以在同一仓库同一命令下构建
6. As an 运维/开发者, I want `dotnet run --project src/Pzd.AppHost` 一条命令拉起 API 与前端 dev server, so that 本地环境不靠手写多窗口启动
7. As a 开发者, I want 在没有 Docker 时 AppHost 仍能启动 API 并明确告知降级状态, so that 新机器不需要先装容器才能推进工作
8. As a 开发者, I want 有 Docker 时 AppHost 自动拉起 MySQL 与 Redis, so that 完整环境只需一条命令
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
24. As a 测试, I want 多租户与异常处理的单元测试不依赖数据库, so that 无 Docker 也能验证核心行为
25. As a 测试, I want 无 Docker 时集成测试明确 Skip 并给出原因, so that 验收流水线不会红
26. As a 测试, I want 有 Docker 时以两个真实租户库验证跨租户隔离, so that 独立库方案得到真实验证
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
4. **Docker 门控接缝**（最低优先）：Testcontainers MySQL 仅验证真实跨库隔离与仓储，无 Docker 时 Skip。

不为测试在生产代码里新增开关；唯一例外是 AppHost 的「外部依赖可选」由配置驱动（见 4.6）。

### 4.3 多租户内核

- `ITenantContext`（放 SharedKernel 抽象）：`TenantId`（**`long`**，与雪花主键一致）、`HasTenant`、`Set(tid)`、`Clear()`；实现 Scoped，禁止静态单例保存租户。`X-Tenant` 头的租户 code → id 解析由 `ITenantStore` 负责（票 05）。
- 解析中间件顺序：`UseAuthentication` → `TenantResolutionMiddleware` → `UseAuthorization`。
  - 已登录：claim `tid` 优先，忽略 `X-Tenant` 头。
  - 匿名：子域名或 `X-Tenant` 头（仅匿名端点可用），二者同时存在时子域名优先。
  - 无法解析且端点要求租户 → 400/403 ProblemDetails（错误码 `Tenant.NotFound`）。
  - `ITenantStore` 校验状态与到期：停用 → 403 `Tenant.Disabled`，过期 → 403 `Tenant.Expired`；每次请求都校验（不缓存「已通过」结论超过请求生命周期）。
- `ITenantDbProvider`：按 `TenantId` 惰性获取连接，加锁双检后 `AddConnection`，并只对该连接配置 AOP / 过滤器；返回 `GetConnectionScope(tid)`；禁止全局 `ChangeDatabase`。租户连接串来自 `ITenantStore`（本轮内存实现 / 配置注入，加密与 KMS 属步骤 3）。
- `master` 连接始终存在（`ConfigId = "master"`）；租户库表仍保留 `TenantId` 与全局 QueryFilter（纵深防御）。

### 4.4 异常处理与 Result

- `Result` / `Result<T>` / `Error`（`Code`、`Type`、`Message`、`Metadata`）与 `ErrorType`（Validation、Unauthorized、Forbidden、NotFound、Conflict、BusinessRule、ClientTooOld、TenantUpgrading、Unexpected）位于 SharedKernel。
- 端点统一 `result.ToHttpResult()`，映射严格按架构 §8.3：400/401/403/404/409/422/426/503；`type` = `https://pzd/errors/{kebab-code}`。
- 全局 `IExceptionHandler` 链只处理意外异常：500、生产不返回堆栈、`traceId` 取 `Activity.Current?.Id`；`AddProblemDetails(o => o.CustomizeProblemDetails = ...)` 统一补 `traceId`、`code`。
- FluentValidation 失败在 Application 层转 Validation 类型 Result，`errors` 字典键与前端字段名一致（camelCase）。
- 前端侧错误拦截（orval mutator）属步骤 4，本轮只留 `packages/api` 目录与 README 占位。

### 4.5 UoW + Outbox + 领域事件

- 一个 Command = 一次 UoW 提交；UoW 在提交前：收集聚合领域事件 → 进程内分发 → 写 Outbox（同事务）→ 提交。
- Outbox 投递由 `BackgroundService` 轮询有界队列/表（本轮先实现内存 Outbox + 可替换 `IOutboxStore` 抽象，真实落库随步骤 3 的租户库 SQL 一起接入）；投递幂等，失败重试并记 Warning/Error。
- 禁止跨库事务（master 与租户库）；跨库流程拆为可重试步骤。
- 消息携带 `TenantId`，消费前 `ITenantContext.Set`。

### 4.6 Aspire 编排与无 Docker 降级

- `Pzd.AppHost` 使用 .NET Aspire AppHost；资源：`Pzd.Api` + Vite dev server（`apps/web`）。
- MySQL / Redis 资源**由配置是否提供连接串决定**：
  - 提供 `ConnectionStrings:mysql` / `redis`（或 Docker 可用）→ 接入外部/容器资源并作为依赖启动；
  - 未提供且无 Docker → 不注册容器资源，API 以内存缓存 / 无 DB 模式启动，日志输出一条 `Degraded mode: MySQL/Redis unavailable` 的 Warning，`/health` 返回 `Degraded`（HTTP 200 + 明细，存活探针仍可用）。
- 无 Docker 不是启动失败条件；这条决定是**假设 A1**，需产品/架构确认。

### 4.7 可观测性与安全横切

- Serilog：控制台 + `UseSerilogRequestLogging`，每请求一行摘要；`LogContext` 推入 `TenantId`、`UserId`、`TraceId`、`ClientIp`。
- OpenTelemetry：traces + metrics + OTLP exporter（端点由配置提供，缺省不导出），ASP.NET Core / HttpClient / 运行时 instrumentation。
- 审计日志：端点 `WithAuditLog(模块, 动作)` metadata → Endpoint Filter 采集 → 有界 `Channel` → `BackgroundService` 批量写；有 DB 时落 `audit_logs`（建表 SQL 随步骤 3），无 DB 时计数并 Warning（本轮不静默丢弃且不影响请求）。
- Antiforgery：非安全方法（POST/PUT/PATCH/DELETE）校验 `X-XSRF-TOKEN`；Cookie 策略 `HttpOnly` + `SameSite=Lax` + `Secure`（开发环境例外）。
- Data Protection：密钥环持久化路径可配置（默认 `%LOCALAPPDATA%\Pzd\dp-keys` 或配置项），生产接 Redis / master 库留待步骤 3。
- 安全响应头：CSP、`X-Frame-Options`、`X-Content-Type-Options`、HSTS（非开发环境）。

### 4.8 Quartz / 健康检查 / 版本

- Quartz：本轮内存存储 + 一个示例任务（如 `HeartbeatJob`），`[DisallowConcurrentExecution]`，`Asia/Shanghai` 时区；MySQL AdoJobStore 配置写好但仅在提供 master 连接串时启用（无 Docker 时走内存，作为 A1 的推论）。
- `/health`：liveness 永远 200（进程存活）；readiness 检查 master / Redis / 对象存储 / 租户库抽样，缺失资源标记 Degraded 而非 Unhealthy（避免无 Docker 机器无法开发）。
- `/api/version`：返回 `version`（MinVer）、`buildId`（git sha，构建注入）、`buildTime`（构建注入）。
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

## 5. 验收标准（本轮 Definition of Done）

- [ ] **AC1** `pwsh -NoProfile -File scripts/verify.ps1` 在无 Docker 的当前机器上退出码 0，并打印 4 段结果：build / architecture tests / unit tests / integration tests(skipped)
- [ ] **AC2** `dotnet build Pzd.slnx -warnaserror` 通过（含 `TreatWarningsAsErrors`），MinVer 版本写入程序集与 `/api/version`
- [ ] **AC3** 架构测试通过，且存在**反向用例**：故意构造的违规类型必须让规则失败（证明规则非空跑）
- [ ] **AC4** 多租户单元测试通过：claim `tid` 优先、匿名 `X-Tenant`/子域、停用 403 `Tenant.Disabled`、过期 403 `Tenant.Expired`、请求间上下文隔离、无上下文取连接报明确错误
- [ ] **AC5** 异常处理单元测试通过：§8.3 全部状态码映射逐条断言；未知异常 500 不含堆栈且含 `traceId`
- [ ] **AC6** `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（普通目录等价 `dotnet test`）无 Docker 时 exit 0 且输出 Skip 原因；提供 Docker 后同一命令跑通两租户隔离用例（不要求本轮机器执行）
- [ ] **AC7** `dotnet run --project src/Pzd.AppHost` 一条命令启动 API（无 Docker 降级并打印 Warning），`/health` 与 `/api/version` 可访问，Ctrl+C 优雅退出
- [ ] **AC8** `pnpm install` + `pnpm -w build` 通过（空壳 SPA，无登录页）
- [ ] **AC9** Serilog 请求摘要、OTel 注册、审计日志通道、Antiforgery、Data Protection、Quartz 示例任务、`/health`、`/api/version` 均有对应单测或启动验证（写入票内验证命令）
- [ ] **AC10** `CLAUDE.md` 落地：命名、分层、错误码、权限码、测试、Conventional Commits 约定
- [ ] **AC11** 本轮无业务模块、无登录/会话/权限实现（范围守卫，审计确认）

## 6. 假设（需确认）

| # | 假设 | 影响 | 若否定的替代方案 |
|---|---|---|---|
| A1 | 无 Docker 时 MySQL / Redis 不启动，API 以「降级模式」运行，`/health` 报 Degraded 而非 Unhealthy | §18 步骤 1「一条命令启动全部服务」降级为「一条命令启动 API + 前端」 | 改为要求先装 Docker，则步骤 1 阻塞到环境就绪；AC7 需改为必须起容器 |
| A2 | 集成测试用动态 Skip（无 Docker）而非 `[Fact(Skip=…)]` 静态跳过 | 无 Docker 机器上测试数不变、状态为 Skipped | 静态 Skip 会让「有无 Docker」写死在代码里，不采纳 |
| A3 | shadcn preset 初始化按架构命令执行；若网络受限则退化为最小 React Router SPA 并记录偏差 | 前端骨架完整度 | 阻塞等网络，或本轮不做前端（与步骤 1 不符） |
| A4 | `git init` + 初始提交 + `v0.1.0` tag 属于本轮工作 | MinVer 版本可预测 | 不建 tag 时 MinVer 产出 `0.0.0-alpha.0.N`，可用但不可读 |
| A5 | Aspire / xUnit v3 模板未安装，允许通过 `dotnet new install` 或手写 csproj + 包引用实现 | 实施方式 | 只用现有 MSTest 模板（与架构 §16 冲突，不采纳） |
| A6 | 本轮不写 `db/master`、`db/tenant` 的版本化 SQL（随步骤 3 首次租户开通一起落地），目录先建 README 占位 | UoW/Outbox 先内存实现 | 本轮补 MySQL 建表 SQL 则会引入无法验证的脚本（无 DB） |
| A7 | 前端错误拦截 mutator、`/api/me`、登录页、权限菜单属步骤 3–4 | 范围边界 | 本轮做则范围翻倍 |

## 7. Testing Decisions

- **好测试的定义**：只测外部可观察行为（HTTP 状态码/响应体、DI 解析结果、跳过原因），不测私有方法与内部调用次数；每个测试自带 setup，不共享可变状态。
- **架构测试**（`tests/Pzd.ArchitectureTests`）：NetArchTest 断言 ① SharedKernel 不引用 Infrastructure / Api；② Infrastructure 不引用 Api；③ Domain 命名空间不引用 SqlSugar / ASP.NET Core；④ 未来模块只能引用对方 `.Contracts`（规则先就位，模块为空时允许空集）。附 1 条反向用例（测试程序集内构造违规类型，断言规则报出违规）。
  - 风险：NetArchTest.Rules 对 net10.0 的兼容性；若不兼容改用 `TngTech.ArchUnitNET`，验收标准不变。
- **单元测试**：`Pzd.SharedKernel.UnitTests`（Result/Error/值对象/聚合事件收集）、`Pzd.Infrastructure.UnitTests`（租户上下文、解析策略、TenantDbProvider 连接选择与缓存、UoW 事件分发与 Outbox 写入、审计通道）、`Pzd.Api.UnitTests`（WebApplicationFactory：中间件顺序、ProblemDetails 映射全表、Antiforgery、`/health`、`/api/version`）。
- **集成测试**：`Pzd.IntegrationTests` 仅 Testcontainers MySQL；`DockerFactAttribute` 在探测不到 Docker 端点时 `Skip = "Docker unavailable: …"`；有 Docker 时建两个租户库验证 A 租户查不到 B 租户数据、QueryFilter 生效、停用租户 403。**不得**把集成测试放进 AC1 的必过集合。
- **无 Mock 数据库**：不用 EF InMemory / SQLite 冒充 MySQL；租户隔离的真实性只由 Docker 门控用例证明，其余用端口替身证明逻辑。
- **验证命令**（每票内重复，便于直接执行；工作区内按 4.10 用 `dotnet run --project`）：
  - `dotnet build Pzd.slnx -warnaserror`
  - `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version`（期望 `0.1.0`）
  - `dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj`
  - `dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj`
  - `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（无 Docker：Skipped，退出码 0）
  - `pnpm install --frozen-lockfile`、`pnpm -w build`
  - `pwsh -NoProfile -File scripts/verify.ps1`（内部按 4.10 选择运行器）

## 8. Out of Scope

- 登录 / 登出 / 会话 / 强制下线 / Cookie 认证实现、`/api/me`、权限码与授权策略、菜单
- Platform / Identity / Organization / System / Workflow 任何模块与 Contracts
- 前端登录页、侧边栏 + 多页签布局、路由守卫、`<Can>`、orval 生成的真实客户端、SignalR 客户端
- 真实 MySQL / Redis 容器编排（无 Docker）、Quartz MySQL AdoJobStore 落库、Outbox 落库、审计日志落库、Data Protection 接 Redis
- `db/master`、`db/tenant` 版本化 SQL 与迁移执行器
- CI 流水线（GitHub Actions / GitLab CI）、Docker 镜像、E2E / Playwright、性能测试
- 对象存储、邮件 / 短信 / 企业微信通知、Excel 导入导出、编号规则

## 9. Further Notes

- 范围守卫：本轮任何票都不应修改 `docs/architecture.md`；架构文档是已确认输入，发现矛盾走本规格的假设表升级，而不是静默改架构。
- 风险清单：① NetArchTest 与 net10 兼容性；② Aspire 13 SDK + AppHost 与 net10 / 无 Docker 组合；③ shadcn preset 为在线交互命令；④ xUnit v3 与 `Microsoft.NET.Test.Sdk` 组合；⑤ MinVer 在无 tag / shallow clone 时的版本。实施中遇到风险按「票内验证命令失败 → 最小替代方案 → 记录到本文件 Further Notes」处理，不得静默降低验收标准。
- **实测记录（2026-10-27，票 01/04 交付时回填）**：
  - ④ 已发生：xUnit v3 + VSTest 在 .NET 10 SDK 不兼容 → 改用 `xunit.v3.mtp-v2` + `global.json` 的 `test.runner=Microsoft.Testing.Platform`（见 4.10）。验收标准未变，测试数量 39（SharedKernel 29 + Architecture 7 + Infrastructure/Api/Integration 各 1）全绿。
  - 新增环境限制：工作区 `E:\pzd` 为 Low Mandatory Level，MTP `dotnet test` 的命名管道握手失败；工作区内改用 `dotnet run --project tests/<工程>` 执行（同一批测试、退出码等价），`dotnet test` 在普通目录 `E:\pzd-copy` 验证通过（39 passed）。此限制不影响代码与验收结论。
  - ⑤ 已处理：`git init` + 初始提交 + `v0.1.0` tag 落地（假设 A4 成立），`Pzd.Api` ProductVersion = `0.1.0+50ac4ec`。
  - 架构反向用例已实测：测试程序集构造 `Pzd.SharedKernel.Domain.Fake` 引用 SqlSugar，4 条规则断言到 1 处违规，规则非空跑（AC3 满足）。
  - CPM 与机器多 NuGet 源冲突 → 新增仓库级 `NuGet.config`（源映射），详见 4.11。
- 文档交付：本轮结束时更新本文件状态为「已验收（日期）」并回填 `CLAUDE.md` 路径。
