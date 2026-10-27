# 测试报告：票 01（工程骨架）+ 票 04（SharedKernel + 架构测试）

> 测试：李总｜日期：2026-10-27｜依据：[`docs/specs/tickets.md`](../specs/tickets.md) 票 01、04，方法：Matt Pocock TDD（tests.md / mocking.md）
> 被测提交：`e0ec679`（票 01）、`50ac4ec`（票 04），tag `v0.1.0`，工作区干净
> 结论：**票 01 通过**（验收命令需按 §4 修正）；**票 04 首测需修改**（F1/F2/F4），旺总返工 `c95e72e` 后**复测通过**，证据见 §7；仅剩一条 low 残余。**票 04 建议审计通过**。

---

## 1. 环境

| 项 | 值 |
|---|---|
| 工作区 | `E:\pzd`（ACL：`Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`，已用 `icacls` 核实） |
| SDK | `dotnet --version` → `10.0.402`（`global.json` 锁 `10.0.400` + `latestPatch`） |
| 测试运行方式 | 工作区内 `dotnet run --project tests/<工程>/<工程>.csproj`（xUnit v3 进程内 runner）；跨目录对照副本 `dotnet test Pzd.slnx` |

## 2. 我实跑过的验证（真实输出）

| 命令 | 结果 |
|---|---|
| `dotnet build Pzd.slnx -warnaserror --no-incremental` | 已成功生成，**0 警告 0 错误**，exit 0 |
| `dotnet run --project tests/Pzd.ArchitectureTests/…` | Total 7 / Failed 0，exit 0 |
| `dotnet run --project tests/Pzd.SharedKernel.UnitTests/…` | Total 29 / Failed 0，exit 0 |
| `dotnet run --project tests/Pzd.Infrastructure.UnitTests/…` | Total 1 / Failed 0，exit 0 |
| `dotnet run --project tests/Pzd.Api.UnitTests/…` | Total 1 / Failed 0，exit 0 |
| `dotnet run --project tests/Pzd.IntegrationTests/…` | Total 1 / Failed 0，exit 0 |
| 合计 | **39 条，0 失败**（与开发自述一致） |
| `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -getProperty:Version` | `1.0.0`（MSBuild 求值默认值，证实票内命令缺 `-t:Build`） |
| 加 `-t:Build` 后同上 | `0.1.0`（在 `50ac4ec`/`v0.1.0` 上）；`1bdc680` 上复测为 `0.1.1-alpha.0.1`（见 F6） |
| `Pzd.Api.dll` VersionInfo | FileVersion `0.1.0.0`，ProductVersion `0.1.0+50ac4ec…` |
| `git log --oneline -1` / `git tag` / `git status --porcelain` | `50ac4ec`、`v0.1.0`、无输出（干净） |
| 副本（非 Low IL 目录）`dotnet test Pzd.slnx` | **总计 39，失败 0，成功 39，exit 0** |

**对照控制（证明测试真实可失败）**：在副本里给 `Pzd.SharedKernel.Domain` 注入一个引用 `SqlSugar` 的 `BadDomainType`，规则③立即失败并报 `违规类型：Pzd.SharedKernel.Domain.BadDomainType`，`dotnet run` exit 1。说明规则本身有效、且 `dotnet run` 能正确上报失败退出码。

## 3. 票 01 逐项核对

| 验收项 | 结论 | 证据 |
|---|---|---|
| `git init` + 初始提交 + 无未跟踪 `bin/obj` | 通过 | `git ls-files` 44 个文件，无 bin/obj |
| tag `v0.1.0`，MinVer 产出 `0.1.0` | 通过 | 上述 Version/ProductVersion |
| `dotnet build Pzd.slnx -warnaserror` exit 0 无警告 | 通过 | 2、实测 |
| `dotnet test Pzd.slnx` exit 0 | **不通过（环境）** | 见 F3 |
| `-getProperty:Version` 输出 `0.1.0` | **不通过（票面命令有误）** | 不加 `-t:Build` 得 `1.0.0`，见 §2 |
| NuGet 版本只在 `Directory.Packages.props` | 通过 | 5 个 csproj 均无 `Version=` |
| `.slnx` 用 `src`/`tests` 解决方案文件夹 | 通过 | Pzd.slnx |
| 本票不含 AppHost / 前端 / 业务类型 | 通过 | `git ls-files`、`src` 仅 3 工程 |
| 5 个测试工程各 1 smoke test | 通过 | 5 个 SmokeTests.cs |
| `.gitignore` 覆盖 bin/obj/node_modules/.turbo/*.user/artifacts | 通过 | .gitignore 实测 |

## 4. 票 04 逐项核对

| 验收项 | 结论 | 证据 |
|---|---|---|
| Result/Result<T> 成功与失败互斥、泛型转换 | 通过 | ResultTests 6 条 |
| Error 按 Code 相等、含 Type/Metadata、错误码格式 | 通过 | ErrorTests 10 条（含非法码拒绝） |
| AggregateRoot 收集事件 + ClearEvents；ValueObject 按值相等 | 通过 | AggregateRootTests 4 条、ValueObjectTests 3 条 |
| 架构规则 4 条 | 通过（规则本身有效） | 规则③注入违规能被抓到（§2 对照控制） |
| 反向用例证明规则非空跑 | **不通过** | F1 |
| 规则④「模块只能引用对方 Contracts」可拦截 | **不通过（机制缺陷）** | F2 |
| 两个测试工程全绿 | 通过 | 36 条（29+7） |

---

## 5. 缺陷与失败用例

### F1 · high · 反向用例没有守住生产规则（票 04「反向用例」验收项实际未达成）

- **期望**：故意构造的违规让**架构测试里那条生产规则**失败，从而证明规则非空跑。
- **实际**：`ReverseCaseTests` 用 `Types.InAssembly(测试程序集).That().HaveName("Fake")…` **重新拼了一条规则**，验证的是 NetArchTest 库的能力；`DependencyRulesTests` 里 `ResideInNamespaceEndingWith(".Domain")` 等生产谓词改坏了也无测试能发现。
- **最小复现**（副本执行，工作区未改）：
  1. `sed -i 's/ResideInNamespaceEndingWith(".Domain")/ResideInNamespaceEndingWith(".DomainMUT")/' tests/Pzd.ArchitectureTests/DependencyRulesTests.cs`
  2. `dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj`
- **实际输出**：`Total: 7, Failed: 0`，exit 0 —— 规则③的命名空间选择器指向不存在的命名空间（规则变成空跑），**反向用例依然全绿**。
- **修复建议**：把 4 条规则抽成接收 `Assembly[]`（或类型集合）的共享断言方法；生产测试传产品程序集，`ReverseCaseTests` 传「产品程序集 + ArchitectureTestAssembly」跑**同一个方法**并断言报出 1 处违规。只改测试，不动实现。

### F2 · high · 规则④的模块发现会静默漏检（票 04「架构测试能自动拦截」）

- **期望**：新增模块若违规引用其他模块（非 `.Contracts`），架构测试变红。
- **实际**：`ModuleAssemblies()` 从 `AppContext.BaseDirectory` 扫描 `Pzd.*.dll`，只能看到 ArchitectureTests 工程引用到的程序集；模块项目若没被它引用，规则④永远对空集通过。
- **A/B 复现**（副本执行）：
  1. 新建 `src/Pzd.Identity`（classlib）与 `src/Pzd.Platform`（真实使用 `Pzd.Identity.IdentityMarker`），加入 `Pzd.slnx`，构建成功、`Pzd.Platform.dll` 的 AssemblyRef 确认含 `Pzd.Identity`（违规：非 Contracts）。
  2. `dotnet run --project tests/Pzd.ArchitectureTests/…` → `Total: 7, Failed: 0`（漏检）。
  3. 给 ArchitectureTests 加上 `Pzd.Platform` 的 ProjectReference 后重跑 → `[FAIL] 违规的模块引用：Pzd.Platform -> Pzd.Identity`，exit 1。
- **结论**：规则逻辑对，发现机制脆；漏检时是"绿得毫无提示"。
- **修复建议**：规则④不要从输出目录发现模块，直接从仓库源树/`Pzd.slnx` 枚举 `src/Pzd.*/Pzd.*.csproj`，断言其 `ProjectReference` 只能指向 `Pzd.SharedKernel`、`Pzd.Infrastructure`、自身、`Pzd.<其他>.Contracts`（禁 `Pzd.Api` 与跨模块）。这比读编译产物更早、更不会漏。

### F3 · high · 票 01 的 `dotnet test Pzd.slnx` 在本机不可执行，票面验收项不成立

- **期望**：票 01「`dotnet test Pzd.slnx` 退出码 0」。
- **实际**：在工作区 `E:\pzd` 执行 → 退出码非 0（bash 报 127）；每个测试工程 `运行了零个测试`，错误为 `System.UnauthorizedAccessException: Access to the path is denied`（`NamedPipeClientStream.TryConnect`），共 10 处 Access denied。原因与开发所述一致：目录是 Low Mandatory Level，子进程低完整性级别连不上 MTP 命名管道；**不是测试失败**。
- **反证**：同一份 `HEAD` 复制到普通目录（`E:\pzd-mut`）后 `dotnet test Pzd.slnx` → 39 通过、exit 0。
- **影响面**：票 11 的 `scripts/verify.ps1` 若照票写 `dotnet test` 会直接红，AC1 无法达成。
- **要求**：
  - 票 01 验收命令改为：`dotnet run --project tests/<工程>/<工程>.csproj`（或用 `dotnet test` 时把仓库置于非 Low IL 目录），并在 `docs/specs/tickets.md` 注明原因；
  - 票 11 的 `verify.ps1` **固定用** `dotnet run --project` 逐工程执行并汇总退出码；不要用 `dotnet test`。
- **附**：`dotnet run` 的失败退出码已实测可信（F2 复现第 3 步 exit 1）。

### F4 · medium · 缺「不同实体类型同 Id 不相等」用例（票 04 Entity 语义）

- **期望**：`Entity<TId>` 注释承诺「避免不同聚合根同 Id 时相等」，应有测试守住。
- **实际**：把 `Entity.Equals` 里的 `GetType() == other.GetType()` 删掉后，SharedKernel 29 条测试仍全绿（变异存活）。
- **修复建议**：补一条 `Assert.NotEqual(new Order(1), new Invoice(1))`（两个不同 Entity 子类同 Id）。
- **附（low）**：`Error.Metadata` 直接持有调用方字典，构造后再改字典会改变 Error；不可变性缺口，建议复制一份并补测试。`AggregateRoot.AddDomainEvent(null)` 抛 `ArgumentNullException` 无测试（low）。

### F6 · medium · 票 01 的版本期望值会随 tag 之后的提交漂移（2026-10-27 复测发现）

- **背景**：规格回填后 HEAD 为 `1bdc680`（比 `v0.1.0` tag 多一个提交）。按票 01 新命令执行：
  `dotnet msbuild src/Pzd.Api/Pzd.Api.csproj -t:Build -getProperty:Version` → **`0.1.1-alpha.0.1`**（不是票面写的 `0.1.0`）。
- **判断**：这是 MinVer 的预期行为（tag 之上有提交 → 预发布递增），**不是缺陷**；但在 `v0.1.0` 提交上实测确为 `0.1.0`，票面把期望值写死成 `0.1.0` 会导致每次文档提交后票 01/09 的版本断言变红。
- **要求**：票 01 该验收项改为「tag `v0.1.0` 所在提交上为 `0.1.0`；tag 后提交为 `0.1.1-alpha.0.N`」；票 09 `/api/version` 的断言需接受 MinVer 预发布版本格式，不要写死 `0.1.0`。

## 6. 通过项与偏差汇总

- 票 01：骨架、CPM、MinVer、tag、`.slnx`、5 个 smoke test 全部通过；仅验收命令需按 F3/§2 修正。
- 票 04：SharedKernel 实现与 36 条单测通过；架构规则本身有效（对照控制证明）；但反向用例与规则④发现机制两处守护需按 F1/F2 补强，**建议返工后再审**。
- 已确认的偏差（开发已声明，需老何回填票面）：xUnit v3 走 MTP（`xunit.v3.mtp-v2` + `global.json test.runner`）；`NuGet.config` 固定源映射；`ITenantContext.TenantId` 为 `long`。
- 未做（不属本报告范围）：前端、AppHost、票 05–11。

## 7. 返工复测（提交 `c95e72e`，只动 4 个测试文件，生产代码零改动）

### 7.1 基线

- `git show --stat c95e72e`：仅 `ArchRules.cs`（新增）、`DependencyRulesTests.cs`、`ReverseCaseTests.cs`、`AggregateRootTests.cs`，无 `src/` 改动。
- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 按票面 `dotnet run` 逐工程：**31 + 10 + 1 + 1 + 1 = 44 条，Failed 0，exit 0**（与返工自述一致）。

### 7.2 变异复测（临时副本，工作区未动）

| 变异 | 期望 | 实际 | 结论 |
|---|---|---|---|
| A. `ArchRules.DomainDependencyViolations` 的 `.Domain` → `.DomainMUT` | 反向用例红 | `ReverseCaseTests…[FAIL]`，Total 10 / Failed 1 / exit 1 | F1 已修 |
| B. 源树加真实违规模块 `Pzd.Platform -> Pzd.Identity`（ArchitectureTests 不引用、模块也不构建） | 规则④红 | `Modules_in_this_repository…[FAIL] 违规的模块引用：Pzd.Platform -> Pzd.Identity`，exit 1 | F2 已修（源树发现生效，不依赖编译产物/测试引用） |
| B2. 同一模块改成引用 `Pzd.Identity.Contracts`（合法，架构 §3.2） | 绿 | Total 10 / Failed 0，exit 0；`dotnet build Pzd.slnx` 也成功 | F5 已修（不再假红） |
| C. 删 `Entity.Equals` 的 `GetType` 判断 | 跨类型同 Id 用例红 | `Entities_of_different_types_with_the_same_id_are_not_equal[FAIL]`，exit 1 | F4 已修 |
| D. 允许集去掉 `*.Contracts` 后缀 | 正向用例红 | `Modules_may_reference_other_modules_contracts[FAIL]`，exit 1 | F5 正/负用例有效 |

### 7.3 残余（low，不阻塞验收，建议并下一张小票）

- **规则④的根目录定位没有测试守护**：把 `ArchRules.FindRepositoryRoot()` 改成指向不存在的目录后，10 条架构测试**全绿**（`DiscoverModules` 返回空集，规则再次静默空跑）。这与 F2 是同一类风险，只是发生在「找到仓库根」这一步。
  - 最小修法：生产用例里断言 `Directory.Exists(Path.Combine(RepositoryRoot, "src"))`，或给 `FindRepositoryRoot` 加一个临时目录夹具（放入 `Pzd.slnx` 后从子目录向上查找）。
- **模块工程命名约定**：`DiscoverModules` 只认 `src/<名>/<名>.csproj`；目录名与 csproj 名不一致的模块会被静默跳过（low，属约定约束）。
- **F8（`Error.Metadata` 只读快照）**仍无测试、需改生产代码，按返工说明由老何并票处理，不属本报告测试资产范围。

### 7.4 复测结论

票 04 的 F1/F2/F5/F4 已由变异验证证明修复有效，44 条全绿，生产代码未动；**测试侧通过**，建议审计据此改判「通过」。

### 7.5 追加复测：`49ee09a`（F7 + 仓库根守护，2026-10-27）

- 基线：`dotnet build -warnaserror --no-incremental` → 0 警告 0 错误；31 + 13 + 1 + 1 + 1 = **47 条全绿**，五个工程 exit 0。
- 仓库根变异（`FindRepositoryRoot` 指向「有 `Pzd.slnx`、无 `src`」的临时目录）→ **2 条 FAIL**：`Repository_root_points_at_a_real_source_tree`（`没有 src`）与 `Modules_in_this_repository…`（`InvalidOperationException: No 'src' directory … module discovery would silently pass`），exit 1。§7.3 的 low 残余**已修复**。
- F7 两条新断言（`*.Domain` 禁依赖 `Pzd.Infrastructure` / `Pzd.Api`）能执行且绿，但当前**不可证伪**，仅作未来防护：
  - `SharedKernel → Infrastructure` 是项目引用环，`dotnet build` 先报 `error MSB4006: 目标依赖项关系图中存在循环依赖项`，走不到测试；
  - `Pzd.Infrastructure` 无任何源文件、`Pzd.Api` 仅内部 `Program`，测试程序集里也构造不出可引用它们的违规模块类型，无法按 F1 的方式加反向用例。
  - 建议：等 Infrastructure 出现公共类型（票 08 起）后补一条反向用例；或将「禁 Infrastructure/Api」改为 csproj 引用图检查（与规则④同源）。不影响本轮验收。
- 结论不变：**票 04 测试侧通过**。

---

## 8. 附录：票 10 环境约定核对（2026-10-27，不改变 §7 结论）

核对对象：[`docs/notes/integration-test-environment.md`](../notes/integration-test-environment.md)。

- **`ServerTimeZone=UTC` 不可用，且会硬失败**：本仓库 `SqlSugarCore 5.1.4.221` 的 MySQL 驱动是 `MySqlConnector 2.6.2`（nuspec 与 `project.assets.json` 双向确认，无 `MySql.Data`）。实测
  `new MySqlConnectionStringBuilder("Server=127.0.0.1;ServerTimeZone=UTC")` → `ArgumentException: Option 'servertimezone' not supported.`；`ConnectionTimeZone` 同样被拒；未知关键字一律抛异常、不会静默忽略。票 10 的 fixture 若照抄该连接串，会在**连接串解析阶段直接红**（不是动态 Skip）。
- **可用的驱动参数是 `DateTimeKind=Utc`**：接受，且只规定读回 `DateTime` 的 `Kind`，不改变服务器会话时区、也不改变存储值。
- **建议的时区断言方式**（不依赖驱动参数）：fixture 连接后执行 `SET time_zone = '+00:00'`（或用 `CONVERT_TZ` 显式转换），并断言 `@@session.time_zone`；禁止「写 `DateTime.UtcNow` → 读回相等」这种吃服务器默认时区的断言。
- 文档第 20 行「SqlSugar 对应 `IsAutoCloseConnection` 同级的时区配置」不存在（`ConnectionConfig` 无时区项），建议删除该句。
- 其余事实（8.0.46、建库权限、`utf8mb4_0900_ai_ci`、Memurai 8.2.10、`PZD_REDIS_TEST_DB=15`、`lower_case_table_names=1`）本轮未独立连实例复验（环境归 Chief），按其实测值采信；票 10 落地时由集成测试实跑覆盖。

---
