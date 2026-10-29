状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 05 基类签名冻结、36 合入后认领
交付状态：待收尾
实施条件：待满足：05 基类签名冻结并合入；36（集成测试 fixture）合入

# 06 持久化约定：映射 / 审计字段 / 软删除与唯一索引 / 并发 / UTC

## 目标

把数据库访问的约定一次定型：映射集中在 Infrastructure、雪花主键对外字符串、审计字段自动填充、软删除全局过滤且不挡同名重建、乐观并发 409、唯一索引含 TenantId、时间入库 UTC。

## 需求依据

- [architecture.md](../../../architecture.md) §5.2、§5.3
- [spec.md](../spec.md) AC-14（出口在 08 号票）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.2

## 范围与非范围

- 做：`src/Pzd.Infrastructure`：SqlSugarScope 注册（仅 master）、`ConfigureExternalServices.EntityService` 集中映射、雪花主键与序列化策略、审计字段 AOP、软删除 + QueryFilter、`Version` 乐观并发、**软删除实体的唯一索引方案**（删除后必须能用同一业务键重建）。
- 不做：不做租户连接管理（07）、不做 UoW/Outbox（08）、不写业务表、不建集成测试工程与 fixture（36 号票）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/**`、`tests/Pzd.IntegrationTests/PersistenceTests.cs`。不碰 `src/Pzd.SharedKernel`、不碰 `tests/Pzd.IntegrationTests/Fixtures/**`（36 号票的）。

## 关键接口与依赖

- 前置票：05、36（05 只需基类签名冻结并合入，不等 05 其余部分）
- 对外契约：Domain 禁止出现 ORM 特性（由 13 号票断言）；对外主键一律字符串；**唯一索引方案被 17、19、20、23、32 号票的建表 SQL 复用，定型后写进票内**。

## 步骤

1. 注册 SqlSugarScope 与映射配置，确认 Domain 无 `[SugarColumn]`。
2. 实现审计字段 AOP、软删除 QueryFilter、并发版本校验。
3. **定软删除唯一索引方案并写进票内**：可空 `DeletedToken`（删除时写入主键、未删除为 NULL）参与唯一索引，或等价方案；明确禁止用「去掉唯一索引」换取同名可重建。
4. 时间约定：入库强制 UTC；Infrastructure **不得**在读取环节改时区语义，出参转换统一交给 API 序列化层。
5. 用 36 号票的 fixture 写集成测试（类名 `PersistenceTests`）：审计字段自动填充、软删除默认不可见、软删除后同名可重建、并发冲突与 1062 可识别、读回时间与写入的 UTC 值一致。
6. 贴命令、通过数（>0）与关键输出行。

## 验收标准

- [ ] 新增实体自动带 `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy`。
- [ ] 软删除记录默认查询不可见，绕过过滤需显式调用。
- [ ] **软删除后能用同一业务键（如 `UserName`）重建成功，有可执行断言证明**；唯一索引方案已在票内留痕，且没有删除唯一索引。
- [ ] 并发更新冲突可被识别为 409 的来源错误（端点映射由 09 号票完成）。
- [ ] MySQL 1062 唯一键冲突可被识别为 409 的来源错误。
- [ ] 直接读回的时间与写入的 UTC 值一致；Infrastructure 未做时区转换。
- [ ] 证据贴出 `PersistenceTests` 的实际通过数（>0）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter PersistenceTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

删除新增项目与配置；测试库为容器，直接重建。

## 任务映射

- worktree：实施时填写（`.worktree/06/`）
- 分支：实施时填写（`feat/06-persistence-base`）
- 提交：实施时填写（完整 SHA）
