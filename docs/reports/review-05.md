# 审计报告：票 05（多租户内核 + 单测）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec）+ 变异复核
> 被测：`057f64a`（`feat(tenancy)`，HEAD `78b83b3`，其后只有文档/注释提交）；工作区 `E:\pzd`
> 依据：[`architecture.md`](../architecture.md) §3.2/§6.2、[`foundation.md`](../specs/foundation.md) §4.3/§6、[`tickets.md`](../specs/tickets.md) 票 05（`4ff0f62` 回填版）
> **结论：票 05 需修改。** 票面 10 条验收项经我实跑全部通过，但发现 **1 条 high（停用/过期门禁的配置 fail-open）+ 3 条 medium（ChangeDatabase 漏检模块、显式租户重载不校验状态、连接缓存数据竞态）**，均须在关闭前修掉；另有 1 条跨票 medium（`/health`、`/api/version` 会被租户中间件拦）归老何改票，2 条 low 随手修。
> **终审（2026-10-27，返工 `e593225`）：以上必修项全部复核通过，票 05 实现审计通过。** 关票剩余动作（票面回填、报告入库、F-05-4/F-05-8 落票）见 §6。

---

## 1. 基线（我实跑）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程 | `dotnet run --project tests/<工程>/…` 逐个 | 35+15+30+9+1 = **90 通过，0 失败**，exit 0 |
| 改动范围 | `git show --stat 057f64a` | 票面文件清单 + 偏差声明的新增文件，未越界 |
| AddConnection 偏差 | 读代码 + 李总复现 | `TenantConnectionCache` 每租户独立 `SqlSugarScope` 的设计成立，票面回填正确 |

票面 10 条验收项逐条都有对应用例，且 `Per-request store re-query`、`16 并发只建一次`、`Scoped 上下文隔离` 等关键行为我抽读了实现与用例，结论一致。

---

## 2. 缺陷复核（临时副本执行，工作区未动）

李总报告的 F-05-1/2/3 我全部独立复现；另外新增 F-05-7（数据竞态）。

### F-05-1 · high · `TenantConfiguration.Load` 对非法配置 fail-open，停用/过期门禁可被配置笔误绕过

- **位置**：[`TenantConfiguration.cs:19-33`](../../src/Pzd.Infrastructure/Tenancy/TenantConfiguration.cs#L19-L33)
- **实测**（副本临时用例，跑完删除）：
  - `Status="Disabeld"` → `Status=Active / IsDisabled=False`，不抛异常；
  - `ExpiresAt="2026-13-99"` → `ExpiresAt=null`（永不过期），不抛异常；
  - `Status="42"` → `Enum.TryParse` 成功得到未定义枚举值 42，`IsDisabled=False`，同样放行——**即使把回落 `Active` 改掉，数字枚举仍是 fail-open**。
- **为什么错**：这是票 05 的核心安全门禁（「停用/过期立即 403」）的数据源，失败方向是放行。部署侧一个拼写错误或数字状态值，会让已停用租户重新可用、到期租户永不过期。另外 `Where(Id>0 && Code 非空)` 会把非法条目**静默丢弃**（安全方向是 404/403，但同样是静默），且 `long.Parse` 失败抛的是 `FormatException`、发生在首次请求（`AddSingleton` 工厂懒执行），不是启动期。
- **必须修**：
  1. `Status` / `ExpiresAt` 只要**出现但解析失败**就抛异常（`InvalidOperationException`，带租户 Id/键名）；`Status` 必须 `Enum.IsDefined` 且只接受 `Active/Disabled` 名字，数字/未定义值一律拒绝；`ExpiresAt` 缺省才允许 `null`（=永不过期），出现非法值必须抛。
  2. 非法条目（`Id<=0`、`Code` 空、重复 Id/Code）启动期抛，不静默过滤。
  3. 在 `Program.cs` **启动期**加载并校验（先 `TenantConfiguration.Load(builder.Configuration)`，再注册单例），不要等首个请求。
  4. 新增 `TenantConfigurationTests`：合法解析全字段、非法 Status 抛、非法 ExpiresAt 抛、数字 Status 抛、缺 Status 抛、重复条目抛。

### F-05-2 · medium · `ChangeDatabase` 扫描只覆盖两个硬编码目录，模块里调用会漏检

- **位置**：[`TenantIsolationRulesTests.cs:13-17`](../../tests/Pzd.ArchitectureTests/TenantIsolationRulesTests.cs#L13-L17)
- **实测**：副本里造 `src/Pzd.Foo`（引用 SharedKernel），源码写入 `.ChangeDatabase(`，并按规则③要求把 `Pzd.Foo` 接进 `ArchitectureTests` 的 ProjectReference → **架构测试 15 条全绿**，漏检成立。
- **为什么错**：这条规则防的是「在共享客户端上切库、串租户」，而未来业务模块正是最可能调用它的地方；现在只扫 `Pzd.Infrastructure`、`Pzd.Api`，规则对模块形同虚设。且规则③已经把模块纳入扫描并会因「模块程序集不可加载」变红，说明模块接入是常态，漏检窗口真实存在。
- **必须修**：扫描目录改为 `src/Pzd.*`（排除 `*.Contracts`、`bin/obj`），或直接复用 `ArchRules.DiscoverModules()` 的路径集合；补一条「模块源码含 `.ChangeDatabase(` 必须红」的夹具用例（可放临时目录）。

### F-05-3 · medium · 显式租户重载不校验停用/到期，后台任务可拿到停用租户连接

- **位置**：[`TenantDbProvider.cs:36-67`](../../src/Pzd.Infrastructure/Tenancy/TenantDbProvider.cs#L36-L67)
- **实测**（副本临时用例）：停用租户 3、过期租户 4，`GetConnectionScopeAsync(3/4)` 均返回可用 client（`disabled -> 3, expired -> 4`）。
- **为什么错**：票 07 的 Outbox/后台消费者会走显式重载，停用/过期租户的数据仍会被处理；`ITenantDbProvider` 是唯一连接入口，校验漏在这里，上层无处补救。
- **建议修法**：显式重载先查 store、校验 `Status`/`ExpiresAt`（注入 `IClock`），再走缓存；把查到的 `TenantInfo` 直接传给缓存工厂，避免二次查库。请求路径（`GetConnectionScopeAsync()`）由中间件保证状态，可继续只查一次。错误码建议沿用 `Tenant.Disabled` / `Tenant.Expired`（抛 `TenantNotResolvedException`）。补停用/过期两条用例。

### F-05-7 · medium（新）· `TenantConnectionCache` 写入未加锁，与读取方构成数据竞态

- **位置**：[`TenantConnectionCache.cs:42-46`](../../src/Pzd.Infrastructure/Tenancy/TenantConnectionCache.cs#L42-L46)（`_scopes[tenantId] = scope;`）对比同文件 `TryGet`/`IsCached` 的 `lock (_scopes)`（`:54-68`）
- **问题**：`_scopes` 是普通 `Dictionary<long, SqlSugarScope>`；读路径都持有 `lock (_scopes)`，但写路径只持有 `_gate`，**没有拿 `_scopes` 锁**。`_gate` 只序列化"创建者"，不阻止其他租户的请求在 `TryGet` 里并发读这个字典。
- **为什么错**：`Dictionary` 不支持并发读 + 写。一个租户首次建连接（可能触发字典扩容）时，另一个租户的请求正在 `TryGet`/`IsCached` 读同一个字典，属于未定义行为：可能读到错误值、丢条目，极端情况抛异常。16 并发用例只测同一租户、且创建完成后才读，覆盖不到这条。多租户线上预热/扩容时才会踩，属于难排查的租户串线风险。
- **必须修**：用 `ConcurrentDictionary<long, SqlSugarScope>`（`TryGetValue` + 双检 + `TryAdd`），或把 `_scopes[tenantId] = scope` 放进 `lock (_scopes)`。补一条并发创建不同租户的压测/夹具（不要求稳定复现竞态，至少覆盖多租户并发路径）。

### F-05-5 · low · 注释与实现不一致

- [`ITenantDbProvider.cs:6`](../../src/Pzd.Infrastructure/Tenancy/ITenantDbProvider.cs#L6) 仍写「按需把租户连接加入共享 `SqlSugarScope`」；[`Program.cs:14`](../../src/Pzd.Api/Program.cs#L14) 注释同样写着「按需加入」。已改成每租户独立 scope，请随本次返工一起改。

### F-05-6 · low · 补测建议（随手）

- 已登录但 **缺少/非法 `tid`** → 400 `Tenant.NotFound`（现在只测了 claim 有效路径）。
- 到期边界：`ExpiresAt == clock.UtcNow` 应过期（代码用 `<=`，无用例）。
- claim 路径的「每请求重查 store」在 HTTP 接缝上未断言（仅匿名 header 路径有）。
- `TenantConfiguration` 完全无单测（并入 F-05-1 修复）。

---

## 3. 跨票问题（归老何改票，不是 05 的代码返工）

### F-05-4 · medium · 中间件对所有请求强制租户，票 03/09 的 `/health`、`/api/version` 会被 400 拦

- 现状：`Program.cs` 中间件在 `UseRouting` 前对每个请求解析租户，缺来源直接 400 `Tenant.NotFound`；票 05 声明「本轮所有端点都要求租户」。
- 冲突：票 03 的 `verify-apphost.ps1` 直接轮询 `http://127.0.0.1:5080/health/ready`（IP、无 `X-Tenant`、无子域）→ 会拿 400；票 09 同样要求 `/health/live`、`/api/version` 可访问。foundation §4.3 第 55 条也写「`/health` 与 `/api/version` 无需数据库即可返回」。
- **要求（老何）**：在票 03/09 开工前定豁免机制并写进票面与 foundation §4.3，例如 `AllowAnonymousTenant` metadata + 中间件对带该 metadata 的端点跳过租户解析；验收补「无租户访问 `/health/live`、`/health/ready`、`/api/version` → 200」。旺总在 03/09 实现。

---

## 4. 结论与返工范围

**票 05：需修改。** 旺总返工范围：

| # | 级别 | 修法 | 归属 |
|---|---|---|---|
| F-05-1 | high | 配置严格解析 + 启动期校验 + `TenantConfigurationTests` | 旺总 |
| F-05-2 | medium | `ChangeDatabase` 扫描改 `src/Pzd.*`（或复用 `DiscoverModules`）+ 夹具用例 | 旺总 |
| F-05-3 | medium | 显式租户重载校验状态/到期（注入 `IClock`）+ 两条用例 | 旺总 |
| F-05-7 | medium | 缓存改 `ConcurrentDictionary` 或写入加锁 + 多租户并发用例 | 旺总 |
| F-05-5 | low | 改掉两处过期注释 | 旺总 |
| F-05-6 | low | 补 invalid `tid`、到期边界、claim 路径重查用例 | 旺总（随手） |
| F-05-4 | medium | 定 `AllowAnonymousTenant` 豁免并写进票 03/09 + foundation §4.3 | 老何 |

返工仍只动 `src/Pzd.Infrastructure/Tenancy/*`、`src/Pzd.Api/Program.cs`、对应测试与架构测试，不引入新功能。修完由李总按票复测（重点：非法配置必须启动失败、模块 `.ChangeDatabase` 必须红、停用/过期显式重载必须抛），再回审计。

---

## 5. 返工复核（工作区未提交 · 2026-10-27）

基线（我在工作区实跑）：`dotnet build -warnaserror --no-incremental` 0 警告 0 错误；35+16+54+10+1 = **116 条全绿**。三个变异在副本独立复跑：

| 变异 | 期望 | 实测 |
|---|---|---|
| M1 非法 `Status` 回落 `Active` | 配置用例红 | ✅ 3 failed（`Disabeld`/`42`/`1`；空值仍走「缺失」分支） |
| M2 去掉显式重载的 `EnsureTenantUsable` | 3 红 | ✅ 3 failed（停用、过期、`ExpiresAt == now` 边界） |
| M3 `ChangeDatabase` 扫描退回两个硬编码目录 | 模块夹具红 | ✅ 1 failed（`A_module_source_calling_ChangeDatabase_is_detected`） |

F-05-7 代码审查确认改为 `ConcurrentDictionary` + 双检（`_scopes[tenantId] = scope` 已是线程安全写），`8 租户 × 4 并发` 用例通过；F-05-5 两处注释已改；F-05-6 的无效 `tid`、到期边界、claim 路径重查用例都在。**F-05-1/2/3/5/6/7 可判修复。**

### 5.1 新发现（跨票，不阻塞 05 关闭但必须落票）

- **F-05-8 · medium · 无参重载只信任 `ITenantContext`，后台路径可绕过停用/到期校验**：我实测手工 `context.Set(3)`（停用租户）后调用 `GetConnectionScopeAsync()` 仍拿到连接（`ConfigId=3`）。HTTP 请求有中间件挡住没问题，但架构 §6.3 写「后台任务 / Outbox 执行前 `ITenantContext.Set`」——票 07 若按这个写法走无参重载，停用/过期租户的数据仍会被处理。要求：票 07 验收明确「后台任务用带状态校验的显式重载」，或给无参路径补校验。归老何落票。
- **票面措辞（low，老何）**：显式重载为校验状态**每次调用查一次 store**（实测同租户两次调用 `FindByIdCalls=2`），与票 05「只查一次 `ITenantStore`」字面冲突。建议改成「连接创建只查一次；显式重载为校验状态按次查库」。
- **可选 low（老何定）**：`Tenants` 为空时 `Load` 返回空集，应用能启动但所有请求 400；是否要在启动期 fail-fast 由老何决定。

### 5.2 关票口径（老登）

1. **旺总提交返工**（10 个改动 + 新增 `TenantConfigurationTests.cs`），给出 commit hash；`git status --porcelain` 对 `src/`、`tests/` 干净。
   - ✅ 2026-10-27 已提交 `e593225`：11 个文件全在 `src/`+`tests/`，`git diff e593225 -- src tests` 为空（与我在未提交树上审计/变异的内容一致）；build 0 警告 0 错误、116 条全绿。
2. 提交后：**我按 hash 复核 diff + 116 条**；**李总按 hash 复跑 116 条**；两边一致才算数。
3. **老何落三件票面事**：① F-05-4（`/health`、`/api/version` 豁免）写进票 03/09 + foundation §4.3，票 03 开工前生效；② F-05-8 写进票 07 验收；③ 修正票 05「只查一次 store」措辞。
4. **老卫**提交 `docs/reports/review-05.md` 与 `test-report-05.md`，并更新 handover（票 05 状态、F-05-4/F-05-8、待办）。
5. 以上齐了票 05 改 ✅。哈希审计与李总冒烟均已通过（见 §6），当前状态为「实现通过、票面与文档待收口」。

---

## 6. 终审结论（`e593225`）

- **哈希复核（我）**：`git show --stat e593225` = 11 个文件，全在 `src/` + `tests/`（10 改 + 新增 `TenantConfigurationTests.cs`），报告未混入；`git diff e593225 -- src tests` 为空——与我在未提交树上读代码、跑变异的内容完全一致。`dotnet build -warnaserror --no-incremental` 0 警告 0 错误；35+16+54+10+1 = **116 条全绿**。
- **李总冒烟（hash 上）**：同一组数字；`git archive e593225` 的干净副本重跑 L1（非法 `Status` 回落 `Active`）→ 3 红，证明守护在提交里、不是工作区残留。
- **结论：票 05 实现审计通过。** F-05-1/2/3/5/6/7 关闭；无遗留代码必修项。
- **关票剩余（非代码，不得丢）**：
  1. **老何**：票 05 状态与测试数回填（`e593225`，116 条）；票面措辞「只查一次 `ITenantStore`」→「连接创建只查一次；显式重载为校验状态按次查库」；**F-05-4** 写进票 03/09 + foundation §4.3（`/health`、`/api/version` 免租户；**票 03 开工硬前置**）；**F-05-8** 写进票 07 验收（后台任务走带状态校验的显式重载，或 provider 补校验）。
  2. **老卫**：提交 `docs/reports/review-05.md`、`test-report-05.md`，handover 记录票 05 通过、F-05-4/F-05-8 两个前置、以及「后台任务不要用无参重载裸取连接」这条约束。
