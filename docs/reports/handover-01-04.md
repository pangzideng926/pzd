# 交接记录：票 01 / 04 / 12 通过 → 下一轮（票 02/03/05 起）

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 状态快照：HEAD = 最新文档提交；代码基线 `1463e9e`（票 12 修复 F8）。`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；五个测试工程 35+13+1+1+1 = **51 条全绿**（我于 2026-10-27 在 `1463e9e` 复跑确认）
> 结论：**票 01、04、12 通过**（依据 [票 01/04 复审计终稿](review-01-04-final.md)、[票 12 审计](review-12.md)）。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / 代码基线 | `main` / `1463e9e`（票 12 修复 F8；其后仅文档提交） |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | `0.1.1-alpha.0.<提交数>`（tag `v0.1.0` 之后；查询必须带 `-t:Build`；`1463e9e` 实测 `.11`） |
| 构建 | 0 警告 0 错误（`-warnaserror --no-incremental`） |
| 测试 | 51 条、0 失败（35+13+1+1+1；见 [`test-report-01-04.md`](test-report-01-04.md)、[`test-report-12.md`](test-report-12.md)） |
| 生产代码 | 票 04 返工只动测试资产；票 12 只动 [`Error.cs`](../../src/Pzd.SharedKernel/Results/Error.cs)（F8 已关闭） |
| 进行中 | 票 05 多租户内核（旺总；交接时工作区有 `Tenancy/`、`SystemClock` 等在途改动，未提交） |

## 2. 未完成项 / 必须跟进（按优先级）

> F8 已由票 12（`1463e9e`）修复并由审计关闭，见 [票 12 审计](review-12.md)；本节不再重复。

### 规则③检查范围（票 05 承接，进行中）

`ArchRules.ProductAssemblies`（[`ArchRules.cs:28-29`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L28-L29)）原先硬编码 3 个底座程序集。`9c3a971` 已把它正式划入票 05：验收要求改为「底座程序集 + `DiscoverModules()` 结果」，模块出现后自动纳入；票 05 同时补 `FakeInfrastructureDependency`（恰好 2 条违规），票 06 补 `FakeApiDependency`（恰好 3 条）。交接关注点：05 审计时确认机制真的随模块自动生效，而不是仍只查底座。

### `*.Contracts` 工程不受规则④约束

`DiscoverModules` 排除 `*.Contracts`（[`ArchRules.cs:79-80`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L79-L80)），老登实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。不在本轮票面，后续补一条「Contracts 只能引用 SharedKernel / 其他 Contracts」，否则 Contracts 会成为绕过模块隔离的后门。

### F7 补测条件（当前不可证伪，不阻塞）

`*.Domain` 禁依赖 `Pzd.Infrastructure` / `Pzd.Api` 两条现在能跑且绿，但直接删掉后 12 条架构测试仍全绿——`SharedKernel -> Infrastructure` 会先报 `MSB4006` 循环依赖，`Pzd.Infrastructure` 无源文件、`Pzd.Api` 只有 internal `Program`，构造不出可引用的违规模块。

补测条件已写入票 05/06 验收（`9c3a971`）：
- 票 05 给 `Pzd.Infrastructure` 加公共类型后，在测试程序集的 `Pzd.SharedKernel.Domain` 命名空间下加 `FakeInfrastructureDependency` 引用它，并入反向用例、断言恰好 2 条违规；
- 票 06 给 `Pzd.Api` 加公共类型（如 `ResultHttpExtensions`）后同理补 `Pzd.Api` 一条，断言恰好 3 条。

### 票 12 边界说明与可选补测（low）

票 12 状态已于 `9c3a971` 回填 ✅（`tickets.md`），审计 low 建议也写进票面「边界说明」；剩余两项：
- **浅快照边界（旺总随 05 补）**：`Error.Metadata` 只做字典结构快照，值若为可变对象（`List<T>`、嵌套字典）仍会随外部修改而变化；`Metadata` 的 XML 注释需写明「仅结构快照，值请使用不可变类型」，不做 `object?` 深拷贝。
- **（可选）只读用例再补两路**：`Clear()` 与 `ICollection<KeyValuePair>.Add` 未覆盖，`ReadOnlyDictionary` 对这两条同样抛 `NotSupportedException`，不影响正确性。

## 3. 下一步顺序

1. **老何**：票 12 状态与票 05 范围已确认（`9c3a971`），无待办；票 01/04 状态与措辞也在 `8e1a1a3` 回填完毕。
2. **旺总**：开票 05（多租户内核，测试先行）。约束已在 [`foundation.md`](../specs/foundation.md) §4.3 与 [`tickets.md`](../specs/tickets.md) 票 05 写明：`ITenantContext.TenantId` 为 `long`、停用/过期 403、Scoped 不跨请求、`TenantDbProvider` 加锁双检，以及规则③扩围与 F7 补测（见「规则③检查范围」「F7 补测条件」）；顺手在 `Error.Metadata` 注释补「仅结构快照」一句（见「票 12 边界说明」）。
3. **李总**：票 10 的测试依据已核对（MySQL 8.0.46 + root 建库权限 + Memurai 8.2.10 + `PZD_REDIS_TEST_DB=15`），等 05 完成后按票面写集成测试。
4. **老登**：05 完成后审计；若「规则③检查范围」未在 05 落实，审计时按 blocker 提。
5. 票 02（前端 monorepo）与 03（Aspire）可并行推进，互不依赖 05；票 11 最后。

## 4. 关键文件

| 用途 | 路径 |
|---|---|
| 架构（已确认） | [`docs/architecture.md`](../architecture.md) §2/§3.2/§18 |
| 规格 | [`docs/specs/foundation.md`](../specs/foundation.md) |
| 票（含状态与验收命令） | [`docs/specs/tickets.md`](../specs/tickets.md) |
| 集成测试环境约定 | [`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md) |
| 测试报告（票 01/04） | [`docs/reports/test-report-01-04.md`](test-report-01-04.md) |
| 审计终稿（票 01/04） | [`docs/reports/review-01-04-final.md`](review-01-04-final.md) |
| 测试记录（票 12） | [`docs/reports/test-report-12.md`](test-report-12.md) |
| 审计报告（票 12） | [`docs/reports/review-12.md`](review-12.md) |
| 架构规则实现 | [`tests/Pzd.ArchitectureTests/ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs) |
| Error 实现（票 12 已改） | [`src/Pzd.SharedKernel/Results/Error.cs`](../../src/Pzd.SharedKernel/Results/Error.cs) |

## 5. 环境约束（踩过的坑，别再踩）

- **测试运行方式**：`E:\pzd` 是 Low Mandatory Level，`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。
- **版本查询**：必须 `-t:Build`，否则得 `1.0.0`；期望值以 tag 为基准，不要硬编码 `0.1.0`。
- **MySQL 时区**：`@@time_zone=SYSTEM`（东八区），不是 UTC。`MySqlConnector 2.6.2` 不支持 `ServerTimeZone` / `ConnectionTimeZone`（未知关键字直接抛 `ArgumentException`），`DateTimeKind=Utc` 也不改会话时区；正确做法是**每次取连接后** `SET time_zone = '+00:00'` 并断言 `@@session.time_zone`（连接池重置会话，不能只在 fixture 里 SET 一次），或在查询里显式 `CONVERT_TZ`。禁止「写 `UtcNow` 读回相等」这种吃服务器默认值的断言。
- **表名大小写**：Windows 上 `lower_case_table_names=1`，隔离证据只认数据行与库名，不认表名大小写差异。
- **凭据**：只走环境变量或 `local.env`（gitignored）；仓库、日志、报告零密码。
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
- `verification-before-completion`：声称完成前跑上面 6 条命令并贴输出，不要引用旧报告代替当前 HEAD 的实跑。
- `requesting-code-review` / `receiving-code-review`：交给老登审计前自检一遍；收到 findings 先复现再改。
- `systematic-debugging`：遇到 MTP 握手、循环依赖、时区断言这类环境型失败时，先分清「环境失败 / 测试失败 / 产品缺陷」。
