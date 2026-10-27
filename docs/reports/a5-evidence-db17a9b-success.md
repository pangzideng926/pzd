# A5 对照证据：成功轮（`db17a9b`）的 DCP 会话 `_out` 原文

> 为什么单独立这份：`artifacts/` 在 `.gitignore`（`.gitignore:4`），李总归档的 `artifacts/a5-evidence/db17a9b-success/` **只是在工作区，没进 git**。按我们自己立的规矩（引原文不引路径），把原文抄进仓库。
>
> 这份补的是一个**明确缺口**：旺总说过「成功那轮的会话目录已经删了，我无法证明成功时 `_out` 里也有 `entry loaded`」。现在有了。

## 1. 这一轮的四要素（老何口径）

| 项 | 值 |
| --- | --- |
| 基线 | `db17a9b`（pin 在 `:85`，vite 文件探针在 `vite.config.ts:44`） |
| 未提交变更 | **无** |
| 启动命令 | `pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire`（副本 `C:/tmp/pzd-db17`；TEMP 指向仓库内 `.aspire-tmp`，所以会话目录在仓库里，可直接 grep） |
| runId | `f730b9e225ee42df95721933c9ab4893` |
| 结果 | `EXIT=0`，8 条 `[ok]`，前端 pid **22096** |

## 2. 证据一：web 资源 `_out` 原文（DCP 捕获的进程 stdout）

文件 `<repo>/.aspire-tmp/aspire-dcpjeoost0x.scs/a9c3ab6e-…_out`，复制件 `web-stdout.log`（1997 B）：

```
 1 2026-10-27T16:32:51.586Z [pzd-web] NODE_ENV=development
17 2026-10-27T16:32:51.683Z [pzd-web] NODE_ENV=development
18 2026-10-27T16:32:51.712Z   ➜  Local:   http://127.0.0.1:5199/
19 2026-10-27T16:32:52.496Z [pzd-web] entry loaded（NODE_ENV=development）
20 2026-10-27T16:32:52.593Z [pzd-web] 首次文档渲染：即将调用 renderToPipeableStream
```

中间 `:2`–`:16` 是 5 组 React Router v8 Future Flag 警告（每组 3 行），与判读无关。

`_err` 全文只有一行（58 B）：`1 2026-10-27T16:32:50.953Z $ react-router dev --port 5199`。

## 3. 证据二：文件探针（另一条独立通道）

`artifacts/pzd-web-env.log`（238 B）：

```
2026-10-27T16:32:51.585Z cwd=C:\tmp\pzd-db17\apps\web NODE_ENV=development PZD_RUN_ID=f730b9e225ee42df95721933c9ab4893
2026-10-27T16:32:51.683Z cwd=C:\tmp\pzd-db17\apps\web NODE_ENV=development PZD_RUN_ID=f730b9e225ee42df95721933c9ab4893
```

**两条通道同毫秒互证**：探针 `16:32:51.585`/`.683` vs `_out:1` 的 `.586`、`_out:17` 的 `.683`；runId 两边一致。所以是**同一个进程、同一次运行**，不是两次跑的拼接。

## 4. 这一轮钉死的三件事

1. **成功轮的 DCP 会话目录里也有锚点**——`entry loaded（NODE_ENV=development）` 在 `_out:19`。所以「AppHost 日志里 `[pzd-web]` / `[pzd/entry.server]` 命中 0 行」在任何一轮都成立（成功 `db17a9b` 0 行、失败 `331fac3-nopin` 同样 0 行），**零判别力**；同时它也证明**不是「DCP 拿不到 web stdout」**，一个文件 0 条、另一个文件 4 条，只是两个对象。
2. **`NODE_ENV` 打印两次不是两次请求**：`_out:1`（vite 配置加载）与 `:17`（SSR 模块加载/重载）各打一次，值相同。数请求次数别用这两行。
3. **门槛那条 `[ok]` 原文断言的是外壳内容**，不只是响应头：

   ```
   [ok] 前端 127.0.0.1:5199 的 X-Pzd-Run-Id 与本次运行一致（pid 22096），且是 SPA 外壳：由本次运行拉起
   ```

   所以这一轮 `/` 确实经过 `entry.server.tsx`，`:45` 在模块加载期无条件打印 → 锚点必然存在，也确实存在（`_out:19`）。

## 5. 引用时的三个坑

- **别混行号**：这份成功轮 `entry loaded` 在 **`_out:19`**；锋哥那份 `331fac3-nopin` 在 **`_out:17`**（Vite Future Flag 警告行数不同）。各引各的，别把 17 写成通用位置。
- **别把这份当「DCP 永远是 development」**：这一轮 pin 在（`:85`），`development` 是该基线的结果，不是 DCP 的固有属性（老何口径，见 [`handover-01-04.md`](handover-01-04.md) §5「逐轮对齐四要素」）。
- **编码按文件名判断，别按印象**：`artifacts/verify-apphost.log` 是 **UTF-8**；而脚本自己输出到终端/转录的 `[ok]` 行是 **GBK**（李总第一次按 UTF-8 读全是乱码，换 GBK 才读出来）。同一批产物编码不止一种，读之前先 `file -bi` 或试 `iconv`；ASCII 判据（`pzd-web`、`jsxDEV`）不受影响，中文判据会静默失败。

## 6. 物证位置

工作区 `artifacts/a5-evidence/db17a9b-success/`（`README.txt` 3906 B、`host.log` 5082 B、`pzd-web-env.log` 238 B、`web-stderr.log` 58 B、`web-stdout.log` 1997 B）——**该目录 gitignore，不会随仓库走**；以本文件正文为准。
