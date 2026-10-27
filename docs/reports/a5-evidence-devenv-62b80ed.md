# A5 定因确认实验：`DOTNET_ENVIRONMENT` 单变量对照（基座 `62b80ed`）

> 这份回答的是最后一个未闭合问题：**「宿主环境 → Aspire JS 托管集成给 web 写 `NODE_ENV`」是不是真的**。结论：**是，已实测**（此前只有元数据反推）。
>
> 为什么单独立文件：物证在 `artifacts/` 下，而 `artifacts/` 被 `.gitignore:4` 忽略，**只在工作区磁盘、没进 git**。按我们自己的规矩（引原文不引路径），原文抄进仓库。

## 1. 实验设计（两棵树、同一份脚本，只差一个变量）

| 组 | 树 | pin | `DOTNET_ENVIRONMENT` | runId | 时间 |
| --- | --- | --- | --- | --- | --- |
| A（对照） | `C:/tmp/pzd-fin`（`62b80ed`） | **已删**（`:85` 整行移除/注释掉） | **未设置**（宿主 Production） | `f6b32412c3464c40a99c0cdf802bb9a6` | 16:07 |
| B（实验） | 同上（李总）/ `C:/tmp/pzd-envtest`（张一一，`pzd-fin` 的完整副本） | **已删**（`grep -c` 命中 0） | **`Development`** | `968c1848dbe84ebd89294b33662749d8`（李总）/ `cf7b5542b12142d18c5349496d720292`（张一一） | 17:48 |

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

## 4. 这次实验钉死的与没钉死的

**钉死了**：**宿主环境是这条链的上游开关**。同一棵树、同一份脚本、**只改 `DOTNET_ENVIRONMENT` 一个变量**，`NODE_ENV`、`/` 状态码、脚本退出码全部跟着变。所以不再是「工程没有 `launchSettings.json` 所以宿主默认 Production」这种推断。

**没钉死（三条边界，引用必带）**：

1. **仍不区分写入时机**：证明不了注入发生在「AppHost 装配资源模型期」还是「DCP 运行时」。那一格只能靠旺总那个 `Build()` 后 dump `EnvironmentCallbackAnnotation` 的办法。
2. **两条通道分属不同运行，别混引**：B 组的 DCP 会话目录在正常退出时被清理了，所以**进程级 `_out` 只有 A 组有**（`331fac3-nopin` 那份）；B 组的进程级读数来自 **vite 文件探针**。`_out` 与探针不是同一次运行的两个文件，说清楚是谁的。
3. **副本树，不是主仓绿**：B 组跑在副本里，不能替代「主仓绿」；主仓 aspire 仍卡 `E:\pzd` 根 ACL。
4. **没证明 JS 集成内部就是那个三元表达式**：`IsDevelopment() ? … : …` 仍是**元数据反推、没反编译 IL**。这次实验证明的是「宿主环境驱动结果」，不是「内部实现就是那行代码」。

## 5. 顺带的两个产出

- 这是 **A11 正向的第 5 次**，且配置与前四次不同（**无 pin + `DOTNET_ENVIRONMENT=Development`**）：前端 pid 43076、`X-Pzd-Run-Id` 一致、`/` 是 SPA 外壳、8 条 `[ok]`、`EXIT=0`。
- 它同时是「pin 是**覆盖**、不是唯一解释」的直接证据：没有 pin 也能绿，只要宿主环境是 Development。**所以「有人加了 `launchSettings.json` 看到正常就删 pin」是最危险的误判**——本地 Development 正常，CI 的 Production 宿主上立刻复发。

## 6. 物证位置（gitignore，不随仓库走）

- `artifacts/a5-evidence/devenv-62b80ed/`（`README.txt`、`host.log`、`pzd-web-env.log`、`script.log`、`Program.cs.orig.bak`；README 记 sha256 `host.log a7f99aa5…`、`pzd-web-env.log 54b31b67…`）
- `artifacts/envtest-62b80ed/`（`README.txt`、`pzd-web-env.log`、`envtest.utf8.log`、`verify-apphost.log`；sha256 头 `27f78b65…`）

以本文件正文为准。
