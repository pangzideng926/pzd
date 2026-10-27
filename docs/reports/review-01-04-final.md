# 复审计终稿：票 01 / 票 04（返工后）

> 审计：老登｜日期：2026-10-27｜方法：Matt Pocock `code-review`（Standards / Spec 两轴）+ 变异复核
> 被测：代码提交 `49ee09a`（HEAD `0fb2e36`，其前 `c95e72e` 返工、`b5e620d` 票面回填）；工作区 `E:\pzd`
> 依据：[`architecture.md`](../architecture.md) §2/§3.2、[`foundation.md`](../specs/foundation.md) §4、[`tickets.md`](../specs/tickets.md) 票 01/04（当前票面含 F6/F7 回填）
> **结论：票 01 通过；票 04 通过。** F1/F2/F5/F4/F7 全部独立验证；李总 §7 的根定位残余项已由 `49ee09a` 修复并实测有效。附 2 项 low 交接项 + 1 项票面措辞建议，均不阻塞。

---

## 1. 基线与独立复核（我实跑）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 31+13+1+1+1 = **47 通过，0 失败**，exit 0 |
| 工作区 | `git status --porcelain` | 仅 `?? docs/reports/`（本轮报告，未提交） |

变异复核全部在临时副本（`git clone` → `E:\pzd-audit3`，已删除）执行，工作区未改。

| 变异 | 期望 | 实测 |
|---|---|---|
| A · F1：`.ResideInNamespaceEndingWith(".Domain")` → `.DomainMUT` | 反向用例红 | ✅ `ReverseCaseTests` FAIL（13 中 1 红） |
| B · F2：源树造 `Pzd.Platform -> Pzd.Identity`，不入 solution、不构建 | 规则④红 | ✅ `违规的模块引用：Pzd.Platform -> Pzd.Identity` |
| B2 · F5 对照：`Pzd.Platform -> Pzd.Identity.Contracts`（§3.2 合法） | 全绿 | ✅ 13/13 绿 |
| C · F4：删 `Entity.Equals` 的 `GetType()` 判断 | 跨类型用例红 | ✅ `Entities_of_different_types_with_the_same_id_are_not_equal` FAIL |
| D · F5：关闭 `.Contracts` 允许 | 正向用例红 | ✅ `Modules_may_reference_other_modules_contracts` FAIL |
| E · 根定位：`FindRepositoryRoot` 指向无 `src` 的目录 | 红（李总 §7 残余项） | ✅ 2 条 FAIL：`Repository_root_points_at_a_real_source_tree` + 模块规则（`DiscoverModules` 抛异常） |
| F · 发现机制：扫描模式改坏 | 夹具用例红 | ✅ `Module_discovery_reads_the_source_tree…` FAIL |
| G · 允许集全放行 | 单测红 | ✅ `Modules_must_not_reference_other_modules_directly` FAIL |

结论：四个主变异（F1/F2/F5/F4）与根定位守护都是**真修复**，不是把测试改绿。

---

## 2. 票 04 验收逐项（按当前票面）

| 验收项 | 结论 | 证据 |
|---|---|---|
| Result/Error/ValueObject/AggregateRoot 语义 + 错误码格式 | 通过 | 31 条 SharedKernel 单测 |
| F4：不同实体类型同 Id 不相等 | 通过 | `AggregateRootTests.cs:57-68`；变异 C 实测 |
| `ITenantContext.TenantId` 为 `long`；`IClock`/`ICurrentUser` 就位 | 通过 | 接口文件核对 |
| 架构规则 4 条（F1 共享断言） | 通过 | 规则定义集中在 `ArchRules`，生产与反向用例共用 `DomainDependencyViolations` |
| 规则③ 4 个禁止项（F7 已补 `Pzd.Infrastructure`/`Pzd.Api`） | 通过 | [`DependencyRulesTests.cs:24-36`](../../tests/Pzd.ArchitectureTests/DependencyRulesTests.cs#L24-L36)，13 条架构测试绿 |
| F2：从 `src/Pzd.*/*.csproj` 的 `ProjectReference` 发现模块 | 通过 | 变异 B（未接入、未构建仍检出） |
| F5：允许其他模块 `*.Contracts` 且不误报 | 通过 | 变异 B2/D + `Modules_may_reference_other_modules_contracts` |
| 反向用例（Fake/SqlSugar，断言恰好 1 条违规） | 通过 | 变异 A |
| 两个测试工程退出码 0、贴 `TEST EXECUTION SUMMARY` | 通过 | 47 绿 |

---

## 3. 残余 low 项（不阻塞，必须进交接记录/后续票）

1. **F8 尚未落票**：`Error.Metadata` 仍直接持有调用方传入的字典（[`Error.cs:33`](../../src/Pzd.SharedKernel/Results/Error.cs#L33)），构造后改原字典会改变 `Error`。修法是复制只读快照 + 一条"构造后改原字典不影响 Error"用例。`tickets.md` 目前没有对应票，请老何补票，别只停在聊天约定。
2. **规则③的检查范围仍是硬编码 3 个底座程序集**（`ArchRules.ProductAssemblies`，[`ArchRules.cs:28-29`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L28-L29)）。未来模块的 Domain 程序集不在其中，F7 对真实模块会是空转。模块票（05+）实现时需把模块程序集纳入检查（或复用 `DiscoverModules` 的结果），否则这条规则只在"只有底座"时绿。
3. **Contracts 工程自身不受规则④约束**：`DiscoverModules` 排除 `*.Contracts`（[`ArchRules.cs:79-80`](../../tests/Pzd.ArchitectureTests/ArchRules.cs#L79-L80)）。我实测 `Pzd.Identity.Contracts -> Pzd.Identity` 全绿。不在本轮票面，建议后续补"Contracts 只能引用 SharedKernel / 其他 Contracts"。
4. **F7 的两条 `InlineData` 当前不可证伪（与李总 §7.5 结论一致）**：直接删掉后架构测试 12 条仍全绿；而且现在也构造不出违规反例——`SharedKernel → Infrastructure` 会触发 `MSB4006` 循环依赖，`Pzd.Infrastructure` 无任何源文件、`Pzd.Api` 只有 internal `Program`，测试程序集引用不到可违规的公共类型。所以这两条是面向未来的防护。补测条件：**票 05** 给 `Pzd.Infrastructure` 加公共类型后，在测试程序集的 `Pzd.SharedKernel.Domain` 命名空间下加一个引用该公共类型的 `FakeInfrastructureDependency`，并入反向用例、断言恰好 2 条违规；`Pzd.Api` 的公共类型（**票 06** 的 `ResultHttpExtensions` 等）到位后同理。不阻塞本轮。

标准轴（smell baseline）无返工项。小瑕疵：`49ee09a` 把老何的 `.gitignore`（`local.env`）改动混进了测试返工提交，无害，但后续提交尽量单一主题。

---

## 4. 票面措辞建议（low，老何）

[`tickets.md:158`](../specs/tickets.md#L158) 与 [`tickets.md:176`](../specs/tickets.md#L176) 写「四条规则抽成共享断言方法，生产测试与反向用例跑同一方法并断言恰好 1 条违规」，而实际只有**规则③**有反向用例：① ② 是对真实程序集的直接断言（无法构造违规程序集），④ 有单元级反例（`Modules_must_not_reference_other_modules_directly`），`tickets.md:179` 本身也只要求规则③反例。建议把 158/176 改成「规则③反向用例 + 规则④单元级反例」，避免下一轮验收各说各话。

---

## 5. 最终结论

- **票 01：通过。** 骨架/CPM/MinVer/tag/slnx/smoke 均达成；F3 已回填（工作区用 `dotnet run --project`）、F6 版本口径已按 tag 基准改写并经我复核认可，无需返工。
- **票 04：通过。** 当前 `0fb2e36` 上 build 0/0、47 条全绿；F1/F2/F5/F4/F7 与根定位守护均经独立变异验证。可交老卫写文档/README/交接记录；第 3 节 1–3 条写入交接记录（F8 必须落票）。
