状态：待实施
主写入者：未认领
更新日期：2026-10-30 12:40 Asia/Shanghai
下一步：前置已满足（06 已合入 `1c8abbb`，写入口自带租户谓词、`RestoreAsync` 改由库判定），可直接认领。**开工顺序固定：先补 Restore/SoftDelete 跨租户红用例（步骤 0），再动解析链路**；两条门槛见「步骤 0」与验收标准里的 2026-10-30 条目
交付状态：待收尾
实施条件：已具备（06 合入 `1c8abbb`；05 `8116b32`、36 `869e745` 已在 main）

# 07 多租户解析链路（TDD）

## 目标

任何请求都能拿到正确的 `ITenantContext`，拿不到就明确失败，绝不静默连到错的库。

## 需求依据

- [architecture.md](../../../architecture.md) §6.1、§6.2、§6.3
- [spec.md](../spec.md) AC-02、AC-03
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.3

## 范围与非范围

- 做：`ITenantStore`（HybridCache）、`TenantResolutionMiddleware`（已登录 claim `tid`；匿名端点走子域名 / `X-Tenant`）、`ITenantContext`、`ITenantDbProvider`（按需 AddConnection，加锁双检，单独配 AOP 与过滤器）、缓存前缀 `t:{tid}:`、禁跨库事务的显式守卫。
- 不做：不做登录与 Cookie（23）、不做租户管理界面（期外）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/Tenancy/**`、`tests/Pzd.IntegrationTests/Tenancy/**`。不碰 `src/Pzd.Identity`。

## 关键接口与依赖

- 前置票：06
- 对外契约：`ITenantContext.Current` 与 `ITenantDbProvider.GetAsync()` 的签名被 08、17、21、24 号票依赖，改动需通知。

## 步骤

0. **先补跨租户用例，且用变异证明它不是空网**（2026-10-30 加，来源：06 八审 M-E、张一一裁定）。这一步不是「写失败测试」：06 的写入口现在**行为本来就对**（租户 1 的上下文更新/软删/恢复租户 2 的记录都返回 `NotFound`/404，别家行逐列未变），所以新用例一写就是绿的。绿不代表守住了东西，必须按下面三步走：
   1. 补三条用例：`UpdateAsync`、`SoftDeleteAsync`、`RestoreAsync` 各一条，租户 1 上下文操作租户 2 的行 → `NotFound`/404（**不是 403、不是 `VersionConflict`**），并断言别家那行 `TenantId`/`Version`/`IsDeleted`/`DeletedToken` 逐列未变。
   2. **变异证明**：把 `PersistenceWriter` 里的 `AND TenantId=@tenantId` 只从被测那条路径拿掉（一次一条），对应用例必须变红；三处各自红过、还原后全绿，才算这三条用例真的在守。
   3. 变异只在本机临时改、改完 `git checkout --` 还原，证据里贴出「红→绿」两段真实输出；不还原干净不算完成。
   > 判据为什么这么严：老登八审只从 `Restore` 路径拿掉租户条件，`PersistenceTests` **36 条全绿、零失败**——代码对、测试空。只交一条永远绿的用例，等于把缺口藏进覆盖率里。

1. **先写失败测试**：无租户上下文访问数据应失败；A 租户查不到 B 租户数据；停用租户 403。确认失败原因是行为缺失而不是编译错误。
2. 实现 `ITenantStore` 与中间件，校验状态与到期。
3. 实现 `ITenantDbProvider` 按需连接与双检锁。
4. 测试转绿，贴前后输出。

## 验收标准

- [ ] 已登录请求从 claim `tid` 解析租户；匿名端点可用 `X-Tenant` 解析。
- [ ] 无租户上下文的数据访问明确失败（不回退 master）。
- [ ] A 租户查询看不到 B 租户数据。
- [ ] 租户停用/到期返回 403 `Tenant.Disabled` / `Tenant.Expired`。
- [ ] 未预加载的租户首次访问能建立连接，并发首次访问不重复建连。
- [ ] 缓存 key 带 `t:{tid}:` 前缀。
- [ ] **补硬用例（2026-10-30 加，来源：06 复核）**：`PersistenceWriter.UpdateAsync` 的 WHERE 只有 `Id` + `Version`、**没有 `TenantId`**，而 `pzd_master` 是共享库、每张表都有 `TenantId`。**处置已定（2026-10-30 调度裁定，经 06 审计 high 2 实测确认「能写穿」）**：租户谓词归 **06**，写入口必须在缺租户上下文时抛异常、不得静默按 `Id` 更新；本票只负责补跨租户红用例——两条不同 `TenantId` 的同 `Id` 记录，A 租户上下文写 B 的必须失败，跑在 `PersistenceWriter` 上。**不得默认按分库理解**——同库多租户这张网目前是漏的。
- [ ] **恢复路径的跨租户也必须有用例（2026-10-30 加，来源：06 八审 medium / M-E）**：`PersistenceWriter.RestoreAsync` 去掉实体自报守卫后，租户隔离**只依赖 SQL 的 `AND TenantId=@tenantId`**（`PersistenceWriter.cs:232-237`）。老登只从恢复路径拿掉这个条件做变异，`PersistenceTests` **36 条全绿、零失败**——行为是对的（实测恢复别家已删记录 → `NotFound/404`，别家那行 `IsDeleted=1 DeletedToken<>0 TenantId=2 Version=2` 一字未动），但**没有任何用例守它**。本票必须补：租户 1 的上下文恢复租户 2 的已删记录 → `NotFound`（不是 403、不是 `VersionConflict`），且别家那行逐列未变；**该用例必须在「从 `Restore` 路径删掉 `AND TenantId`」时变红**，否则不算覆盖。同样的验收要求适用于 `SoftDeleteAsync`（它也没有内存守卫，活跃态与租户全由库判定）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Tenancy
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚项目改动；租户库为测试容器，可重建。

## 任务映射

- worktree：实施时填写（`.worktree/07/`）
- 分支：实施时填写（`feat/07-tenant-resolution`）
- 提交：实施时填写（完整 SHA）
