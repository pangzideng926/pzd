# 测试报告：票 02（前端 monorepo 骨架）

> **定稿依据：干净 `git worktree` @ `4bb7218`（当前 HEAD）**，四条命令全部在该 hash 的副本上、以 **`127.0.0.1:5199`** 正式跑通（见 §2、§2b）。
> 历史 hash 关系：`57c226e` 修掉 `no output files` 告警 → `d32eb67` 首次在 5199 上跑通四条（当时端口空出）→ `c0da821` 复核 F-02-1/F-02-3（§6）→ `4bb7218` 为本次最终定稿，与 `c0da821` 代码树一致、只差 README。
> 早前入库的 `bbfe7fd` 版本自述为「快照/待定稿」，以本文件为准。

## 1. 环境与复现入口

```
git worktree add --detach C:/tmp/head02 4bb7218
cd C:/tmp/head02
pnpm install --frozen-lockfile
pnpm -w build --force        # 必须 --force：否则 turbo 缓存日志会假装构建过
pnpm -w typecheck --force
pnpm exec react-router dev   # 走 vite.config：host 127.0.0.1 + port 5199 + strictPort
```

- Node **v24.20.0**，pnpm **12.14.0**（与 `packageManager` 一致）。
- 干净检出：`node_modules`、`apps/web/build`、`.react-router` 均不在库内，安装与构建从零开始。

## 2. 四条验收命令与结果（正式定稿）

| 命令 | 结果 | 证据 |
|---|---|---|
| `pnpm install --frozen-lockfile` | ✅ 通过 | 退出 0；`+104` 包，`esbuild postinstall` 正常（`allowBuilds: esbuild` 生效） |
| `pnpm -w build --force` | ✅ 通过 | `Tasks: 4 successful, 4 total`，`Cached: 0`（真实构建）；`SPA Mode: Generated build\client\index.html`，产物 1462 字节；**`no output files found` 计数 = 0** |
| `pnpm -w typecheck --force` | ✅ 通过 | `Tasks: 7 successful, 7 total`，`Cached: 0`，无 `no output files` 告警 |
| dev 起服务 + `GET /` | ✅ 200 | 副本按 `vite.config` 起在 **`127.0.0.1:5199`**（`LISTENING`），`http=200 bytes=1747`；HTML 含 `lang="zh-CN"`、`isSpaMode`、`加载中…`，引用路径为副本 `C:/tmp/head02`（证明是本副本在应答） |

本次 `4bb7218` 的实测计数：`build --force` → 4/4、`Cached: 0`、`no output files` **0**；`typecheck --force` → 7/7、`Cached: 0`、告警 **0**；dev 在 5199 上真实监听并 200。端口在此前的复验里多次被他人 dev 实例占用（pid 99312 / 125456 / 125412），`strictPort` 按设计报 `Port 5199 is already in use`；占用释放后本次定稿即在该端口完成，结论不依赖替代端口。

## 3. 票面验收项逐条对照

1. **端口与绑定**（`c3d69f1`）：`vite.config.ts` 为 `host: "127.0.0.1"` + `port: 5199` + `strictPort: true`，`dev` 脚本 `react-router dev --port 5199`。✅ 实测 `127.0.0.1:5199` 直接 200。
   说明：`curl` 返回 `000` 只发生在 `server.host` **留空**（只绑 `::1`）时，属绑定差异不是应用失败；显式绑 IPv4 后 `127.0.0.1` 与 `localhost` 都通。
2. **生成物不入库**（`c3d69f1`）：`.gitignore` 含 `node_modules/`、`build/`、`*.tsbuildinfo`、`.react-router/`。✅ 强制构建后 `git status --porcelain` = **0 行**，`git ls-files apps/web/build` = 0、`git ls-files apps/web/.react-router` = 0。`pnpm-lock.yaml` 仍在库内（两者不混淆）。
3. **`pnpm -w build` 输出无噪音**（`b761c94` / 票面第 8 条）：✅ **本轮已满足**。修法为 `packages/api|realtime|ui` 各加一份包级 `turbo.json`（`extends: ["//"]`，`build.outputs: []`），`--force` 实建后 3 条 `WARNING no output files found` 消失，计数 0。
4. **lint**：`pnpm -w lint --force` = 4/4，但四个包都是 echo 占位（`"web: no linter yet"` 等），**没有任何实际规则**。按 `d32eb67` 的口径，lint 只能证明脚本存在，**不得作为质量证据**写入结论。

## 4. 环境说明（不是缺陷）

- 复验期间 **5199 曾被他人的 dev 实例占用**（pid 99312，14:58 起）。`strictPort: true` 下副本会直接报 `Port 5199 is already in use`，这是正确行为，不应改成自动换端口（否则票面与脚本的固定端口失效）。
- 占用进程释放后，本轮正式定稿即在该端口 `127.0.0.1:5199` 完成，结论不依赖替代端口。
- 仓库根的 `dev5200.html` / `dev-home.html` 是探测临时文件，未入库；建议删除或加忽略。

## 5. 结论

- 票 02 的**四条验收命令与票面第 8 条全部通过**（`4bb7218`，正式 5199 端口）：可重复安装、真实构建产出 SPA 外壳、类型检查 7/7、dev 首页 200、构建不污染工作区、build 输出无告警。
- 无新增缺陷；`lint` 为占位、shadcn/orval/SignalR 属步骤 4，已在 foundation §9 记偏差，本报告不写「已接入」。
- 审计已按 `96770f7` 终审通过；若需复核实现细节，代码树与 `c0da821`、`d32eb67` 等价（仅 README 差异）。
- 未验证（不属本轮验收）：浏览器运行时水合（无头环境只验 HTML）、`GET /` 之外的业务路由、真实 lint 规则。

## 6. 补充复核 @ `c0da821`（针对 review-02 的 F-02-1 / F-02-3）

老登 `review-02.md` 的被测点是 `2f48e7d`，比 `57c226e` 早一笔，所以报告里 F-02-1（`no output files` 未消除）在当时的 hash 上成立；该问题已在 `57c226e` 修复。我在 `c0da821`（含后续占位页 import 与端口单一来源改动）上复核：

| 项 | 复核命令 | 结果 |
|---|---|---|
| F-02-1 build 无噪音 | `pnpm -w build --force` | 4/4，`Cached: 0`，`no output files found` 计数 **0** |
| F-02-1 typecheck 无噪音 | `pnpm -w typecheck --force` | 7/7，`Cached: 0`，告警计数 **0** |
| F-02-3 端口单一来源 | 读 `apps/web/package.json` | `"dev": "react-router dev"`（已去掉 `--port 5199`），端口只来自 `vite.config.ts` |
| 占位页真实引用 | `apps/web/app/routes/home.tsx` | 真实 `import` `@pzd/api`、`@pzd/realtime`、`@pzd/ui` 三个 workspace 包 |
| dev 可用性 | `react-router dev --port 5201` | `http=200 bytes=1751`，`lang="zh-CN"`、`isSpaMode`，引用路径为副本 |

端口说明：复核时 `127.0.0.1:5199` 被他人 dev 实例占用（pid 125412），我的副本按 `strictPort` 启动会直接报 `Port 5199 is already in use` 并退出——这是票面要求的正确行为，不是回归；5199 上的正式四条命令已在 `d32eb67`（端口空出时）跑通，配置在这两个 hash 之间未变。

