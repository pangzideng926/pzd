# 测试报告：票 10（租户隔离集成测试，真实 MySQL）

> 复测人：李总｜被测提交：`151f2ba`｜日期：2026-10-27
> 环境：干净 worktree `C:/tmp/pzd-t10` @ `151f2ba`，`dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj -warnaserror` → **0 警告 0 错误**；本机 MySQL **8.0.46**（真实实例，凭据来自 gitignored `local.env`，报告不贴连接串）。
> 运行方式（本机不用 `dotnet test`，命名管道会被沙箱拒绝）：`dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`

## 1. 主路径：10/10 全绿

```
Pzd.IntegrationTests  Total: 10, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.800s
退出码 0
```

真创建并删除 `pzd_it_a` / `pzd_it_b`，不是容器也不是 mock。

## 2. 残留核查（独立于测试自述）

用 MySQL 客户端直接查服务器：

```
SHOW DATABASES LIKE 'pzd_it_%';   → 空
SELECT VERSION();                  → 8.0.46
```

跑完一轮后 `pzd_it_%` 残留为 **0**，与测试自带的 `Only_this_runs_pzd_it_databases_are_left_on_the_server` 一致。

## 3. 动态 Skip 三态

| 场景 | 复测方式 | 结果 |
|---|---|---|
| 无凭据 | 临时移走 `local.env` | `MySQL credentials not configured`，Total 10 / **Skipped 8** / **退出码 0** |
| 实例不可达 | `local.env` 的 `PZD_MYSQL_PORT=1` | `MySQL unreachable`，Total 10 / **Skipped 8** / **退出码 0** |
| 无建库权限 | 未复测（需临时 MySQL 账号） | 开发侧自述 `no CREATE DATABASE privilege` + Skipped 8，本条**我不背书** |

两条复测过的 Skip 路径都做到了「跳过而不是失败、退出码 0」，CI/票 11 不会把无实例环境判成产品故障。

## 4. 密码泄漏

用 `local.env` 里的真实密码在整棵代码树做字面量搜索：命中文件数 **0**。

## 5. QueryFilter 用例的质量与偏差核实

用例 `QueryFilter_hides_rows_that_carry_another_tenants_id` 实测通过，断言结构是有效的：

- 双向写脏数据（A 库里插 B 的 TenantId、B 库里插 A 的 TenantId）
- 查询只拿到自己的行：`rowsA == [11]`、`rowsB == [21]`
- 再断言**物理行数仍是 2**（证明过滤发生在查询层，不是数据没插进去）

偏差核实（读代码，不采信自述）：过滤器是在 `TenantIsolationTests.cs:150` 由**测试自己**在查询接缝上挂的：

```csharp
database.QueryFilter.AddTableFilter<TenantWidget>(widget => widget.TenantId == tenantId);
```

而生产侧 `src/Pzd.Infrastructure/Tenancy/TenantDbProvider.cs` 里没有任何 `QueryFilter` 挂载。因此：

- 这个用例证明的是**过滤器语义成立**（脏行看不到），**不是**生产链路会自动挂过滤器；
- 即使生产层永远不挂，这个用例也照样会绿——它守不住「生产是否接线」这件事。

这与开发自述的偏差描述一致，属于**真实的验证边界收窄**，不是粉饰。但必须写进票面与移交清单，不能让后来人读成「QueryFilter 已在生产生效」。

## 6. 结论

**通过**，前提是把 QueryFilter 这条偏差按以下口径登记：

1. 生产隔离由「一租户一库」证明（`Tenant_connection_targets_its_own_database` 走的是真实 provider，属生产代码路径）；
2. QueryFilter 用例只证明防御语义，**不证明生产自动挂载**；
3. 「每请求 SqlSugarClient / 仓储层统一挂过滤器」列入步骤 3 移交清单，届时再补生产链路的用例。

未由我复测的项：无建库权限的 Skip 原因（需临时授权账号）——建议老登审计时补，或写进票面由开发提供命令与输出。

## 7. 复测补充：`95cfc5d`（含「裸 SQL 对照」）

> 锋哥在 `95cfc5d` 补了老何要求的对照组（`取消过滤器必须看见脏行`）。`95cfc5d` 相对 `151f2ba` 多 34 行，全部在 `tests/Pzd.IntegrationTests/**`。在干净 worktree `C:/tmp/pzd-t10b` @ `95cfc5d` 上重跑。

```
Pzd.IntegrationTests  Total: 10, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 1.387s
退出码 0
```

对照组断言（新增）：

```csharp
var rawA = await TestSchema.ReadRowsAsync(_fixture.DatabaseAConnectionString, CancellationToken);
Assert.Equal([10L, 11L], rawA.Select(row => row.Id));
Assert.Contains(rawA, row => row.Id == 10 && row.TenantId == MySqlFixture.TenantBId);
```

即：**裸 SQL 能看见带别的 TenantId 的脏行（10 / 20），经过滤器才只剩本租户行**。这正好排掉「过滤后只剩本租户」可能只是「数据没插进去」的假绿——老何要求的对照成立。

跑完再查服务器：`SHOW DATABASES LIKE 'pzd_it_%'` → **空**，零残留。

结论不变：**票 10 通过**（QueryFilter 仍按 §5 的偏差口径登记：证明过滤器语义，不证明生产自动挂载）。

## 8. 变异验证：这三条测试是不是真能红（@ `95cfc5d`）

> 锋哥给了三条「跑红」配方。我照做，每条单独在干净 worktree `C:/tmp/pzd-mut` @ `95cfc5d` 上打一个变异、跑一次全量、再 `git checkout` 还原。**目的是证明这 10 条不是「怎么写都绿」**。

基线（未变异）：`Total 10 / Failed 0 / Skipped 0`，退出码 0。

| # | 变异 | 结果 | 关键断言 |
|---|---|---|---|
| M1 | 注释掉 `TenantIsolationTests.cs:162` 的 `AddTableFilter<TenantWidget>` 挂载 | **Failed 1**，Skipped 0 | `Expected: [11]` / `Actual: [10, 11]` |
| M2 | 把 `TestSchema.cs:207` 守卫正则 `\Apzd_it_[A-Za-z0-9_]{1,57}\z` 放宽成 `\A.*\z` | **Failed 1**，Skipped 0 | `Assert.Throws: No exception was thrown`（期望 `InvalidOperationException`） |
| M3 | 删掉 `TenantIsolationTests.cs:49-51` 的脏行种子（A 库写 `Id=10` + `TenantBId`） | **Failed 1**，Skipped 0 | 裸 SQL `Expected: [10, 11]` / `Actual: [11]` |

还原后复跑基线：`Total 10 / Failed 0 / Skipped 0`，退出码 0。worktree 已删。

**一处自我更正**：M2 我第一次用 `sed` 替换，反斜杠被吃掉，正则变成 `A.*z`（连合法库名都拒），结果 `Failed 1` 但 **`Skipped 8`**——那不是「守卫被抓住」，是夹具建库失败连累其它用例。改用 PowerShell 原样替换后才得到干净的 `Failed 1 / Skipped 0`。**变异验证里 Skipped 抬头就当变异写坏了重做，不能当证据。**

结论：三条核心测试（过滤器生效、库名守卫、脏行对照）都能被对应缺陷打红，不存在「实现错了还绿」的通道。

## 9. 报告重锚 @ `204f93e`（最终基线，取代 §7/§8 的 hash 与行号）

> 锋哥指出 §7（`151f2ba`）与 §8（`95cfc5d`）都不是最终代码：中间还有 `014f2c8`（F-10-A1 等）与 `204f93e`（删恒真前缀断言、改名）。本节在 `204f93e` 上重跑基线 + 三条变异，**行号与用例名以本节为准**。

### 9.1 基线

```
Pzd.IntegrationTests  Total: 11, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 1.253s
退出码 0
```

`Total` 从 10 变 11 是因为新增了 `Skip_reasons_are_exactly_the_three_the_ticket_allows`（锁 Skip 措辞）。跑完 `SHOW DATABASES LIKE 'pzd_it_%'` → **零残留**。

### 9.2 三条变异（新 hash 上重打，全部只红对应那一条）

| 变异 | 新位置 | 结果 | 关键断言 |
|---|---|---|---|
| 去掉过滤器挂载 | `TenantIsolationTests.cs:180` | **Failed 1**，Skipped 0 | `Expected [11]` / `Actual [10, 11]` |
| 库名守卫正则放宽为 `\A.*\z` | `TestSchema.cs:207` | **Failed 1**，Skipped 0 | `Assert.Throws: No exception was thrown` |
| 删掉 A 库脏行种子 | `TenantIsolationTests.cs:49-51` | **Failed 1**，Skipped 0 | 裸 SQL `Expected [10, 11]` / `Actual [11]` |

还原后复跑：`Total 11 / Failed 0 / Skipped 0`，退出码 0。worktree `C:/tmp/pzd-t10c` 已删。

### 9.3 与旧节的对应

- §7 的 `151f2ba`、§8 的 `95cfc5d` 仍然有效**但只代表那两笔代码**；`Total 10`、`:150`、`Only_this_runs_...` 这些标识已被 `204f93e` 取代（现在是 `:180`、`This_runs_test_databases_are_present_and_no_system_schema_is_listed`）。
- 结论不变：**票 10 通过**。QueryFilter 仍按偏差口径记录——该用例只证过滤器语义，生产隔离由「一租户一库」保证。
