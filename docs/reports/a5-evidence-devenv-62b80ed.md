# A5 定因确认实验：`DOTNET_ENVIRONMENT` 单变量对照（基座 `62b80ed`）

> 这份回答的是最后一个未闭合问题：**「宿主环境 → Aspire JS 托管集成给 web 写 `NODE_ENV`」是不是真的**。结论：**是，已实测**（此前只有元数据反推）。
>
> 为什么单独立文件：物证在 `artifacts/` 下，而 `artifacts/` 被 `.gitignore:4` 忽略，**只在工作区磁盘、没进 git**。按我们自己的规矩（引原文不引路径），原文抄进仓库。

## 1. 实验设计（两棵树、同一份脚本，只差一个变量）

| 组 | 树 | pin | `DOTNET_ENVIRONMENT` | runId | 时间 |
| --- | --- | --- | --- | --- | --- |
| A（反向对照） | `C:/tmp/pzd-fin`（`62b80ed`） | **当轮文件状态不可考、也不需要考**——判据是**进程读数 + runId**，不是事后文件状态 | **未设置**（宿主 Production） | `f6b32412c3464c40a99c0cdf802bb9a6` | 16:07（本地 00:07） |
| B（实验）· **主证据（按归属）** | `C:/tmp/pzd-envtest`（张一一，`pzd-fin` 的完整副本；**代码 `62b80ed` + 脚本 `ce094d7d…`（+34/−7）**） | **已删**（整行移除；活代码 pin **0**） | **`Development`** | `cf7b5542b12142d18c5349496d720292` | 17:48 |
| B′（同格，**第二份独立运行**） | `C:/tmp/pzd-fin`（李总） | **也是 un-pin**：`:85` 是注释态，活代码 pin **0**（`mtime 01:47:53.771`、sha256 `f1ea6ffc…`，比探针首行早 18 秒） | `Development` | `968c1848dbe84ebd89294b33662749d8` | 17:48 |

## 1.5 A 组（反向对照）的判据：进程读数，不是文件状态

A 组那一轮（`pzd-fin`，`f6b32412…`，本地 00:07）**有自己的进程级证据**，会话目录现在还留着：

```
C:/tmp/pzd-fin/.aspire-tmp/aspire-dcpi4124qsy.vok/d973dad1-…_out:1   2026-10-27T16:07:27.712Z [pzd-web] NODE_ENV=production
                                            d973dad1-…_out:17  2026-10-27T16:07:27.801Z [pzd-web] NODE_ENV=production
                                            d973dad1-…_out:20  2026-10-27T16:07:29.151Z [pzd-web] entry loaded（NODE_ENV=production）
```

同毫秒、两条独立通道、同一次运行：探针文件里 `16:07:27.712Z … NODE_ENV=production PZD_RUN_ID=f6b32412…` 与 `_out:1` 是**同一毫秒**。

**所以「那一轮当时是不是 un-pin」这个问题不必靠文件状态回答**——vite 进程自己读出了 `production`，是**当轮读数**。谁要说那不是反向证据，得先解释为什么「基座原件 `:85` 本来有 pin、又读到了 `production`」；这恰恰是 pin 生效与不生效的分界。

**同时记两个不许用的论证**（今晚各踩一次）：

- **不许用 mtime 反推历史**：`mtime 01:47:53.771` 只记**最后一次写入**。00:07 那轮把整行删掉、01:47 改回注释态，两次都会更新 mtime，所以现在的 mtime 对「00:07 时是活代码」和「00:07 时是整行删除」**完全无差别**。这条对 `pzd-fin` 与 `pzd-envtest` 一视同仁。
- **不许用现查计数/现查状态反推历史**：`git status` 显示 `M src/Pzd.AppHost/Program.cs` 只说明**改过**，不说明**当时**是什么。

**第三份独立反向证据**：锋哥那份 `331fac3-nopin`（`C:/tmp/a5-ab-331fac3-nopin/`，基座 `331fac3:85` 本身有 pin、当轮 `git diff --stat` 2 增 1 删、`_out:17` 读到 `production`、`_err:2` 是 `jsxDEV`）。它**并列**存在，不替换 A 组——A 组那份有毫秒对齐，更硬；它的价值是**当轮 diff 留存**。

> **两棵树都是真 un-pin，两条运行各自成立、互不依赖（2026-10-28 定稿，中间经过一次撤回）**：
>
> - **`pzd-fin` 那轮（`968c1848…`）成立、可复核**：同一基座 `62b80ed`，`:85` 是**注释态**（**15170 字节 = 原件 15167 + `// ` 3 字节**、sha256 `f1ea6ffc…`、`mtime 01:47:53.771`，比探针首行早 18 秒），**活代码 pin 0**，探针 runId `968c1848…` 对得上。按**归属**不作主证据（那是李总的树和 runId）——**与这条数据是否成立无关，它成立**。
> - **判据作废与替代（照抄即可）**：`grep -c 'WithEnvironment("NODE_ENV"'` **一律不算**——它会数到注释行，能把有无 pin 判反。改用**活代码行** `grep -n '^\s*web = web\.WithEnvironment("NODE_ENV"'`，再配**当轮 diff/启动记录**和 **runId**，三样齐才算「这棵树的证词」。
> - 按新判据复核：`pzd-envtest` = 整行已删、活代码 pin **0**；`pzd-fin:85` = 注释态、活代码 pin **0**。**两棵都是 un-pin**，李总那轮（`968c1848…`）成立，老登已书面撤回降档。
> - **引主证据按「归属」记、不按「强度」记**（老登/李总/张一一一致）：主证据引 `pzd-envtest` 是因为**那是张一一的树、他的 runId**，不是因为另一条弱。
> - **`.bak` 别写成「两棵树里都有」**：两棵树内 `find` 都找不到 `Program.cs.orig.bak`；李总那份备份放在**树外** `artifacts/a5-evidence/devenv-62b80ed/Program.cs.orig.bak`。
> - **也别说它「与原件逐字节相同」**：它**内容相同、行尾不同**——去 CR 后与 `62b80ed` 版逐字节一致，但文件 15480 字节 vs 15167 字节，**差 313 = 313 个 CR**，即那份 `.bak` 是 **CRLF 行尾**。正确写法是「**内容一致（行尾 CRLF，313 行）**」。这类「字节数对不上」的差异今晚已坑过两次（BOM 让脚本解析失败、GBK 让 `[ok]` 读成乱码），写「逐字节」会让人拿 `cmp` 一对就起疑。

## 2. 结果并排

| 观测 | A（无 pin，不设 DEV env，16:07） | B（无 pin + `DOTNET_ENVIRONMENT=Development`，17:48） |
| --- | --- | --- |
| 资源快照 `NODE_ENV` | `production`（103 项） | **`development`（109 项）** |
| vite 进程内探针 | `production` | **`development`** |
| `/` | **500** + `jsxDEV` 同步抛出 | **200** + SPA 外壳 |
| 脚本 `EXIT` | `1` | **`0`**（8 条 `[ok]`） |

B 组（张一一那轮）的脚本原文关键行：

```
[pzd-diag] apphost 自身 NODE_ENV=<unset>
[pzd-diag] env NODE_ENV=development
[ok] /api/version 0.1.1-alpha.0.199+62b80ed
[ok] X-Pzd-Run-Id 与本次运行一致（cf7b5542…）：应答来自本次启动的进程
[ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 43076），且是 SPA 外壳
验收通过（/health/ready 期望模式：Healthy）
```

B 组（李总那轮）进程级探针原文：

```
2026-10-27T17:48:11.541Z cwd=C:\tmp\pzd-fin\apps\web NODE_ENV=development PZD_RUN_ID=968c1848dbe84ebd89294b33662749d8
2026-10-27T17:48:11.680Z cwd=C:\tmp\pzd-fin\apps\web NODE_ENV=development PZD_RUN_ID=968c1848dbe84ebd89294b33662749d8
```

行为侧：`/` 200 + SPA 外壳（pid 111888）、`/api/version 0.1.1-alpha.0.199+62b80ed`、8 条 `[ok]`、`EXIT=0`。

## 2.5 判据四档（先定判据，别等结果出来再吵）

| 探针文件读到 | 结论 |
| --- | --- |
| `NODE_ENV=development` | 宿主环境这条成立（B 组命中此档） |
| `production` | 第 2 步错，整条链重读 |
| **值既非 dev 也非 prod（`<unset>`/空）** | 宿主环境不是唯一决定因素，另有分岔 |
| **文件根本没生成** | web 没跑到写文件（没起或秒退）→ **判本轮无效，不是反证** |

第四档是老登补的：李总早先踩过这个坑，若不写进去，「没生成」会被读成某条结论。判「文件没生成」之前，先看 `state=` 与进程在不在。

## 3. ⚠️ 引用前必读：探针文件是**追加写**的，一个文件混着多轮

`artifacts/a5-evidence/devenv-62b80ed/pzd-web-env.log` 的全文（10 行）里就有 **5 个不同 runId**：

```
16:06:04  development  runId=e8c2d03f41a04ed09c9b0b281cf21a17
16:07:27  production   runId=f6b32412c3464c40a99c0cdf802bb9a6     ← A 组（就是这一轮）
16:24:26  development  runId=bc3ecc7f947145d9b90e28ffade698b9
16:26:12  development  runId=probe-foreign-abc123                 ← 反例靶子那轮
17:48:11  development  runId=968c1848dbe84ebd89294b33662749d8     ← B 组（就是这一轮）
```

**所以：引用这个文件必须按 `runId`（或时间戳）拆，不能 `grep NODE_ENV` 取第一条**——否则会把 A 组的 `production` 说成 B 组的，或者反过来。这跟「同一行打两次不是两次请求」是同一类坑。

## 4. 三块证据分工（老登定稿，别合并成一句「实验证明根因」）

| 环节 | 证据 | 强度 |
| --- | --- | --- |
| ① 宿主环境默认是 **Production** | 旺总 `ce08feb` 那行在正式 `Pzd.AppHost.exe` 上读出 `宿主环境=Production`、两个 `*_ENVIRONMENT` `<unset>` | **运行期直接读数** |
| ② `DOTNET_ENVIRONMENT=Development` → `EnvironmentName=Development` | 雨哥那个**不启 DCP** 的 15 行探针 | 实测，但不在正式 AppHost 内 |
| ③ 宿主环境 → web 的 `NODE_ENV` | 本实验：`pzd-envtest`（pin=0 + Development → `development` + `EXIT=0`）；反向 `331fac3-nopin`（pin=0 + 默认宿主 → `production` + `jsxDEV`） | **两个独立树各一次，都可复核** |

**拼起来才是完整链条**：① 证明「原来是 Production」，③ 证明「这个值决定了 web 的 `NODE_ENV`」。**缺任何一段都只是半条链**，交接里分开写。

**仍不成立的（必须保留的四条）**：

1. **写入时机未区分**：证明不了注入发生在「AppHost 装配资源模型期」还是「DCP 运行时」；那一格只能靠旺总 `Build()` 后 dump `EnvironmentCallbackAnnotation`。
2. **两条通道分属不同运行**：B 组 DCP 会话目录被正常退出清理，进程级 `_out` 只有 A 组有；B 组的进程级读数来自 vite 文件探针。别混引。
3. **副本树，不等于「主仓绿」**：主仓 aspire 仍卡 `E:\pzd` 根 ACL。
4. **没证明 JS 集成内部就是 `IsDevelopment() ? … : …`**（元数据反推、未反编译 IL）。

**还有一个诚实的缺口（老登指出，锋哥补证）**：`pzd-envtest` 停在 `62b80ed`，**早于 `ce08feb`，没有「宿主环境」那行诊断**。所以那一轮「宿主环境=Development」依据的是 `DOTNET_ENVIRONMENT` 的**继承链**（`scripts/verify-apphost.ps1:345` 的 `Start-Process` 没有 `-UseNewEnvironment`，脚本也不清环境），**是推断，不是日志里读出来的**。交接写「继承链推断」，**不要写成「日志显示 Development」**。

## 5. 顺带的两个产出

- 这是 **A11 正向的第 5 次**，且配置与前四次不同（**无 pin + `DOTNET_ENVIRONMENT=Development`**）：前端 pid 43076、`X-Pzd-Run-Id` 一致、`/` 是 SPA 外壳、8 条 `[ok]`、`EXIT=0`。
- 它同时是「pin 是**覆盖**、不是唯一解释」的直接证据：没有 pin 也能绿，只要宿主环境是 Development。**所以「有人加了 `launchSettings.json` 看到正常就删 pin」是最危险的误判**——本地 Development 正常，CI 的 Production 宿主上立刻复发。

## 6. 物证位置（gitignore，不随仓库走）

- `artifacts/a5-evidence/devenv-62b80ed/`（`README.txt`、`host.log`、`pzd-web-env.log`、`script.log`、`Program.cs.orig.bak`；README 记 sha256 `host.log a7f99aa5…`、`pzd-web-env.log 54b31b67…`）
- `artifacts/envtest-62b80ed/`（`README.txt`、`pzd-web-env.log`、`envtest.utf8.log`、`verify-apphost.log`；sha256 头 `27f78b65…`）

以本文件正文为准。
