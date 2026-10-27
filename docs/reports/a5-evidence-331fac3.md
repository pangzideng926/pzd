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
| hash 与进程 | `Pzd.AppHost` 61936 / `dcp.exe` 49688 / vite 56392；证据取完后停掉 |
| 性质 | **手工删行的对照件**，不是仓库里的一条提交；引用时必须写明这一点 |

## 2. 证据强度分级（别把四个来源写成同一种）

| # | 来源 | 类型 | 强度 |
| --- | --- | --- | --- |
| 1 | `web-stdout.log:17` | vite **进程内** `process.env.NODE_ENV` 打印的字符串 | **最硬**——进程自己说的，不是反推、不是声明 |
| 2 | `verify-apphost.log:39` | AppHost 侧资源快照（`Running`、103 项，来源 `ResourceNotificationService.WatchAsync()` 的 `Snapshot.EnvironmentVariables`） | 中等偏上——是**解析后的完整环境集合**（含 `PATH`，所以不是我们自己的声明），比声明强、比进程 `process.env` 弱；能看值，**仍不能替代进程级读数** |
| 3 | `web-stderr.log:2` | 同一进程同一请求的真错误原文（同步抛出） | 硬——抛出点在调用点，且与 #1 相隔 **106ms** |
| 4 | `GET / → 500` + `X-Pzd-Run-Id` 一致 | HTTP 面回包 | 硬——证明应答来自本轮启动的进程，不是残留实例 |

**读法**：先立 #1（进程级字符串），再用 #2 同值互证，用 #3 说明后果，用 #4 排除「打到了别人的实例」。**不要**从 #2 单独推「环境变量进了进程」——快照语义不支持这个结论，这也是先前那条纪律的由来。



## 3. 证据一（最硬）：vite 进程自己打出的 `NODE_ENV`

来源：DCP 对 web 资源 stdout 的捕获（`..._out`），复制件 `web-stdout.log`：

```
17 2026-10-27T15:52:27.658Z [pzd-web] entry loaded（NODE_ENV=production）
18 2026-10-27T15:52:27.761Z [pzd-web] 首次文档渲染：即将调用 renderToPipeableStream
```

第 17 行来自 [`apps/web/app/entry.server.tsx`](../../apps/web/app/entry.server.tsx) `:45` 的锚点，读的是 **vite 进程的 `process.env`**，所以这是**进程真实值**，不是资源声明快照。

## 4. 证据二：同一次请求的真错误（同步抛出，未被 abort TDZ 掩盖）

来源：同一目录的 `..._err`，复制件 `web-stderr.log`：

```
1 2026-10-27T15:52:25.629Z $ react-router dev --port 5199
2 2026-10-27T15:52:27.764Z [pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）： TypeError: (0 , __vite_ssr_import_0__.jsxDEV) is not a function
3 2026-10-27T15:52:27.765Z     at eval (E:/pzd-a5pre/apps/web/app/entry.server.tsx:39:113)
```

与证据一相隔 **106ms**（`15:52:27.658` → `15:52:27.764`），同一进程、同一请求；抛出点在调用点本身（不是 6 秒后的 TDZ `ReferenceError`）。

## 5. 证据三：资源声明快照侧（辅助，不单独作为结论）

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

## 6. 证据四：HTTP 面（GET，不是 HEAD）

```
GET http://127.0.0.1:5199/  →  HTTP 500，耗时 4.6ms，body 88B
响应头  X-Pzd-Run-Id: fbf41a8096254c91bbdb2df31b8f4839
```

响应头与本轮 runId 逐字符一致 → 这个 500 是**本轮拉起的进程**给出的，不是残留实例。父进程链终点是 `dcp.exe run-controllers`（`vite ← cmd ← pnpm ← node(corepack) ← cmd ← dcp.exe`），所以是 DCP 真实拉起，不是手动自测。

**探活纪律**：`entry.server.tsx:57` 对 `HEAD` 在渲染前短路返回初始 200，所以**HEAD 恒 200**，任何探活只能用 GET 的状态码。

## 7. 证据五：TDZ 已经终结 + 双调用在 aspire 侧也成立（计数，老卫独立数过）

同目录 `web-stderr.log` 有 **10401 行 / 1.6MB**，跨度 `15:52:25.629Z` → `15:55:43.073Z`（约 **3 分 17 秒**），几乎全是重复错误栈。按关键词计数：

| 关键词 | 命中 |
| --- | --- |
| `Cannot access`（TDZ 的报错形态） | **0** |
| `ReferenceError` | **0** |
| `jsxDEV` | **1040**（= 520 次 × 2：消息里一次 + 栈帧里一次） |
| `同步抛出` | **520** |
| 抛出时间戳去重后 | 366（同一毫秒内成对时只算一个） |
| 文件总行数 | 10401 |

**怎么读这 520**：抛出是**成对**出现的，两两间隔约 1ms，相邻两对之间约 **0.77 秒**；整轮约 260 个文档请求 × 每次 2 轮 entry 调用 = **520 次抛出**。也就是说 **双调用在 DCP 拉起的 vite 上全程成立**，不是只在本地 `pnpm dev` 成立，也不只在超时路径成立（对应同步抛出这条路径）。

> **一处口径更正（避免后人拿小样本当全量）**：老登数到的是「8 次抛出、4 对时间戳」（`27.764/27.771`、`28.558/28.559`、`29.328/29.328`、`30.098/30.098`），那是**文件头部前 4 个请求的窗口**，不是全文；全文是 520 次 / 约 260 个请求。两个数都对，但引用「这轮到底发生了几次」时用 520。

读法：**520 次真错误、0 次 TDZ**。在真实 DCP 编排下，自定义 entry 每次都在调用点把真错误打出来并回 500，**没有任何一次退化成「6 秒后 `Cannot access 'abort' before initialization` 带崩进程」**。1.6MB 不是某行特别长，而是**同一条完整栈被重复 520 次**——这就是缺陷态日志膨胀的直接原因，别据此判断 web 进程本身有异常。

**边界**：aspire 侧只有成对的时间戳，**看不到请求头**，所以「发起方是上层重试」这个定性来自本机实验（锋哥：两次请求逐字节相同，含自定义 `x-probe-marker: first`），**不要写成「aspire 侧也验过请求头」**。

## 8. 通道边界（这条最容易判反）

**两个地方，两条通道，别混：**

| 地方 | 装什么 |
| --- | --- |
| `artifacts/verify-apphost.log` | **只有 AppHost 自己的输出**（`[pzd-diag]` 之类） |
| `<TEMP>/aspire-dcp*/<guid>_out` / `_err`（仓库内模式下是 `.aspire-tmp/aspire-dcp*/…`） | **DCP 给每个资源捕获的 stdout/stderr**（`[pzd-web]`、`[pzd/entry.server]` 在这里） |

**硬事实（李总/旺总，`db17a9b`）**：一次**全链路成功**的 aspire（`EXIT=0`、web 全程 `Running`、`/` 正确回包、runId 一致）里，44 行 `artifacts/verify-apphost.log` 中 `[pzd-web]` = **0 行**、`[pzd/entry.server]` = **0 行**。entry 必然加载过、`handleRequest` 必然调用过，但 AppHost 日志一行都没有。

所以：**「AppHost 日志里没有 entry 行」是零判别力信号**，它既不能证明「错误在入口之前」，也不能证明「修复没生效」——写成判据会把「一切正常」判成「模块加载期失败」。这条我们今晚踩了四次同族错误，记成通用规则：**「日志里没有」永远不能当判据，只能当「去看别的通道」的提示。**

**诚实的边界**：目前只有**失败那轮**的 `_out` 里见过 `entry loaded`（本文件 §3 就是）；**成功那轮**（`db17a9b`）的会话目录已被清理，所以「成功时 `_out` 里也有这两行」**尚未验证**——别外推。

**操作纪律（今晚吃过两次亏）**：结果重要的那一轮，**先把 DCP 会话目录整个复制走（`%TEMP%/aspire-dcp*` 或 `.aspire-tmp/aspire-dcp*`），再停进程**。锋哥那轮因为先复制到 `C:/tmp/a5-ab-331fac3-nopin/` 才可复核；另外两轮的会话目录都没了，只能靠转述。

## 9. 结论

**观测事实**：DCP 拉起的 vite 进程里 `NODE_ENV=production`（见 §3，进程级）；未钉住时它拿到 production 的 JSX 运行时，而 `react-router dev` 的 SSR 变换仍生成 `jsxDEV` 调用 → 在 `renderToPipeableStream` 调用点**同步抛出** → 500。钉成 `development` 后该路径不再触发（`37bd872` 是修复，不是巧合）。

**四条证据链上的分工（按 §2 的强度）**：① 进程级字符串 `NODE_ENV=production`（最硬）→ ② 快照同值（互证）→ ③ 同一请求 106ms 后 `jsxDEV is not a function`（后果）→ ④ `GET /` 500 且归属头与本轮 runId 一致（排除残留实例）。

对照另一侧由李总在含钉住行的树上跑出（`5682cd0` 干净副本）：进程级 `NODE_ENV=development`、`GET /` 200、`X-Pzd-Run-Id` 与 runId 一致、全链路 `EXIT=0`。**两边唯一差异就是那一行 `WithEnvironment`。**

**来源层：工具链已排除，剩两支都在编排器侧（老登定稿口径）**：仓库里**没有任何一处主动设置 `NODE_ENV`**（全仓只有三处：`entry.server.tsx:45` 打印、`vite.config.ts:31/45` 打印、`Program.cs:85` 的 pin；`local.env`、`package.json` 的 `dev`=裸 `react-router dev`、所有脚本都没有它），bash / Windows 用户级 / 机器级三处也未设置（雨哥、旺总各扫一遍）。

- **工具链侧已排除**：同一副本手动 `pnpm run dev --port 5199` **5/5 起得来、3/3 返回 200**（李总）——走的是完全相同的 `pnpm → corepack → cmd.exe → react-router dev` 链路、**没有 DCP 参与**。若工具链在 build 语义下会自行翻成 `production`，手动跑也必须 500；它没有。所以雨哥原提的 `pnpm exec react-router dev --port 5231` 判别实验**不必再跑**。
- **剩下的两支**：① **Aspire JS 集成在构建期加的**；② **DCP supervisor 在运行时加的**。快照能说明「它在 DCP 应用之前就存在」，但**区分不了这两支**（旺总自己标的边界）。

**要分清就一步**：在 `Build()` 之后、`app.Run()` 之前打印 web 的 `EnvironmentCallbackAnnotation` 展开结果——**主仓也能跑**（那两分钟超时发生在更后面）。有 `production` = ①；没有 = ②。这属于归档细节，**不影响 A5 通过**，pin 在两种归因下都成立。

**所以这一段的定稿措辞只有一句**：**非仓库、非 shell、非脚本注入的 `NODE_ENV=production`** 进入了 DCP 拉起的 vite 进程（进程级 `_out:17` 实测）。**别写「DCP 注入」当定论**——那是把「非我们来源」硬说成「某个具体来源」。

**两条边界（引用时不能省）**：① 这一侧是 `331fac3` **手工删掉钉住行的对照件**（不是某个 un-pinned 提交，也不是 `git revert`）；② **两侧来自两次不同的运行**（`331fac3` 删行 vs `5682cd0` 含钉住），是「同一改动差异、不同运行现场」的对照，不是同一次运行里的 A/B 开关。

## 10. 不能写的话

- 不能写「**aspire 下看不到 `[pzd-web]` 就等于模块图失败**」：web 资源的 stdout/stderr 落在 `.aspire-tmp/aspire-dcp*/<guid>_out` / `_err`，不在 `artifacts/verify-apphost.log`。读法 `grep -a -r "pzd-web\|pzd/entry.server" .aspire-tmp`。
- 不能写「**已有进程级 `NODE_ENV` 的文件探针证据**」：本包是 DCP 会话目录的 `_out`/`_err`，`331fac3` 上还没有 `writeFileSync` 探针（那是 `cd17520`）。
- 不能写「**`bf08bd4` 上失败也回 500**」：那个 200 已被李总解释为「干净态跑的（变异撤了或没生效）」，**这个观察撤回、不算缺陷**；要引用失败回 500，只用 `db17a9b` 变异态的三次实测（`onShellError`、归属头仍在），**按 hash 分家**。
- 不能说「AppHost 日志里没有 `[pzd-web]` / `[pzd/entry.server]` → 错误在入口之前」：`db17a9b` 全链路成功那轮两者同样 0 行（见 §8），这条是零判别力。
- 不能把本包当成「仓库里的一次可复现运行」：它来自人工删行的对照件，复现需重建同一改动。
- 不能把 `Cannot access` 计数为 0 读成「**TDZ 在全部场景都消失了**」：这只覆盖「该调用点同步抛出 → 失败回 500」这一类，且是 `331fac3` 的自定义 entry；别的失败形态（`onShellError`、卡死超时）各有各的实测，见交接 §5。

## 11. 复制件位置与可复核性（原件已随 worktree 删除，副本留存）

本文件内容抄自 `C:/tmp/a5-ab-331fac3-nopin/`。**原件（`E:\pzd-a5pre\.aspire-tmp/aspire-dcp*/<guid>_out` / `_err`）已随那份 worktree 一起删除，不可再生**——所以别写「原件仍在盘上」，只能写「副本留存，sha256 如下」（锋哥，2026-10-28）。**该副本是整个 A5 定因唯一不可再生的物证，请勿清理 `C:/tmp`**；本文件正文已把关键原文抄全，即使副本没了也能复核。

| 文件 | 大小 | sha256 |
| --- | --- | --- |
| `web-stdout.log` | 1878 B | `33cc091a2910fcd9bbc36ed13f6e8ab1aaab95367881ed5724ac30ead8cc586c` |
| `web-stderr.log` | 1624876 B | `fdcd5c752d53036ad14afe24a3f1d5b00df67643a98092f5bc7f1fe3b7ef441e` |
| `verify-apphost.log` | 4677 B | `e5438442ef6cda3a38450d13a01521540e6735a34bb6e56d8587582a7f885785` |
| `README-证据说明.txt` | 3555 B | `75085d411ae76b3a8b86f56365ddb06be82e06cb2456f6cb172ca47da1273e14` |

**「它确实来自 DCP 的资源 stdout 捕获通道」的格式证据**：`web-stdout.log:17` 的原始字节以 `1 7 ␠ 2 0 2 6 - 1 0 - 2 7 T 1 5 : 5 2 : 2 7 . 6 5 8 Z ␠ [ p z d - w e b ] …` 开头——**行号前缀 + ISO 时间戳是 DCP 捕获格式**，我们自己 `console.log` 只写 `[pzd-web] entry loaded（…）`，不会自己加这两样。所以这行不是拼出来的。
