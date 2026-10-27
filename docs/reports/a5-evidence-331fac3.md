# A5 根因证据包（锚 `331fac3`，手工删钉住行的 A/B）

> 归档原因：决定性日志原先只存在被删掉的 worktree（`E:\pzd-a5pre\.aspire-tmp\aspire-dcp*/<guid>_out`）和 `C:\tmp\` 下的复制件里，前者已随目录清理消失。**证据不能只写路径**——把原文抄进仓库，后来人不必再找运行现场。
>
> 本文件只放原文与读法，结论见 [`handover-01-04.md`](handover-01-04.md) §5 A5。

## 1. 这次 A/B 是什么

| 项 | 值 |
| --- | --- |
| 基座 | commit `331fac3`（`origin/main` 上的树，含自定义 entry + 两行诊断锚点） |
| 唯一差异 | 工作区手动删掉 `src/Pzd.AppHost/Program.cs` 里 `37bd872` 引入的那一行 `web = web.WithEnvironment("NODE_ENV", "development");` |
| 运行方式 | `pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire`（**DCP 真实编排**，不是手动 `pnpm dev`） |
| runId | `fbf41a8096254c91bbdb2df31b8f4839` |
| 运行时间 | 2026-10-27 23:52:24 起（+08:00；日志内为 UTC 15:52） |
| hash 与进程 | cart `Pzd.AppHost` 61936 / `dcp.exe` 49688 / vite 56392；证据取完后停掉 |
| 性质 | **手工删行的对照件**，不是仓库里的一条提交；引用时必须写明这一点 |

## 2. 证据一：vite 进程自己打出的 `NODE_ENV`（进程级）

来源：DCP 对 web 资源 stdout 的捕获（`..._out`），复制件 `web-stdout.log`：

```
17 2026-10-27T15:52:27.658Z [pzd-web] entry loaded（NODE_ENV=production）
18 2026-10-27T15:52:27.761Z [pzd-web] 首次文档渲染：即将调用 renderToPipeableStream
```

第 17 行来自 [`apps/web/app/entry.server.tsx`](../../apps/web/app/entry.server.tsx) `:45` 的锚点，读的是 **vite 进程的 `process.env`**，所以这是**进程真实值**，不是资源声明快照。

## 3. 证据二：同一次请求的真错误（同步抛出，未被 abort TDZ 掩盖）

来源：同一目录的 `..._err`，复制件 `web-stderr.log`：

```
1 2026-10-27T15:52:25.629Z $ react-router dev --port 5199
2 2026-10-27T15:52:27.764Z [pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）： TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
3 2026-10-27T15:52:27.765Z     at eval (E:/pzd-a5pre/apps/web/app/entry.server.tsx:39:113)
```

与证据一相隔 **0.1 秒**，同一进程、同一请求；抛出点在调用点本身（不是 6 秒后的 TDZ `ReferenceError`）。

## 4. 证据三：资源声明快照侧（辅助，不单独作为结论）

来源：`artifacts/verify-apphost.log` 复制件 `verify-apphost.log`：

```
[pzd-diag] resource=web state= exitCode=-
[pzd-diag] resource=web 环境变量共 0 项（下表仅白名单取值）
[pzd-diag] resource=web state=Running exitCode=-
[pzd-diag] resource=web 环境变量共 103 项（下表仅白名单取值）
[pzd-diag] env NODE_ENV=production
[pzd-diag] env PZD_RUN_ID=fbf41a8096254c91bbdb2df31b8f4839
```

`0 项` 在 `Running` **之前**、`103 项` 在 `Running` 之后——这就是「`0 项` 是时序没到、不是没传」的同一份实证。注意 `[pzd-diag]` 是 **AppHost 自己的 `console.log`**，可见性跟 web 资源 stdout 是两条独立通道，不能互相证明。

## 5. 证据四：HTTP 面（GET，不是 HEAD）

```
GET http://127.0.0.1:5199/  →  HTTP 500，耗时 4.6ms，body 88B
响应头  X-Pzd-Run-Id: fbf41a8096254c91bbdb2df31b8f4839
```

响应头与本轮 runId 逐字符一致 → 这个 500 是**本轮拉起的进程**给出的，不是残留实例。父进程链终点是 `dcp.exe run-controllers`（`vite ← cmd ← pnpm ← node(corepack) ← cmd ← dcp.exe`），所以是 DCP 真实拉起，不是手动自测。

**探活纪律**：`entry.server.tsx:57` 对 `HEAD` 在渲染前短路返回初始 200，所以**HEAD 恒 200**，任何探活只能用 GET 的状态码。

## 6. 结论

DCP/supervisor 向 web 资源注入 `NODE_ENV=production`；未钉住时 vite 进程拿到 production 的 JSX 运行时，而 `react-router dev` 的 SSR 变换仍生成 `jsxDEV` 调用 → 在 `renderToPipeableStream` 调用点**同步抛出** → 500。钉成 `development` 后该路径不再触发（`37bd872` 是修复，不是巧合）。

对照另一侧由李总在含钉住行的树上跑出（`5682cd0` 干净副本）：进程级 `NODE_ENV=development`、`GET /` 200、`X-Pzd-Run-Id` 与 runId 一致、全链路 `EXIT=0`。**两边唯一差异就是那一行 `WithEnvironment`。**

## 7. 不能写的话

- 不能写「**aspire 下看不到 `[pzd-web]` 就等于模块图失败**」：web 资源的 stdout/stderr 落在 `.aspire-tmp/aspire-dcp*/<guid>_out` / `_err`，不在 `artifacts/verify-apphost.log`。读法 `grep -a -r "pzd-web\|pzd/entry.server" .aspire-tmp`。
- 不能写「**已有进程级 `NODE_ENV` 的文件探针证据**」：本包是 DCP 会话目录的 `_out`/`_err`，`331fac3` 上还没有 `writeFileSync` 探针（那是 `cd17520`）。
- 不能写「**`bf08bd4` 上失败也回 500**」：`bf08bd4` 上同一变异测得 200，回 500 是 `db17a9b`（含 `4485ca0`）的行为，**按 hash 分家**。
- 不能把本包当成「仓库里的一次可复现运行」：它来自人工删行的对照件，复现需重建同一改动。

## 8. 复制件位置（非权威，目录可能已被清理）

本文件内容抄自 `C:\tmp\a5-ab-331fac3-nopin\`（`web-stdout.log` 1.8KB、`web-stderr.log` 1.6MB、`verify-apphost.log` 4.7KB、`verify-apphost.log.err` 103B、`README-证据说明.txt`）。**该目录随时可能被删；以本文件正文为准。**
