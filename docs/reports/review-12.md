# 审计报告：票 12（F8 · `Error.Metadata` 只读快照）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec）+ 变异复核
> 被测：`1463e9e`（`fix(sharedkernel): Error.Metadata 改为构造时只读快照`），前置 `8e1a1a3`（票面回填 + 新增票 12）
> 依据：[`tickets.md`](../specs/tickets.md) 票 12、[`review-01-04-final.md`](review-01-04-final.md) §3 第 1 条（F8）
> **结论：票 12 通过。** 四项验收全部达成且各有独立守护；`Error` 的相等/错误码语义未变；未越界改架构测试或其它工程。

---

## 1. 基线（我实跑）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 35+13+1+1+1 = **51 通过，0 失败**，exit 0 |
| 改动范围 | `git show --stat 1463e9e` | 只动 `Error.cs` + 新增 `ErrorMetadataTests.cs`，符合票面「只改 SharedKernel 这一处实现 + 用例」 |

---

## 2. 票 12 验收逐项

| 验收项 | 结论 | 证据 |
|---|---|---|
| 构造后改传入字典（增/改/删）不影响 `Error.Metadata` | 通过 | [`ErrorMetadataTests.cs:12-31`](../../tests/Pzd.SharedKernel.UnitTests/Results/ErrorMetadataTests.cs#L12-L31)，增/改/删三路断言 |
| 只读快照，强转写入抛 `NotSupportedException` | 通过 | 同文件 `:34-50`，indexer/`Add`/`Remove` 三路；实现为 `ReadOnlyDictionary` |
| 键比较为 `StringComparer.Ordinal` | 通过 | `:53-65`（源字典 `OrdinalIgnoreCase`，快照后 `UserName`/`USERNAME` 查不到）；实现 [`Error.cs:69`](../../src/Pzd.SharedKernel/Results/Error.cs#L69) |
| `EmptyMetadata` 不可改写且保持单例 | 通过 | `:68-81`（`Assert.Same` + 强转 `Add` 抛异常）；实现 [`Error.cs:17-18`](../../src/Pzd.SharedKernel/Results/Error.cs#L17-L18)、`:64-67` |
| 现有 SharedKernel 单测全绿 + 新增用例 | 通过 | 31 → 35 全绿 |
| 不改变 `Equals` / `Code` / `ErrorType` / 错误码校验 | 通过 | `Error.cs` diff 只加 `CreateSnapshot` 与 `using`；15 条既有 Error 测试绿 |
| 不改架构测试与其它工程 | 通过 | commit 只含 2 个文件 |

---

## 3. 变异复核（副本执行，跑完已删）

| 变异 | 期望 | 实测 |
|---|---|---|
| M1 完全回退：`Metadata = metadata ?? EmptyMetadata` + 可变单例 | 4 条新用例全红 | ✅ 4 failed（快照/强转/Ordinal/单例各一） |
| M2 只复制、不包 `ReadOnlyDictionary` | 只读用例红 | ✅ 1 failed（`Metadata_cannot_be_mutated_through_a_dictionary_cast`） |
| M3 快照改用 `OrdinalIgnoreCase` | Ordinal 用例红 | ✅ 1 failed |
| M4 空元数据不再复用单例 | 单例用例红 | ✅ 1 failed |

结论：快照隔离、只读、Ordinal、空单例是**四项独立守护**，不是一条断言顺带全绿；`李总` 报的 M2 半变异结论一致。

---

## 4. 观察（low，不阻塞）

1. **浅快照边界**：只复制字典结构，值本身仍是引用。若调用方把可变对象（`List<T>`、字典）放进 `Metadata`，构造后改那个对象，`Error.Metadata` 仍会看到变化。`Error.cs:58` 的注释写「调用方之后改动原字典不会影响已创建的 Error」，严格说只对字典结构成立。建议在注释/票面补一句「仅结构快照，metadata 的值应使用不可变类型」，不建议为 `object?` 做深拷贝。
2. **只读覆盖可再补一路**：测试覆盖 `IDictionary` 的 indexer/`Add`/`Remove`，未覆盖 `Clear()` 与 `ICollection<KeyValuePair>.Add`；`ReadOnlyDictionary` 对这两条同样抛 `NotSupportedException`，补不补都不影响正确性。
3. **实现选择合理**：`ReadOnlyDictionary` + 私有副本在 .NET 10 上是稳定做法；换 `FrozenDictionary` 也无妨，测试语义不变。

---

## 5. 结论

**票 12 通过**，F8 关闭。交接记录 [`handover-01-04.md`](handover-01-04.md) 需同步：代码基线改 `1463e9e`、测试数改 51、删掉 §2.1（F8 已修）、§2.5 的 158/176 措辞与状态回填已由 `8e1a1a3` 完成。`docs/reports/test-report-12.md` 与李总在途的 `test-report-01-04.md` 改动一并入库。
