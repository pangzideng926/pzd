状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 02、28 合入后认领
交付状态：待收尾
实施条件：待满足：02、28 合入（前置票）

# 31 版本更新检测与提示刷新

## 目标

发布新版本时用户能知道并可刷新；强制更新时逐一刷新；多标签同步；资源 404 时兜底刷新而不是白屏。

## 需求依据

- [architecture.md](../../../architecture.md) §15.2
- [spec.md](../spec.md) AC-09、AC-10
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M4.6

## 范围与非范围

- 做：四路检测（`version.json` 轮询、`visibilitychange`、响应头 `X-App-Build`、SignalR 通知）、普通提示与强制刷新两种对话框、BroadcastChannel 多标签同步、`vite:preloadError` 兜底、静态资源缓存头与旧 assets 保留期配置。
- 不做：不做后端发布流水线（§15.4 另立）、不做灰度发布配置。

## 责任文件范围（认领时在群里先说清）

`apps/web/app/lib/version.ts`、版本提示组件、`apps/web/vite.config.ts`（缓存头）、`packages/api` 的版本头读取（只读取）。

## 关键接口与依赖

- 前置票：02、28
- 对外契约：`version.json` 的文件名与字段（`version`、`buildTime`）固定，与 16 号票的构建产物对齐。

## 步骤

1. 实现版本比较与检测四路入口（可分别开关）。
2. 实现普通/强制两种提示与刷新行为。
3. 接 BroadcastChannel 与 `vite:preloadError`。
4. 写单测覆盖版本比较函数，手工验证提示与刷新。

## 验收标准

- [ ] `version.json` 版本变化后出现更新提示，点刷新加载新构建。
- [ ] 强制更新时对话框不可关闭（除刷新）。
- [ ] 多标签页同时收到提示。
- [ ] `vite:preloadError` 触发整页刷新而非白屏。
- [ ] 静态资源缓存头与旧 assets 保留期有明确配置值与注释理由。

## 验证入口

```bash
pnpm --filter web test
pnpm --filter web build（手工改 version.json 验证）
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚前端改动与缓存头配置。

## 任务映射

- worktree：实施时填写（`.worktree/31/`）
- 分支：实施时填写（`feat/31-version-detection`）
- 提交：实施时填写（完整 SHA）
