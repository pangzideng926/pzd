# 审计报告：票 02（前端 monorepo 骨架）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Spec / Standards 两轴）+ 实跑验收 + 变异
> 被测：`2f48e7d`（骨架由 `1dc8f74` 引入；`df94f80` 清出误入库的 `.react-router/types`；`aaf077f` 固定端口与绑定；`2f48e7d` 为当前 HEAD，与前端文件无关）
> 依据：[`tickets.md`](../specs/tickets.md) 票 02、[`architecture.md`](../architecture.md) §14.1/§14.2/§14.5、[`foundation.md`](../specs/foundation.md) §9 偏差记录
> **结论：需修改。** 骨架本身与四条验收命令我全部实跑通过（install / build / typecheck / dev），但票面第 8 条「turbo 对 echo 型包不应报 `no output files`」**未实现**——缓存命中把它掩盖了，冷缓存下 `build` 与 `typecheck` 都会打三条 WARNING。另 4 条 low。

---

## 1. 基线（我实跑）

| 项 | 环境 | 结果 |
|---|---|---|
| `pnpm install --frozen-lockfile` | 主仓 + 干净 worktree `2f48e7d` | exit 0（lockfile v9，5 个 importer：`.``/`apps/web`/`packages/api|realtime|ui`） |
| `pnpm -w build` | 同上 | exit 0，`4 successful, 4 total`，产出 `apps/web/build/client/index.html`，`SPA Mode: Generated build\client\index.html` |
| `pnpm -w typecheck` | 同上 | exit 0，`7 successful, 7 total`；删掉 `apps/web/.react-router` 后强制重跑仍 7/7（`react-router typegen` 先生成类型） |
| `pnpm --filter @pzd/web dev` + `GET http://127.0.0.1:5199/` | 主仓 | **200**，`<html lang="zh-CN">`，非错误页 |
| 工具版本 | 本机 | node 24.20.0（`engines: >=22` ✓）、pnpm 12.14.0（`packageManager` ✓） |

## 2. 票面验收逐条核对

| # | 验收项 | 结果 |
|---|---|---|
| 1 | `pnpm install --frozen-lockfile` 退出码 0 | **通过**（主仓与干净 worktree 各一次） |
| 2 | `pnpm -w build` 退出码 0，`apps/web/build/client` 产出 | **通过** |
| 3 | `pnpm -w typecheck` 退出码 0，TS strict | **通过**；变异「给 `packages/ui` 加 `const BROKEN: number = "not a number"`」→ `Failed: @pzd/ui#typecheck`、exit 2，strict 是真生效 |
| 4 | dev 起服务后 `GET /` 200 且不是错误页 | **通过**（`127.0.0.1:5199`、`lang="zh-CN"`；SPA 模式下 `<title>` 由客户端 `meta()` 设置，静态 HTML 里没有属预期） |
| 5 | 端口与绑定显式写死：`port = 5199`、`host = "127.0.0.1"`、strictPort | **通过**（`vite.config.ts` 三项都在，注释也写清了 host 留空只绑 `::1` 的坑） |
| 6 | 包关系正确、lockfile 提交 | **通过**（web 声明三个 workspace 包；见 F-02-4 的措辞边界） |
| 7 | 产物与生成物不入库 | **通过**（`.gitignore` 覆盖 `node_modules/`、`build/`、`.react-router/`、`*.tsbuildinfo`、`.turbo/`；HEAD 树里查无 build/生成物。`1dc8f74` 曾把 `.react-router/types` 5 个文件带进仓库，`df94f80` 已删除，现为未跟踪） |
| 8 | 生产 build 链路无噪音：echo 型包不得报 `no output files` | **未通过 → F-02-1** |
| 9 | shadcn preset 失败则退化并在 foundation §9 记录偏差 | **通过**（§9 记了「交互式命令在非 TTY 下无法完成」、`packages/ui` 只有包骨架与导出面、剩余项归步骤 4、以及 pnpm 12 `esbuild` 的 `allowBuilds` 偏差） |
| 10 | `vite.config.ts` dev proxy `/api`、`/hubs` → `127.0.0.1:5080` | **通过**（`/hubs` 带 `ws: true`） |
| 11 | 不含登录页、侧边栏、多页签 | **通过**（`app/root.tsx` + `routes.ts` + 一个占位 `home.tsx`） |

## 3. 发现

### F-02-1 · medium（必修）· echo 包的 `no output files` 警告没消除，只是被缓存藏住了

- **位置**：[`turbo.json:4-7`](../../turbo.json#L4-L7)（只有全局 `build` 的 `outputs: ["build/**", "dist/**"]`）；`packages/api|realtime|ui` 的 `build` 是 `echo`，不产出任何文件。
- **实测**（干净 worktree，非缓存路径）：
  ```
  pnpm exec turbo run build --force
  WARNING  no output files found for task @pzd/api#build.      Please check your `outputs` key in `turbo.json`
  WARNING  no output files found for task @pzd/realtime#build. ...
  WARNING  no output files found for task @pzd/ui#build.       ...
  ```
  `pnpm -w typecheck --force` 同样会打这三条——因为 `turbo.json` 里 `typecheck` 有 `dependsOn: ["^build"]`，先把三个 echo 包 `build` 跑一遍。
- **为什么之前「看不到」**：`pnpm -w build` 走的是缓存命中（`4 cached ... FULL TURBO`），回放的是包内构建日志，不含 turbo 自己的 WARNING。CI 首次构建、`--force`、改依赖后失配都会打出来——正是票面第 8 条要防的场景（票 11 的一键脚本要读这段输出）。
- **为什么错**：票面把消除方式写死了（「按包在 `turbo.json` 里把这些 task 的 `outputs` 设为 `[]`」），实现没做；验收词与代码对不上，且症状在冷缓存时真实出现。
- **修法**（我验证过可行）：给三个 echo 包各加一份 `packages/<pkg>/turbo.json`：
  ```json
  { "extends": ["//"], "tasks": { "build": { "outputs": [] } } }
  ```
  我只给 `packages/ui` 加了一份并 `turbo run build --force` → `@pzd/ui#build` 那条 WARNING 消失，只剩 api/realtime 两条，证明按包覆盖生效。三个包都加即清空。加完请把 `pnpm -w build` 与 `pnpm -w typecheck` 各跑一次 `--force`，确认零 WARNING。

### F-02-2 · low · 每次 build 都有 5 条 React Router v8 future-flag 告警

- **位置**：`apps/web/react-router.config.ts`（未设任何 `future` 标记）+ RR 7.18 的构建日志。
- **实测**：`pnpm -w build` 输出固定 5 条 `⚠️ Future Flag Warning: … changing in React Router v8`（route middleware、route module splitting、Vite Environment API、passThroughRequests、trailingSlashAwareDataRequests）。
- **为什么算问题**：它们不是票面点名的 `no output files`，但同样污染 `pnpm -w build` 输出，票 11 的脚本若按「输出里出现 WARNING 即失败」判定会误红。
- **建议**：票 11 用**退出码**判定而不是字符串匹配（推荐）；或在 `react-router.config.ts` 显式落 `future` 标记——但那是行为选择（会改变路由/数据加载语义），要老何定，不要为了消警告顺手开。

### F-02-3 · low · 端口写死在两处，改一处会漂

- **位置**：[`apps/web/vite.config.ts`](../../apps/web/vite.config.ts#L12-L16)（`port: 5199`）与 [`apps/web/package.json`](../../apps/web/package.json#L7)（`"dev": "react-router dev --port 5199"`）。
- **问题**：两处字面值；CLI `--port` 优先级高于配置，两处不一致时以 `--port` 为准，`strictPort` 下的表现是「换个端口起不来」而不是报错说明。
- **建议**：dev 脚本去掉 `--port 5199`，端口只留 `vite.config.ts` 一处（票面要求的「写死」本来就在配置里）。

### F-02-4 · low · web 声明了三个 workspace 依赖，但当前零 import

- **位置**：[`apps/web/package.json`](../../apps/web/package.json#L13-L17) 的 `@pzd/ui`、`@pzd/api`、`@pzd/realtime`。
- **问题**：票面验收写「web 依赖 ui/api/realtime 中**实际需要的**包」，骨架阶段三个都没被 import，`pnpm -w build` 也不校验它们被用到；现在等于「先声明、后使用」的预留。
- **判断**：不阻塞（`turbo typecheck` 会覆盖三个包源码，不产生死代码）；但要么在占位页引用一次（例如渲染 `UI_PACKAGE`）证明链路通，要么在票面把「预留」写明，免得后续以为已验证。

### F-02-5 · low · `lint` 是 echo 占位，`pnpm -w lint` 恒 0

- **位置**：根 `package.json` 的 `lint`、四个 workspace 包的 `lint`（`echo "...: no linter yet"`）。
- **问题**：票面只要求根脚本里有 `lint`（未要求真 linter），但 `pnpm -w lint` 通过不代表任何东西；票 11 的一键验收若把它当门禁，是假的绿灯。建议在票 11 的脚本里明确「lint 本轮不参与判定」，或标注 TODO。

## 4. Standards 轴（简述）

- 命名与注释：`@pzd/*` 与后端命名空间一致；`vite.config.ts` 的 host 注释、`react-router.config.ts` 的 `ssr:false` 理由都直接引架构 §14.1/§14.5，符合本仓库「注释说明为什么」的既有风格。
- TypeScript 严格度高于票面要求：`strict` + `noUncheckedIndexedAccess` + `exactOptionalPropertyTypes` + `verbatimModuleSyntax`，`web` 还带 `rootDirs` 修 typegen 虚拟路径——这是正确的做法（比我要求的高）。
- 无过度抽象：三个包只有导出面与 README，没有提前造空壳 API，符合「步骤 4 再长」的口径。
- 唯一实质 Standards 问题是 F-02-1（同一事实在票面与实现里不一致）与 F-02-3（同一端口两处定义）。

## 5. 结论

- **必须修**：**F-02-1**（三个 echo 包按包设 `outputs: []`，用 `--force` 验证 build 与 typecheck 均零 WARNING）。
- **建议随本轮或票 11 处理**：F-02-2（票 11 用退出码判定）、F-02-3（端口只留一处）、F-02-4（引用一次或票面写明预留）、F-02-5（lint 不算门禁）。
- 修完给我 hash，我只复核 `turbo run build --force` / `turbo run typecheck --force` 的 WARNING 是否清零 + 其余文件未回退，不再重跑四条验收。
