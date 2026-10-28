# F-03-B5 范畴实测（脚本删除点）

审计发现（原话）：`scripts/verify-apphost.ps1:182-183` 的
`Get-ChildItem -Path $env:TEMP -Filter "aspire-dcp*" -Directory | Remove-Item -Recurse -Force`
会删掉整机所有 `aspire-dcp*` 会话目录，包括别的仓库正在跑的那一份。

**结论：按当前代码时序不成立（误报）。** 但顺着这个范畴问题，同一函数里另有两处真缺口，已修。

## 1. 为什么不成立：调用时 `$env:TEMP` 已经是本轮专用目录

`Remove-StaleDcpState` 只在 aspire 分支被调用（`if ($Mode -eq "aspire")`），而 aspire 分支在**函数被调用之前**就把进程级 TEMP/TMP 指到了本轮一次性目录：

```powershell
$aspireTempDir = Join-Path $TempRoot ("run-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
[Environment]::SetEnvironmentVariable("TEMP", $aspireTempDir, "Process")
```

`[Environment]::SetEnvironmentVariable(..., "Process")` 会改当前进程的环境块，`$env:TEMP` 立即可见，子进程继承。实测（09:05）：

```
before=C:\Users\q1887\AppData\Local\Temp
after =E:\.pzd-aspire-tmp-pzd\run-probe-env
child sees: E:\.pzd-aspire-tmp-pzd\run-probe-env
```

所以那一行扫的是 `run-probe-env` 这种**本轮空目录**，整机其它会话的会话目录在各自的 TEMP 根下，扫不到。

## 2. 反向验证：预置一个“别的会话”目录，跑完仍在

```
预置：C:\Users\q1887\AppData\Local\Temp\aspire-dcp-OTHERSESSION
重定向后 $env:TEMP = E:\.pzd-aspire-tmp-pzd\run-probe-scope
执行  Get-ChildItem -Path $env:TEMP -Filter "aspire-dcp*" -Directory | Remove-Item -Recurse -Force
结果  would delete: （无输出）
      others' dir survived = True
```

## 3. 顺着查出的两处真缺口（已修，`affad87`）

| 缺口 | 原状 | 修法 |
|---|---|---|
| 历史 `run-*` 目录清不到 | TEMP 已换根，`$env:TEMP` 下永远是本轮空目录 → 清理形同虚设 | 显式扫本仓库 `-TempRoot` 下的 `run-*`，**只删 >6h**，保留本轮目录 |
| 通知套接字按用户全局 | `LOCALAPPDATA\dcp\dcp-notify-sock-*` 与仓库无关，删它会影响别的仓库在跑的 dcp | 先查有无其它仓库的 `dcp.exe` 存活，有则跳过并打 warn |

三条路径各打一行诊断，删的是哪个根可从日志复核。确认轮原文（`docs/reports/verify-apphost-main-affad87.log`）：

```
[diag] DCP 会话目录清理：本轮 0 个（根 E:\.pzd-aspire-tmp-pzd\run-20261028-090640）
[diag] 历史运行目录清理：5 个（根 E:\.pzd-aspire-tmp-pzd，仅 >6h）
```

## 4. 已知限制（不属于本发现在档范围，但如实记）

浏览器验收轮退出后，本轮 `run-*` 目录**删不掉**（`VBCSCompiler\AnalyzerAssemblyLoader\...\Quartz.Analyzers.dll` 拒绝访问）：Aspire 会把拿到的临时目录加固（Low 标签 + AppContainer ACE + 去掉当前用户完全控制），这与主仓根目录那层标签同源。脚本按非致命 warn 处理，不影响验收结论；清理需要管理员或先取所有权。

## 5. `-Mode api` 路径未变

`Remove-StaleDcpState` 及其调用点都只在 `$Mode -eq "aspire"` 分支内，票 08/09 用的 `-Mode api` 路径本次未触碰。
