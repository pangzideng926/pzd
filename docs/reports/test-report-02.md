# 测试报告：票 02（前端 monorepo 骨架）

> **定稿依据：干净 `git worktree` @ `d32eb67`**（含 `57c226e` 包级 `turbo.json` 把 echo 型包 `outputs` 设为 `[]`、`d32eb67` 固定规范探测地址为 `127.0.0.1:5199` 并标注 lint 为占位）。
> 代号等价说明：`aaf077f` / `bbfe7fd` 与本次被测代码树一致（`git diff --stat aaf077f bbfe7fd -- . ':(exclude)docs'` 为空）；本轮差异只在 `57c226e` 的 turbo 告警修复。
> 早前入库的 `bbfe7fd` 版本自述为「快照/待定稿」，以本文件为准。

## 1. 环境与复现入口

```
git worktree add --detach C:/tmp/final02 d32eb67
cd C:/tmp/final02
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
| dev 起服务 + `GET /` | ✅ 200 | 副本按 `vite.config` 默认起在 **`127.0.0.1:5199`**（`LISTENING`），`http=200 bytes=1749`；HTML 含 `lang="zh-CN"`、`isSpaMode`、`加载中…`，引用路径为副本 `C:/tmp/final02`（证明是本副本在应答） |

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

- 票 02 的**四条验收命令与票面第 8 条全部通过**（`d32eb67`，正式 5199 端口）：可重复安装、真实构建产出 SPA 外壳、类型检查 7/7、dev 首页 200、构建不污染工作区、build 输出无告警。
- 无新增缺陷；`lint` 为占位、shadcn/orval/SignalR 属步骤 4，已在 foundation §9 记偏差，本报告不写「已接入」。
- 建议 @老登 按 `d32eb67`（或代码树等价的 `bbfe7fd` + `57c226e`）出审计结论。
- 未验证（不属本轮验收）：浏览器运行时水合（无头环境只验 HTML）、`GET /` 之外的业务路由、真实 lint 规则。
