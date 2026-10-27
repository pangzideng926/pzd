# 交接记录：票 01 / 04 / 05 / 06 / 12 通过（票 07 返工中）→ 下一轮

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 文件名沿用首轮，内容已覆盖票 01–12；下一轮请从本文件第 2、3 节接着走。
> 状态快照：代码基线 `abde749`（票 06 终审）；HEAD 为最新文档提交。`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；五个测试工程 35+71+30+19+1 = **156 条全绿**（我于 2026-10-27 在 `abde749` 的干净 worktree 复跑确认）。
> 结论：**票 01、04、05、06、12 已收口**（[01/04 终审](review-01-04-final.md)、[05 终审](review-05.md)、[06 终审](review-06.md)、[12 审计](review-12.md)）。**票 07 首轮审计需修改**（F-07-1/F-07-2，[review-07.md](review-07.md) §5），旺总正在工作区返工，未提交；07 的复测与终审在下一轮。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / `abde749`（票 06 终审；其后仅文档提交） |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`；`abde749` 实测 `.31`） |
| 构建 | 0 警告 0 错误（`-warnaserror --no-incremental`） |
| 测试 | 156 条、0 失败（35+71+30+19+1；见 [`test-report-01-04.md`](test-report-01-04.md)、[`test-report-05.md`](test-report-05.md)、[`test-report-06.md`](test-report-06.md)、[`test-report-12.md`](test-report-12.md)） |
| 生产代码 | 05：`Infrastructure/Tenancy/*`；06：`Api/Errors/*` + `Infrastructure/Errors/ProblemDetailsWriter.cs`；07：`Infrastructure/Persistence/*`（返工中，工作区未提交） |
| 进行中 | 票 07 返工（旺总）：工作区有 `Tenancy/*`、`Persistence/*`、`ArchRules`/测试改动，另有变异用的 `.bak`/`mutate07b.py` 临时文件；**不要把这些临时文件提交** |
| 下一张票 | 票 07 返工 → 李总复测 → 老登终审；之后 08/09/10 |

## 2. 未完成项 / 待办与已落票约束

> 票 06 已于 `71529fd` 回填 ✅（`abde749`，156 条），`Auth.*` 文案已对齐；票 05、12 的票面均已收口。下面按「下一轮必做」与「已落票约束」分开。

### 票 07 返工（旺总，下一轮必做）

- **F-07-1**（medium）：F-05-8 的代码检查按 token 变量名白名单，`ct2` 这类改名即可绕过。修法首选把无参重载改名（如 `GetCurrentConnectionScopeAsync`），规则只认 API 形状；反向用例补一条「改名 token 也必须红」；同步更新 `ITenantDbProvider` 注释。
- **F-07-2**（medium）：`DomainEventDispatcher` 零测试覆盖（把泛型解析改坏后 152 条仍全绿）。补 3 条行为用例（同一事件实例、按 DI 顺序、零处理器算成功）+ F-07-5 的同步抛出用例。
- **顺手项**：F-07-3（零消费者不得静默标记已处理）、F-07-4（失败原因落存储、最大尝试次数/防饿死）。
- **落步骤 3**：F-07-5（`TargetInvocationException` 解包）、F-07-6（处理器级幂等）；**本票只用补偿删除，不等于「事务回滚已验证」**，步骤 3 换真实事务并删补偿代码。
- 完成后给 hash，李总按 hash 复测、老登按 hash 终审（review-07 会补终审结论）。

### 票 06 遗留 low（旺总，06 关票后顺手）

- `ToCamelCaseKeys` 只认 `IDictionary<string, object?>` / `IDictionary<string, string[]>`，`ReadOnlyDictionary` 等形态会静默不转换；camelCase 撞键会抛。`6922421` 已定口径：**落点在出口、不用全局 `DictionaryKeyPolicy`，改用非泛型 `IDictionary` 重建 + Ordinal 去重（后写覆盖先写）**；补两条用例（只读字典、`List` 值字典）。

### 已落票 · F-05-4（票 03 开工硬前置，`73f02e4`）

`/health/live`、`/health/ready`、`/api/version` 免租户已写入 foundation §4.3 与票 03/09；票 03 的 `Blocked by` 为 `01, 02, 05`，票 09 为 `03, 04`。实现用 `AllowAnonymousTenant` metadata，验收：无 claim / `X-Tenant` / 子域访问三端点均 200，其余端点仍 400。**票 03 实现时按此做，不要再新造豁免机制。**

### 已落票 · F-05-8（票 07 验收约束，`73f02e4`）

后台任务/消费者必须用带 `tenantId` 的显式重载 `GetConnectionScopeAsync(tenantId)`（先校验停用/到期），禁止无参裸取；停用/过期租户的消息拒绝处理并记录理由。F-07-1 修完后，这条约束才真正有自动化守护。

### 已落票 · `Auth.*` 错误码口径（`a2982ca` + `71529fd`）

- `Http.*` = HTTP 语义（404/405 等）、`Auth.*` = 认证与授权横切、`Tenant.*` = 租户解析与状态、`System.*` = 意外异常；**`Auth.*` 保留，不合并成 `Http.*`**。
- `Auth.Unauthorized` 只表示框架兜底「未认证」（无 token / token 无效）；**步骤 3 登录时账号密码错误是 Identity 业务错误，不得复用此码**。
- 步骤 3 的授权中间件与 `.RequirePermission` 产生的 401/403 **必须复用** `Auth.Unauthorized` / `Auth.Forbidden`，不得另造前缀；文案以 `CommonErrors` 为单一来源（已与票面对齐）。

### 未落票 · `*.Contracts` 工程不受规则④约束

`DiscoverModules` 排除 `*.Contracts`，老登实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。后续补一条「Contracts 只能引用 SharedKernel / 其他 Contracts」，否则 Contracts 会成为绕过模块隔离的后门。

### 步骤 3 移交清单（`tickets.md` 内，别丢）

已登记：FluentValidation 管道 + Application 层校验（票 06 只验证 `errors` 透传 + camelCase，**不得写成「FluentValidation 已验证」**）、真实事务替换补偿删除、`Auth.*` 复用、登录类端点与匿名租户解析策略、10 张票之外的前端登录外壳。

## 3. 下一步顺序

1. **旺总**：完成票 07 返工（F-07-1/F-07-2 + 顺手 3/4），提交后给 hash；06 关票后按 `6922421` 顺手修 `ToCamelCaseKeys`。
2. **李总**：按 07 新 hash 复测（重点：改名 token 必须红、`DomainEventDispatcher` 变坏必须红），其后票 08/09/10。
3. **老登**：07 新 hash 终审并更新 `review-07.md` 结论；08 起继续按票审计。
4. **老何**：规格侧无待办（06 已回填）；03 开工前确认 F-05-4 已生效；维护「步骤 3 移交清单」。
5. 票 02（前端）与 03（Aspire）可并行（03 受 F-05-4 约束，已落票）；08 依赖 06；09 依赖 03/04；10 依赖 05；11 最后。

## 4. 关键文件

| 用途 | 路径 |
|---|---|
| 架构（已确认） | [`docs/architecture.md`](../architecture.md) §2/§3.2/§6.3/§8/§18 |
| 规格 | [`docs/specs/foundation.md`](../specs/foundation.md) §4.3/§4.4/§4.5 |
| 票（含状态、验收与步骤 3 移交清单） | [`docs/specs/tickets.md`](../specs/tickets.md) |
| 集成测试环境约定 | [`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md) |
| 票 01/04 测试报告、审计终稿 | [`test-report-01-04.md`](test-report-01-04.md)、[`review-01-04-final.md`](review-01-04-final.md) |
| 票 05 测试报告、审计终稿 | [`test-report-05.md`](test-report-05.md)、[`review-05.md`](review-05.md) |
| 票 06 测试报告、审计终稿 | [`test-report-06.md`](test-report-06.md)、[`review-06.md`](review-06.md) |
| 票 07 测试报告、审计报告（首轮，待终审更新） | [`test-report-07.md`](test-report-07.md)、[`review-07.md`](review-07.md) |
| 票 12 测试记录、审计报告 | [`test-report-12.md`](test-report-12.md)、[`review-12.md`](review-12.md) |
| 多租户实现 | [`src/Pzd.Infrastructure/Tenancy/`](../../src/Pzd.Infrastructure/Tenancy/) |
| ProblemDetails 唯一实现 | [`src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs`](../../src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs) |
| 持久化 / Outbox（返工中） | [`src/Pzd.Infrastructure/Persistence/`](../../src/Pzd.Infrastructure/Persistence/) |
| 架构规则实现 | [`tests/Pzd.ArchitectureTests/ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs) |

## 5. 环境约束（踩过的坑，别再踩）

- **测试运行方式**：`E:\pzd` 是 Low Mandatory Level，`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。
- **干净副本验证**：`git archive` 出来的目录没有 `.git`，MinVer 会因 `MINVER1001`（不是 Git 仓库）报错导致 `-warnaserror` 构建失败，但测试仍可跑。要验证 build 0/0，用 `git worktree add --detach` 而不是 `git archive`。
- **版本查询**：必须 `-t:Build`，否则得 `1.0.0`；期望值以 tag 为基准，不要硬编码 `0.1.0`。
- **MySQL 时区**：`@@time_zone=SYSTEM`（东八区），不是 UTC。`MySqlConnector 2.6.2` 不支持 `ServerTimeZone` / `ConnectionTimeZone`（未知关键字直接抛 `ArgumentException`），`DateTimeKind=Utc` 也不改会话时区；正确做法是**每次取连接后** `SET time_zone = '+00:00'` 并断言 `@@session.time_zone`（连接池重置会话，不能只在 fixture 里 SET 一次），或在查询里显式 `CONVERT_TZ`。禁止「写 `UtcNow` 读回相等」这种吃服务器默认值的断言。
- **表名大小写**：Windows 上 `lower_case_table_names=1`，隔离证据只认数据行与库名，不认表名大小写差异。
- **凭据**：只走环境变量或 `local.env`（gitignored）；仓库、日志、报告零密码。
- **租户配置**：`Tenants` 的非法值（`Status` 拼错/数字、非法 `ExpiresAt`、重复 Id/Code）会在**启动期**抛 `InvalidOperationException`，不会等首个请求；`Tenants` 为空允许启动但请求 400（不做 fail-fast）；`ExpiresAt` 缺省或 `null` 为永不过期，空字符串为配置错误。
- **后台任务取连接**：不要用无参 `GetConnectionScopeAsync()` 裸取（只信任已 `Set` 的上下文，不校验停用/到期），走带租户 id 的显式重载；07 返工后无参重载会改名（F-07-1）。
- **07 内存实现**：Outbox/UoW 的事务是**补偿删除**，不是真实回滚；失败原因未落存储、无最大尝试次数——这些属步骤 3，报告与文档不得写成「事务回滚已验证」。
- **MinVer**：浅克隆丢 tag 会让版本掉成 `0.0.0-alpha…`。

## 6. 可直接复制的验证命令

```powershell
dotnet build Pzd.slnx -warnaserror --no-incremental
dotnet run --project tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version
```

## 7. Suggested skills（给下一个 agent）

- `test-driven-development`：07 返工先从「改名 token 必须红」「分发器变坏必须红」写起。
- `verification-before-completion`：声称完成前跑第 6 节整套命令并贴输出；干净副本用 worktree，别用 `git archive` 验 build。
- `requesting-code-review` / `receiving-code-review`：交给老登终审前自检；收到 findings 先复现再改。
- `systematic-debugging`：遇到 MTP 握手、MinVer、循环依赖、时区断言这类环境型失败时，先分清「环境失败 / 测试失败 / 产品缺陷」。
