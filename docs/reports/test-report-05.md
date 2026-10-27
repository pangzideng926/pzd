# 测试报告：票 05（多租户内核 + 单测）

> 测试：李总｜日期：2026-10-27｜票面：[`docs/specs/tickets.md`](../specs/tickets.md) 票 05
> 被测提交：`057f64a`（`feat(tenancy)`，28 文件 / +1413）
> 结论：**票面验收项全部通过**；但复测发现 1 条 high（配置笔误会静默让停用/过期租户「复活」）与 2 条 medium 覆盖缺口，建议返工或并票后再关闭票 05（由审计定级）

## 1. 基线

- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 逐工程 `dotnet run`：35 + 15 + 30 + 9 + 1 = **90 条，Failed 0，exit 0**（与交付自述一致）。
- 单测不依赖数据库：连接工厂用例只构造 `SqlSugarScope` 与 `ConnectionConfig`，不打开连接；HTTP 接缝只打 `/`。

## 2. 票面验收项 → 证据

| 验收项 | 用例 | 结果 |
|---|---|---|
| 已登录 claim `tid` 优先、`X-Tenant` 被忽略 | 中间件 `Logged_in_claim_wins_and_the_header_is_ignored`（`FindByCodeCalls==0`）+ API 接缝同名用例 | 通过 |
| 匿名 `X-Tenant` / 子域名 / 子域优先 | 中间件 3 条 + API 接缝 3 条（`RequestedCodes` 精确断言） | 通过 |
| 无来源 → 400 `Tenant.NotFound` | 中间件 + API 接缝，响应体 `code`/`type` 精确断言 | 通过 |
| 停用 403 `Tenant.Disabled` / 过期 403 `Tenant.Expired` | 中间件 + API 接缝，`type=https://pzd/errors/tenant.*` | 通过 |
| 每请求重新查 store | 中间件两条请求 `FindByCodeCalls==2`；API 接缝两次请求 | 通过 |
| Scoped 上下文不跨请求泄漏 | `TenantContextTests.Scoped_contexts_do_not_share_state`（两个 DI scope） | 通过 |
| 连接工厂：双检、只查一次、16 并发、无上下文 `Tenant.NotResolved`、未知租户 `Tenant.NotFound` | `TenantDbProviderTests` 6 条（含 16 并发） | 通过 |
| 全局单例无 `ChangeDatabase` | `TenantIsolationRulesTests` 源码扫描 | 通过（但见 F-05-2） |
| 规则③扫描范围 = 底座 + `DiscoverModules()`，模块不可加载时红 | `Domain_scan_covers_base_assemblies_and_fails_loudly_for_unloadable_modules` | 通过 |
| F7：`FakeInfrastructureDependency`，反向用例恰好 2 条违规 | `ReverseCaseTests.Domain_dependency_rule_detects_both_deliberate_violations` | 通过 |

## 3. 变异复测（临时副本，工作区未动）

| 变异 | 期望 | 实际 |
|---|---|---|
| 已登录分支不再认 claim（`IsAuthenticated == true && false`，走匿名头） | claim 用例红 | 中间件 `Logged_in_claim_wins…` **1 failed**，exit 1 |
| 删掉停用拦截 | 403 用例红 | 中间件 **1 failed** + API 接缝 **1 failed** |
| 在 `src/Pzd.Infrastructure` 源码引入 `.ChangeDatabase(` | 架构检查红 | `Change_database_is_never_used_in_product_sources` **1 failed**，报出违规文件 |
| 旺总声明的偏差：`SqlSugarScope.AddConnection` 是否 async 上下文私有 | 若成立，`await` 后只剩 master | 实测：`ids after await: master`（**偏差成立**，每租户独立 scope 的设计有依据） |

## 4. 缺陷与覆盖缺口

### F-05-1 · high · `TenantConfiguration.Load` 对非法值 fail-open（停用/过期门禁可被配置笔误绕过）

- **代码**：`TenantConfiguration.cs:21-30`——`Enum.TryParse<Status>` 失败回落 `Active`；`DateTimeOffset.TryParse(ExpiresAt)` 失败回落 `null`（永不过期）。
- **最小复现**（临时用例，副本执行）：配置 `Status="Disabeld"`、`ExpiresAt="2026-13-99"`，`TenantConfiguration.Load` 返回 **`Status=Active, ExpiresAt=null`**，不抛异常。
- **影响**：部署侧一个拼写错误会让已停用租户重新可用、或让到期租户永不过期——这正是票 05 的核心安全门禁，且失败方向是「放行」。
- **建议**：`Status`/`ExpiresAt` 非空但解析失败时启动即抛（fail-fast），并补 `TenantConfiguration.Load` 单测（合法解析、非法值抛、缺失值默认、到期边界 `== now`）。

### F-05-2 · medium · `ChangeDatabase` 扫描不含业务模块（未来违规会漏检）

- `TenantIsolationRulesTests` 只扫 `src/Pzd.Infrastructure` 与 `src/Pzd.Api` 两个硬编码目录；规则④发现的模块不在扫描范围。
- **最小复现**（副本）：新建模块 `src/Pzd.Foo`（引用 SharedKernel）并在源码里写入 `.ChangeDatabase(`，同时把它的 ProjectReference 加进 `Pzd.ArchitectureTests`（规则③可加载）→ **架构测试 15 条全绿**。
- **建议**：扫描目录改为 `src/Pzd.*`（排除 `*.Contracts` 与 bin/obj），或直接复用 `ArchRules.DiscoverModules()` 的路径。

### F-05-3 · medium · 显式租户重载不校验停用/到期（后台任务路径可拿到停用租户连接）

- `TenantDbProvider.GetConnectionScopeAsync(long tenantId)` 只查存在性与连接串，不查 `Status`/`ExpiresAt`。
- **最小复现**（临时用例）：停用租户 3、过期租户 4，显式重载均返回可用 client（`disabled -> 3, expired -> 4`）。
- **影响**：票 07 的 Outbox/后台消费者若走显式重载，停用/过期租户的数据仍会被处理。
- **建议**：在连接工厂内统一校验状态与到期（或明确该重载只允许受信内部调用，并在票 07 补校验）。

### F-05-4 · medium（跨票）· 中间件对所有请求强制租户，票 03/09 的 `/health`、`/api/version` 会被 400 拦掉

- 本轮所有请求都要求租户（旺总已声明），当前 `Program.cs` 只有 `/` 端点所以测试发现不了。
- 票 03 的 `/health`、票 09 的 `/api/version` 验收要求「无需租户可访问」，按现实现会得到 400 `Tenant.NotFound`。
- **建议**：票 03/09 前给端点加豁免机制（如 `AllowAnonymousTenant` metadata 或 `MapHealthChecks` 前短路），并补「无租户访问 `/health`、`/api/version` → 200」的 HTTP 接缝用例。

### F-05-5 · low · 接口 XML 注释与实现不一致

`ITenantDbProvider.cs:6` 仍写「按需把租户连接加入共享 `SqlSugarScope`」，实际已改为每租户独立 scope。建议随票面偏差一起改文案。

### F-05-6 · low · 补测建议

- 已登录但 **没有 / 非法 `tid`** claim → 400 `Tenant.NotFound`（当前无用例）。
- 到期边界：`ExpiresAt == clock.UtcNow` 应过期（代码用 `<=`，事件未测）。
- claim 路径的「每请求重查 store」在 HTTP 接缝上未断言（仅匿名 header 路径有）。

## 5. 结论

- 票 05 的 10 条票面验收项均有可复现证据通过；交付自述的 90 条、build 0/0、AddConnection 偏差均核实。
- F-05-1 属安全门禁的静默 fail-open，建议**返工后再关票**；F-05-2/F-05-3 建议并入小票；F-05-4 需在票 03/09 票面落实豁免；F-05-5/F-05-6 随手修。
- 最终定级（返工 / 并票）由审计按 §4 决定。

## 6. 返工复测（工作区改动，**尚未提交**）

> 复测对象：旺总按 `review-05.md` 的返工，当前位于工作区（10 个 `M` 文件 + 新增 `TenantConfigurationTests.cs`），无提交号。

### 6.1 基线

- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 逐工程：35 + 16 + 54 + 10 + 1 = **116 条全绿**，与返工自述一致。

### 6.2 逐条验收与变异

| 返工项 | 复测证据 | 变异复测 |
|---|---|---|
| F-05-1 严格解析 + 启动期校验 | `TenantConfigurationTests` 14 条：非法/缺失 `Status`（含 `42`、`1`、拼写错误、空）抛、非法 `ExpiresAt` 抛、`Id<=0`/空 Code/重复 Id/忽略大小写重复 Code 抛 | 把非法 `Status` 改回回落 `Active` → **3 红**（Disabeld/42/1） |
| F-05-1 启动期 fail-fast | 端到端：`Tenants__0__Status=Disabeld` 启动 API → `Unhandled exception: InvalidOperationException: Tenants 配置 …Status 非法`，未监听端口 | — |
| F-05-2 扫描范围 | `ChangeDatabaseViolations` 扫 `src/Pzd.*`（排除 `*.Contracts`），fixture 用例断言模块违规被报出 | 退回两个硬编码目录 → fixture 用例 **1 红** |
| F-05-2 真实模块 | 副本里造真实 `src/Pzd.Foo`（引用 SharedKernel + SqlSugarCore，方法体 `db.ChangeDatabase(...)`）并接进 ArchitectureTests → `Current_sources_never_call_ChangeDatabase` **1 红**，报出 `src/Pzd.Foo/FooService.cs` | — |
| F-05-3 显式重载校验 | `Explicit_tenant_validates_status_and_expiry` 3 条（Disabled / 过期 / `ExpiresAt == now`） | 去掉 `EnsureTenantUsable` → **3 红** |
| F-05-7 缓存并发 | `ConcurrentDictionary` + 双检；8 租户 × 4 并发，每租户工厂恰好 1 次、32 次调用返回 | —（并发用例覆盖创建竞态） |
| F-05-5 注释 | `ITenantDbProvider`、`Program.cs` 的「共享 scope」措辞已改 | — |
| F-05-6 补测 | 已登录缺/非法/不存在 `tid` → 400；`ExpiresAt == now` → 403；claim 路径每请求 store 计数（HTTP 接缝） | — |

### 6.3 残余

- **F-05-R1（已修复）**：返工曾未提交，旺总已提交为 **`e593225`**（11 文件：10 改 + 新增 `TenantConfigurationTests.cs`），`src/`、`tests/` 干净，只剩 `docs/reports/` 待老卫入库。
- **F-05-4（跨票）仍开放**：`AllowAnonymousTenant` 豁免尚未写进票 03/09 与 foundation §4.3（`tickets.md` 当前无此改动），票 03/09 开工前必须落票。
- **F-05-8（审计新增，归票 07）**：手工 `ITenantContext.Set(停用租户)` 后走无参重载仍返回连接（无参重载信任中间件结论）；票 07 后台任务按「先 Set 再取连接」会绕过状态校验，已由老登写入票 07。
- 低优先级口径建议：`ExpiresAt` 出现空字符串会抛、JSON `null` 视为「永不过期」，建议在票面/README 写清；`TenantInfo.ConnectionString` 为空目前运行期才报 `Tenant.NotResolved`，是否启动期校验由老何定。

### 6.4 复测结论

F-05-1/F-05-2/F-05-3/F-05-5/F-05-6/F-05-7 复测通过，可以关闭；测试侧结论：**功能通过，已提交待终审**。

### 6.5 提交冒烟（`e593225`）

- `git show --stat e593225`：11 个文件，与 §6.2 复测的工作区一致；`src/`、`tests/` 干净。
- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误；逐工程 35 + 16 + 54 + 10 + 1 = **116 条全绿**，exit 0。
- 在 `git archive e593225` 的副本上重跑 L1 变异（非法 `Status` 回落 `Active`）→ **3 红**，确认提交里的守护不是工作区残留。


