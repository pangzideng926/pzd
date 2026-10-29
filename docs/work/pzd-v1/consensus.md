# PZD V1 需求拆分清单（对齐需求阶段）

> 状态：待老何确认后进入规格阶段
> 来源基线：`docs/architecture.md`（786 行，§1–§19，状态「已确认 2026-10-27」），本文件只是它的拆解与排序，不改动架构结论
> 作者：老卫　日期：2026-10-29

## 0. 本文件是什么

读者是接手出规格与票的人（老何），以及按票实现的开发 Bot。

- 本文件回答：V1 要做哪些能力、每个能力覆盖架构书哪一节、谁依赖谁、什么顺序做。
- 本文件不回答：怎么做、接口长什么样、表结构、验收用例。这些是 `spec.md` 与票的内容。
- 架构书已定的选型视为已确认约束，本文件只引用章节号，不重新论证。
- 能力编号 `M<里程碑>.<序号>` 供票回填「需求依据」，一张票可以覆盖一个能力块，也可以拆细，但要写明它覆盖的编号。

## 1. 目标与非目标

**目标**：交付一个可用的多租户 Web 后台底座 —— 平台可开通租户、租户可登录并按权限看到菜单、具备标杆 CRUD 全链路、基础数据可维护、审批流第一期可跑通一张示例单据。

**非目标**（§17，不排票）：PDA / 硬件对接、打印设计器、多语言、代码生成器、表单设计器 / 低代码、共享库多租户、API 版本化、独立 Worker 宿主。审批流第二期能力（前后加签、驳回至任意节点、超时自动处理、委托代理，§12.4）同样不进 V1。

**成功标准**：§18 八个步骤各自的「完成标志」全部达成，即从「一条命令启动全部服务」到「示例单据走通审批」。

## 2. 里程碑与能力清单

里程碑顺序沿用 §18 实施路线，不另行重排。以下每块给出：做什么（边界）→ 依赖 → 出口判据。

### M1 工程底座（§18 步骤 1）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M1.1 | 后端解决方案骨架：`Pzd.slnx` 解决方案文件夹分组、`global.json` 锁 SDK、`Directory.Build.props`（MinVer、`TreatWarningsAsErrors`、`Nullable`）、`Directory.Packages.props` 中央包管理 | §2、§15.1、§16.3 | — |
| M1.2 | 前端 monorepo 骨架：shadcn 初始化（preset `b2oDW9T8C`、radix、react-router 模板、monorepo）后扁平化到根目录；`apps/web` + `packages/{ui,api,realtime}`；Turbo 任务依赖 `api#generate → web#build` | §1、§14.1 | — |
| M1.3 | Aspire 编排：MySQL / Redis / API / Vite 一条命令拉起 | §1、§18 步骤 1 | M1.1、M1.2 |
| M1.4 | 仓库约定落地：Conventional Commits、`db/master` 与 `db/tenant` 目录骨架、CI 骨架 | §2、§16.3 | M1.1 |

出口判据：一条命令启动全部服务。

### M2 横切能力（§18 步骤 2）

M2 是后面所有里程碑的地基，其中的顺序有内部依赖，按编号推进。

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M2.1 | SharedKernel：`Entity` / `AggregateRoot` / `ValueObject` / `DomainEvent` / `Result` / `Error` / `IRepository` | §2、§3.3 | M1.1 |
| M2.2 | 持久化底座：SqlSugarScope 注册、映射集中在 Infrastructure、雪花主键（对外序列化为字符串）、审计字段 AOP、软删除 + 全局 QueryFilter、乐观并发 409、含 `TenantId` 的唯一索引、UTC 时间 | §5.2、§5.3 | M2.1 |
| M2.3 | 多租户解析链路：`ITenantStore`（HybridCache）、`TenantResolutionMiddleware`（已登录取 claim `tid`，匿名走子域名 / `X-Tenant`）、`ITenantContext`、`ITenantDbProvider` 按需加连接（加锁双检 + AOP 配置）、缓存前缀 `t:{tid}:`、禁跨库事务 | §6.1、§6.2、§6.3 | M2.2 |
| M2.4 | UoW + Outbox：一个 Command 一次提交、领域事件提交前分发、跨模块转集成事件经 Outbox 投递、投递幂等 | §3.3、§5.2、§10.1 | M2.2 |
| M2.5 | 异常与错误响应：Domain 返回 `Result`、FluentValidation pipeline、`result.ToHttpResult()`、`IExceptionHandler` 链、ProblemDetails（含 `traceId` / `code`）、§8.3 状态码映射 | §8 | M2.1 |
| M2.6 | 日志与可观测：Serilog + OpenTelemetry、`LogContext` 推 `TenantId`/`UserId`/`TraceId`、请求摘要日志、SqlSugar AOP（慢 SQL / 错误）、脱敏策略 | §9.2 | M2.2 |
| M2.7 | 安全基线：Antiforgery + `X-XSRF-TOKEN`、Data Protection 密钥持久化、安全响应头、`RateLimiter`、关键写操作 `Idempotency-Key` | §7.3 | M1.1 |
| M2.8 | Quartz 基础：master 库 AdoJobStore + 集群 + JSON 序列化、建表 SQL 进 `db/master/`、`[JobDefinition]` 启动扫描、`TenantFanOutJob` 基类（遍历租户、独立 scope、异常隔离）、时区与 Misfire 约定 | §11.1–§11.3、§11.5 | M2.3、M2.4 |
| M2.9 | 架构测试：NetArchTest 校验分层依赖方向、模块不得互相引用实现、Domain 禁 SqlSugar | §3.2、§16.1 | M2.1、M2.2 |
| M2.10 | 通用能力：`IFileStorage`（S3 兼容、`tenant/{tid}/` 前缀、预签名直传）、分布式锁（Medallion）、HybridCache 封装 + 领域事件失效 | §13 | M2.2 |
| M2.11 | Excel 导入导出：MiniExcel 流式；大数据量异步生成 → 对象存储 → SignalR 通知下载 | §13 | M2.10、M2.4 |
| M2.12 | 健康检查 `/health`（master、Redis、对象存储、租户库抽样）与 OpenAPI 3.1 导出（供 orval） | §13 | M2.3 |

出口判据：架构测试通过；租户隔离集成测试通过。

### M3 Platform + Identity（§18 步骤 3）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M3.1 | master 库结构与迁移：`tenants` / `tenant_packages` / `menus` / `permissions` / `platform_users`、版本化 SQL 与迁移执行器、`schema_versions` | §6.1、§6.4 | M2.2 |
| M3.2 | 租户模型与套餐：套餐 → 可用权限码上限，租户状态 / 到期时间 / 加密连接串 / 库版本 | §4 Platform、§6.3 | M3.1 |
| M3.3 | 租户开通流程：后台分步任务（建库 → 建最小权限账号 → 迁移 → 初始化管理员 / 角色 / 字典 / 默认流程 → 标记可用），每步可重试，单租户失败不阻塞其他 | §6.3 | M3.1、M2.8 |
| M3.4 | 租户库迁移：`db/tenant/` 脚本逐租户执行、灰度、运行时 `schema_version` 校验返回 `503 Tenant.Upgrading`、发布先迁移后部署 | §6.4、§15.4 | M3.1 |
| M3.5 | 认证：ASP.NET Core Identity + 同源 Cookie（`HttpOnly`/`Secure`/`SameSite=Lax`）、登录 / 登出、图形验证码、失败锁定、密码强度、会话存 Redis、开发期 Vite 代理保持同源 | §7.1、§7.3 | M2.3、M2.7 |
| M3.6 | 授权模型：用户-角色-权限码、数据范围五档、`RequirePermission` 策略提供者、权限缓存（key 含租户与用户）+ 领域事件失效、租户套餐限额校验 | §7.2 | M3.5 |
| M3.7 | `/api/me`：用户信息 + 权限码 + 菜单树 | §7.2 | M3.6、M6.2 |

出口判据：新租户可登录。**注意** M3.7 依赖菜单定义（M6.2），见 §4 待决问题 1。

### M4 前端外壳（§18 步骤 4）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M4.1 | 认证页与路由骨架：`_auth` 匿名区、`_app` 区 `clientLoader` 请求 `/api/me`、未登录 redirect | §14.2 | M3.5、M1.2 |
| M4.2 | 布局：侧边栏 + 多页签（只做导航不做 KeepAlive，状态靠 URL 与 Query 缓存恢复）、按权限码过滤菜单与可访问路由、页头连接状态 | §14.2、§14.4 | M4.1 |
| M4.3 | 请求层：orval 生成 client / Query hooks / zod schema、自定义 mutator（CSRF 头、版本头、统一错误拦截 401/403/400/422/409/426/503）、路由级 `ErrorBoundary` | §8.5、§14.1、§14.3 | M2.12、M4.1 |
| M4.4 | 权限与字典：`<Can>` + `usePermission()` 按钮级权限、`useDict` 统一渲染 | §7.2、§14.2、§14.3 | M4.2 |
| M4.5 | 实时客户端 `packages/realtime`：`withAutomaticReconnect()`、`useHubEvent`、重连后统一失效关键查询 | §10.5 | M2.4、M4.3 |
| M4.6 | 版本检测：`version.json` 轮询 / `visibilitychange` / `X-App-Build` / SignalR 四路检测、普通与强制刷新两种提示、BroadcastChannel 多标签同步、`vite:preloadError` 兜底、缓存头配置 | §15.2 | M1.2、M4.3 |

出口判据：登录后看到按权限过滤的菜单。

### M5 标杆模块（§18 步骤 5）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M5.1 | Identity 用户管理后端全链路：聚合 + 仓储 + Command/Query + 端点 + 权限码 + 审计日志 + 数据范围 | §3.1、§4 Identity、§9.3 | M2.5、M3.6、M6.5 |
| M5.2 | 用户管理前端：TanStack Table + data-table、筛选 / 排序 / 分页放 URL、react-hook-form + zod、后端 `errors` 映射字段 | §14.3 | M4.4、M5.1 |
| M5.3 | `CLAUDE.md` 工程约定：命名、分层、错误码、权限码、前端页面结构 | §16.2 | M5.1、M5.2 |
| M5.4 | E2E 基线（Playwright）：登录、权限、标杆增删改查 | §16.1 | M5.2 |

出口判据：E2E 覆盖增删改查与权限。标杆模块是后续业务模块的模板，M5.3 必须在此定型。

### M6 Organization + System（§18 步骤 6）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M6.1 | Organization：部门树（含主管、层级）、岗位、用户主部门、对外 `IOrgQuery` | §4 Organization、§12.5 | M2.2 |
| M6.2 | System 菜单与权限定义（master 库）+ 前端菜单渲染与路由过滤 | §4 System、§7.2 | M3.1、M4.2 |
| M6.3 | 字典 / 参数配置：后端缓存、前端 `useDict` 渲染 | §13 | M6.2 |
| M6.4 | 编号规则：后台配置 + 租户库号段表（`SELECT … FOR UPDATE` 或 Redis `INCR`），并发不重号 | §13 | M2.10 |
| M6.5 | 通知：模板配置 + `INotificationChannel`（站内信必需，邮件 / 短信 / 企业微信 / 钉钉按 §4 待决）、站内信先落库后推送 | §13、§10.2 | M2.4、M4.5 |
| M6.6 | 业务日志：审计（Endpoint Filter + Channel 异步批量落库）、登录日志、数据变更日志（DiffLog）、按月分表或归档 | §9.1、§9.3 | M2.6、M2.4 |
| M6.7 | 定时任务管理页：任务列表只来自代码注册、启停 / 改 Cron / 立即执行 / 下次执行时间、执行记录、Cron 校验与中文描述、长任务进度推送 | §11.4 | M2.8、M6.2 |
| M6.8 | 回收站：软删除数据的查看与恢复 | §13 | M2.2、M6.2 |
| M6.9 | 前端错误上报：`window.onerror` / `unhandledrejection` / `ErrorBoundary` → `POST /api/client-logs`（限流） | §9.4 | M4.3 |

出口判据：基础数据可维护。

### M7 Workflow 第一期（§18 步骤 7）

| 编号 | 能力 | 依据 | 依赖 |
|---|---|---|---|
| M7.1 | 流程定义：`FlowDefinition` 聚合（Code / Version / Status / NodeTree JSON）、发布后不可改、运行中实例用启动时版本快照 | §12.2 | M2.5、M6.2 |
| M7.2 | 流程设计器前端：钉钉 / 飞书式节点树、节点间「+」插入、条件分支横向展开、右侧抽屉配置 | §12.6 | M7.1、M4.4 |
| M7.3 | 审批人解析：指定成员 / 角色 / 部门主管（第 N 级）/ 连续多级主管 / 发起人自选 / 单据人员字段；处理人为空（自动通过或转管理员）、相邻同一人自动通过；依次审批 / 会签 / 或签 | §12.3 | M6.1 |
| M7.4 | 条件分支求值：可视化条件 + 安全表达式求值器（NCalc 或自定义规则结构），禁止动态执行 C# | §12.3 | M7.1 |
| M7.5 | 实例流转：`FlowInstance`（Tasks 在聚合内保证会签计数一致、Records 轨迹）、状态机（审批中 / 通过 / 驳回 / 撤回 / 终止）、抄送、办理人节点 | §12.2、§12.3 | M7.3、M7.4 |
| M7.6 | 第一期操作：同意、驳回至发起人、撤回（下一节点未处理前）、转交、催办 | §12.4 | M7.5 |
| M7.7 | 待办 / 已办 / 我发起的 / 抄送我的：查询端直接查任务表投影 + 四个页面；审批详情页（业务单据区按 `approvalViews[code]` 注册、时间线、操作栏） | §12.2、§12.6 | M7.6、M4.4 |
| M7.8 | 业务集成：`IWorkflowService.StartAsync`（经 Contracts）、`FlowApproved` / `FlowRejected` 集成事件经 Outbox 回写业务状态、一张示例单据走通 | §12.5 | M7.6、M2.4 |
| M7.9 | 待办通知：SignalR 推送 + 站内信；权限码 `workflow:definition:design` / `workflow:task:handle` 等 | §12.5 | M7.5、M6.5 |

出口判据：示例单据走通审批。

### M8 业务模块（§18 步骤 8）

按 M5 标杆的模式逐个接入，无固定清单。每接一个业务模块，前提是 M5.3 的 `CLAUDE.md` 约定与 M2.9 架构测试已可复用；首个业务模块接入后按 [开发流程] 的「首个业务闭环复核」回看底座是否要修。

## 3. 依赖关系与并行建议

关键路径（串行不可跳）：**M1 → M2 → M3 → M4 → M5 → M6 → M7 → M8**。

可并行、且有明确切入点的：

- M1.2（前端骨架）与 M1.1 / M2 后端底座互不阻塞，可同时开工。
- M2.10 / M2.11 / M2.12（文件存储、Excel、健康检查、OpenAPI 导出）在 M2.2 完成后即可做，不必等 M3。
- M4.5（实时客户端）与 M4.6（版本检测）只需 M4.3 的请求层，可与 M4.2 / M4.4 并行。
- M6.1（Organization）只依赖 M2.2，可与 M5 标杆并行先行。
- M6.4（编号规则）、M6.8（回收站）、M6.9（前端错误上报）相互独立，可各自成票并行。
- M7 内部 M7.1 / M7.4 与 M7.3 可并行；M7.2 设计器依赖 M7.1 的 NodeTree 结构定型后开工。

硬约束（架构书明确禁止或必须前置）：

- 发布顺序永远「先迁移、后部署」，M3.4 的 expand-contract 在这一条上不留给实现者自由。
- 禁跨库事务，跨 master / 租户库的行为只能拆成可重试步骤（§3.3、§6.3）。
- 三名开发并行时先认领文件范围，只改认领的路径（群规）。
- M5 未定型 `CLAUDE.md` 之前不进 M6 / M7 的大面积页面开发，否则约定会被各人自行发明。

## 4. 待决问题（交老何出规格前确认，不在此猜测）

1. **菜单与权限定义的落地时机**：§4 说菜单 / 权限定义在 master 库，§18 排在第 6 步；但第 3 步 `/api/me` 就要返回菜单树，第 5 步用户管理页也需要菜单项。需要定：最小菜单 + 权限种子是否提前到 M3.1，M6.2 只做完整维护界面。
2. **租户库迁移执行器选型**：DbUp 还是自研（§6.4 二选一，未定）。
3. **条件分支求值器**：NCalc 还是自定义规则结构（§12.3 二选一，未定）。
4. **通知渠道 V1 范围**：站内信必需；邮件 / 短信 / 企业微信 / 钉钉是否 V1 交付，影响 M6.5 票的粒度。
5. **通用能力（§13）的落位**：架构 §18 没有为文件存储、Excel、锁、缓存分配步骤，本文件暂放 M2.10–M2.12 并按首个使用方提前，需确认这个处理方式。
6. **套餐 → 权限码上限的种子数据**由谁维护、在哪个迁移里初始化。
7. **备份与单租户恢复（§6.3）** 是否 V1 交付，还是记为运维手册内容。
8. **CI 平台**：GitHub Actions 还是 GitLab CI（§16.3 二选一）。
9. **M7 的示例业务单据**选哪个（决定 M7.8 的验收物）。

## 5. 建议的 feature 目录与票的落位

按 [存储映射]，票进 `docs/work/<feature-id>/tickets/NN-<short-name>.md`。建议 feature-id 与里程碑一一对应，避免各人自起名字：

| 里程碑 | 建议 feature-id |
|---|---|
| M1 | `bootstrap` |
| M2 | `cross-cutting` |
| M3 | `platform-identity` |
| M4 | `web-shell` |
| M5 | `user-management` |
| M6 | `org-system` |
| M7 | `workflow-p1` |
| M8 | 每个业务模块一个 feature-id |

本文件所在 `pzd-v1` 只放 V1 全局的目标、非目标、成功标准与待决问题，不重复 spec 正文（B7 单一正文）。

## 6. 来源与依据

- `docs/architecture.md` §1–§19（状态：已确认 2026-10-27）
- [存储映射](../../../.ai/规则/存储映射.md)、[四阶段技能](../../../.ai/规则/四阶段技能.md)、[Bot分工](../../../.ai/规则/Bot分工.md)
