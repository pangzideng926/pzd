状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 07、08 合入后认领
交付状态：待收尾
实施条件：待满足：07、08 合入（前置票）

# 12 Quartz 基础（master AdoJobStore + 集群 + 租户扇出基类）

## 目标

定义任务类就能注册；集群内同一时刻只触发一次；按租户扇出执行且单租户失败不影响其他。

## 需求依据

- [architecture.md](../../../architecture.md) §11.1–§11.3、§11.5
- [spec.md](../spec.md) AC-09
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.8

## 范围与非范围

- 做：master 库 AdoJobStore + 集群 + JSON 序列化、`db/master/` 下 Quartz 建表 SQL、`[JobDefinition]` 启动扫描注册、`TenantFanOutJob` 基类（遍历租户、每租户独立 scope、异常隔离）、时区与 Misfire 约定。
- 不做：不做任务管理界面与执行记录（原 M6.7，期外）；不写业务任务。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/Scheduling/**`、`db/master/` 下 Quartz 建表 SQL、`tests/Pzd.IntegrationTests/Scheduling/**`。不碰 `db/tenant/`。

## 关键接口与依赖

- 前置票：07、08
- 对外契约：`[JobDefinition]` 特性与 `TenantFanOutJob` 基类被后续所有后台任务依赖。

## 步骤

1. 落 Quartz 建表 SQL 到 `db/master/`。**旧表处置归 37 号票**（本机 `pzd_master` 里另有 15 张 `d34cf66` 遗留的 `qrtz_*`，`CREATE_TIME` 均为 `2026-10-27 17:46:45`，其中 `qrtz_scheduler_state` 最后心跳 `2026-10-28 09:06:56`、已无活进程）：37 负责备份后按名单逐个 drop，**本票只建新表**。开工时若发现旧表仍在，停止并报告，不许自行 drop。建表只走 `db/master/` 版本化脚本，禁止依赖运行期自动创建，避免 schema 漂移。
2. 配 AdoJobStore 与集群，接 JSON 序列化。
3. 实现启动扫描与 `TenantFanOutJob`。
4. 写一个自造任务验证：集群单点触发、扇出时一个租户抛异常不中断其他。

## 验收标准

- [ ] `pzd_master` 里那 15 张 2026-10-27 的旧 `qrtz_*` 表已由 37 号票处置完毕（本票开工前核实为 0 张）；若仍存在则停止并报告，不自行 drop。
- [ ] 注册的任务在 `qrtz_*` 表可见，按 Cron 触发。
- [ ] 多实例下同一任务同一时刻只在一个实例执行。
- [ ] `TenantFanOutJob` 对每个租户独立 scope 执行。
- [ ] 某租户任务抛异常，其他租户照常执行，异常被记录。
- [ ] 时区与 Misfire 策略写在基类注释里并有对应配置。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Scheduling
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚代码与 SQL；测试库重建。

## 任务映射

- worktree：实施时填写（`.worktree/12/`）
- 分支：实施时填写（`feat/12-quartz-base`）
- 提交：实施时填写（完整 SHA）
