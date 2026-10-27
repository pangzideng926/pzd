# 交接记录：票 01 / 04 通过 → 下一轮（票 02/03/05 起）

> 交接：老卫｜日期：2026-10-27｜读者：下一轮接手的老何（规格）、旺总（开发）、李总（测试）、老登（审计），或新的 agent 会话
> 状态快照：HEAD `0fb2e36`（tag `v0.1.0` 之后第 5 个提交），工作区干净；`dotnet build Pzd.slnx -warnaserror` 0 警告 0 错误；五个测试工程 31+13+1+1+1 = **47 条全绿**（我于 2026-10-27 复跑确认）
> 结论：**票 01 通过、票 04 通过**（依据 [复审计终稿](review-01-04-final.md)）。本文件只记录未完成项、下一步与环境约束；已完成工作的证据不复述，直接链报告。

## 1. 交接时的事实

| 项 | 值 |
|---|---|
| 分支 / HEAD | `master` / `0fb2e36` |
| tag | `v0.1.0`（MinVer 基准） |
| 版本号 | HEAD 实测 `0.1.1-alpha.0.5`（`dotnet msbuild … -t:Build -getProperty:Version`） |
| 构建 | 0 警告 0 错误（`-warnaserror --no-incremental`） |
| 测试 | 47 条、0 失败（见 [`test-report-01-04.md`](test-report-01-04.md) §7.5 与 [`review-01-04-final.md`](review-01-04-final.md) §1） |
| 生产代码 | 票 04 返工只动测试资产；SharedKernel 实现自 `50ac4ec` 后未变 |

## 2. 未完成项 / 必须跟进（按优先级）

### 2.1 F8 必须落票（老何，别只停在聊天约定）

`Error.Metadata` 仍直接持有调用方传入的字典（[`Error.cs:33`](../../src/Pzd.SharedKernel/Results/Error.cs#L33)），构造后修改原字典会改变已创建的 `Error`；共享的 `EmptyMetadata` 理论上也能被强转改写。修法：复制成 `ReadOnlyDictionary`（`StringComparer.Ordinal`）快照，并补一条「构造后改原字典不影响 Error」的用例。**`tickets.md` 目前没有对应票**，请老何新增（可并入 05 或单独一张小票），否则这条会在无人认领的情况下丢失。

### 2.2 规则③的检查范围只覆盖 3 个底座程序集（模块票必须处理）

`ArchRules.ProductAssemblies`（[`ArchRules.cs:28-29`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L28-L29)）硬编码 `Pzd.SharedKernel` / `Pzd.Infrastructure` / `Pzd.Api`。票 05 起出现真实模块后，模块的 Domain 程序集不在检查范围内，F7 对它们等于空转。落地票 05/06 时把模块程序集纳入（建议复用 `DiscoverModules` 的结果）。

### 2.3 `*.Contracts` 工程自身不受规则④约束

`DiscoverModules` 排除 `*.Contracts`（[`ArchRules.cs:79-80`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L79-L80)），老登实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。不在本轮票面，后续补一条「Contracts 只能引用 SharedKernel / 其他 Contracts」，否则 Contracts 会成为绕过模块隔离的后门。

### 2.4 F7 的两条断言当前不可证伪（补测条件明确，不阻塞）

`*.Domain` 禁依赖 `Pzd.Infrastructure` / `Pzd.Api` 两条现在能跑且绿，但直接删掉后 12 条架构测试仍全绿——`SharedKernel -> Infrastructure` 会先报 `MSB4006` 循环依赖，`Pzd.Infrastructure` 无源文件、`Pzd.Api` 只有 internal `Program`，构造不出可引用的违规模块。

补测条件（票 05/06 触发）：
- 票 05 给 `Pzd.Infrastructure` 加公共类型后，在测试程序集的 `Pzd.SharedKernel.Domain` 命名空间下加 `FakeInfrastructureDependency` 引用它，并入反向用例、断言恰好 2 条违规；
- 票 06 给 `Pzd.Api` 加公共类型（如 `ResultHttpExtensions`）后同理补 `Pzd.Api` 一条。

### 2.5 票面措辞与状态回填（老何）

- [`tickets.md:158`](../specs/tickets.md#L158) 与 [`tickets.md:176`](../specs/tickets.md#L176) 写「四条规则都有反向用例」，与实现不符：只有**规则③**有生产级反向用例，规则④是**单元级**反例（`Modules_must_not_reference_other_modules_directly`），① ② 是对真实程序集的直接断言。建议改成「规则③反向用例 + 规则④单元级反例」。
- 顶部进度行（`tickets.md:5`）与票 04 `Status`（`tickets.md:170`）仍是「返工中」，应回填为票 01/04 审计通过。
- 票 04 的 `Status` 建议改为 done/通过，并在票面附审计报告链接。

### 2.6 提交纪律（旺总，低）

`49ee09a` 把老何的 `.gitignore`（`local.env`）改动混进了测试返工提交，无害，但后续尽量单一主题。

## 3. 下一步顺序

1. **老何**：补 F8 票；回填票 01/04 状态与 158/176 措辞；确认票 05 是否顺带接 2.2 的规则③范围。
2. **旺总**：开票 05（多租户内核，测试先行）。约束已在 [`foundation.md`](../specs/foundation.md) §4.3 与 [`tickets.md`](../specs/tickets.md) 票 05 写明：`ITenantContext.TenantId` 为 `long`、停用/过期 403、Scoped 不跨请求、`TenantDbProvider` 加锁双检。
3. **李总**：票 10 的测试依据已核对（MySQL 8.0.46 + root 建库权限 + Memurai 8.2.10 + `PZD_REDIS_TEST_DB=15`），等 05 完成后按票面写集成测试。
4. **老登**：05 完成后审计；若 2.2 未在 05 处理，审计时按 blocker 提。
5. 票 02（前端 monorepo）与 03（Aspire）可并行推进，互不依赖 05；票 11 最后。

## 4. 关键文件

| 用途 | 路径 |
|---|---|
| 架构（已确认） | [`docs/architecture.md`](../architecture.md) §2/§3.2/§18 |
| 规格 | [`docs/specs/foundation.md`](../specs/foundation.md) |
| 票（含状态与验收命令） | [`docs/specs/tickets.md`](../specs/tickets.md) |
| 集成测试环境约定 | [`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md) |
| 测试报告 | [`docs/reports/test-report-01-04.md`](test-report-01-04.md) |
| 审计终稿 | [`docs/reports/review-01-04-final.md`](review-01-04-final.md) |
| 架构规则实现 | [`tests/Pzd.ArchitectureTests/ArchRules.cs`](../../tests/Pzd.ArchitectureTests/ArchRules.cs) |
| Error 实现（F8 位置） | [`src/Pzd.SharedKernel/Results/Error.cs`](../../src/Pzd.SharedKernel/Results/Error.cs) |

## 5. 环境约束（踩过的坑，别再踩）

- **测试运行方式**：`E:\pzd` 是 Low Mandatory Level，`dotnet test` 会因 MTP 命名管道握手失败而报错（不是测试失败）。工作区内用 `dotnet run --project tests/<工程>/<工程>.csproj`；普通目录/CI 用 `dotnet test Pzd.slnx`。
- **版本查询**：必须 `-t:Build`，否则得 `1.0.0`；期望值以 tag 为基准，不要硬编码 `0.1.0`。
- **MySQL 时区**：`@@time_zone=SYSTEM`（东八区），不是 UTC。集成测试连接串显式 `ServerTimeZone=UTC`（或 `CONVERT_TZ` 后断言），禁止「写 `UtcNow` 读回相等」这种吃服务器默认值的断言。
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
