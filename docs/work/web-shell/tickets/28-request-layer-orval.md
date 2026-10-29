状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 16、26 合入后认领
交付状态：待收尾
实施条件：待满足：16、26 合入（前置票）

# 28 请求层：orval 客户端与统一错误拦截

## 目标

接口客户端由 `openapi.json` 生成；所有错误在一个地方统一处理，页面不再各自判状态码。

## 需求依据

- [architecture.md](../../../architecture.md) §8.5、§14.1、§14.3
- [spec.md](../spec.md) AC-05、AC-06、AC-07
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M4.3

## 范围与非范围

- 做：orval 生成 client、TanStack Query hooks、zod schema；自定义 mutator 注入 CSRF 头与版本头；错误映射（401→登录、403→无权限、400/422→字段错误、409→刷新提示、426→强制刷新、503→升级中、500→traceId）；路由级 ErrorBoundary。
- 不做：不做页面、不做重试策略优化、不做离线缓存。

## 责任文件范围（认领时在群里先说清）

`packages/api/**`（orval 配置、mutator）、`apps/web/app/lib/errors.ts`、`apps/web/app/root.tsx`（ErrorBoundary）。不碰后端。

## 关键接口与依赖

- 前置票：16、26
- 对外契约：错误映射函数的签名与返回结构被 29、33 号票的表单使用。

## 步骤

1. 配 orval 读 `openapi.json` 生成客户端。
2. 写 mutator：CSRF 头、版本头、错误分流。
3. 写单测覆盖错误映射函数（422 带/不带 errors，401、409、503）。
4. 改一个后端 DTO 字段，验证前端编译报错（证明类型贯通）。

## 验收标准

- [ ] 客户端由 `openapi.json` 生成，产物入库且可重复生成（无手工改动）。
- [ ] 422 带 `errors` 时映射到字段；不带 `errors` 时落页面级提示。
- [ ] 401 跳登录并带 `redirect`；403/409/426/503/500 各有对应行为。
- [ ] 改后端字段后前端 `pnpm build` 报错。

## 验证入口

```bash
pnpm --filter api generate
pnpm --filter web build
pnpm --filter web test
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚前端与包改动；重新生成客户端即可恢复。

## 任务映射

- worktree：实施时填写（`.worktree/28/`）
- 分支：实施时填写（`feat/28-request-layer-orval`）
- 提交：实施时填写（完整 SHA）
