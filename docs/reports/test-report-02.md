# 测试报告：票 02（前端 monorepo 骨架）

> **定稿依据**：干净 `git worktree` @ **`aaf077f`**（`fix(web): dev 端口按票面固定 5199、生成物 .react-router 不入库`）。
> 早前入库的 `bbfe7fd` 版本测的是**尚未提交的工作区骨架**（快照），本版按 hash 重跑，替换该快照结论。
> 票面依据：`1dc8f74`（端口/host）、`c3d69f1`（显式 `127.0.0.1:5199`，「一律 localhost」作废）、`b761c94`（echo 型包 `outputs: []`、build 无噪音）。

## 1. 环境与复现入口

```
git worktree add --detach C:/tmp/smoke02final aaf077f
cd C:/tmp/smoke02final
pnpm install --frozen-lockfile
pnpm -w build --force        # 必须 --force，否则 turbo 缓存的日志会假装构建过
pnpm -w typecheck --force
```

- Node **v24.20.0**，pnpm **12.14.0**（与 `packageManager` 一致）。
- 副本为干净检出：`node_modules`、`apps/web/build`、`.react-router` 都不在库内，安装与构建从零开始。

## 2. 四条验收命令与结果

| 命令 | 结果 | 证据 |
|---|---|---|
| `pnpm install --frozen-lockfile` | ✅ 通过 | 退出 0；`+104` 包，`esbuild postinstall` 正常执行（`pnpm-workspace.yaml` 的 `allowBuilds: esbuild` 生效） |
| `pnpm -w build --force` | ✅ 通过（**但输出仍有噪音，见 §3.3**） | `Tasks: 4 successful, 4 total`，`Cached: 0 cached`（真实构建，非缓存回放）；`SPA Mode: Generated build\client\index.html`，产物 `apps/web/build/client/index.html` 1462 字节 |
| `pnpm -w typecheck --force` | ✅ 通过 | `Tasks: 7 successful, 7 total`，`Cached: 0 cached`；`@pzd/web:typecheck` = `react-router typegen && tsc --noEmit` |
| dev 起服务后 `GET /` | ✅ 200 | 见 §4：我的副本在 5200 实起、`127.0.0.1:5200` 返回 `http=200 bytes=1759`，HTML 引用路径为 `C:/tmp/smoke02final`（证明是我副本在应答）；工作区 5199 实例同样 `http=200 bytes=1733` |

## 3. 票面验收项逐条对照

1. **端口与绑定**：`apps/web/vite.config.ts` @ `aaf077f` 为 `host: "127.0.0.1"` + `port: 5199` + `strictPort: true`；`dev` 脚本为 `react-router dev --port 5199`。✅
   - 行为验证：副本用 `--port 5200` 覆盖端口后，`http://127.0.0.1:5200/` 直接 200，说明显式绑 IPv4 后 127.0.0.1 探测是通的——**假红的真因是 `host` 留空只绑 `::1`**，与票面 `c3d69f1` 一致。
2. **生成物不入库**：`.gitignore` 含 `node_modules/`、`build/`、`*.tsbuildinfo`、`.react-router/`；复验时 `git ls-files apps/web/.react-router` = 0、`git ls-files apps/web/build` = 0，且 `pnpm -w build --force` 之后 `git status --porcelain` 仍为 **0 行**（构建产物不会污染提交）。✅
3. **`pnpm -w build` 输出无噪音（`b761c94`）**：❌ **未满足**。`--force` 实建后仍输出 3 条：
   ```
   WARNING  no output files found for task @pzd/api#build. Please check your `outputs` key in `turbo.json`
   WARNING  no output files found for task @pzd/realtime#build. (...)
   WARNING  no output files found for task @pzd/ui#build. (...)
   ```
   `turbo.json` @ HEAD（含 `485d37b`）仍是全局 `"outputs": ["build/**", "dist/**"]`，没有给 echo 型包设 `outputs: []`。
   影响：票 11 的脚本要读这段输出，噪音会干扰判断；退出码仍为 0，不影响构建本身。**需要一小笔返工**（见 §5）。

## 4. 环境说明（不是缺陷）

- 复验时 **5199 已被工作区里的 dev 实例占用**（`TCP 127.0.0.1:5199 LISTENING`），`strictPort: true` 下我的副本不能抢占，因此副本改用 `--port 5200` 验证自身构建 + 服务；5199 那条则直接对运行中的工作区实例探测，同样 200。
- 探测端口一律走 `127.0.0.1`（票面 `c3d69f1`）。`curl` 的 `000` 只出现在 `host` 留空（只绑 `::1`）的场景，且本机 sandbox 连 `::1` 会被拒；这不是应用失败的信号。
- 工作区根目录有个未跟踪的 `dev5200.html`（别人探测留下的），建议删掉或加进 `.gitignore`，别跟着 02 一起提交。

## 5. 结论与待办

- 票 02 的**四条验收命令在 `aaf077f` 全部通过**：frozen-lockfile 可重复安装、真实构建产出 SPA 首页、类型检查 7/7、dev 起来 `GET /` 返回 200，且构建不污染工作区。
- **一条新验收未满足**：`b761c94` 要求 `pnpm -w build` 输出不带 `no output files` 告警。修法一行级：在 `packages/api`、`packages/realtime`、`packages/ui` 的 `package.json` 里各加
  `"turbo": { "tasks": { "build": { "outputs": [] } } }`（或 turbo 2 支持的等价包级覆盖），然后 `pnpm -w build --force` 确认 3 条 WARNING 消失。
  修完只需重跑 `pnpm -w build --force`，不必重跑四条命令；我按新 hash 复核这一条即可。
- 未验证（不属本轮验收）：`lint`（三包均为 echo 占位）、浏览器运行时水合（无头环境只验 HTML）、`GET /` 之外的业务路由、shadcn preset（交互式，Chief 已记偏差到 foundation §9）。
