状态：待实施
主写入者：未认领
更新日期：2026-10-30 09:15 Asia/Shanghai
下一步：等 06、37 完成后认领；37 未处置旧 `qrtz_*` 前不得开工
交付状态：待收尾
实施条件：待满足：06 合入；37（重置遗留 Quartz 表）完成

# 17 master 库结构与迁移

## 目标

master 库从空库能迁移到最新版本，可重复执行且幂等；表结构与 §6.1 一致。

## 需求依据

- [architecture.md](../../../architecture.md) §6.1、§6.4
- [spec.md](../spec.md) AC-01
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M3.1

## 范围与非范围

- 做：`db/master/` 版本化 SQL：`tenants`、`tenant_packages`、`menus`、`permissions`、`platform_users`、`schema_versions`；迁移执行入口（master 侧）；一个超管种子账号（首次登录强制改密）。
- 不做：不写租户库 SQL（20、22 号票）；不建业务表；不写菜单种子内容（19 号票）。

## 责任文件范围（认领时在群里先说清）

`db/master/**`、`src/Pzd.Platform/**`（迁移执行部分）、`tests/Pzd.IntegrationTests/Platform/**`。

## 关键接口与依赖

- 前置票：06、37（37 先备份并清掉 `d34cf66` 遗留的 15 张 `qrtz_*`，否则首次迁移撞「表已存在」或 schema 漂移）
- 对外契约：表名为唯一约定，19、18、21 号票依赖；`schema_versions` 结构与 22 号票共用。

## 步骤

1. 写建表 SQL（含 `TenantId` 相关索引约定到租户库侧、`utf8mb4_0900_ai_ci`）。
2. 实现 master 迁移执行入口，记录到 `schema_versions`。
3. 插超管种子（密码不入库明文，首次登录强制改）。
4. 空库跑到最新并重复执行一次证明幂等。

## 验收标准

- [ ] 空库执行到最新版本成功，`schema_versions` 有记录。
- [ ] 重复执行不报错、不重复建表。
- [ ] 六张表结构完整，字符集为 `utf8mb4_0900_ai_ci`。
- [ ] 超管种子存在且密码非明文。
- [ ] 迁移全程 `pzd_master` 里不存在任何 `qrtz_%` 表：旧表由 37 号票清场，新表由 12 号票按 `db/master/` 脚本创建，**本票不建也不 drop `qrtz_*`**；发现旧表仍在就停止并报告，不自行处理。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter MasterSchema
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

测试库重建；已上线环境按 expand-contract 另写回滚脚本（V1 无上线环境）。

## 任务映射

- worktree：实施时填写（`.worktree/17/`）
- 分支：实施时填写（`feat/17-master-schema-migration`）
- 提交：实施时填写（完整 SHA）
