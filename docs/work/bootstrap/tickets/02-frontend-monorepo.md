状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：可直接认领开工，可与 01 并行
交付状态：待收尾
实施条件：已具备

# 02 前端 monorepo 骨架（shadcn react-router 扁平化）

## 目标

建出与后端同级的前端 monorepo：`apps/web` + `packages/{ui,api,realtime}`，Turbo 任务链可跑通。

## 需求依据

- [architecture.md](../../../architecture.md) §1、§14.1
- [spec.md](../spec.md) AC-03
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.2

## 范围与非范围

- 做：用 shadcn 官方模板初始化（preset `b2oDW9T8C`、`--base radix`、`--template react-router --monorepo`）后扁平化到根；`pnpm-workspace.yaml`、`turbo.json`、根 `package.json` 脚本；`api#generate → web#build` 任务依赖（本票 `api#generate` 读占位 `openapi.json`）。
- 不做：不写业务页面、不做认证与路由守卫（26 号票）、不接真实接口。

## 责任文件范围（认领时在群里先说清）

新建 `package.json`、`pnpm-workspace.yaml`、`turbo.json`、`apps/web/**`、`packages/ui/**`、`packages/api/**`、`packages/realtime/**`。不碰 `src/`、`tests/`、`db/`。

## 关键接口与依赖

- 前置票：无（可立即开工）
- 对外契约：向 26–31 号票提供 `apps/web` 与三个包的骨架；`packages/api` 的生成入口在本票定型。

## 步骤

1. 跑官方模板初始化命令，记录完整命令与输出。
2. 把生成目录扁平化到根，删除多余嵌套，核对与 §2 一致。
3. 配 turbo 任务依赖 `api#generate → web#build`，占位 `openapi.json` 放在包内。
4. `pnpm install && pnpm build` 跑通并贴输出。

## 验收标准

- [ ] 根目录 `pnpm install && pnpm build` 成功，退出码 0。
- [ ] turbo 日志能看出先 `api#generate` 后 `web#build`。
- [ ] 目录结构与 §2 一致，无 `backend/`、`frontend/` 一类嵌套。
- [ ] `apps/web` dev 模式能启动并渲染模板首页。

## 验证入口

```bash
pnpm install
pnpm build
pnpm --filter web dev
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除本票新增文件，重新 `git checkout` 根配置即可；无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/02/`）
- 分支：实施时填写（`chore/02-frontend-monorepo`）
- 提交：实施时填写（完整 SHA）
