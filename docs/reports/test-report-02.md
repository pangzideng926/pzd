# 测试报告：票 02（前端 monorepo 骨架）

> 状态：**快照冒烟**，不是终审。被测对象是 `E:\pzd` 工作区里**尚未提交**的骨架（`apps/`、`packages/`、`package.json`、`pnpm-lock.yaml`、`pnpm-workspace.yaml`、`turbo.json`、`tsconfig.base.json` 均为未跟踪文件），
> 为了不干扰旺总正在进行的改动，验证在复制出来的临时副本里做（`C:/tmp/smoke02`，只复制上述 JS 文件，**不含** `node_modules`、`build`）。
> 旺总提交 02 后给我 hash，我按同一组命令在干净副本里复跑一遍再定稿。

## 1. 环境

- Node **v24.20.0**，pnpm **12.14.0**（`packageManager` 字段一致）。
- 副本大小 492 KB（排除 `node_modules`），确认是「全新安装」而不是复用工作区依赖。

## 2. 验收命令与结果

| 命令 | 结果 | 证据 |
|---|---|---|
| `pnpm install --frozen-lockfile` | ✅ 通过 | `Packages: +104`，`reused 68 / downloaded 36`，5.5s 完成；`esbuild postinstall` 正常执行（`pnpm-workspace.yaml` 的 `allowBuilds: esbuild` 生效，否则 Vite 构建会缺二进制） |
| `pnpm -w build` | ✅ 通过 | `Tasks: 4 successful, 4 total`，3.549s；`@pzd/web:build` 输出 `SPA Mode: Generated build\client\index.html`，产物存在 |
| `pnpm -w typecheck` | ✅ 通过 | `Tasks: 7 successful, 7 total`，2.378s；`@pzd/web:typecheck` = `react-router typegen && tsc --noEmit` |
| dev 起服务后 `GET /` | ✅ 200 | `http=200 bytes=1749`，`content-type: text/html`；正文含 `app/root.tsx`、`加载中…`、`window.__reactRouterContext`（`isSpaMode: true`）；HTML 内引用路径为副本 `C:/tmp/smoke02`，**已排除**是工作区里别的 dev 进程在应答。注：当时副本用 `--host 127.0.0.1` 起服务；`127.0.0.1` 返回 `000` 只发生在 host 留空时，见 §3.2 |

## 3. 观察项（low，不阻塞；均已转成票面验收项）

1. **turbo 对 source-only 包报「无输出文件」警告**：`pnpm -w build` 结尾出现
   `WARNING no output files found for task @pzd/api#build`（`@pzd/realtime`、`@pzd/ui` 同样）。原因是这三个包 `build = echo "no emit: source-only package"`，而 `turbo.json` 的 `outputs: ["build/**", "dist/**"]` 匹配不到文件。
   已按票面 `b761c94` 定为验收项：echo 型包在 `turbo.json` 里 `outputs: []`，`pnpm -w build` 输出不得带这类告警（票 11 的脚本要读这段输出）。复验时按「输出无 `no output files` 告警」判定。
2. **`server.host` 留空时 Windows 上只绑 `::1`**：host 留空时 `curl http://127.0.0.1:PORT/` 返回 `http=000`（服务其实已起），这是**绑定差异、不是应用失败**，也不能当作 200 的反例。
   票面终版 `c3d69f1` 定案：`vite.config.ts` 里**显式** `server.host = "127.0.0.1"` + `server.port = 5199` + `strictPort: true`，验收与排查统一探 `http://127.0.0.1:5199/`，与后端 `127.0.0.1:5080` 同一 IPv4 口径；此前「前端一律用 localhost」的写法作废。
   （过程说明：我最初只观察到 host 留空时的 `000`，据此建议 `--host 127.0.0.1`；Chief 实测显式绑 IPv4 后 `127.0.0.1` 是通的，票面据此修正——留空与否才是真因，不是「IPv4 不能用」。）
3. **dev 端口此前写死 5173**：已改为固定 `5199` + `strictPort: true`。撞端口时 dev 会直接失败而不是静默换端口，验收脚本因此不会探到错误实例。
4. **生成物不入库**：`node_modules/`、`apps/web/build/`、`.react-router/`、`*.tsbuildinfo` 已进 `.gitignore`；票面 `c3d69f1` 把它列为验收项（不含 `pnpm-lock.yaml`，lockfile 必须入库）。本轮实测 `git ls-files apps/web/.react-router`、`git ls-files apps/web/build` 均为空。

## 4. 结论

- 票 02 骨架的四条验收命令在**当前工作区快照**上全部通过，`pnpm-lock.yaml` 与 `pnpm-workspace.yaml` 的 `frozen-lockfile` 可重复安装，SPA 产物与 dev 首页正常。
- 待旺总提交 02 给出 hash 后，我在干净副本（`git worktree` + 只装 JS 依赖）按上表复跑，把本文「快照」改为具体 hash 并定稿。
- 未验证项（不属于本轮四条命令）：`lint`（三个包都是 echo 占位）、浏览器运行时水合（无头环境只验了 HTML）、`GET /` 之外的业务路由。
