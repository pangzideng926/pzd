# 票 10 代码审计报告（跨租户隔离集成测试）

- **审计对象**：`95cfc5d`（`test(integration): 票 10 补 QueryFilter 裸 SQL 对照，脏行必须可见`）
- **审计人**：老登
- **审计日期**：2026-10-27
- **结论**：**通过**（锚 **`204f93e`**；`014f2c8` 修完必修项后，`204f93e` 再按审计口径收敛了残留用例）
- **最终基线**：`204f93e` 上 **11/11**（原 10 条 + Skip 措辞锁），Failed 0、Skipped 0、退出码 0
- **通过的部分**：主路径真跑、四条变异全部打红、Skip 三态全部符合票面、幂等与残留、凭据红线

---

## 1. 基线与被测范围

被测路径 `tests/Pzd.IntegrationTests/**` 在 `95cfc5d..HEAD` 之间**无差异**（`git diff --stat 95cfc5d..HEAD -- tests/Pzd.IntegrationTests` 为空），因此 `95cfc5d` 与当时 HEAD 的测试代码等价。

**但它与 HEAD 的 `src/**` 不等价**：`95cfc5d..HEAD` 含票 08 的 `src/Pzd.Api/Program.cs`（+39/−10）与 `src/Pzd.Infrastructure/{Audit,Observability,Security}/**`。锋哥在群里说的"代码树无差异"只对 `tests/Pzd.IntegrationTests` 成立，对 `src/**` 不成立。因此本次审计**全部在 `95cfc5d` 的干净 worktree 上执行**，未在含 08 改动的 HEAD 上取结论。

> 给票 11 的提醒：`verify.ps1` 会同时跑单测与集成测试，那时两者都在同一 HEAD 上，08 的 `Program.cs` 改动已进入被测路径。届时若集成测试出现与租户无关的失败，先查 08 的 `Program.cs` 注册顺序（健康检查 44-47 / 服务注册 62-66 之间被旺总插入 Quartz），不要回退到票 10 找。

## 2. 环境

| 项 | 值 |
| --- | --- |
| OS | Windows |
| .NET SDK | 10.0.402（`global.json` 锁 `10.0.400` + `latestPatch`） |
| MySQL | `8.0.46`（`SELECT VERSION()` 实测，与票面期望一致） |
| 凭据 | 仓库根 `local.env`（gitignored），运行器不打印 |
| 运行器 | `dotnet run --project tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj`（工作区为 Low IL，MTP 命名管道不可用） |
| worktree | 独立干净副本，基于 `95cfc5d` 分离头 |

## 3. 主路径实测

```
xUnit.net v3 In-Process Runner v4.0.2+0c7aa4991f (64-bit .NET 10.0.13)
  Pzd.IntegrationTests  Total: 10, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 1.055s
[exit code: 0]
```

10 条构成（与锋哥报的一致）：7 条隔离/守卫 + 2 条 HTTP + 1 条冒烟。

- 隔离与守卫（`TenantIsolationTests`，7 条）：跨库可见性、QueryFilter 纵深防御 + 裸 SQL 对照、连接指向自身库、实例版本、建表幂等、库名白名单、测试库残留。
- HTTP 接缝（`TenantStatusTests`，2 条）：停用租户 403 `Tenant.Disabled`、正常租户可达。
- 冒烟（`SmokeTests`，1 条）。

**停用租户这条是真 HTTP 管道**：`WebApplicationFactory<Program>` + `UseEnvironment("Production")`，断言 `403` / `code=Tenant.Disabled` / `type=https://pzd/errors/tenant.disabled` / `status=403` / `traceId` 非空。不是只调中间件方法。

## 4. 反向验证（证明断言不是假绿）

每个变异只改一处、跑完立即还原，还原后复跑确认回到 10/10。

| # | 变异 | 期望 | 实测 | 判定 |
| --- | --- | --- | --- | --- |
| 1 | 注释掉 `QueryWidgetsAsync` 里的 `AddTableFilter` 挂载 | 过滤用例红，实测 `[10,11]` | `QueryFilter_hides_rows_that_carry_another_tenants_id` **FAIL**（`Assert.Equal() Failure`），Total 10 / Failed 1 | ✅ 只红对应条 |
| 2 | `EnsureSafeDatabaseName` 守卫改成 `if (false)` 放行任意库 | 白名单用例红 | `Database_guard_only_allows_pzd_it_prefixed_databases` **FAIL**（`Assert.Throws() Failure`），Failed 1 | ✅ 只红对应条 |
| 3 | 删掉 A 库那条 `TenantId=TenantBId` 的脏行插入 | 裸 SQL 对照红 | `QueryFilter_hides_rows_...` **FAIL**（`Assert.Equal() Failure`），Failed 1 | ✅ 证明"数据没插进去"会红 |
| 4 | `TenantDbProvider` 对所有租户都返回 `pzd_it_a`（拿错库） | 生产隔离断言红 | **3 条红**：`Tenant_a_and_tenant_b_only_see_rows_in_their_own_database`、`Tenant_connection_targets_its_own_database`、`QueryFilter_hides_rows_...` | ✅ 见 §4.1 |
| 5 | 本机额外存在 `pzd_it_zzz` 库 | —— | `This_runs_test_databases_are_present_and_no_system_schema_is_listed` **FAIL** | ⚠️ 见 F-10-A1 |

### 4.1 关于"能不能用过滤器掩盖拿错库"

票面明确要求：**生产隔离断言"不依赖测试显式过滤器，不能用过滤器掩盖拿错库"**。

代码现状：`Tenant_a_and_tenant_b_only_see_rows_in_their_own_database` 复用了 `QueryWidgetsAsync`，而该方法内部**确实挂了** `AddTableFilter`。字面上不满足"不依赖显式过滤器"。

实测结论：**断言有效性不受影响**。变异 4 让 provider 一律返回 A 库后，该条立刻变红——因为 A 库里没有 `TenantBId` 的行，`rowsB` 期望 `[3L]` 实得空，与过滤器无关。另外 `Tenant_connection_targets_its_own_database` 用 `SELECT DATABASE()` 直接读库名、完全不挂过滤器，是这条要求最干净的落实。

建议（不阻塞）：把跨库可见性那条改用不带过滤器的查询路径（或显式断言 `rowsB` 来自 `SELECT DATABASE()`），让"不依赖过滤器"在代码层面也成立，而不只是靠数据分布论证。

## 5. Skip 三态实测

票面要求：原因只能是三种之一、状态 **Skipped 而非 Passed**、退出码 **0**。

| 态 | 构造方式 | Total / Skipped | 原因原文 | 退出码 |
| --- | --- | --- | --- | --- |
| 无凭据 | 移走 `local.env`，清空 `PZD_TEST_MYSQL` / `ConnectionStrings__mysql` / `PZD_MYSQL_*` | 10 / **8** | `MySQL credentials not configured` | **0** |
| 实例不可达 | `PZD_TEST_MYSQL` 指向 `127.0.0.1:7999` | 10 / **8** | `MySQL unreachable` | **0** |
| 无建库权限 | 临时账号仅 `USAGE` 权限、无既有库 | 10 / **8** | `no CREATE DATABASE privilege` | **0** |
| 无建库权限 + 既有库 | 同上账号，`PZD_TEST_MYSQL_DB_A/B` 指向已授权的 `pzd_it_fb_a/b` | 10 / **0** | —— **真跑 10/10** | **0** |
| 对照（有凭据） | 正常 `local.env` | 10 / **0** | —— 真跑 10/10 | **0** |

- 第三态、第四态的临时账号 `pzd_it_nopriv`（`localhost` + `127.0.0.1` 两条）与临时库 `pzd_it_fb_a/b` 在脚本退出时已删除，未留残留；密码为脚本内随机生成，未打印、未落盘。
- `Skipped: 8` 而非 10，是因为 `Database_guard_only_allows_pzd_it_prefixed_databases` 是不依赖实例的 `[Fact]`，另外 8 条 `IntegrationFact` 被跳。这与"状态是 Skipped 而非 Passed"的要求一致。

## 6. 幂等、残留与凭据红线

- **幂等**：同一 worktree 连续跑 6 次以上（含变异前后各一次还原复跑），每次 10/10，无一次因残留状态失败。
- **残留**：每次跑完后查 `SELECT SCHEMA_NAME … LIKE 'pzd_it_%'` 返回 **0 行**，`pzd_it_a` / `pzd_it_b` 均已被 `DisposeAsync` 删除。
- **库名守卫生效范围**：`EnsureSafeDatabaseName` 校验 `pzd_it_` 前缀 + 只允许 `[A-Za-z0-9_]` + 长度上限；`pzd_master` / `mysql` / `information_schema` / `pzd_t_a` / `pzd_it_a; DROP DATABASE x` / 空串全部被拒（变异 2 反向证明这条断言真会被触发）。
- **凭据**：`grep` 测试代码无硬编码密码/连接串；`MySqlTestSettings.ToString()` 只输出主机、用户、库名；`AdminConnectionString` 与 `ConnectionStringFor()` 的注释均标"禁止打印"；实测运行输出与报告无凭据。

## 7. 发现清单

### F-10-A1（medium，**必修** → **已闭环 `014f2c8`**）`This_runs_test_databases_are_present_and_no_system_schema_is_listed`（原名 `Only_this_runs_pzd_it_databases_are_left_on_the_server`，`204f93e` 改名）断言把"本机只有两个测试库"写死

- **文件与行**：`tests/Pzd.IntegrationTests/TenantIsolationTests.cs:136-146`
- **问题**：`Assert.Equal(2, databases.Count)` 要求 `pzd_it_%` 匹配到的库恰好是 2 个。
- **为什么错**：这条断言的实际意图是"只操作 `pzd_it_` 前缀库 + 不残留"，但 `Equal(2, …)` 把"别人的库"也算成自己的失败。
- **两处实测证据**：
  1. 本机建一个额外的 `pzd_it_zzz` → 该条 **FAIL**（Total 10 / Failed 1）。
  2. **更严重：直接命中票面第 4 条验收自身的场景。**按票面要求用既有库模式跑（`PZD_TEST_MYSQL_DB_A/B=pzd_it_fb_a/b`），只要机器上同时还存在默认名的 `pzd_it_a` / `pzd_it_b`（上一次中断的残留、或同事刚跑过），断言当场崩：
     ```
     Assert.Equal() Failure: Values differ
     Expected: 2
     Actual:   4
     [exit code: 1]
     ```
     也就是说，**票面明确要求的"无建库权限 → 改用既有库仍真跑"这条正路径，会因为一条与被测行为无关的计数断言而整轮判失败**。这不是极端环境，而是该验收分支的常见状态（默认名库是 `CreateAndDrop` 模式的产物，前一次运行中断就会留下）。
  3. 并行风险：`[Collection(…, DisableParallelization = true)]` 只保证**进程内**串行，跨进程不保护；本项目多名成员并行，李总与锋哥同时跑会互建互删同名库。票 11 的 `verify.ps1` 把它列为必过段后，这类误红会直接卡住一键验收，而排查方向会被引到"是不是有人建库"而不是真缺陷。
- **具体修改建议**（三选一，推荐第 1 条）：
  1. 把"不残留"和"只碰测试库"拆成两条断言：`Assert.All(databases, db => Assert.StartsWith("pzd_it_", db))` + `Assert.Contains(A/B)` + `Assert.DoesNotContain("pzd_master")`，删掉 `Equal(2, …)`；"本次运行不留残库"改为在 `DisposeAsync` 之后单独断言 `DatabaseExistsAsync(A/B) == false`（这是真正要证明的行为）。
  2. 库名加每次运行唯一后缀（如 `pzd_it_{runId}_a/b`），断言只针对本次两个库名，不再对全表计数。
  3. 保留计数但改为"扣除基线快照"：`InitializeAsync` 时记下已有的 `pzd_it_%` 集合，断言最终集合等于基线（不残留），而不是恒定 2。

### F-10-A2（low，建议同批）权限探测把连接类异常误归成"无建库权限"

- **文件与行**：`tests/Pzd.IntegrationTests/MySqlAvailability.cs:98-102`
- **问题**：`CanCreateAndDropDatabase` 的内层 `catch` 同时捕获 `MySqlException or InvalidOperationException or TimeoutException`，一律 `return false`，随后 `ProbeCore` 落到 `no CREATE DATABASE privilege`（:67）。
- **为什么错**：`TimeoutException` / `InvalidOperationException` 在 `IsConnectivityFailure`（:145-148）里被明确定义为**连通性故障**，应报 `MySQL unreachable`；在内层却被当成权限不足。实例连得上但建库超时（锁表、磁盘满、`max_connections` 打满）时会给出误导性的 Skip 原因，把排查引向"账号权限"。
- **修改建议**：内层 `catch` 只捕 `MySqlException` 且按 `ErrorCode` 判定权限类错误（`1044` / `1045` / `1142`）才 `return false`；其余异常不捕获，让它冒泡到 `ProbeCore` 的 `when (IsConnectivityFailure(exception))` 得到 `MySQL unreachable`。

### F-10-A3（low，建议同批）库名守卫用例只断言异常类型，不断言被拒的原因

- **文件与行**：`tests/Pzd.IntegrationTests/TenantIsolationTests.cs:122-133`
- **问题**：`Assert.Throws<InvalidOperationException>` 只证明"抛了这个类型"。
- **为什么错**：`EnsureSafeDatabaseName` 未来若因别的原因抛 `InvalidOperationException`（例如依赖注入失败、空引用被包装），这条仍绿，但它本意是"因为库名不安全被拒"。断言强度不足。
- **修改建议**：接住返回值并断言消息含关键片段，例如
  `var ex = Assert.Throws<InvalidOperationException>(() => TestSchema.EnsureSafeDatabaseName(forbidden)); Assert.Contains("pzd_it_", ex.Message);`

### F-10-A4（说明项，不是缺陷）显式过滤器的口径必须随结论一起交付

- `QueryWidgetsAsync`（`TenantIsolationTests.cs:154-167`）在**测试查询所在上下文**显式挂 `AddTableFilter`，并在注释里写明这是票面认可的偏差。票面原文要求"此结果只证明显式挂载的防御语义，不证明 provider 自动挂载或跨 async 上下文传播"。
- **因此**：本报告、README、票 11 的任何汇总**都不得**写成"租户过滤已在生产链路自动生效"。生产隔离当前只由"每租户独立库"保证；自动挂载的可靠接缝留步骤 3 仓储层。这条不阻塞，但禁止扩写成已实现保证。

### F-10-A5（low，建议同批）三种 Skip 原因字面量分散在多处

- **文件与行**：`MySqlAvailability.cs:43`（`MySQL credentials not configured`）、`:59` 与 `MySqlFixture.cs:86`（`MySQL unreachable`）、`MySqlAvailability.cs:67`（`no CREATE DATABASE privilege`）；`IntegrationFactAttribute.cs:7-8` 的 XML 注释里又列了一遍。
- **为什么错**：票面的验收是"Skip 原因必须是这三种之一"，而这个约束靠**四处手写字面量**维持一致。改一处漏一处，验收就在无人察觉的情况下失真（注释尤其容易漏）。
- **修改建议**：收敛成 `MySqlAvailability` 上的三个 `public const string`，`IntegrationFactAttribute` 的注释改为 `<see cref="MySqlAvailability.PrivilegeSkipReason"/>` 引用；或用一条单测断言"Skip 原因 ∈ 常量集合"。

## 8. 票面验收对照

| 票面验收项 | 结论 | 证据 |
| --- | --- | --- |
| 本机凭据可用：真跑全绿，贴 SUMMARY 与 `SELECT VERSION()` | ✅ | §3、`8.0.46` |
| 生产隔离验收：经真实 `ITenantDbProvider`，A/B 只访问自己的库，不能被过滤器掩盖拿错库 | ✅（建议见 §4.1） | §4 变异 4 → 3 条红；`SELECT DATABASE()` 直读库名 |
| QueryFilter 防御语义独立用例 + 裸 SQL 对照必须看见脏行 | ✅ | §4 变异 1、3 |
| 停用租户 403 `Tenant.Disabled` 经 HTTP 接缝 | ✅ | §3 |
| 无建库权限改用既有库仍真跑；都不可用时 Skip 原因为 `no CREATE DATABASE privilege` | ✅ 机制成立 / ⚠️ 计数断言会误红 | §5 第三、四态；F-10-A1 证据 2 |
| 无凭据 / 不可达：动态 Skip、原因明确、退出码 0、状态 Skipped | ✅ | §5 第一、二态 |
| 幂等与安全：重复运行不残留、只操作 `pzd_it_` 前缀 | ✅（计数断言见 F-10-A1） | §6 |
| 密码不落日志/报告/异常消息 | ✅ | §6 |
| 本机可用时是 `verify.ps1` 必过段 | ⏳ 票 11 | —— |

## 9. 结论

**通过**（`014f2c8` 修复 → `204f93e` 收敛 → **以 `204f93e` 为准**）。票 10 主体质量高：真跑全绿、四条反向变异全部精确打红、Skip 三态与票面逐字对上、幂等与凭据红线无问题。

> 哈希口径：**结论锚 `204f93e`**（最终被测版本）。本文中出现的 `014f2c8` 一律指「修掉 F-10-A1 的那笔提交」，不是基线——`204f93e` 只是在其上删掉一条恒真断言并改名，没有引入新的被测行为。`git diff 204f93e -- tests/Pzd.IntegrationTests` 必须为空，否则本结论作废。

### 9.1 F-10-A1 闭环复验（`014f2c8` 修复、`204f93e` 收敛；在独立干净 worktree 上）

改动与建议一致：删掉 `Assert.Equal(2, …)`，改为「前缀合法 + 本次两个库在 + 不含 `pzd_master`」；「本次运行不残留」移入 `MySqlFixture.CleanupAsync`——把本次删过的库记入 `droppedDatabases`，逐个复查 `DatabaseExistsAsync`，残留即抛错。用例同时改名 `Test_databases_are_pzd_it_prefixed_and_include_this_runs_pair`。

| 场景 | 返工前 | 返工后实测 |
| --- | --- | --- |
| 基线 | 10/10 | **11/11**（新增 1 条 `[Fact]` 锁 Skip 措辞） |
| 本机存在 `pzd_it_zzz` | **FAIL** | **11/11 绿**，exit 0 |
| 既有库模式 + 默认名 `pzd_it_a/b` 并存（4 个库） | **`Expected: 2, Actual: 4`，exit 1** | **11/11 绿**，exit 0 |
| 反向：故意不删 A 库（变异） | —— | `Total: 11, Errors: 1`，**exit 1** —— 复查确实会触发，不是摆设 |

清理后 `pzd_it_%` 残留 0。

### 9.2 其余三项

F-10-A2（权限探测收窄到 `1044/1045/1142/1227`，超时冒泡报 `MySQL unreachable`）、F-10-A3（守卫断言补消息含 `pzd_it_`）、F-10-A5（三种 Skip 原因收敛为 `MySqlAvailability.SkipReasons` 常量并加断言锁措辞）均已落地，改动方向与要求一致。

### 9.3 一条交付口径（非缺陷）

**F-10-A4**：显式挂载过滤器的范围限制必须随票 10 的结论一起写进文档——只证明显式挂载的防御语义，不证明 provider 自动挂载或跨 async 传播，禁止扩写成"生产全局过滤已验证"。

### 9.4 给票 11 的两条提醒

1. `Assert.All(databases, db => Assert.StartsWith("pzd_it_", db))` 是**恒真**断言（查询本身就是 `LIKE 'pzd_it_%'`），留着无害但没有信息量；真正起作用的是 `CleanupAsync` 的残留复查。
2. 「不残留」现在只覆盖**本次运行创建的库**（`_createdDatabase*`）。既有库模式下 A/B 是别人的库，本来就不该删也不该复查——这是正确取舍，但如果票 11 想让"跑完不留库"成为硬门禁，需要在进程外另查一次快照。

### 9.5 最终锚点 `204f93e` 的复验（2026-10-27）

`204f93e` 按审计口径继续收敛：删掉 `Assert.All(… StartsWith("pzd_it_"))`（与 `ListTestDatabases` 内部 `LIKE 'pzd_it_%'` 是同一条件的复述，恒真、无信息量），用例改名为 `This_runs_test_databases_are_present_and_no_system_schema_is_listed`。

在 `204f93e` 的独立 worktree 上重跑四个场景：

| 场景 | 结果 |
| --- | --- |
| 基线 | **11/11**，exit 0 |
| 本机存在 `pzd_it_zzz` | **11/11**，exit 0 |
| 既有库模式 + 默认名 `pzd_it_a/b` 并存（4 个库） | **11/11**，exit 0 |
| 反向：故意不删 A 库 | `Errors: 1`，**exit 1** |

**结论以 `204f93e` 为准。** `014f2c8` 的实测记录保留在本节之上，用于说明返工前后的对照（返工前那个场景是 `Expected: 2, Actual: 4` / exit 1）。

**关于 `Assert.DoesNotContain("pzd_master", databases)` 的处理**：保留，与 `StartsWith` 不是一回事。`StartsWith` 复述的是 `ListTestDatabases` 内部已有的过滤条件，删掉是对的；`pzd_master` 是「绝不触碰平台库」这条安全边界的显式声明，一旦将来 `ListTestDatabases` 放宽过滤（例如改为枚举全部 schema），它立刻从恒真变成真正的守卫。安全类断言按「宁可保留显式声明」处理。
