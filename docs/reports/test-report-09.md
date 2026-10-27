# 票 09 复测报告：Quartz + 健康检查 + 版本端点

- 被测：HEAD `6b67c2d`（源码干净，工作区只有 docs 改动）
- 方式：一键脚本 `scripts/verify-apphost.ps1`（张一一要求：不手拼连接串）+ 手工 95 秒真实驻留 + MySQL 侧独立查询
- 结论：**通过**（6 项验收全过），附两条观察（非缺陷）

## 1. 一键脚本正常路径

```
pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode api -SkipWeb
EXIT=0
[ok] /health/live 200（免租户）
[ok] http://127.0.0.1:5080 由本次启动的进程树监听（pid 69368）
[ok] /health/ready 根状态 = Healthy / mysql = Healthy / redis = Healthy
[ok] /api/version 0.1.1-alpha.0.168+6b67c2d
[ok] X-Pzd-Run-Id 与本次运行一致（a3a4a984…）：应答来自本次启动的进程
[ok] / 无租户仍 400
```

Quartz 初始化日志（`artifacts/verify-api.log`）：

```
Successfully validated presence of 37 schema objects
LocalTransactionJobStore initialized.
Adding 1 jobs, 1 triggers
Replacing job: pzd.heartbeat
Quartz Scheduler 4.6.2.0 - 'pzd' with instanceId 'AUTO' initialized
```

`Degraded mode:` 命中 **0** 条 → 一键脚本没有退回内存存储（这是 09 之前缺的那一条）。

## 2. 库侧独立核查（不采信应用自述）

```
pzd_master 存在；qrtz_ 表 15 张；非 qrtz 表 0 张
QRTZ_JOB_DETAILS: pzd / heartbeat / pzd
QRTZ_CRON_TRIGGERS: heartbeat 0 0/1 * * * ?  Asia/Shanghai
QRTZ_TRIGGERS: 运行中 TRIGGER_STATE=WAITING, NEXT/PREV 依次推进
```

## 3. Heartbeat 真的触发（95 秒驻留）

```
[18:52:00] Heartbeat job fired … status=Succeeded | next=18:53:00
[18:53:00] Heartbeat job fired … status=Succeeded | next=18:54:00
```

连 API 进程是手工起的（不经脚本），所以中间没有被收工打断。

## 4. 重启后任务仍在（持久化）

- 停机后查库：`QRTZ_JOB_DETAILS` 仍有 `pzd / heartbeat / pzd`；
- 重启后：`/health/live` 200、`QRTZ_TRIGGERS` 回到 `WAITING`、`next=18:54:00`（未被重置）、`QRTZ_JOB_DETAILS` **仍 1 行**（没有重复注册）；
- 重启后的触发：`[18:54:00] Heartbeat job fired … next=18:55:00`。

## 5. 无连接串 → 内存存储 + Warning（不失败）

```
pwsh … -Mode api -SkipWeb -NoCredentials
EXIT=0；/health/ready 三项 Degraded；/api/version 三字段；/ 400
[WRN] Degraded mode: Quartz persistent store unavailable（ConnectionStrings:master 为空或连不上），已降级为内存存储，重启后任务不保留。
[INF] RAMJobStore initialized. / Using job store 'RAMJobStore', supports persistence: False, clustered: False
```

## 6. 两条观察（非缺陷，但别误用）

1. **`qrtz_execution_history` 表建了但 0 行**。两次成功触发后仍是 0 —— 执行记录目前只进日志（`JobExecutionLogger`），没有落这张表。别把「表存在」当作「执行历史有数据」的证据；要用这张表得先接 Quartz 的 history 插件。
2. **`QRTZ_TRIGGERS` 的时间列是 .NET DateTime ticks，不是 Unix 毫秒**（如 `639286952400000000`）。我第一次用 `FROM_UNIXTIME(ms/1000)` 查出来全是 NULL，差点误判成「trigger 没有下次执行时间」。以后核查要用 `[DateTime]::new(ticks)` 或换算。
3. 强杀 API 后 trigger 可能短暂停在 `ACQUIRED`，下次启动由 misfire 恢复；实测重启后回到 `WAITING` 并正常触发，不构成缺陷。

## 7. 清理

5080/5199 均已释放；未创建任何 `pzd_it_*` 库；`pzd_master` 内容未被本轮改动（只读查询 + 应用自身注册心跳）。
