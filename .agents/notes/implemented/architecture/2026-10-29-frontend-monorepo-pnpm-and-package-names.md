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

## SPA 模式与回环地址

审计返工时补的两条，和上面的选型同属「模板默认与架构不一致」这一类，记在这里，不另开篇。

- **`ssr: false`，不留服务端渲染**：`react-router.config.ts` 用 SPA 模式（§1、§14.1）。模板默认 `ssr: true`，产物是 `build/server/index.js`、`build/client` 里没有 `index.html`；而 §14.5 要把 `build/client` 拷进 `Pzd.Api` 的 wwwroot 同源部署，§15.2 的版本提示也要纯客户端产物。同时删掉 `start` 脚本与 `@react-router/serve`——SPA 没有 Node 服务可起。
  - 备选 **保留 SSR** — 最强理由：首屏更快、模板默认即可用。没选，是因为 §1/§14.1 已写死 SPA，改要维护者拍板改架构；本票只是骨架。
- **`vite.config.ts` 固定 `server.host: "127.0.0.1"`**：SPA 模式构建时会预渲染 `/`，本地起临时服务并**默认连 `::1`**；在禁用 IPv6 回环的环境（本机 Bot 沙箱就是）直接 `Prerender: Request failed for /: connect EACCES ::1`，构建退出 1。A/B 验证过：不加就红，加了输出 `SPA Mode: Generated build\client\index.html` 退出 0。
  - 备选 **构建时加 `NODE_OPTIONS=--dns-result-order=ipv4first`** — 最强理由：不动仓库文件，纯环境变量。没选，是因为它要求每个跑构建的人（李总、老登、CI）都记得带，漏一个就红；写进配置才是默认正确的。

**重访信号**：§14.5 的部署方式若改成 Node 托管前端（不再拷 `build/client`），或环境恢复 IPv6 回环支持时，可以重新评估这两条。
## Verification

```bash
cd .worktree/02
pnpm install                # 退出码 0，esbuild postinstall 有 "Done"
pnpm build --force          # 日志里 @workspace/api:generate 行号 < web:build 行号
pnpm test --force           # @workspace/api:test pass 2 / fail 0
```

再补一条：`ls apps/web/build/client/index.html` 必须存在，且 `git status --porcelain` 在 `node packages/api/scripts/generate.mjs` 之后为空。
