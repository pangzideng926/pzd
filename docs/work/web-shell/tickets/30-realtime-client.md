状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 08、28 合入后认领
交付状态：待收尾
实施条件：待满足：08、28 合入（前置票）

# 30 实时客户端封装（SignalR）

## 目标

前端能收到服务端推送的失效信号并在重连后自动补齐数据。

## 需求依据

- [architecture.md](../../../architecture.md) §10.5
- [spec.md](../spec.md) AC-08
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M4.5

## 范围与非范围

- 做：`packages/realtime`：连接封装、`withAutomaticReconnect()`、`useHubEvent`、重连成功后统一失效关键查询、连接状态暴露给页头。
- 不做：不做逐消息状态同步（只推轻量失效信号）；不做离线队列。

## 责任文件范围（认领时在群里先说清）

`packages/realtime/**`、`apps/web/app/hooks/use-realtime.ts`、页头连接状态接入。不碰 `packages/api`。

## 关键接口与依赖

- 前置票：08、28
- 对外契约：`useHubEvent(event, handler)` 签名被后续业务页面依赖。

## 步骤

1. 封装连接与重连策略。
2. 实现 `useHubEvent` 与查询失效。
3. 用一个测试 Hub 事件验证：断网重连后查询自动刷新。
4. 贴验证步骤与结果。

## 验收标准

- [ ] 断线后自动重连，页头状态正确变化。
- [ ] 重连成功后关键查询被失效并重新拉取。
- [ ] 收到事件时只失效指定查询，不做全量刷新。
- [ ] 重复连接不产生多条连接（StrictMode 下也成立）。

## 验证入口

```bash
pnpm --filter web build
pnpm --filter web test（手工断网验证步骤贴票）
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚包与 hook。

## 任务映射

- worktree：实施时填写（`.worktree/30/`）
- 分支：实施时填写（`feat/30-realtime-client`）
- 提交：实施时填写（完整 SHA）
