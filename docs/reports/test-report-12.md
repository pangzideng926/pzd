# 测试记录：票 12（F8，Error.Metadata 只读快照）

> 测试：李总｜日期：2026-10-27｜票面：[`docs/specs/tickets.md`](../specs/tickets.md) 票 12
> 被测提交：`1463e9e`（只改 `src/Pzd.SharedKernel/Results/Error.cs` + 新增 `tests/Pzd.SharedKernel.UnitTests/Results/ErrorMetadataTests.cs`）
> 结论：**通过**（51 条全绿，四条验收各有独立失败守护，无阻塞项）

## 1. 基线

- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 逐工程 `dotnet run`：35 + 13 + 1 + 1 + 1 = **51 条，Failed 0，exit 0**。
- `git show --stat 1463e9e`：仅 2 个文件，未动架构测试与其它工程。

## 2. 验收项 → 证据

| 票 12 验收项 | 用例 | 结果 |
|---|---|---|
| 构造后改传入字典（增/改/删键）不影响 `Metadata` | `Mutating_the_source_dictionary_after_construction_does_not_affect_metadata` | 通过 |
| `Metadata` 只读，强转写入抛 `NotSupportedException` | `Metadata_cannot_be_mutated_through_a_dictionary_cast`（改 / 增 / 删三路） | 通过 |
| 键比较为 `StringComparer.Ordinal` | `Metadata_keys_are_compared_ordinally`（源字典 `OrdinalIgnoreCase`，快照后 `UserName` 查不到） | 通过 |
| `EmptyMetadata` 不可改写 + 单例、空元数据不重复分配 | `Empty_metadata_is_a_shared_read_only_instance`（`Assert.Same` + 强转写入抛异常） | 通过 |
| 不改 `Equals` / `Code` / `ErrorType` / 错误码校验 | 既有 35 条 SharedKernel 用例全绿（含原 Error/Result 用例，文件未改） | 通过 |

## 3. 变异验证（临时副本，工作区未动）

| 变异 | 期望 | 实际 |
|---|---|---|
| 完全回退：`Metadata = metadata ?? EmptyMetadata`（别名 + 可变单例） | 新增 4 条全红 | **4 failed**（别名、强转写入、Ordinal、单例各一条），exit 1 |
| 半变异：复制但返回可变 `Dictionary`（不包 `ReadOnlyDictionary`，其余保持新实现） | 只红只读用例 | **1 failed**（`Metadata_cannot_be_mutated_through_a_dictionary_cast`），exit 1 |

结论：快照隔离、只读写入、Ordinal 键、空单例四项各有独立守护，不是「一条断言顺带全绿」。

## 4. 备注

- 票 12 为测试先行交付；提交内测试与实现同批入库，红→绿顺序无法从历史直接核对，上表变异证明用例对旧实现确实会失败（等价证据）。
- 票 12 不影响票 01/04 的测试结论；`Error.Equals`/`Code` 语义未变。
