# Agent Note: 前端 monorepo 用 pnpm，包名沿用模板的 @workspace scope

Status: implemented

## Problem

[architecture.md](../../../../docs/architecture.md) §1 与 §14.1 指定前端由 shadcn 官方命令 `--template react-router --monorepo` 生成，同时指定包管理是 pnpm workspace + Turborepo。两条要求直接冲突：该模板 2026-10-29 生成出来的是 **npm workspaces** 结构——根 `package.json` 带 `workspaces` 字段、`packageManager: npm@11.19.1`、仓库里落了一份 `package-lock.json`，没有 `pnpm-workspace.yaml`。

不处理会怎样：`pnpm install` 虽然能靠 `workspaces` 字段认出子包，但锁文件、安装命令、CI 与文档会分成 npm 和 pnpm 两套说法；再往后 26–31 号票都要在这套骨架上写代码，越晚改代价越大。

## Decision

1. **包管理器统一为 pnpm**：删掉模板的 `package-lock.json` 与 `workspaces` 字段，新增 `pnpm-workspace.yaml`（`apps/*`、`packages/*`），`packageManager` 写 `pnpm@12.14.0`，`pnpm-lock.yaml` 入库。根脚本 `build / dev / generate / test / format / typecheck` 一律走 turbo。
2. **pnpm 12 必须用 `allowBuilds`，不是 `onlyBuiltDependencies`**：`pnpm-workspace.yaml` 里写
   ```yaml
   allowBuilds:
     esbuild: true
   ```
   旧键名在 pnpm 12 里会被解析但**不生效**，`pnpm install` 直接以 `ERR_PNPM_IGNORED_BUILDS` 退出 1。
3. **包名沿用模板 scope**：`@workspace/ui`、`@workspace/api`、`@workspace/realtime`；`apps/web` 的包名保持 `web`，不改成 `@pzd/web`。
4. **turbo 的跨包顺序在 `turbo.json` 里显式写**：`web#build` 的 `dependsOn` 含 `@workspace/api#generate`，因此 `api#generate` 一定先于 `web#build` 执行。

## Alternatives considered

- **保留 npm workspaces，改架构书选型** — 最强理由：模板原样落地最省事，npm 自带的 workspaces 够用。没选，是因为 §1 已把 pnpm + Turborepo 定为结论，改选型得维护者拍板，而本票只是工程底座，不该顺手改架构。
- **只加 `pnpm-workspace.yaml`、保留 `workspaces` 字段与 `package-lock.json`** — 最强理由：两种客户端都能装，过渡期谁都不挡。没选，是因为同一仓库两份锁文件必然漂移，`pnpm install` 与 `npm install` 装出的依赖树不同，问题会以「我这儿能跑」的形式出现。
- **把三个包统一改名成 `@pzd/*`** — 最强理由：与后端 `Pzd.*` 命名对齐，一眼看出属于本项目。没选，是因为改名要同步 `apps/web` 的 import、两个 `components.json` 的别名和 tsconfig paths，而票面验收命令写的是 `pnpm --filter web dev`；要统一就单开一张票，别在骨架票里暗中改契约。
- **`packageManager` 交给 corepack 自动选 pnpm 最新版** — 最强理由：少一个要维护的版本号。没选，是因为本机 pnpm 是 12.14.0，锁住版本才能让李总和老登复跑出同样的结果。

## Consequences

- **收益**：一个仓库一套锁文件和一条安装命令；`pnpm build` 的任务顺序由 `turbo.json` 决定而不是靠目录顺序；后续 26–31 号票的验证入口在本票就固定下来。
- **代价**：模板自带的 `package-lock.json` 被删，`npx shadcn add` 之后再新增组件时要用 `pnpm dlx shadcn@latest add`，不能再用 npm 语义；`@workspace/*` 这个 scope 与仓库名 pzd 不一致，读代码的人会疑惑一次。
- **重访信号**：①pnpm 升到不再支持 `allowBuilds` 的版本；②26–31 号票里有两张以上因为 `@workspace/*` scope 与后端命名不一致而写错 import；③CI（04 号票）需要 npm 才能跑某一步。

## Verification

```bash
cd .worktree/02
pnpm install                # 退出码 0，esbuild postinstall 有 "Done"
pnpm build --force          # 日志里 @workspace/api:generate 行号 < web:build 行号
pnpm test                   # @workspace/api:test pass 2 / fail 0
```
