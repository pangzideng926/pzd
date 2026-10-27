# 审计报告：票 02（前端 monorepo 骨架）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Spec / Standards 两轴）+ 实跑验收 + 变异
> 被测：`10dd9bc`（前端代码 = `1588559`）：骨架 `1dc8f74` → 端口/绑定 `aaf077f` → 清出 `.react-router/types` `df94f80` → **F-02-1 修复 `57c226e`** → **F-02-3/4 修复 `1588559`** → 票据/文档 `10dd9bc`
> 依据：[`tickets.md`](../specs/tickets.md) 票 02、[`architecture.md`](../architecture.md) §14.1/§14.2/§14.5、[`foundation.md`](../specs/foundation.md) §9 偏差记录
> **结论：通过（复核 `10dd9bc` 即前端 `1588559`；四条验收命令全绿）。** 首轮我判「需修改」的唯一必修 **F-02-1**（echo 包 `no output files` 告警）已在 `57c226e` 按包级 `turbo.json` 修好，我用 `--force` 冷缓存复核 build 与 typecheck **均零 `no output files` WARNING**；F-02-3、F-02-4 同批闭环。剩 F-02-2、F-02-5 两条**登记项**（不阻塞，见 §3、§5）。

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
| 8 | 生产 build 链路无噪音：echo 型包不得报 `no output files` | **通过（复核）**：首轮未通过（F-02-1），`57c226e` 修好后 `--force` 冷缓存零 WARNING |
| 9 | shadcn preset 失败则退化并在 foundation §9 记录偏差 | **通过**（§9 记了「交互式命令在非 TTY 下无法完成」、`packages/ui` 只有包骨架与导出面、剩余项归步骤 4、以及 pnpm 12 `esbuild` 的 `allowBuilds` 偏差） |
| 10 | `vite.config.ts` dev proxy `/api`、`/hubs` → `127.0.0.1:5080` | **通过**（`/hubs` 带 `ws: true`） |
| 11 | 不含登录页、侧边栏、多页签 | **通过**（`app/root.tsx` + `routes.ts` + 一个占位 `home.tsx`） |

## 3. 发现

### F-02-1 · medium（必修）· echo 包的 `no output files` 警告没消除，只是被缓存藏住了

> **状态：已闭环（`57c226e`）。** 三个包各加 `packages/<pkg>/turbo.json`（`extends: ["//"]`、`tasks.build.outputs: []`，三份内容一致，md5 相同）。我在干净 worktree `022fd4e`（及后续 `10dd9bc`）按 `--force` 冷缓存复核：`turbo run build --force` **零 `no output files` WARNING**、`turbo run typecheck --force`（含 `dependsOn: ^build`）**同样零 WARNING**。以下为原始发现，保留作证据。

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

### F-02-2 · low（登记项）· 每次 build 都有 5 条 React Router v8 future-flag 告警

> **状态：登记，不阻塞。** 属 React Router 7→8 的迁移提示，不是本票引入的缺陷；判定责任在票 11（用退出码）。

- **位置**：`apps/web/react-router.config.ts`（未设任何 `future` 标记）+ RR 7.18 的构建日志。
- **实测**：`pnpm -w build` 输出固定 5 条 `⚠️ Future Flag Warning: … changing in React Router v8`（route middleware、route module splitting、Vite Environment API、passThroughRequests、trailingSlashAwareDataRequests）。
- **为什么算问题**：它们不是票面点名的 `no output files`，但同样污染 `pnpm -w build` 输出，票 11 的脚本若按「输出里出现 WARNING 即失败」判定会误红。
- **建议**：票 11 用**退出码**判定而不是字符串匹配（推荐）；或在 `react-router.config.ts` 显式落 `future` 标记——但那是行为选择（会改变路由/数据加载语义），要老何定，不要为了消警告顺手开。

### F-02-3 · low · 端口写死在两处，改一处会漂

> **状态：已闭环。** `apps/web/package.json` 的 `dev` 现在就是 `react-router dev`，端口只在 `vite.config.ts` 一处。

- **位置**：[`apps/web/vite.config.ts`](../../apps/web/vite.config.ts#L12-L16)（`port: 5199`）与 [`apps/web/package.json`](../../apps/web/package.json#L7)（`"dev": "react-router dev --port 5199"`）。
- **问题**：两处字面值；CLI `--port` 优先级高于配置，两处不一致时以 `--port` 为准，`strictPort` 下的表现是「换个端口起不来」而不是报错说明。
- **建议**：dev 脚本去掉 `--port 5199`，端口只留 `vite.config.ts` 一处（票面要求的「写死」本来就在配置里）。

### F-02-4 · low · web 声明了三个 workspace 依赖，但当前零 import

> **状态：已闭环。** `app/routes/home.tsx` 现在 import 了 `API_PACKAGE`、`REALTIME_PACKAGE`、`UI_PACKAGE` 三个占位导出，workspace 链路在构建与渲染路径上真被走到。

- **位置**：[`apps/web/package.json`](../../apps/web/package.json#L13-L17) 的 `@pzd/ui`、`@pzd/api`、`@pzd/realtime`。
- **问题**：票面验收写「web 依赖 ui/api/realtime 中**实际需要的**包」，骨架阶段三个都没被 import，`pnpm -w build` 也不校验它们被用到；现在等于「先声明、后使用」的预留。
- **判断**：不阻塞（`turbo typecheck` 会覆盖三个包源码，不产生死代码）；但要么在占位页引用一次（例如渲染 `UI_PACKAGE`）证明链路通，要么在票面把「预留」写明，免得后续以为已验证。

### F-02-5 · low（登记项）· `lint` 是 echo 占位，`pnpm -w lint` 恒 0

> **状态：登记，不阻塞。** 票面 `d32eb67` 已标注「lint 是占位、不得当证据」。

- **位置**：根 `package.json` 的 `lint`、四个 workspace 包的 `lint`（`echo "...: no linter yet"`）。
- **问题**：票面只要求根脚本里有 `lint`（未要求真 linter），但 `pnpm -w lint` 通过不代表任何东西；票 11 的一键验收若把它当门禁，是假的绿灯。建议在票 11 的脚本里明确「lint 本轮不参与判定」，或标注 TODO。

## 4. Standards 轴（简述）

- 命名与注释：`@pzd/*` 与后端命名空间一致；`vite.config.ts` 的 host 注释、`react-router.config.ts` 的 `ssr:false` 理由都直接引架构 §14.1/§14.5，符合本仓库「注释说明为什么」的既有风格。
- TypeScript 严格度高于票面要求：`strict` + `noUncheckedIndexedAccess` + `exactOptionalPropertyTypes` + `verbatimModuleSyntax`，`web` 还带 `rootDirs` 修 typegen 虚拟路径——这是正确的做法（比我要求的高）。
- 无过度抽象：三个包只有导出面与 README，没有提前造空壳 API，符合「步骤 4 再长」的口径。
- 唯一实质 Standards 问题是 F-02-1（同一事实在票面与实现里不一致）与 F-02-3（同一端口两处定义）。

## 5. 结论

**票 02：通过（复核 `10dd9bc`，前端代码 = `1588559`）。**

- **必须修 0 项**：F-02-1 已在 `57c226e` 闭环（包级 `turbo.json` + `outputs: []`，我用 `--force` 冷缓存验到 build/typecheck 零 `no output files` WARNING）；F-02-3（端口单处）、F-02-4（三个包被 import）同批闭环。
- **四条验收命令复核（干净 worktree `10dd9bc`，即 `1588559` 的前端代码）**：`pnpm install --frozen-lockfile` exit 0；`turbo run build --force` 4/4、产出 `apps/web/build/client/index.html`；`turbo run typecheck --force` 7/7；dev 在 `127.0.0.1:5199` 上 `GET /` = **200**、`<html lang="zh-CN">`（SPA 模式下静态 HTML 无 `<title>`、无正文文案属预期）。
- **登记项（不阻塞）**：
  - **F-02-2**：每轮 build 仍有 5 条 React Router v8 future-flag `⚠️` 告警。**票 11 的一键脚本必须用退出码判定，不要按输出里出现 `WARNING`/`⚠️` 判失败**；要不要落 `future` 标记属行为选择，由老何定。
  - **F-02-5**：`lint` 是 echo 占位，`pnpm -w lint` 恒 0，不得当质量证据（票面 `d32eb67` 已标注同一口径）。
- 偏差记录：shadcn preset 未执行（交互式 + 仓库根已按扁平结构铺好，跑模板会生成第二套根结构），`packages/ui` 只有导出面，剩余项随步骤 4 —— foundation §9 与 `2c60daa` 已写清，**报告不得写成「shadcn 已接入」**。
