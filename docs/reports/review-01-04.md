# 代码审计报告：票 01（工程骨架）/ 票 04（SharedKernel + 架构测试）

> 审计：老登｜日期：2026-10-27｜方法：Matt Pocock `code-review`（Standards / Spec 两轴）+ 实跑复核
> 被测：`HEAD = 1bdc680`（票 01 `e0ec679`、票 04 `50ac4ec`），tag `v0.1.0`；工作区 `E:\pzd`
> 依据：[`architecture.md`](../architecture.md) §2/§3.2/§5.2、[`foundation.md`](../specs/foundation.md) §4、[`tickets.md`](../specs/tickets.md) 票 01/04
> **结论：票 01 通过（票面版本口径需回填）；票 04 需修改（F1/F2/F5/F4 四项，只改测试资产，不动实现）。**

---

## 1. 审计方法与独立复核

读了全部生产代码与测试代码，并**不采信报告结论、自己重跑**。所有变异都在临时副本 `E:\pzd-audit`（`git clone`，已删除），`E:\pzd` 除本报告外无改动（`git status` 仅 `?? docs/reports/`）。

| 复核项 | 命令 | 实际结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 29+7+1+1+1 = **39 通过，0 失败**，exit 0 |
| F1 变异（副本） | `.Domain` → `.DomainMUT` 后跑架构测试 | **7/7 仍全绿**——生产谓词被改坏，测试无感知（复现） |
| F2 变异（副本） | 造 `Pzd.Identity`、`Pzd.Platform`（引用 Identity）入 solution，测试工程不引用 | **7/7 绿（漏检）**；给 ArchitectureTests 加 `Pzd.Platform` 引用后 → `[FAIL] 违规的模块引用：Pzd.Platform -> Pzd.Identity` |
| F4 变异（副本） | 删 `Entity.Equals` 的 `GetType() == other.GetType() &&` | **29/29 仍全绿**（复现） |
| F3 环境（工作区） | `dotnet test Pzd.slnx` | exit **127**，10 处 `UnauthorizedAccessException`（命名管道），每个工程「运行了零个测试」（复现） |
| 版本查询 | 无/有 `-t:Build` | `1.0.0` / `0.1.1-alpha.0.1`（见 F6） |

---

## 2. 票 01：通过

骨架、`.slnx` 目录结构、`Directory.Packages.props`（5 个 csproj 无 `Version=`）、MinVer + `v0.1.0`、5 个 smoke test、`.gitignore` 均符合规格；`bin/obj` 未被跟踪。**代码与环境无需返工。**

两处票面口径需要回填，归老何（见 F3、F6），不影响票 01 的交付判定。

---

## 3. 票 04：需修改

SharedKernel 的实现本身是对的（读代码 + 单测复核，`Result`/`Error`/`Entity`/`AggregateRoot`/`ValueObject` 语义正确，空值防护到位）；规则③的**规则逻辑**有效（注入违规能报）。问题全部在**测试资产**：

1. 反向用例没有守住生产规则（F1）——票 04「反向用例」验收项未达成；
2. 规则④的模块发现会静默漏检（F2）——票 04「架构测试能自动拦截」未达成；
3. 规则④的允许集不认「对方 Contracts」，会误报合法引用（F5，新发现）；
4. 实体相等语义缺跨类型同 Id 用例，变异存活（F4）。

---

## 4. 发现清单

### F1 · high · 反向用例测的是 NetArchTest，不是我们的生产规则

- **位置**：[`ReverseCaseTests.cs:14-22`](../../tests/Pzd.ArchitectureTests/ReverseCaseTests.cs#L14-L22) 对比 [`DependencyRulesTests.cs:42-54`](../../tests/Pzd.ArchitectureTests/DependencyRulesTests.cs#L42-L54)
- **问题**：反向用例用 `Types.InAssembly(typeof(Fake).Assembly).That().HaveName(nameof(Fake))…` **重新拼了一条规则**，与生产测试里的四条规则没有任何共享断言。
- **为什么错**：把生产谓词 `ResideInNamespaceEndingWith(".Domain")` 改成 `.DomainMUT`（我已在副本实测），生产规则变成对空集通过，而 7 条架构测试**全部绿**。反向用例只证明「NetArchTest 能报违规」，证明不了「我们写的规则非空跑」——票 04 的验收项字面要求后者。
- **修改建议（只改测试）**：把每条规则的检查抽成接收 `Assembly[]`（或类型集合）的共享方法，例如 `ArchRules.DomainMustNotDependOn(assemblies, dependency)`；生产测试传 `ProductAssemblies`，反向用例传 `ProductAssemblies + typeof(Fake).Assembly` 并断言**恰好 1 条**违规、且违规类型名为 `Pzd.SharedKernel.Domain.Fake`。

### F2 · high · 规则④从输出目录发现模块，未接入的模块永远漏检

- **位置**：[`DependencyRulesTests.cs:88-107`](../../tests/Pzd.ArchitectureTests/DependencyRulesTests.cs#L88-L107)（98-100 行扫描 `AppContext.BaseDirectory`）
- **问题**：`ModuleAssemblies()` 只扫描架构测试**输出目录**里的 `Pzd.*.dll`，即只覆盖「ArchitectureTests 直接/传递引用」的程序集；新增模块如果没接进测试工程，规则对空集通过。此外 `GetReferencedAssemblies()` 只反映 IL 里**实际使用过**的引用，未被使用的 `ProjectReference` 被编译器裁掉（李总实验一），所以即便模块被引用，也可能漏。
- **为什么错**：这正是本该防的场景——新建模块 A 违规引用模块 B，规则却「绿得毫无提示」。我在副本里复现：两模块入 solution、测试未引用 → 7/7 绿；给测试加 `Pzd.Platform` 引用 → 1 failed `违规的模块引用：Pzd.Platform -> Pzd.Identity`。漏检是静默的，比红更危险。
- **修改建议**：规则④不要从编译产物发现模块，改为从源树枚举 `src/Pzd.*/Pzd.*.csproj`（排除 `*.Contracts`、`Pzd.Api`、`Pzd.AppHost`），解析 csproj 的 `ProjectReference`，断言只能指向 `Pzd.SharedKernel`、`Pzd.Infrastructure`、自身、`Pzd.<任意模块>.Contracts`（禁 `Pzd.Api`、禁其他模块的非 Contracts）。这比读 DLL 更早、更全，也天然解决 F5。

### F5 · high · 规则④的允许集不认「对方 Contracts」，合法引用被误报（新）

- **位置**：[`DependencyRulesTests.cs:64-70`](../../tests/Pzd.ArchitectureTests/DependencyRulesTests.cs#L64-L70)
- **问题**：`allowed` 只有 `Pzd.SharedKernel`、`Pzd.Infrastructure`、`moduleName`、`moduleName + ".Contracts"`，**没有其他模块的 `Pzd.<X>.Contracts`**。
- **为什么错**：[`architecture.md:135`](../architecture.md#L135) 与票 04 规则④（[`tickets.md:171`](../specs/tickets.md#L171)）都写明「模块 A ──► 模块 B.Contracts（只允许引用对方 Contracts）」；当前实现会把合法引用判成违规，是**假红**，且测试名 `Modules_only_reference_their_own_contracts…` 把错误语义写进了名字。我已在副本实测：`Pzd.Platform -> Pzd.Identity.Contracts` 被报 `[FAIL] 违规的模块引用`——这条引用恰恰是架构允许的。
- **修改建议**：按 F2 的 csproj 方案重写，允许集加 `Pzd.<任意>.Contracts`；测试名改为 `Modules_only_reference_core_and_other_modules_contracts`，并补一条正向用例（合法 Contracts 引用必须绿）。

### F4 · medium · 缺「不同实体类型同 Id 不相等」用例，变异存活

- **位置**：[`Entity.cs:28`](../../src/Pzd.SharedKernel/Domain/Entity.cs#L28)；测试 [`AggregateRootTests.cs:46-55`](../../tests/Pzd.SharedKernel.UnitTests/Domain/AggregateRootTests.cs#L46-L55)
- **问题**：`Entity.Equals` 的类型判断删掉后 29 条单测仍全绿（我复现）。
- **为什么错**：`Entity` 注释承诺「避免不同聚合根同 Id 时相等」，这是该判断存在的唯一理由，却没有测试守住；当前唯一相等用例只测同类型同 Id。
- **修改建议**：补两个 `Entity<long>` 子类（如 `Order`/`Invoice`），断言 `Assert.NotEqual(new Order(1), new Invoice(1))`，并断言 `(object)order != invoice`。

### F3 · high（环境，已回填）· 工作区内 `dotnet test` 不可用

- **位置**：[`tickets.md:11-13`](../specs/tickets.md#L11-L13)、[`tickets.md:66`](../specs/tickets.md#L66)、票 11 `verify.ps1` 条款
- **复核**：工作区 `dotnet test Pzd.slnx` → exit 127、10 处 `UnauthorizedAccessException`、每个工程「运行了零个测试」；副本普通目录 39 通过 exit 0。**结论与李总一致，且 1bdc680 已把各票命令改为 `dotnet run --project`，票 11 也写明按环境选运行器。此条无需再返工**，保留在报告中作为票 11 实现的约束：`verify.ps1` 在工作区必须逐工程 `dotnet run --project`，不要 `dotnet test`。

### F6 · medium · 版本验收项在 HEAD 上不成立（新，归老何回填）

- **位置**：[`foundation.md:197`](../specs/foundation.md#L197)、[`tickets.md:67`](../specs/tickets.md#L67)
- **问题**：验收要求 `-t:Build -getProperty:Version` 输出 `0.1.0`，但该值只在 **tag 所在提交**成立。当前 HEAD `1bdc680`（`v0.1.0` 之后 1 个文档提交）实测为 `0.1.1-alpha.0.1`、`InformationalVersion = 0.1.1-alpha.0.1+1bdc680…`；不加 `-t:Build` 为 `1.0.0`。
- **为什么错**：MinVer 语义就是「最近 tag + 提交数」，tag 之后任何提交（包括本次文档回填）都会让这条验收「变红」。这不是实现缺陷，但票面写死了 `0.1.0`，后续每次照票验收都会误判失败，或诱导把正常版本号当成 bug。
- **修改建议**：口径改为「在 `v0.1.0` 所在提交上验证得 `0.1.0`；HEAD 按 MinVer 语义为 `0.1.1-alpha.0.N`，验收以 tag 为基准（`git checkout v0.1.0` 后执行）」，并保留「必须带 `-t:Build`，否则得 `1.0.0`」的说明。

### F7 · medium（建议，后续票）· 规则③覆盖面小于架构 §3.2

- **位置**：[`DependencyRulesTests.cs:42-54`](../../tests/Pzd.ArchitectureTests/DependencyRulesTests.cs#L42-L54)；[`architecture.md:136`](../architecture.md#L136)
- **问题**：规则③只禁 `SqlSugar` / `Microsoft.AspNetCore`，而 §3.2 还禁止 `Domain → Pzd.Infrastructure`、`Domain → 其他模块`、`Application → SqlSugar`。
- **为什么重要**：§3.1 说「由架构测试强制依赖方向」，但最关键的一条箭头（Domain 不碰 Infrastructure/其他模块）目前无测试。票 04 只列了 4 条规则，所以**不算本次 blocker**；建议老何决定并入票 04 返工（加一条规则成本很低）还是新开一张测试票。Application 层的规则留到出现 Application 层的第一张票。

### F8 · low · `Error.Metadata` 直接持有调用方可变字典

- **位置**：[`Error.cs:33`](../../src/Pzd.SharedKernel/Results/Error.cs#L33)
- **问题**：构造后再改传入的字典，会改变已创建的 `Error`；`EmptyMetadata` 是共享单例，理论上可被强转回 `Dictionary` 改写。`ErrorTests.Metadata_is_preserved` 只验证「值在」，没验证「快照」。
- **修改建议**：复制成只读字典（`new ReadOnlyDictionary<string, object?>(new Dictionary<...>(metadata, StringComparer.Ordinal))`），补「构造后改原字典不影响 Error」用例。

### F9 · low · `AggregateRoot.AddDomainEvent(null)` 无测试

- **位置**：[`AggregateRoot.cs:18-20`](../../src/Pzd.SharedKernel/Domain/AggregateRoot.cs#L18-L20)
- **问题**：有 `ThrowIfNull` 但没有用例；同票要求「聚合收集事件」已有测试，空值分支漏掉。
- **修改建议**：补一条 `Assert.Throws<ArgumentNullException>`（测试子类暴露 protected 方法调用即可）。

### 标准轴（Baseline smells）

未发现需要返工的气味。仅一处 judgement call：`DependencyRulesTests` 同时承担「发现模块」与「判定引用」两件事，F2/F5 修好后应拆成 `ArchRules`（共享断言）+ `DependencyRulesTests`（数据源与用例），可读性和可测性都会更好。

---

## 5. 结论与待办

**票 04：需修改。** 必须修：**F1、F2、F5、F4**（全部只改 `tests/Pzd.ArchitectureTests/` 与 `tests/Pzd.SharedKernel.UnitTests/`，不动生产代码）。修完后李总按原票复测：`dotnet run` 两个测试工程、39 条全绿、反向变异与「新增违规模块」两个实验都必须能红。

**票 01：通过。** 无需返工；F3（已回填）确认、F6 版本口径由老何回填；F7 归属由老何定。

**证据留存**：本报告表格中的命令与输出均在 2026-10-27 实跑；变异副本已删除，工作区仅新增 `docs/reports/` 下两份报告（未提交）。
