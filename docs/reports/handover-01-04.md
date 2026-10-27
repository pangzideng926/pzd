# 交接记录：票 01 / 04 / 05 / 12 通过 → 下一轮（票 02/03/06 起）

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 文件名沿用首轮，内容已覆盖票 01–12；下一轮请从本文件第 2、3 节接着走。
> 状态快照：HEAD = 最新文档提交；代码基线 `e593225`（票 05 返工）。`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；五个测试工程 35+16+54+10+1 = **116 条全绿**（我于 2026-10-27 在 `e593225` 复跑确认）
> 结论：**票 01、04、05、12 全部收口**（[票 01/04 复审计终稿](review-01-04-final.md)、[票 05 终审](review-05.md)、[票 12 审计](review-12.md)）。票 05 票面已于 `73f02e4` 回填，F-05-4/F-05-8 已落票。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / `e593225`（票 05 返工提交；其后仅文档提交） |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`；`e593225` 实测 `.18`） |
| 构建 | 0 警告 0 错误（`-warnaserror --no-incremental`） |
| 测试 | 116 条、0 失败（35+16+54+10+1；见 [`test-report-01-04.md`](test-report-01-04.md)、[`test-report-05.md`](test-report-05.md)、[`test-report-12.md`](test-report-12.md)） |
| 生产代码 | 票 04 返工只动测试；票 12 只动 [`Error.cs`](../../src/Pzd.SharedKernel/Results/Error.cs)；票 05 只动 `src/Pzd.Infrastructure/Tenancy/*`、`SystemClock`、`src/Pzd.Api/Program.cs` 与对应测试 |
| 下一张票 | **06 异常处理 + Result→HTTP**（无阻塞前置）；03 的 F-05-4 已在 `73f02e4` 落票，实现时按票面加豁免 |

## 2. 未完成项 / 已落票约束

> 票 05 票面已于 `73f02e4` 回填；F-05-4、F-05-8 已落票。代码侧 F8、规则③扩围、F7-Infrastructure 均已关闭。下面只剩两条未落票事项，以及两条实现时必须遵守的既定约束。

### 未落票 · `*.Contracts` 工程不受规则④约束

`DiscoverModules` 排除 `*.Contracts`（[`ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs)），老登实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。不在本轮票面，后续补一条「Contracts 只能引用 SharedKernel / 其他 Contracts」，否则 Contracts 会成为绕过模块隔离的后门。

### 未落票 · F7 的 API 侧补测（票 06）

票 05 已补 `FakeInfrastructureDependency`（反向用例断言恰好 2 条违规，`e593225`），`Pzd.Infrastructure` 这一侧关闭。剩：票 06 给 `Pzd.Api` 加公共类型（如 `ResultHttpExtensions`）后补 `FakeApiDependency`，断言恰好 3 条违规。

### 已落票 · F-05-4（票 03 开工硬前置，`73f02e4`）

`/health/live`、`/health/ready`、`/api/version` 免租户已写入 foundation §4.3 与票 03/09；票 03 的 `Blocked by` 已改为 `01, 02, 05`，票 09 改为 `03, 04`。实现方式为 `AllowAnonymousTenant` metadata，验收要求：无 claim / `X-Tenant` / 子域访问三端点均 200，其余端点仍 400。**票 03 实现时按此做，不要再新造豁免机制。**

### 已落票 · F-05-8（票 07 验收约束，`73f02e4`）

后台任务/消费者必须用带 `tenantId` 的显式重载 `GetConnectionScopeAsync(tenantId)`（先校验停用/到期），**禁止无参裸取**；停用/过期租户的消息拒绝处理并记录理由。票 07 实现与审计都按此判定。

## 3. 下一步顺序

1. **老何**：规格侧无待办（票 05 回填、F-05-4、F-05-8 已随 `73f02e4` 落地）；03/07 实现中若遇票面歧义再回规格。
2. **旺总**：开票 06（异常处理 + Result→HTTP，测试先行），并按票面补 F7 的 `FakeApiDependency`（恰好 3 条）；随后票 09（Quartz / 健康检查 / 版本），按已落票的 F-05-4 给三端点加 `AllowAnonymousTenant`。票 05 代码不再动，回归除外。
3. **李总**：票 06 测试；票 10 集成测试等 06 完成后按票面写（MySQL/Redis 依据已核，见 [`integration-test-environment.md`](../notes/integration-test-environment.md)）。
4. **老登**：票 06 审计；重点看 F7 API 反向用例是否真的可证伪。
5. 票 02（前端 monorepo）与 03（Aspire）可并行（03 的 F-05-4 前置已落票，按票面实现即可）；票 07 依赖 05/06 且受 F-05-8 约束；票 11 最后。

## 4. 关键文件

| 用途 | 路径 |
|---|---|
| 架构（已确认） | [`docs/architecture.md`](../architecture.md) §2/§3.2/§18 |
| 规格 | [`docs/specs/foundation.md`](../specs/foundation.md) |
| 票（含状态与验收命令） | [`docs/specs/tickets.md`](../specs/tickets.md) |
| 集成测试环境约定 | [`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md) |
| 测试报告（票 01/04） | [`docs/reports/test-report-01-04.md`](test-report-01-04.md) |
| 审计终稿（票 01/04） | [`docs/reports/review-01-04-final.md`](review-01-04-final.md) |
| 测试报告（票 05） | [`docs/reports/test-report-05.md`](test-report-05.md) |
| 审计终稿（票 05） | [`docs/reports/review-05.md`](review-05.md) |
| 测试记录（票 12） | [`docs/reports/test-report-12.md`](test-report-12.md) |
| 审计报告（票 12） | [`docs/reports/review-12.md`](review-12.md) |
| 多租户实现 | [`src/Pzd.Infrastructure/Tenancy/`](../../src/Pzd.Infrastructure/Tenancy/) |
| 架构规则实现 | [`tests/Pzd.ArchitectureTests/ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs) |
| Error 实现（票 12 已改） | [`src/Pzd.SharedKernel/Results/Error.cs`](../../src/Pzd.SharedKernel/Results/Error.cs) |

## 5. 环境约束（踩过的坑，别再踩）

- **测试运行方式**：`E:\pzd` 是 Low Mandatory Level，`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。
- **版本查询**：必须 `-t:Build`，否则得 `1.0.0`；期望值以 tag 为基准，不要硬编码 `0.1.0`。
- **MySQL 时区**：`@@time_zone=SYSTEM`（东八区），不是 UTC。`MySqlConnector 2.6.2` 不支持 `ServerTimeZone` / `ConnectionTimeZone`（未知关键字直接抛 `ArgumentException`），`DateTimeKind=Utc` 也不改会话时区；正确做法是**每次取连接后** `SET time_zone = '+00:00'` 并断言 `@@session.time_zone`（连接池重置会话，不能只在 fixture 里 SET 一次），或在查询里显式 `CONVERT_TZ`。禁止「写 `UtcNow` 读回相等」这种吃服务器默认值的断言。
- **表名大小写**：Windows 上 `lower_case_table_names=1`，隔离证据只认数据行与库名，不认表名大小写差异。
- **凭据**：只走环境变量或 `local.env`（gitignored）；仓库、日志、报告零密码。
- **租户配置**：`Tenants` 的非法值（`Status` 拼错/数字、非法 `ExpiresAt`、重复 Id/Code）会在**启动期**抛 `InvalidOperationException`，不会等首个请求；做「坏配置」用例时断言启动失败，不要断言首次请求 400。样例见 `src/Pzd.Api/appsettings.json`，真实连接串走 env / `local.env`。
- **后台任务取连接**：不要用无参 `GetConnectionScopeAsync()` 裸取（只信任已 `Set` 的上下文，不校验停用/到期），走带租户 id 的显式重载（F-05-8）。
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

- `test-driven-development`：票 05 从租户解析用例入手，先红后绿。
- `verification-before-completion`：声称完成前跑第 6 节整套命令并贴输出，不要引用旧报告代替当前 HEAD 的实跑。
- `requesting-code-review` / `receiving-code-review`：交给老登审计前自检一遍；收到 findings 先复现再改。
- `systematic-debugging`：遇到 MTP 握手、循环依赖、时区断言这类环境型失败时，先分清「环境失败 / 测试失败 / 产品缺陷」。
