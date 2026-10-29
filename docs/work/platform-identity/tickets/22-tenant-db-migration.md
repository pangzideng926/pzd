状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等维护者定执行器选型（DbUp / 自研）后认领
交付状态：待收尾
实施条件：待满足：执行器选型未定（consensus §4 待决 2，须由维护者拍板并在本票批注）；17 合入

# 22 租户库迁移与运行时版本校验

## 目标

`db/tenant/` 的版本化 SQL 能逐租户（或灰度批次）执行并记录版本；代码发现租户版本过低时明确返回 503。

## 需求依据

- [architecture.md](../../../architecture.md) §6.4、§15.4
- [spec.md](../spec.md) AC-04、AC-05
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M3.4

## 范围与非范围

- 做：租户库迁移执行器接入（现成库或自研，二选一由选型结论定）、逐租户与灰度执行、`schema_versions` 记录、单租户失败只标记该租户、运行时版本校验中间件（`503 Tenant.Upgrading`）。
- 不做：不做迁移界面；不做自动回滚 DDL；不做跨库事务。

## 责任文件范围（认领时在群里先说清）

`src/Pzd.Infrastructure/Migrations/**`、`db/tenant/**`、`tests/Pzd.IntegrationTests/Migrations/**`。

## 关键接口与依赖

- 前置票：17
- 对外契约：`schema_versions` 结构与 17 号票共用；版本校验的 503 响应形状被 28 号票前端拦截依赖。

## 步骤

1. 写选型结论到本票的「遗留/决策」段（谁定的、备选、放弃理由）。
2. 接执行器，跑通单租户执行与灰度批次。
3. 实现运行时版本校验。
4. 测试：版本落后 → 503；只迁移部分租户时其他租户不受影响；脚本必须兼容旧代码（expand-contract）由审查确认。

## 验收标准

- [ ] 迁移能对指定租户执行，`schema_versions` 记录版本。
- [ ] 灰度：只迁移部分租户，其余租户继续正常服务。
- [ ] 单租户迁移失败只标记该租户。
- [ ] 运行时版本落后返回 503 `Tenant.Upgrading`。
- [ ] 选型结论已在票内留痕。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Migrations
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚代码；测试租户库重建。已上线的 DDL 不做自动回滚，按 expand-contract 手工处理。

## 任务映射

- worktree：实施时填写（`.worktree/22/`）
- 分支：实施时填写（`feat/22-tenant-db-migration`）
- 提交：实施时填写（完整 SHA）
