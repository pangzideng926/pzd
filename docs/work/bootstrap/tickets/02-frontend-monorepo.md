状态：进行中
主写入者：PZD-实现Bot-2
更新日期：2026-10-29 18:45 Asia/Shanghai
下一步：交付测试 Bot 复跑验收；等 26–31 号票消费骨架
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

- [x] 根目录 `pnpm install && pnpm build` 成功，退出码 0。
- [x] turbo 日志能看出先 `api#generate` 后 `web#build`。
- [x] 目录结构与 §2 一致，无 `backend/`、`frontend/` 一类嵌套。
- [x] `apps/web` dev 模式能启动并渲染模板首页。

## 验证入口

```bash
pnpm install
pnpm build
pnpm --filter web dev
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 交接（实现 → 测试）

**1. 目标**：证明「模板扁平化到根 + pnpm workspace + Turbo `api#generate → web#build`」这条底座链路真的能跑，且顺序不是靠缓存或巧合排出来的。

**2. 提交**

- 分支：`chore/02-frontend-monorepo`
- 代码提交：`ce36a281e41dafcf8b610bebbf5357d2d0b89352`（38 个文件，`PZD-实现Bot-2 <impl-bot-2@pzd.local>`，无 AI 署名）
- 本票认领与证据：本分支后续提交（`git log -1` 即当前 HEAD）

**3. 验证**（命令、真实退出码、关键输出行，原样粘贴）

模板生成（在系统临时目录跑，只作生成源）：

```
$ npx --yes shadcn@latest init --preset b2oDW9T8C --base radix --template react-router --monorepo --name pzd --yes
√ Creating a new React Router project.
√ Installing dependencies.
Project initialization completed.
```

安装：

```
$ cd .worktree/02 && pnpm install
Scope: all 5 workspace projects
.../esbuild@0.28.2/node_modules/esbuild postinstall: Done
Done in 260ms using pnpm v12.14.0
EXIT=0
```

构建（`--force` 跳过缓存，行号为日志行）：

```
$ pnpm build --force
8:@workspace/api:generate: cache bypass, force executing f1bf3c47d5e7618e
9:@workspace/api:generate: $ node scripts/generate.mjs
10:@workspace/api:generate: api#generate: .../packages/api/openapi.json -> .../packages/api/src/generated/client.ts
11:web:build: cache bypass, force executing 133039258ee23d5f
12:web:build: $ react-router build
...
Tasks:    2 successful, 2 total
Cached:    0 cached, 2 total
EXIT=0
```

测试（假绿防线：贴实际通过用例数）：

```
$ pnpm test
@workspace/api:test: ℹ pass 2
@workspace/api:test: ℹ fail 0
Tasks:    1 successful, 1 total
TEST_EXIT=0
```

两条用例是 `packages/api/test/generate.test.mjs`：①给定一份 openapi.json 能生成含标题/版本/路径的 TS 客户端；②`paths` 缺失时必须抛错而不是静默产出空文件。先跑红（`Cannot find module '.../test'`，生成器未实现）再实现转绿。

类型检查：

```
$ pnpm typecheck
@workspace/ui:typecheck: $ tsc --noEmit
web:typecheck: $ react-router typegen && tsc
Tasks:    2 successful, 2 total
TYPECHECK_EXIT=0
```

dev 首页：

```
$ cd apps/web && pnpm exec react-router dev --host 127.0.0.1 --port 5173
  ➜  Local:   http://127.0.0.1:5173/
$ node -e "fetch('http://127.0.0.1:5173/')..."
status 200 len 4680
含 Project ready!
含 add components 文案
```

**4. 遗留**

- 模板默认是 **npm workspaces**（生成时带 `package-lock.json`、`workspaces` 字段），已按 §1 改成 pnpm：删 `package-lock.json`、加 `pnpm-workspace.yaml`、`packageManager` 改 `pnpm@12.14.0`。`pnpm-lock.yaml` 入库。
- pnpm 12 不认旧的 `onlyBuiltDependencies`，必须用 `allowBuilds: { esbuild: true }`（映射写法），否则 `pnpm install` 直接 `ERR_PNPM_IGNORED_BUILDS` 退出 1。这条已写进 `pnpm-workspace.yaml` 注释。
- 包名沿用模板 scope：`@workspace/ui`、`@workspace/api`、`@workspace/realtime`；`apps/web` 包名保持 `web`，因为票面验证入口是 `pnpm --filter web dev`，改名会让验收命令失效。若要统一成 `@pzd/*`，建议单开一张票，别在本票暗中改。
- 未建根 `README.md`、未配 Vite `/api` `/hubs` 代理：按 03 号票（Aspire 编排）的责任文件范围归 03，避免同一文件两边改。
- 未改 `.gitignore`：模板自带的 `.gitignore` 一度覆盖了仓库版本，已 `git checkout -- .gitignore` 恢复；现有条目已覆盖 `node_modules`、`.turbo`、`build`、`dist`、`.react-router`。
- `packages/api/src/generated/client.ts` 是占位产物，**已入库**，保证干净 clone 直接 `pnpm build` 也能过；28 号票接 orval 时再决定是否改为生成后不入库。
- `packages/api` 目前只有 `generate` 与 `test` 脚本，没有 `typecheck`：本票不引入 TS 源码，真实 TS 入口随 28 号票落地。
- `packages/realtime` 只有 placeholder 导出，`useHubEvent` 等 API 由 30 号票定型。
- 本机 `react-router dev` 只监听 `[::1]:5173`，`curl` 直连 `localhost`/`127.0.0.1` 返回 `000`，改用 `--host 127.0.0.1` 才能被 fetch 到。李总复跑时先 `netstat -ano | grep 5173` 确认监听地址，别把连接失败当成页面渲染失败。

## 恢复方式

删除本票新增文件，重新 `git checkout` 根配置即可；无数据影响。

## 任务映射

- worktree：`.worktree/02/`
- 分支：`chore/02-frontend-monorepo`
- 提交：`ce36a281e41dafcf8b610bebbf5357d2d0b89352`（实现）；票面认领与证据见本分支后续文档提交

