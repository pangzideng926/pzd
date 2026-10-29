状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 06 合入后认领；先写失败测试
交付状态：待收尾
实施条件：待满足：06 合入（前置票）

# 08 UoW + Outbox（TDD）

## 目标

一个 Command 只提交一次；领域事件在提交前分发；跨模块消息进 Outbox，投递至少一次且消费幂等。

## 需求依据

- [architecture.md](../../../architecture.md) §3.3、§5.2、§10.1
- [spec.md](../spec.md) AC-04、AC-05
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.4

## 范围与非范围

- 做：`UnitOfWork`：收集聚合事件 → 分发 → 写 Outbox → 提交事务；Outbox 表结构（租户库）与投递器；消费端幂等（按消息 Id 去重）；投递失败重试与死信标记。
- 不做：不做跨库事务（明确禁止）；不做 SignalR 推送（30 号票）；不做业务消息；不用事务回滚做用例隔离（36 号票定为每用例独立库，真提交语义依赖它）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/UnitOfWork/**`、`src/Pzd.Infrastructure/Outbox/**`、`db/tenant/` 下 outbox 建表脚本、`tests/Pzd.IntegrationTests/Outbox/**`。不碰 `src/Pzd.Infrastructure/Tenancy`。

## 关键接口与依赖

- 前置票：06
- 对外契约：`IOutboxPublisher`/`IIntegrationEvent` 的形状被 12、30、35 号票依赖。

## 步骤

1. **先写失败测试**：事务回滚后不得残留 Outbox 记录；同一消息投递两次只产生一次副作用。确认失败原因正确。
2. 实现 UoW 与事件分发顺序。
3. 实现 Outbox 写入同事务提交与后台投递。
4. 测试转绿，贴前后输出。

## 验收标准

- [ ] 一个 Command 只有一次事务提交。
- [ ] 领域事件在提交前分发；分发失败时事务回滚。
- [ ] 事务回滚不留 Outbox 记录。
- [ ] 同一消息投递两次只产生一次副作用。
- [ ] 投递失败可重试，超过阈值标记死信并可查询。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Outbox
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚代码与 outbox 建表脚本（测试库可重建）。

## 任务映射

- worktree：实施时填写（`.worktree/08/`）
- 分支：实施时填写（`feat/08-uow-outbox`）
- 提交：实施时填写（完整 SHA）
