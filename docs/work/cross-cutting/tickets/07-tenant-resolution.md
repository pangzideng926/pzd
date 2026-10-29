状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 06 合入后认领；先写失败测试
交付状态：待收尾
实施条件：待满足：06 合入（前置票）

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

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Tenancy
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚项目改动；租户库为测试容器，可重建。

## 任务映射

- worktree：实施时填写（`.worktree/07/`）
- 分支：实施时填写（`feat/07-tenant-resolution`）
- 提交：实施时填写（完整 SHA）
