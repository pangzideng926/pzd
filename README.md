# PZD

PZD 是一个模块化单体后台系统（.NET 10，后续接入 React 前端）。当前处于第二轮，范围是架构文档 §18 的**步骤 1（工程底座）**与**步骤 2（横切能力）**；登录外壳与业务模块留到后续轮次。

> 文档入口：[架构](docs/architecture.md) · [规格](docs/specs/foundation.md) · [票](docs/specs/tickets.md) · [测试/审计/交接](docs/reports/)

## 当前进度（2026-10-27）

| 票 | 内容 | 状态 |
|---|---|---|
| 01 | 仓库与 .NET 骨架 | ✅ 审计通过 |
| 04 | SharedKernel + 架构测试基线 | ✅ 审计通过（返工后 `49ee09a`） |
| 12 | `Error.Metadata` 只读快照（F8） | ✅ 审计通过（`1463e9e`） |
| 02 / 03 | 前端 monorepo / Aspire 编排 | ready-for-agent |
| 05 | 多租户内核 + 单测 | ✅ 审计通过（`e593225`，116 条） |
| 06 | 异常处理 + Result→HTTP | ✅ 审计通过（`abde749`，156 条） |
| 07 | UoW + Outbox + 领域事件 | 🔧 返工中（`0da4a18`，173 条；剩票面 F-07-4 规则收紧） |
| 08–10 | 可观测性、Quartz、租户隔离集成测试 | ready-for-agent |
| 11 | CLAUDE.md + 一键验收脚本 | 依赖 02/03/07–10 |

票 07 返工：`40f016a`（F-07-1 规则改认形状、F-07-2 分发器用例、补偿顺序）、`001a731`（失败原因与最大重试、反射异常解包、零处理器/零消费者告警）、`0da4a18`（F-05-8 规则补第二层：零实参旧名调用也报），当前 173 条全绿。还剩票面 **F-07-4（规则收紧）** 与 **F-07-5（重试上限改为可配置、终态写 `LastError`）**：前者把后台路径改为标识符级禁用（容忍空白、方法组引用同算）、清单并入 `Jobs/**` 与 `*Job.cs`，并补三条反向用例；两者修完由老登终审后接 08/09/10。173 条是 `001a731` 的实测快照，F-07-4 落地会再增约三条反向用例，以最终 hash 为准。06 的 `ToCamelCaseKeys` 残余 low 已随 `837117f` 修毕。票 03 的 F-05-4（`/health`、`/api/version` 免租户）已落票，实现时按票面加 `AllowAnonymousTenant`；细节见 [交接记录](docs/reports/handover-01-04.md)。

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
  Pzd.Infrastructure/        多租户内核、ProblemDetailsWriter、UoW/Outbox（07 返工中）
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
- 前端票 02 尚未开工，当前不需要 Node/pnpm

## 构建与测试

```powershell
dotnet build Pzd.slnx -warnaserror
```

五个测试工程（当前 35 + 25 + 79 + 33 + 1 = **173 条全绿**，2026-10-27 于 `001a731` 实测）：

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

## 配置与凭据

数据库连接串与租户清单只允许通过环境变量或本地 `local.env` 注入（`local.env` 已被 `.gitignore` 忽略）：

- `ConnectionStrings__master`：API 主库连接串（`appsettings.json` 里是空密码占位）
- `Tenants__0__Id` / `Code` / `Status` / `ExpiresAt` / `ConnectionString`：租户清单（等价于 `appsettings.json` 的 `Tenants` 数组）
- `ConnectionStrings__redis`：Redis（票 09 起使用）
- `PZD_TEST_MYSQL`（票 10 集成测试）
- `PZD_REDIS_TEST_DB`（票 10，约定用 `15` 号逻辑库）

仓库、日志、测试报告与提交信息中**零密码**。`Tenants` 的非法值（`Status` 拼错或数字、非法 `ExpiresAt`、重复 Id/Code）会在**启动期**抛异常；`Tenants` 为空允许启动，但所有请求返回 400（不做启动期 fail-fast）；`ExpiresAt` 缺省或 `null` 视为永不过期，空字符串为配置错误、启动失败。

## 开发约定

- 工作以 [票](docs/specs/tickets.md) 为单位，每张票完成时必须贴「验证命令 + 实际输出摘要」；失败不许标完成。
- 测试先行：新行为先写用例；架构分层由 `tests/Pzd.ArchitectureTests` 自动拦截，不靠人工 review。
- 提交保持单一主题，提交信息用 `feat|test|docs|chore(scope): …`。
- 集成测试环境约定见 [integration-test-environment.md](docs/notes/integration-test-environment.md)：`MySqlConnector 2.6.2` 不支持连接串时区参数，每次取连接执行 `SET time_zone = '+00:00'` 并断言（连接池会重置会话）；`lower_case_table_names=1`，隔离证据只认数据行与库名。
- 改动前先读 [规格](docs/specs/foundation.md) 对应章节与票面，不要猜 API。
