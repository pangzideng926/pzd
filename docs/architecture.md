# PZD 后台管理系统 — 架构设计

> 状态：已确认（2026-10-27）
> 范围：Web 后台 + 实时通信。PDA / 硬件对接、打印设计器、多语言、代码生成器不在当前范围（见 [§17 范围外](#17-范围外与预留扩展点)）。

---

## 目录

1. [技术选型总览](#1-技术选型总览)
2. [仓库结构](#2-仓库结构)
3. [后端分层与 DDD 约定](#3-后端分层与-ddd-约定)
4. [模块划分](#4-模块划分)
5. [持久化：SqlSugar + MySQL](#5-持久化sqlsugar--mysql)
6. [多租户：独立库](#6-多租户独立库)
7. [认证、授权与安全](#7-认证授权与安全)
8. [异常处理](#8-异常处理)
9. [日志与可观测性](#9-日志与可观测性)
10. [实时通信：SignalR](#10-实时通信signalr)
11. [定时任务：Quartz.NET](#11-定时任务quartznet)
12. [审批流：自研 Workflow 模块](#12-审批流自研-workflow-模块)
13. [通用基础能力](#13-通用基础能力)
14. [前端架构](#14-前端架构)
15. [版本更迭与发布](#15-版本更迭与发布)
16. [测试、AI 协作与工程规范](#16-测试ai-协作与工程规范)
17. [范围外与预留扩展点](#17-范围外与预留扩展点)
18. [实施路线](#18-实施路线)
19. [决策记录（ADR 摘要）](#19-决策记录adr-摘要)

---

## 1. 技术选型总览

| 领域 | 选型 |
|---|---|
| 后端运行时 | .NET 10，ASP.NET Core Minimal API |
| 架构风格 | 模块化单体（Modular Monolith）+ DDD（纯净领域层）+ CQRS-lite |
| 进程内中介 | `Mediator`（martinothamar，源生成器）。MediatR / AutoMapper 已商业化，不使用 |
| 映射 | 手写或 Mapster |
| 校验 | FluentValidation（pipeline behavior） |
| ORM | SqlSugarCore |
| 数据库 | MySQL 8（utf8mb4，时间统一存 UTC） |
| 缓存 | `HybridCache`（内存 + Redis） |
| 多租户 | 独立库：master 平台库 + 每租户一库 |
| 认证 | ASP.NET Core Identity，同源 Cookie |
| 授权 | 权限码 + 数据范围 + 租户套餐 |
| 错误响应 | ProblemDetails（RFC 9457） |
| 日志 | Serilog（结构化）+ OpenTelemetry（traces / metrics），开发用 Aspire Dashboard，生产用 Seq |
| 实时通信 | SignalR（强类型 Hub，Redis backplane 备用） |
| 定时任务 | Quartz.NET（MySQL AdoJobStore，集群模式） |
| 审批流 | 自研 `Pzd.Workflow` 模块 |
| 本地编排 | .NET Aspire（MySQL / Redis / API / Vite） |
| 版本号 | MinVer（来自 git tag） |
| 前端脚手架 | `npx shadcn@latest init --preset b2oDW9T8C --base radix --template react-router --monorepo` |
| 前端框架 | React Router v7 framework mode，`ssr: false`（SPA） |
| 前端状态 / 数据 | TanStack Query（服务端状态）、URL search params（列表筛选状态） |
| 表格 / 表单 | TanStack Table + shadcn data-table；react-hook-form + zod |
| API 契约 | .NET 10 内置 OpenAPI 3.1 → orval 生成 TS 客户端、Query hooks、zod schema |
| 包管理 / 构建 | pnpm workspace + Turborepo |
| 测试 | xUnit v3、Testcontainers（MySQL）、NetArchTest、Vitest、Playwright |

---

## 2. 仓库结构

根目录扁平：.NET 解决方案与 pnpm monorepo 共存于根，不使用 `backend/`、`frontend/`、`BuildingBlocks/`、`Modules/` 这类物理嵌套；分组交给 `.slnx` 的解决方案文件夹。

```
pzd/
├─ Pzd.slnx
├─ global.json                     # 锁定 .NET SDK
├─ Directory.Build.props           # 公共编译设置、MinVer
├─ Directory.Packages.props        # NuGet 中央包管理
├─ package.json                    # 根脚本
├─ pnpm-workspace.yaml
├─ turbo.json
├─ CLAUDE.md                       # AI 协作约定（见 §16）
├─ docs/
│  └─ architecture.md
├─ db/
│  ├─ master/                      # master 库版本化 SQL（含 Quartz 表）
│  └─ tenant/                      # 租户库版本化 SQL
├─ src/
│  ├─ Pzd.SharedKernel/            # Entity、AggregateRoot、ValueObject、DomainEvent、Result、Error、IRepository
│  ├─ Pzd.Infrastructure/          # SqlSugar、多租户、UoW、Outbox、缓存、文件存储、锁、编号、调度、日志 AOP
│  ├─ Pzd.Realtime/                # SignalR Hub 与 IRealtimeNotifier 实现
│  ├─ Pzd.Platform/                # 租户、套餐（master 库）
│  ├─ Pzd.Platform.Contracts/
│  ├─ Pzd.Identity/                # 用户、角色、权限、登录、会话
│  ├─ Pzd.Identity.Contracts/
│  ├─ Pzd.Organization/            # 部门（含主管与层级）、岗位
│  ├─ Pzd.Organization.Contracts/
│  ├─ Pzd.System/                  # 菜单、字典、参数、编号规则、通知、审计、任务管理、回收站
│  ├─ Pzd.System.Contracts/
│  ├─ Pzd.Workflow/                # 流程定义、流程实例、待办
│  ├─ Pzd.Workflow.Contracts/
│  ├─ Pzd.Api/                     # 宿主：端点注册、认证、中间件、静态资源
│  └─ Pzd.AppHost/                 # Aspire 编排
├─ tests/
│  ├─ Pzd.ArchitectureTests/
│  ├─ Pzd.<Module>.UnitTests/
│  └─ Pzd.IntegrationTests/
├─ apps/
│  └─ web/                         # React Router 后台
└─ packages/
   ├─ ui/                          # shadcn 组件
   ├─ api/                         # orval 生成产物
   └─ realtime/                    # SignalR 客户端封装
```

新增业务模块 = 新增 `src/Pzd.Xxx` + `src/Pzd.Xxx.Contracts`，并在 `Pzd.Api` 中 `AddXxxModule()` / `MapXxxEndpoints()`。

---

## 3. 后端分层与 DDD 约定

### 3.1 模块内部结构

每个模块一个项目，内部按文件夹分层，由架构测试强制依赖方向：

```
Pzd.Identity/
├─ Domain/            # 聚合、实体、值对象、领域事件、仓储接口、领域错误
├─ Application/       # Command / Query / Handler / Validator / DTO
├─ Infrastructure/    # 仓储实现、SqlSugar 映射配置、外部服务适配
├─ Endpoints/         # Minimal API 路由组
└─ IdentityModule.cs  # AddIdentityModule / MapIdentityEndpoints
```

### 3.2 依赖规则

```
Pzd.Api ──► Pzd.<Module> ──► Pzd.SharedKernel
                       └──► Pzd.Infrastructure
模块 A ──► 模块 B.Contracts        （只允许引用对方 Contracts）
Domain/ ──✗──► SqlSugar / ASP.NET Core / Infrastructure / 其他模块
Application/ ──✗──► SqlSugar
```

### 3.3 DDD 约定

- **聚合根**守护不变量，状态变更只通过方法（`user.AssignRoles(...)`），属性 `private set`。
- **仓储**接口定义在 `Domain/`，只为聚合根建仓储；按聚合整体加载与保存。
- **值对象**：`Email`、`PhoneNumber`、`PermissionCode` 等，不可变、按值相等。
- **领域事件**：聚合内收集，UoW 提交前分发；跨模块通信转换为**集成事件**经 Outbox 投递。
- **业务失败**返回 `Result` / `Result<T>`，不抛异常（见 §8）。
- **CQRS-lite**：写走聚合 + 仓储；读直接用 SqlSugar `Select` 投影 DTO，不经聚合。
- **分级使用 DDD**：核心业务充血建模；字典、参数这类纯 CRUD 允许简单实现，但仍遵守分层。
- **事务边界**：一个 Command = 一次 UoW 提交；禁止跨库事务（master 与租户库）。

---

## 4. 模块划分

| 模块 | 数据所在 | 职责 |
|---|---|---|
| **Platform** | master | 租户、套餐（套餐 → 可用权限码）、租户开通 / 停用 / 到期、租户库版本看板、平台超管 |
| **Identity** | 租户库 | 用户、角色、权限分配、登录 / 登出、会话、在线用户、强制下线、密码策略 |
| **Organization** | 租户库 | 部门树（含**主管**、**层级**）、岗位、用户主部门；提供 `IOrgQuery` 给审批流与数据权限 |
| **System** | 租户库（菜单/权限定义在 master） | 菜单、字典、参数配置、编号规则、通知模板与站内信、审计 / 登录 / 数据变更日志、定时任务管理、回收站 |
| **Workflow** | 租户库 | 流程定义、流程实例、待办 / 已办 / 抄送（见 §12） |
| **业务模块** | 租户库 | 后续按限界上下文逐个加入 |

---

## 5. 持久化：SqlSugar + MySQL

### 5.1 选型理由

截至 2026-10，Pomelo.EntityFrameworkCore.MySql 最新为 9.0.0，**无 EF Core 10 版本**；Oracle 官方 MySql.EntityFrameworkCore 有 10.x 但能力较弱。SqlSugar 对 MySQL 与多租户支持成熟，因此选用 SqlSugar，并通过以下约定保持领域层纯净。

### 5.2 保持领域纯净的约定

| DDD 需求 | SqlSugar 做法 |
|---|---|
| 领域层零 ORM 特性 | 映射集中在 `Infrastructure/`，用 `ConfigureExternalServices.EntityService` 配置；**Domain 中禁止 `[SugarColumn]`**（架构测试检查） |
| 聚合整体保存 | 无变更跟踪，仓储显式 `InsertNav` / `UpdateNav` 保存聚合根与子实体 |
| 值对象 | `IsOwnsOne` 平铺列；复杂值对象存 JSON 列 |
| 领域事件 | 自研 `UnitOfWork`：提交前收集聚合事件 → 分发 → 写 Outbox → 提交事务 |
| 读模型 | Query Handler 直接 `Queryable().Select(...)` 投影 |

### 5.3 数据约定

- **主键**：雪花 `long`（SqlSugar `SnowFlakeSingle`）；对外暴露时序列化为字符串，避免 JS 精度丢失。
- **审计字段**：`CreatedBy/CreatedAt/UpdatedBy/UpdatedAt` 由 AOP 自动填充。
- **软删除**：`IsDeleted` + `DeletedAt/DeletedBy`，全局 `QueryFilter` 过滤；回收站可恢复。
- **乐观并发**：`Version` 列 + `IsEnableUpdateVersionValidation`，冲突映射为 409。
- **唯一索引**：包含 `TenantId`（如 `(TenantId, UserName)`），配合软删除时需考虑已删除数据。
- **字符集**：`utf8mb4` / `utf8mb4_0900_ai_ci`；时间存 UTC，展示时转 `Asia/Shanghai`。
- **表结构变更**：不使用 CodeFirst 自动比对，统一走 `db/` 下的版本化 SQL（见 §6.4）。

---

## 6. 多租户：独立库

### 6.1 库划分

```
pzd_master（平台库）
  ├─ tenants              租户、编码、状态、到期时间、套餐、加密连接串、库版本
  ├─ tenant_packages      套餐 → 权限码
  ├─ menus / permissions  全局菜单与权限定义
  ├─ platform_users       平台超管
  └─ qrtz_*               Quartz 调度表

pzd_t_{code}（租户库）
  └─ users / roles / depts / 业务表 / audit_logs / outbox / schema_versions ...
```

租户库内表**仍保留 `TenantId` 列与 `QueryFilter`**：纵深防御，并为未来"小租户合并共享库"的混合模式留口。

### 6.2 请求链路

```
启动
 └─ 注册 SqlSugarScope（单例），仅含 ConfigId = "master"

请求
 ├─ UseAuthentication
 ├─ TenantResolutionMiddleware
 │    已登录 → claim "tid"
 │    未登录 → 子域名或 X-Tenant 头（仅对匿名端点：登录、验证码）
 │    → ITenantStore 查 master（HybridCache），校验状态 / 到期
 │    → 失败返回 403 ProblemDetails（Tenant.Disabled / Tenant.Expired）
 │    → ITenantContext.Set(tid)（Scoped）
 ├─ UseAuthorization
 └─ 仓储 → ITenantDbProvider.GetAsync()
           不存在连接 → 加锁双检 → AddConnection + 对该连接配置 AOP / 过滤器
           → GetConnectionScope(tid)
```

要点：

- 启动时**不预加载**全部租户连接，按需加入。
- token / Cookie 中的 `tid` 只在登录后可信；**每次请求都校验租户状态**（token 不会因租户停用而失效）。
- 禁止在全局单例上 `ChangeDatabase`；统一通过 Scoped 的 `ITenantDbProvider` 获取。

### 6.3 运维要点

| 问题 | 方案 |
|---|---|
| 租户开通 | 后台任务分步执行：建库 → 建专属 MySQL 账号（最小权限）→ 执行迁移 → 初始化管理员 / 角色 / 字典 / 默认流程 → 标记可用；每步可重试 |
| 租户变更 | 停用或改连接串后：清 `ITenantStore` 缓存 + `RemoveConnection(id)`；多节点通过 Redis pub/sub 广播 |
| 连接池 | 每租户连接串单独一个池，`Max Pool Size` 调小（如 10），闲置连接及时回收 |
| 连接串安全 | Data Protection / KMS 加密存 master，仅内存解密 |
| 后台任务 / Outbox | 消息携带 `TenantId`，执行前 `ITenantContext.Set` |
| 缓存 | key 统一前缀 `t:{tid}:` |
| 平台超管跨租户 | 后端显式切换上下文，必须写审计日志；前端不能传任意租户 ID |
| 跨库事务 | 禁止；跨 master/租户库的流程拆为可重试步骤，最终一致 |
| 备份恢复 | 每租户库定时备份（mysqldump / XtraBackup），支持单租户恢复 |

### 6.4 租户库迁移

- `db/tenant/` 下的版本化 SQL，由迁移执行器（DbUp 或自研）逐租户执行，记录到各库 `schema_versions`。
- 发布顺序：**先迁移，后部署代码**；脚本必须兼容旧代码（expand-contract，见 §15.3）。
- 支持灰度（先部分租户）；单租户失败只标记该租户，不阻塞其他租户。
- 运行时校验 `schema_version`：未达代码要求的租户返回 `503 Tenant.Upgrading`，前端展示"系统升级中"。

---

## 7. 认证、授权与安全

### 7.1 认证

- ASP.NET Core Identity，**同源 Cookie**：`HttpOnly`、`Secure`、`SameSite=Lax`。
- 开发期 Vite proxy `/api` 与 `/hubs` 到后端，保持同源。
- 认证方案按 scheme 组织；未来 PDA / 第三方调用时增加 Bearer scheme，并用 Policy Scheme 自动选择，业务代码不变。
- 会话存 Redis，支持在线用户列表与强制下线（配合 SignalR `ForceLogout`）。

### 7.2 授权模型

```
用户 ─N:N─ 角色 ─N:N─ 权限码（system:user:create）
                └── 数据范围（全部 / 本部门 / 本部门及下级 / 仅本人 / 自定义）
租户套餐 ── 限定该租户可分配的权限码上限
菜单 ── 绑定权限码（菜单是展示元数据，权限码是授权依据）
```

- 权限码格式：`{模块}:{资源}:{动作}`，如 `system:user:create`。
- 后端：自定义 `IAuthorizationPolicyProvider`，端点声明 `.RequirePermission("...")`。
- 数据范围：基于 Organization 数据的 `QueryFilter` 自动追加条件。
- 权限缓存 key 含租户与用户；角色权限变更时通过领域事件失效。
- `GET /api/me` 返回用户信息、权限码、菜单树。

### 7.3 安全基线

| 项 | 方案 |
|---|---|
| CSRF | ASP.NET Antiforgery，前端读取 Cookie 中的 token 放入 `X-XSRF-TOKEN` 头 |
| Data Protection 密钥 | 持久化到 Redis / master 库，生产加证书保护；否则多节点或重启后 Cookie 全部失效 |
| 登录安全 | 图形验证码、失败锁定、密码强度策略、内置 `RateLimiter` |
| 安全响应头 | CSP、`X-Frame-Options`、HSTS、`X-Content-Type-Options` |
| 敏感数据 | 日志脱敏；必要字段加密存储 |
| 配置与密钥 | `appsettings.{Env}.json` + 环境变量；本地 user-secrets；密钥不进仓库 |
| 幂等 | 关键写操作支持 `Idempotency-Key` |

---

## 8. 异常处理

### 8.1 原则

**预期失败用 Result，意外才抛异常；HTTP 状态码准确；响应体统一 ProblemDetails。**
不使用"永远 200 + `{code,msg,data}`"包装。

### 8.2 分层职责

| 层 | 职责 |
|---|---|
| Domain | 返回 `Result.Failure(Error)`；错误码 `模块.实体.原因`，如 `Identity.User.NameDuplicated`；错误文案集中定义在各模块 `Errors` 类 |
| Application | FluentValidation pipeline，校验失败返回 Validation 类型 Result |
| Endpoints | `result.ToHttpResult()` 按 Error 类型映射 ProblemDetails |
| 全局 | `IExceptionHandler` 链，只处理意外异常 |

### 8.3 状态码映射

| 场景 | 状态码 |
|---|---|
| 参数校验失败（含 `errors` 字典） | 400 |
| 未登录 / 会话过期 | 401 |
| 无权限 / 租户停用 | 403 |
| 资源不存在 | 404 |
| 乐观锁冲突、MySQL 唯一键冲突（1062） | 409 |
| 业务规则不允许 | 422 |
| 客户端版本过低 | 426 |
| 未知异常（生产不返回堆栈） | 500 |
| 租户库升级中 | 503 |

### 8.4 响应格式

```json
{
  "type": "https://pzd/errors/identity.user.name-duplicated",
  "title": "用户名已存在",
  "status": 422,
  "code": "Identity.User.NameDuplicated",
  "traceId": "00-4bf9...",
  "errors": { "userName": ["用户名已存在"] }
}
```

通过 `AddProblemDetails(o => o.CustomizeProblemDetails = ...)` 统一补 `traceId`、`code`。

### 8.5 前端处理

orval 自定义 mutator 统一拦截：

- 401 → 跳转登录页（带 `redirect`）
- 403 → 提示无权限
- 400 / 422 → `errors` 通过 `setError` 映射到表单字段
- 409 → 提示数据已被修改，请刷新
- 426 → 强制刷新对话框
- 503 `Tenant.Upgrading` → 升级中页面
- 500 → 提示并附 traceId

每个路由配置 `ErrorBoundary` 兜住 loader 错误。

---

## 9. 日志与可观测性

### 9.1 两类日志

| | 技术日志 | 业务日志 |
|---|---|---|
| 读者 | 开发 / 运维 | 租户管理员（后台界面） |
| 内容 | 请求、异常、SQL、性能 | 操作审计、登录日志、数据变更 |
| 存储 | Seq（生产）/ Aspire Dashboard（开发） | 租户库 |
| 方案 | Serilog + OpenTelemetry | Endpoint Filter + Channel 异步批量写入 |

### 9.2 技术日志

- 业务代码只依赖 `ILogger<T>`；热点日志用 `[LoggerMessage]` 源生成器。
- OpenTelemetry 采集 traces / metrics，OTLP 导出；规模变大后可换 Grafana Loki + Tempo 或 ELK。
- 中间件通过 `LogContext` 推入 `TenantId`、`UserId`、`TraceId`、`ClientIp`。
- `UseSerilogRequestLogging`：每请求一行摘要。
- SqlSugar AOP：
  - `OnLogExecuting`：Debug 级别完整 SQL
  - `OnLogExecuted`：超过 500ms 记 Warning（慢 SQL）
  - `OnError`：Error，带参数
- 脱敏：destructuring policy 处理 password、token、手机号、身份证号。
- 保留期：例如 30 天。

### 9.3 业务日志

```csharp
group.MapPost("/users", CreateUser)
     .RequirePermission("system:user:create")
     .WithAuditLog("用户管理", "新增用户");
```

- 采集：操作人、租户、模块、动作、请求参数（脱敏）、结果、耗时、IP、UA。
- 写入有界 `Channel`，由 `BackgroundService` 批量落库，不阻塞请求。
- 数据变更：SqlSugar `Aop.OnDiffLogEvent` 记录前后值，存 `data_change_logs`。
- 登录日志单独成表（成功 / 失败 / 锁定）。
- 审计日志按月分表或定期归档。

### 9.4 前端错误上报

`window.onerror`、`unhandledrejection`、ErrorBoundary → `POST /api/client-logs`（限流），后端以 Warning 记录。后续可接 Sentry。

---

## 10. 实时通信：SignalR

### 10.1 架构

```
领域事件 → Outbox → 集成事件处理器
                       └─ IRealtimeNotifier（定义在 Contracts，业务模块不依赖 SignalR）
                            └─ Pzd.Realtime：IHubContext<NotificationHub, INotificationClient>
                                 └─ 分组：
                                      t:{tid}              整个租户
                                      t:{tid}:u:{uid}      用户
                                      t:{tid}:r:{role}     角色
```

### 10.2 原则

- **分组由服务端决定**：`OnConnectedAsync` 按 claim 加入分组；客户端不得指定分组。
- **实时通道只投递不存储**：站内信先落库再推送；重连后通过 REST 拉取未读。
- **推送失效信号而非完整数据**：如 `{ type: "entity-changed", entity: "order", id }`，前端 `invalidateQueries`，权限过滤仍由 REST 负责。
- 强类型 Hub：`Hub<INotificationClient>`。

### 10.3 典型用途

站内信 / 待办提醒、审批待办推送、导入导出进度、强制下线（用户禁用 / 改密 / 租户停用）、新版本发布通知。

### 10.4 部署

- Web 端随 Cookie 认证。
- 多节点：Redis backplane（`AddStackExchangeRedis`）；客户端只用 WebSocket 并跳过 negotiate，无需粘性会话。
- 反向代理配置 `Upgrade` / `Connection` 头，调大 `proxy_read_timeout`。

### 10.5 前端（`packages/realtime`）

- 封装 `@microsoft/signalr`，`withAutomaticReconnect()`，提供 `useHubEvent(name, handler)`。
- 重连成功后统一失效关键查询，补齐断线期间的变化。
- 页头显示连接状态（已连接 / 重连中 / 已断开）。

---

## 11. 定时任务：Quartz.NET

### 11.1 选型

| 方案 | 结论 |
|---|---|
| **Quartz.NET** | ✅ 采用：官方 MySQL AdoJobStore、原生集群、功能完整、Apache 2.0；管理界面自建 |
| Hangfire | MySQL 存储仅社区实现，有稳定性风险；官方 Redis 存储需 Pro |
| TickerQ | 依赖 EF Core，与 SqlSugar 冲突 |
| Coravel | 无持久化、无集群 |
| XXL-JOB 等 | 过重 |
| `BackgroundService` | 仅用于基础设施循环：Outbox 投递、审计批量写、连接回收 |

### 11.2 多租户设计

- **一个调度器**，Quartz 表在 master 库。
- 任务三类：

| 类型 | 例子 | 做法 |
|---|---|---|
| 平台任务 | 租户到期检查、全局清理 | 单次执行，操作 master |
| 租户批量任务（默认） | 各租户日志清理、日报 | 一个任务遍历所有可用租户；限并发；每租户独立 DI scope 并设置租户上下文；单租户失败不影响其他 |
| 租户自定义任务 | 租户自定义日报时间 | 每租户一个 Trigger，`JobKey = {tid}.{job}`，`JobDataMap` 带租户 ID |

- 基类 `TenantFanOutJob` 封装遍历、scope、日志作用域与异常隔离。

### 11.3 注册

```csharp
builder.Services.AddQuartz(q =>
{
    q.SchedulerName = "pzd";
    q.UsePersistentStore(s =>
    {
        s.UseMySqlConnector(o => o.ConnectionString = masterConn);
        s.UseClustering();
        s.UseSystemTextJsonSerializer();
    });
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
```

### 11.4 任务管理（System 模块 + 前端页面）

- 任务列表**只来自代码注册**：`[JobDefinition("清理审计日志", DefaultCron = "0 0 3 * * ?")]`，启动时扫描；**禁止界面填写类名创建任务**。
- 操作：启用 / 停用、修改 Cron、立即执行、查看下次执行时间（`IScheduler` API）。
- 执行记录：`IJobListener` 写 `job_execution_logs`（任务、租户、起止时间、耗时、状态、异常）。
- Cron 编辑：按 Quartz 语法（含秒、`?`）校验，`cronstrue` 中文描述，展示未来 5 次执行时间。
- 长任务进度经 SignalR 推送给触发人。

### 11.5 注意事项

| 项 | 做法 |
|---|---|
| 时区 | Trigger 显式 `InTimeZone(Asia/Shanghai)` |
| Misfire | 默认 `DoNothing`；补偿类任务 `FireOnceNow` |
| 并发 | `[DisallowConcurrentExecution]` |
| 幂等 | 任务可能重复执行，写操作需可重入 |
| 取消 | 传递 `ctx.CancellationToken`，支持优雅停机 |
| 可观测 | `Quartz.OpenTelemetry.Instrumentation` |
| 建表 | `tables_mysql_innodb.sql` 纳入 `db/master/` 迁移 |

### 11.6 部署形态

初期调度器运行在 `Pzd.Api` 进程内（集群模式保证不重复）；任务增多后拆出 `Pzd.Worker` 宿主，任务定义在各模块，换宿主只改注册位置。

---

## 12. 审批流：自研 Workflow 模块

### 12.1 选型

| 方案 | 结论 |
|---|---|
| **自研** | ✅ 采用：审批语义明确，适合 DDD 建模；设计器可用 shadcn 实现；范围可控 |
| Elsa 3 | 通用工作流，无会签 / 或签 / 转交 / 加签语义；设计器基于 Blazor；持久化依赖 EF Core |
| WorkflowCore | 维护不活跃 |
| Camunda / Flowable | Java + BPMN，过重；BPMN 对业务人员不友好 |

### 12.2 领域模型

```
FlowDefinition（聚合根）
  ├─ Code              purchase.order（一个业务单据类型一个流程）
  ├─ Version / Status  草稿 / 已发布；发布后不可改，运行中实例使用其启动时版本
  └─ NodeTree          设计器输出的节点树（JSON）

FlowInstance（聚合根）
  ├─ DefinitionVersion 快照、BusinessKey（单据类型 + ID）、发起人
  ├─ Variables         条件变量（金额、部门、类型……）
  ├─ Status            审批中 / 已通过 / 已驳回 / 已撤回 / 已终止
  ├─ Tasks[]           待办：节点、处理人、状态、操作、意见（在聚合内以保证会签计数一致）
  └─ Records[]         审批轨迹
```

查询端（待办 / 已办 / 我发起的 / 抄送我的）直接查任务表投影。

### 12.3 节点能力

| 节点 | 能力 |
|---|---|
| 审批人 | 方式：依次审批 / 会签 / 或签<br>处理人：指定成员、角色、部门主管（第 N 级）、连续多级主管、发起人自选、单据人员字段<br>处理人为空：自动通过或转管理员<br>相邻同一人：自动通过 |
| 条件分支 | 可视化配置条件；后端用安全表达式求值器（NCalc 或自定义规则结构），**禁止动态执行 C#** |
| 抄送 | 指定成员 / 角色 |
| 办理人 | 只办理不决策（如财务付款） |

### 12.4 操作

- **第一期**：同意、驳回（至发起人）、撤回（下一节点未处理前）、转交、催办
- **第二期**：前 / 后加签、驳回至任意节点、超时提醒与自动处理（Quartz）、委托代理

### 12.5 与业务模块集成

```
业务模块                                 Workflow 模块
order.Submit() → 状态"审批中"
  └─ IWorkflowService.StartAsync   ──►  创建 FlowInstance、生成首批待办
     ("purchase.order", key, 变量)       （经 Workflow.Contracts）
                                          ...审批流转...
order.MarkApproved()  ◄── Outbox ──   FlowApproved / FlowRejected 集成事件
order.MarkRejected()
```

- 业务单据自管状态，Workflow 只负责流转，双方仅依赖 Contracts。
- 处理人解析依赖 `Organization.Contracts.IOrgQuery`（部门主管、层级、用户主部门）——**部门模型必须包含主管与层级**。
- 待办通知：SignalR 推送 + 站内信；后续接企业微信 / 钉钉。
- 权限码：`workflow:definition:design`、`workflow:task:handle` 等。

### 12.6 前端

- 流程设计器：钉钉 / 飞书式自上而下节点树，React + shadcn 自研（卡片节点、节点间"+"插入、条件分支横向展开、右侧抽屉配置）。
- 页面：我的待办、我的已办、我发起的、抄送我的。
- 审批详情：上方业务单据（按流程编码注册展示组件 `approvalViews["purchase.order"] = lazy(...)`），下方时间线，底部操作栏。
- 不做表单设计器 / 低代码表单，流程绑定具体业务单据。

---

## 13. 通用基础能力

| 能力 | 方案 | 所在 |
|---|---|---|
| 文件存储 | `IFileStorage`；S3 兼容（MinIO / 阿里云 OSS / 腾讯云 COS）；路径前缀 `tenant/{tid}/`；预签名 URL 直传直下 | Infrastructure |
| 单据编号 | 后台配置编号规则；租户库号段表 + `SELECT ... FOR UPDATE`，或 Redis `INCR`；保证并发不重号 | Infrastructure + System |
| 分布式锁 | `Medallion.Threading`（Redis / MySQL） | Infrastructure |
| 缓存 | `HybridCache`；key 前缀 `t:{tid}:`；领域事件驱动失效 | Infrastructure |
| Excel 导入导出 | MiniExcel 流式处理；大数据量异步生成 → 对象存储 → SignalR 通知下载 | Infrastructure + 各模块 |
| 通知渠道 | `INotificationChannel`：站内信、邮件（MailKit）、短信（阿里云 / 腾讯云）、企业微信 / 钉钉；模板后台配置 | System |
| 字典 / 参数 | 后端缓存；前端 `useDict("order_status")` 统一渲染 | System |
| 在线用户 / 会话 | Redis 会话；强制下线 | Identity |
| 回收站 | 基于软删除的查看与恢复 | System |
| 健康检查 | master、Redis、对象存储、租户库抽样；`/health` | Api |
| API 文档 | 内置 OpenAPI 3.1 + Scalar；构建时导出 `openapi.json` 供 orval | Api |

---

## 14. 前端架构

### 14.1 Monorepo

由 `npx shadcn@latest init --preset b2oDW9T8C --base radix --template react-router --monorepo` 生成，调整为根目录扁平结构：

| 包 | 内容 |
|---|---|
| `apps/web` | React Router v7 应用（`ssr: false`） |
| `packages/ui` | shadcn 组件与主题（preset `b2oDW9T8C`） |
| `packages/api` | orval 生成的 client、TanStack Query hooks、zod schema；自定义 mutator（错误处理、CSRF 头、版本头） |
| `packages/realtime` | SignalR 客户端封装 |

Turbo 任务依赖：`api#generate`（读取后端 `openapi.json`）→ `web#build`。

### 14.2 路由与布局

```
routes.ts
├─ _auth/            登录、忘记密码（匿名）
└─ _app/             侧边栏 + 多页签布局
   │                 clientLoader：请求 /api/me，未登录 redirect
   ├─ dashboard
   ├─ system/...
   ├─ org/...
   └─ workflow/...
```

- **路由由前端定义**（类型安全、按路由代码分割），由后端返回的权限码过滤菜单与可访问路由；不做后端下发动态路由。
- 按钮级权限：`<Can permission="system:user:delete">` + `usePermission()`。

### 14.3 数据与状态

- 服务端状态：TanStack Query；`clientLoader` 中 `queryClient.ensureQueryData` 预取。
- 列表：TanStack Table + shadcn data-table；**分页 / 排序 / 筛选状态放 URL search params**。
- 表单：react-hook-form + zod（orval 生成），shadcn Field 组件；后端 `errors` 自动映射字段。
- 字典：`useDict` 统一获取与渲染。

### 14.4 多页签

只做页签导航，不做路由级 KeepAlive；页面状态放 URL 与 Query 缓存，切换页签时由缓存恢复。

### 14.5 部署

- 首选：前端 `build/client` 拷入 `Pzd.Api` 的 `wwwroot`，单容器同源部署。
- 备选：nginx 托管静态文件并反代 `/api`、`/hubs`。

---

## 15. 版本更迭与发布

### 15.1 版本号

- 前后端**同一版本号、同时发布**，来自 git tag（如 `v1.4.0`）。
- .NET：MinVer；前端：CI 注入 `APP_VERSION` 与 `GIT_SHA`。
- 版本 = SemVer（给人看）+ `buildId`（git sha，程序比对）。
- `GET /api/version` 返回版本、buildId、构建时间。
- CHANGELOG 由 Conventional Commits 生成，更新说明可显示在刷新提示中。

### 15.2 前端新版本提示刷新

```
构建时生成 version.json { version, buildId, force, notes }
同一 buildId 编译进常量 __BUILD_ID__

检测时机：
  ① 每 5 分钟轮询 /version.json（no-store）
  ② 页面从后台切回（visibilitychange）
  ③ API 响应头 X-App-Build 与本地不一致
  ④ SignalR 推送新版本通知
        └─ buildId 不同：
             普通更新 → Sonner toast「系统已更新 [立即刷新]」，不自动刷新
             强制更新 → 不可关闭的 AlertDialog
多标签页通过 BroadcastChannel 同步提示状态
```

- Vite 插件在 client 环境 `generateBundle` 中输出 `version.json`（React Router 构建同时有 client / ssr 环境，只在 client 输出）。
- **旧 chunk 兜底**：监听 `vite:preloadError`，借助 `sessionStorage` 标记只自动刷新一次；根 `ErrorBoundary` 识别动态导入失败并提示刷新。
- **部署保留最近 2～3 个版本的 `/assets/*`**，旧页面在刷新前仍可用。
- 不使用 Service Worker / PWA。

缓存头：

| 资源 | Cache-Control |
|---|---|
| `index.html` | `no-cache` |
| `version.json` | `no-store` |
| `/assets/*` | `public, max-age=31536000, immutable` |

### 15.3 接口兼容

- 默认只做增量修改：可加字段 / 接口，不删字段、不改含义与类型。
- 破坏性变更走 expand-contract：N 版本新旧并存并切换前端，N+1 版本删除旧的。
- 必须强制升级时：后端配置 `MinClientBuild`，响应头 `X-Min-Client-Build`；过旧客户端返回 426，前端弹强制刷新。
- 暂不引入 API 版本号（Asp.Versioning）；对外开放 API 时再加 `/api/v2`。

### 15.4 发布流水线

```
1. 构建与测试（后端、前端、架构测试）
2. master 库迁移
3. 租户库迁移（灰度 → 全量，失败租户单独标记）
4. 部署新代码（保留旧 assets）
5. SignalR 推送新版本通知
6. 下一版本执行 contract 阶段脚本
```

---

## 16. 测试、AI 协作与工程规范

### 16.1 测试

| 层 | 工具 | 内容 |
|---|---|---|
| 架构测试 | NetArchTest | 分层依赖、模块边界、Domain 禁止引用 SqlSugar |
| 单元测试 | xUnit v3 | 聚合不变量、领域服务、审批引擎流转 |
| 集成测试 | xUnit v3 + Testcontainers（MySQL） | 仓储、多租户隔离、端点 |
| 前端单元 | Vitest | hooks、工具函数 |
| E2E | Playwright | 登录、权限、标杆 CRUD、审批主流程 |

### 16.2 AI 协作（替代代码生成器）

| 投入 | 作用 |
|---|---|
| 标杆模块 | 用户管理做成前后端全链路示范，AI 以其为模板 |
| `CLAUDE.md` | 命名、分层、错误码、权限码、前端页面结构等约定 |
| 架构测试 + lint | NetArchTest、ESLint、TS strict 自动拦截不合规代码 |
| 项目 skill（可选） | 如 `/new-crud`：按标杆模块步骤生成整套代码 |

### 16.3 工程规范

- NuGet 中央包管理；`global.json` 锁 SDK；`TreatWarningsAsErrors`、`Nullable` 开启。
- 提交信息遵循 Conventional Commits。
- Docker 镜像 + CI（GitHub Actions 或 GitLab CI）；环境 dev / staging / prod。

---

## 17. 范围外与预留扩展点

| 范围外 | 预留 |
|---|---|
| PDA / 硬件对接（扫码、打印机） | `packages/api`、`packages/realtime` 与 Web 解耦可复用；认证可加 Bearer scheme；未来 PDA 需先确认 WebView ≥ Chrome 111（Tailwind v4 要求） |
| 打印设计器 | 无 |
| 多语言 | 错误文案集中在各模块 `Errors` 类 |
| 代码生成器 | 由标杆模块 + `CLAUDE.md` + AI 替代 |
| 表单设计器 / 低代码 | 审批流绑定业务单据 |
| 共享库多租户 | 租户库保留 `TenantId` 列与过滤器，可演进为混合模式 |
| API 版本化 | 对外开放时引入 |
| 独立 Worker 宿主 | 任务定义在模块内，可平移 |

---

## 18. 实施路线

| 步骤 | 内容 | 完成标志 |
|---|---|---|
| 1. 工程底座 | 解决方案、中央包管理、shadcn monorepo 初始化（扁平化）、Aspire 编排（MySQL / Redis / API / Vite） | 一条命令启动全部服务 |
| 2. 横切能力 | SharedKernel、多租户连接工厂与中间件、UoW + Outbox、异常处理链、Serilog + OTel、审计日志、Antiforgery、Data Protection、Quartz 基础 | 架构测试通过；租户隔离集成测试通过 |
| 3. Platform + Identity | 创建租户（开通流程）→ 登录 → `/api/me` 返回权限码与菜单 | 新租户可登录 |
| 4. 前端外壳 | 登录页、侧边栏 + 多页签布局、路由守卫、`<Can>`、错误处理 mutator、版本检测、SignalR 连接 | 登录后看到按权限过滤的菜单 |
| 5. 标杆模块 | System/Identity 用户管理全链路 CRUD；编写 `CLAUDE.md` | E2E 覆盖增删改查与权限 |
| 6. Organization + System | 部门（主管、层级）、岗位、菜单、字典、参数、编号规则、通知、任务管理、回收站 | 基础数据可维护 |
| 7. Workflow 第一期 | 流程定义 + 设计器、实例流转、待办页面、与一个示例业务单据集成 | 示例单据走通审批 |
| 8. 业务模块 | 按标杆逐个接入 | — |

---

## 19. 决策记录（ADR 摘要）

| # | 决策 | 理由 |
|---|---|---|
| 1 | 模块化单体 | 后台系统无需微服务；模块边界清晰，未来可拆 |
| 2 | 纯净领域层 + CQRS-lite | 业务复杂度可增长；读写分离避免查询经过聚合 |
| 3 | SqlSugar 而非 EF Core | MySQL 的 EF Core 10 提供程序不成熟；SqlSugar 多租户成熟 |
| 4 | Mediator（源生成器） | MediatR 商业化；零反射 |
| 5 | 独立库多租户 | 数据隔离强、便于单租户备份恢复；保留 `TenantId` 以便演进 |
| 6 | 同源 Cookie 认证 | SPA 安全性优于 localStorage JWT；需配套 CSRF 防护 |
| 7 | ProblemDetails + 准确状态码 | 标准化，前端 / 网关 / 监控可统一处理 |
| 8 | Serilog + OpenTelemetry；业务日志落租户库 | 技术与业务日志读者不同 |
| 9 | SignalR 推送失效信号 | 权限过滤留在 REST；消息轻量 |
| 10 | Quartz.NET | 官方 MySQL 持久化与集群 |
| 11 | 自研审批流 | 审批语义与国内习惯（钉钉式）通用引擎不覆盖 |
| 12 | React Router SPA（`ssr: false`） | 后台在登录后，无需 SSR；静态部署 |
| 13 | 前端定义路由 + 权限码过滤 | 类型安全、代码分割；避免动态路由复杂度 |
| 14 | orval 生成客户端 | 前后端类型贯通 |
| 15 | version.json + 响应头 + SignalR 提示刷新 | 多重检测，配合旧 assets 保留避免白屏 |
| 16 | 不做代码生成器 | AI + 标杆模块 + 约束更灵活 |
| 17 | 根目录扁平结构 | 减少嵌套，.slnx 与 pnpm workspace 共存 |
