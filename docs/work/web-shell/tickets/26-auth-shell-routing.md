状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 23、02 合入后认领
交付状态：待收尾
实施条件：待满足：23、02 合入（前置票）

# 26 认证页与受保护区路由骨架

## 目标

用户有地方登录；未登录访问内部地址被带回登录页并在登录后回到原处。

## 需求依据

- [architecture.md](../../../architecture.md) §14.2
- [spec.md](../spec.md) AC-01
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M4.1

## 范围与非范围

- 做：`routes.ts` 分 `_auth`（登录、忘记密码占位）与 `_app`；`_app` 的 `clientLoader` 请求 `/api/me`，未登录 redirect 并带 `redirect` 参数；登录表单接后端。
- 不做：不做布局与页签（27 号票）、不做按钮权限（29 号票）、不做忘记密码真实流程。

## 责任文件范围（认领时在群里先说清）

`apps/web/app/routes.ts`、`apps/web/app/routes/_auth/**`、`apps/web/app/routes/_app.tsx`。不碰 `packages/*`。

## 关键接口与依赖

- 前置票：23、02
- 对外契约：路由树结构被 27、29、33 号票追加子路由；`redirect` 参数约定固定。

## 步骤

1. 建 `_auth`、`_app` 两组路由与 `clientLoader`。
2. 接登录端点，成功后跳回 `redirect`。
3. 未登录访问 `_app` 下任意路由验证重定向。
4. 贴浏览器实测步骤与结果。

## 验收标准

- [ ] 未登录访问 `/system/user` 被重定向到登录页。
- [ ] 登录成功后回到 `/system/user`。
- [ ] 已登录访问登录页时被送回 `_app` 首页。
- [ ] `_app` 的 loader 失败有可见错误，不白屏。

## 验证入口

```bash
pnpm --filter web build
pnpm --filter web dev（手工走一遍并用截图/步骤记录）
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚前端改动。

## 任务映射

- worktree：实施时填写（`.worktree/26/`）
- 分支：实施时填写（`feat/26-auth-shell-routing`）
- 提交：实施时填写（完整 SHA）
