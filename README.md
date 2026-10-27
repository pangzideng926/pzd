# PZD

PZD 是一个模块化单体后台系统（.NET 10，后续接入 React 前端）。当前处于第二轮，范围是架构文档 §18 的**步骤 1（工程底座）**与**步骤 2（横切能力）**；登录外壳与业务模块留到后续轮次。

> 文档入口：[架构](docs/architecture.md) · [规格](docs/specs/foundation.md) · [票](docs/specs/tickets.md) · [测试/审计/交接](docs/reports/)

## 当前进度（2026-10-27）

| 票 | 内容 | 状态 |
|---|---|---|
| 01 | 仓库与 .NET 骨架 | ✅ 审计通过 |
| 04 | SharedKernel + 架构测试基线 | ✅ 审计通过（返工后 `49ee09a`） |
| 12 | `Error.Metadata` 只读快照（F8） | ✅ 审计通过（`1463e9e`） |
| 02 | 前端 monorepo 骨架 | ✅ 审计通过（`57c226e` 闭环 F-02-1；报告 `review-02.md`） |
| 03 | Aspire 编排 + 健康检查/版本端点（F-05-4） | ✅ 审计通过（`f778a32`，报告 `review-03.md` §9；A1/A2/A9 全闭环，退出码 `0/1/2` 与票面 `756c1fc` 一致） |
| 05 | 多租户内核 + 单测 | ✅ 审计通过（`e593225`，116 条） |
| 06 | 异常处理 + Result→HTTP | ✅ 审计通过（`abde749`，156 条） |
| 07 | UoW + Outbox + 领域事件 | ✅ 审计通过（`7f699c5`，185 条） |
| 08 | 可观测性 + 审计 + 安全横切 | 🧪 测试通过（`e8f2e2d`，报告 `test-report-08.md`：229 条 = 35+30+86+68+10，0 失败 0 Skip；两条覆盖边界见报告 §4，非缺陷），待老登审计 |
| 09 | Quartz + 健康检查 + 版本 | 🧪 测试通过（锚 `6b67c2d`，报告 `test-report-09.md`：一键脚本 exit 0、37 个 schema 对象、95s 真实驻留两次触发、重启后 job 行不重复且 `next` 推进、无连接串降级内存）。两条观察别误用：`qrtz_execution_history` 建表但 0 行（执行记录只进日志）、`QRTZ_TRIGGERS` 时间列是 **.NET ticks 不是 Unix 毫秒** |
| 10 | 租户隔离集成测试 | ✅ 审计通过（锚 `204f93e`，11/11、Failed 0、Skipped 0；四条变异全打红；报告 `review-10.md`） |
| 11 | CLAUDE.md + 一键验收脚本 | 依赖 02/03/07–10（按退出码判定：0 通过 / 1 票面缺陷 / 2 环境问题）。A10 ✅ `79bbe36`、A12 ✅ `562fd88`、A13 ✅ `16bd0f4`。**A11 代码面已全部落地**：`X-Pzd-Run-Id` 响应头 + 载荷锁死三字段（`beb83f3`）、vite 回同一响应头 + AppHost 给 **vite 资源**透传 + 脚本两腿比对同一个 `$runId`（`564ae6d`；**注意不能用 `server.headers`**——它只管 Vite 自身中间件，`/` 由 RRD 文档中间件应答，实测 `/@vite/client` 带头而 `/` 不带头，故改用 `configureServer` 最前置中间件）、HTTP 管道断言（`3f2e667`）、源码级接线守卫（`0a2c998` → `d674420`：全文恰好一处 `ApiRunId.Apply(` 且必须落在 `/api/version` 端点片段内，变异「挪到 `/health/live`」必红；**三层保护 ≠ 注入态 HTTP 自动化测试**——接线靠源码守卫、写头靠实跑，仍没有自动化能在注入态打一次 `/api/version` 并断言头）、aspire 腿「取不到监听 pid 即判失败」（`728536a`）。**A11 仍未判通过**：只剩 **aspire 全链路实跑**一条；它原先被 A5 挡住，A5 修复已交付，现在等一次带自定义 entry 的 aspire 全链路现场。**A5 已改判为前端代码缺陷（high），不是环境/权限问题**：`react-router` 7.18.4（锁文件实为 7.18.4，`package.json` 写 `^7.8.0`）默认 `entry.server.node.tsx` 里 `setTimeout(() => abort(), 6000)`（:42，`streamTimeout 5_000` + 1000）**先于** `const { pipe, abort } = renderToPipeableStream(...)`（:47），首次文档请求进 SSR 时同步抛错 → `abort` 停在 TDZ，6 秒后定时器抛 `ReferenceError` 把进程带崩（`exit 1`），5199 自始至终无人监听；手动跑 5/5 正常是因为没人那么早请求 `/`。**修复已交付 `bf08bd4` + `4485ca0`**：`apps/web/app/entry.server.tsx` 里 `abort` 先占位（`let abort: (() => void) | undefined`）、定时器**挪到渲染成功之后才挂**（`:123`）、同步抛出在 `:105` 立即打印真错并 `reject`、`:127` 到点仍未就绪则显式 `reject`（超时保护仍在）；锋哥本机 A/B 已复现因果链 `NODE_ENV=production` → `jsxDEV is not a function` → 同步抛出 → TDZ `exit 1`（同环境下自定义 entry 0.16 秒打真错、进程存活），AppHost 侧已钉住 `37bd872`（`web.WithEnvironment("NODE_ENV", "development")`：被注入 `production` 时它就是修复，没注入时是无害的显式声明），进程真实值由 `331fac3` 的 `[pzd-web] entry loaded（NODE_ENV=…）` 锚点打印（`:45`）；**判别必须读钉住之前那一跑**，之后只会显示 `development`。**DCP 是否真给 `web` 注入了 `NODE_ENV=production` 仍待确认，根因按「待定位」记**——注意 `ResourceNotifications` 的快照只反映「资源声明」，那一跑实测 `EnvironmentVariables` 共 0 项，所以「快照里没有」不能当反证；另日志是 **GBK**，用 UTF-8 方式 grep 会假阴性（曾误判成「零输出」），老登已对 `331fac3` 逐行审计：**修复件通过**（定时器在 `abort` 回填后才挂、两个 `| undefined` 提前声明、三处 `clearTimeout`、`pzd-probe` 计数 0），但**代码通过不等于 A11 放行**；超时保护那条实跑是**缩短值**（`streamTimeout` 200ms／限制 1.2s）的变异，只能证明「到点显式中止而非挂死」，**不能写成「已按真实 6.2 秒验证超时保护」**。**不许靠删 `setTimeout`**——那只是拿掉掩盖层，真错还在（修完按老何四条验收：真错可见且脱敏／失败路径不遗留定时器／6 秒 SSR 超时保护仍在／脱敏机械判据；②与③是一对反作用力，「进程不崩」不能当通过信号）。诊断可见性已由 `6b67c2d`（AppHost 订阅资源状态流，打印 `web` 状态与退出码）和 `90ee1b2`（订阅中断不再静默吞异常、建订阅打锚点）补上。A13 的退出清理**超时只 `[warn]`、不 `exit 2`**（那一轮应用结论已出，清理滞后不该把整轮判成环境问题；判决语义若改需老何裁定） |

票 07 已终审通过（`7f699c5`，185 条全绿：SharedKernel 35 + 架构 30 + Infrastructure 86 + Api 33 + 集成 1）：`40f016a`（F-07-1/2/3）、`001a731`（失败原因与重试、反射异常解包、零处理器/零消费者告警）、`0da4a18`（规则第二层）、`0c29633`（F-07-4 规则标识符级 + `Jobs/**`、`*Job.cs`）、`7f699c5`（F-07-5 重试上限只从 `Outbox:MaxAttempts` 读、终态同写 `RejectionReason` 与 `LastError`）。三条口径务必照写：**多处理器部分失败重放是明确遗留（与步骤 3 Outbox 落库同批）**、**事务只有补偿删除，不等于已回滚验证**、**拒绝 ≠ 死信队列（本轮只有终态标记）**。

当前推进：**08、09 都已测试通过、等老登审计**（08 锚 `e8f2e2d`/[报告](docs/reports/test-report-08.md)，09 锚 `6b67c2d`/[报告](docs/reports/test-report-09.md)）；**10 已审计通过**（`204f93e`，11/11）。**剩余主线是 A5 → A11**：A5 是 `apps/web` 的前端缺陷（`react-router` 7.18.4 默认 `entry.server` 的 `abort` TDZ，详见票 11 行），A5 不修，aspire 全链路起不来，A11 就拿不到 aspire 现场证据；两件事是**一条链，不是并行**。票 03 后端面已审计通过（`f778a32`，`review-03.md` §9），前端腿在 aspire 下仍**不标通过**（不是断定功能坏，是这条路径既不稳也看不见）。06 的 `ToCamelCaseKeys` 残余 low 已随 `837117f` 修毕；票 03 的 F-05-4（`/health`、`/api/version` 免租户）已落票，实现按票面加 `AllowAnonymousTenant`；细节见 [交接记录](docs/reports/handover-01-04.md)。

## 多租户内核（票 05）

- 解析顺序：登录 claim `tid`（忽略 `X-Tenant`）→ 匿名 `X-Tenant` → 子域名；每个请求重新查 `ITenantStore`，`ITenantContext` 为 Scoped、不跨请求。
- 停用/过期：中间件直接 403（`Tenant.Disabled` / `Tenant.Expired`）；`Tenants` 配置在启动期严格校验，非法值启动失败。
- 连接：每租户独立 `SqlSugarScope`，懒加载 + 双检 + `ConcurrentDictionary`；禁止 `ChangeDatabase`，架构测试扫描 `src/Pzd.*`。
- 后台任务：用显式 `GetConnectionScopeAsync(tenantId)`（每次校验状态）；无参重载已改名 `GetCurrentConnectionScopeAsync`（票 07 F-07-1），后台路径禁止使用。

## 异常处理（票 06）

- 端点返回 `Result` / `Result<T>`，`ToHttpResult()` 映射为准确的 200/204/400/401/403/404/409/422/426/503 ProblemDetails；`type = https://pzd/errors/{kebab-code}`，`traceId` 与 `X-Trace-Id` 同源。
- `ProblemDetailsWriter` 是唯一实现：租户中间件短路、`ToHttpResult`、全局异常、框架 401/403 兜底共用一份；404/405 保持 `Http.*`，空 body 的 401/403 落 `Auth.*`。
- 未知异常 500 不带堆栈/类型名；`errors` 键在出口统一 camelCase（FluentValidation 管线属步骤 3，本轮未验证）。

## 仓库结构

```text
Pzd.slnx                     解决方案（src / tests 解决方案文件夹）
global.json                  SDK 锁定 10.0.4xx（本机实测 10.0.402）
Directory.Build.props        Nullable / ImplicitUsings / TreatWarningsAsErrors / MinVer
Directory.Packages.props     NuGet 中央包管理
NuGet.config                 packageSourceMapping，保证 restore 可复现
src/
  Pzd.SharedKernel/          领域原语、Result/Error、ITenantContext/IClock/ICurrentUser
  Pzd.Infrastructure/        多租户内核（含 AllowAnonymousTenant 豁免）、ProblemDetailsWriter、UoW/Outbox（07 ✅）
  Pzd.Api/                   最小 API + 租户中间件 + Result→HTTP / 异常处理
tests/
  Pzd.ArchitectureTests/     分层规则 + 租户隔离规则（ChangeDatabase）+ 反向用例
  Pzd.SharedKernel.UnitTests/
  Pzd.Infrastructure.UnitTests/  Pzd.Api.UnitTests/  Pzd.IntegrationTests/
docs/
  architecture.md            已确认的上游架构
  specs/                     foundation.md（规格）、tickets.md（票）
  notes/                     环境约定（集成测试时区/大小写等）
  reports/                   测试报告、审计报告、交接记录
```

## 环境要求

- **.NET SDK 10.0.4xx**（`global.json` 锁 `10.0.400` + `latestPatch`）
- **Git**：MinVer 从 git tag 推导版本，`git clone` 时不要浅到丢 tag
- 可选：本机 MySQL 8.0.46 与 Redis（Memurai）实例，供票 03/09/10 使用；无 Docker 也能完成本轮验收
- **Node.js ≥ 22**（`package.json` 的 `engines`）与 **pnpm 12.14.0**（`packageManager` 字段锁定；用 `corepack enable` 或同版本 pnpm，别用别的包管理器，lockfile 会漂）
- 前端票 02 已开工，`apps/web` 在 `apps/`、`packages/*` 同仓（pnpm workspace + turbo）

## 构建与测试

```powershell
dotnet build Pzd.slnx -warnaserror
```

五个测试工程（当前 35 + 30 + 86 + 33 + 1 = **185 条全绿**，2026-10-27 于 `7f699c5` 实测；顺序同上，即 SharedKernel 35、架构 30、Infrastructure 86、Api 33、集成 1）：

```powershell
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
```

测试运行器是 xUnit v3 的 Microsoft.Testing.Platform（`tests/Directory.Build.props` 统一配置，不引用 `Microsoft.NET.Test.Sdk`）。

> **为什么用 `dotnet run`**：当前工作区 `E:\pzd` 被标了 Low Mandatory Level，MTP 的 `dotnet test` 命名管道握手会失败（不是测试失败）。工作区内一律用等价的 `dotnet run --project`；普通目录或 CI 可以 `dotnet test Pzd.slnx`。

## 版本号

版本来自 git tag（`v0.1.0`），由 MinVer 推导：tag 提交上是 `0.1.0`，之后是 `0.1.1-alpha.0.<提交数>`。查询必须带 `-t:Build`，否则只有 MSBuild 求值默认值 `1.0.0`：

```powershell
dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version
```

## 本地运行

```powershell
# 后端（票 03 的 AppHost 把 API 固定在 127.0.0.1:5080）
dotnet run --project src/Pzd.Api/Pzd.Api.csproj

# 前端（pnpm workspace；dev 端口固定 5199）
pnpm install --frozen-lockfile
pnpm dev
```

- **前端 dev 地址是 `http://127.0.0.1:5199/`**：`vite.config.ts` 里 `server.port = 5199`、`strictPort: true`，并且**显式绑定 `server.host = "127.0.0.1"`**，与后端统一走 IPv4。**别把 `server.host` 留空**——留空时 Node 只绑 `::1`，此时 `curl 127.0.0.1:5199` 会连接被拒（`curl` 返回 `000`），那是假红不是服务没起来；遇到先查 `server.host` 有没有写。
- **后端地址是 `http://127.0.0.1:5080`**，前端 dev 期把 `/api`、`/hubs` 代理到它（`/hubs` 带 WebSocket）；生产由 `Pzd.Api` 托管 `build/client`，同源部署，不需要代理。
- 健康检查与版本端点免租户（票 03 的 F-05-4 前置）：`/health/live`、`/health/ready`、`/api/version` 不带 claim / `X-Tenant` / 子域也返回 200，其余端点仍 400。

## 配置与凭据

数据库连接串与租户清单只允许通过环境变量或本地 `local.env` 注入（`local.env` 已被 `.gitignore` 忽略）：

- `ConnectionStrings__master`：API 主库连接串（`appsettings.json` 里是空密码占位）
- `Tenants__0__Id` / `Code` / `Status` / `ExpiresAt` / `ConnectionString`：租户清单（等价于 `appsettings.json` 的 `Tenants` 数组）
- `ConnectionStrings__redis`：Redis（票 09 起使用）
- `PZD_TEST_MYSQL`（票 10 集成测试）
- `PZD_REDIS_TEST_DB`（票 10，约定用 `15` 号逻辑库）

仓库、日志、测试报告与提交信息中**零密码**。`Tenants` 的非法值（`Status` 拼错或数字、非法 `ExpiresAt`、重复 Id/Code）会在**启动期**抛异常；`Tenants` 为空允许启动，但所有请求返回 400（不做启动期 fail-fast）；`ExpiresAt` 缺省或 `null` 视为永不过期，空字符串为配置错误、启动失败。

**Outbox 重试上限**：配置键 `Outbox:MaxAttempts`，缺省 **5**。默认值只有一处定义——`OutboxOptions.DefaultMaxAttempts`；`appsettings.json` **不写**这个键（缺键即走默认），将来要改默认值只改代码那一处，否则会出现两处字面值、改了不生效。`0`、`-1`、非数字会在启动期直接抛异常，不静默回退；达到上限的消息进入终态「已拒绝」并同时写 `RejectionReason` 与 `LastError`，消息保留、同批后续消息继续处理——**「拒绝」不是死信队列**，死信表与人工重放属步骤 3。

## 开发约定

- 工作以 [票](docs/specs/tickets.md) 为单位，每张票完成时必须贴「验证命令 + 实际输出摘要」；失败不许标完成。
- 测试先行：新行为先写用例；架构分层由 `tests/Pzd.ArchitectureTests` 自动拦截，不靠人工 review。
- 提交保持单一主题，提交信息用 `feat|test|docs|chore(scope): …`。
- **产物与生成物不入库**：`node_modules/`、`apps/web/build/`、`apps/web/.react-router/`（typegen 产物）、`*.tsbuildinfo` 都不提交；**`pnpm-lock.yaml` 必须入库**（加 `--frozen-lockfile` 才能复现），两件事别混。
- 集成测试环境约定见 [integration-test-environment.md](docs/notes/integration-test-environment.md)：`MySqlConnector 2.6.2` 不支持连接串时区参数，每次取连接执行 `SET time_zone = '+00:00'` 并断言（连接池会重置会话）；`lower_case_table_names=1`，隔离证据只认数据行与库名。
- 改动前先读 [规格](docs/specs/foundation.md) 对应章节与票面，不要猜 API。
