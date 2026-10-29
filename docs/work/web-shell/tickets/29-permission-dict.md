状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 27 合入后认领
交付状态：待收尾
实施条件：待满足：27 合入（前置票）

# 29 按钮级权限 `<Can>` 与字典 `useDict`

## 目标

页面判断权限和渲染字典有统一入口，不再各写一套字符串比较。

## 需求依据

- [architecture.md](../../../architecture.md) §7.2、§14.2、§14.3
- [spec.md](../spec.md) AC-03
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M4.4

## 范围与非范围

- 做：`<Can permission="...">` 组件、`usePermission()` hook（数据来自 `/api/me` 的 Query 缓存）、`useDict(code)` hook（后端字典接口 V1 未交付时读占位数据并在票里写明）。
- 不做：不做字典维护界面（期外）、不做权限变更实时推送刷新（依赖 30 号票的重连策略）。

## 责任文件范围（认领时在群里先说清）

`apps/web/app/components/can.tsx`、`apps/web/app/hooks/use-permission.ts`、`apps/web/app/hooks/use-dict.ts`、对应单测。不碰 `packages/api` 的生成产物。

## 关键接口与依赖

- 前置票：27
- 对外契约：`<Can>` 与 `usePermission()` 被 33 号票页面使用，签名固定。

## 步骤

1. 实现 `usePermission` 读 `/api/me` 权限码。
2. 实现 `<Can>` 与单测（有权限/无权限/嵌套）。
3. 实现 `useDict` 统一取数与缓存。
4. 贴单测输出。

## 验收标准

- [ ] 无权限时 `<Can>` 不渲染子节点。
- [ ] `usePermission()` 与 `<Can>` 结果一致，有单测。
- [ ] `useDict` 同一 code 多次调用只发一次请求。
- [ ] 字典数据的占位方案在票里写明（后端字典期外）。

## 验证入口

```bash
pnpm --filter web test
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚组件与 hook。

## 任务映射

- worktree：实施时填写（`.worktree/29/`）
- 分支：实施时填写（`feat/29-permission-dict`）
- 提交：实施时填写（完整 SHA）
