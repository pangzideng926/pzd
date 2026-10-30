状态：待实施
主写入者：未认领
更新日期：2026-10-30 09:15 Asia/Shanghai
下一步：`pzd_dev` 账号已可用，可直接认领；处置完 17 号票才允许开工
交付状态：待收尾
实施条件：已具备（`pzd_dev` 账号对 `pzd_master` 有 DDL 权限，2026-10-30 李总实测通过）

# 37 重置 master 库遗留的旧 Quartz 表

## 目标

把 `d34cf66`（清空前的实现）留在 `pzd_master` 里的 15 张 `qrtz_*` 表**备份后干净重置**，让 17 号票的首次迁移不会撞「表已存在」或 schema 漂移。

## 需求依据

- [architecture.md](../../../architecture.md) §11.1（Quartz.NET 已是确定选型，不是可选项）、§18 步骤 2「Quartz 基础」
- [spec.md](../spec.md) AC-01、AC-09（`qrtz_*` 由 `db/master/` 版本化脚本创建）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.8；2026-10-30 实测数据

## 为什么单开一张票

Quartz 本身要保留（§11.1 已定），所以这 15 张表**不是「要不要」的问题，是「旧 schema 怎么清」的问题**。它挡在 17 号票的第一次迁移前面，而 12 号票（真正建新表）要等 07、08，太晚。所以：

- 本票只做**清场**，不建任何新表；
- 12 号票负责用 `db/master/` 脚本建新表；
- 17 号票前置加本票，认领人**不得自行 drop** 这些表。

## 范围与非范围

- 做：备份 → 按心跳数据确认无活进程 → 按名单逐个 drop 这 15 张表 → 记录处置结论与备份指纹。
- 不做：不建新表（12 号票）、不改 Quartz 选型、不碰业务表、不修半死状态（`heartbeat` 触发器 `WAITING` 但 `NEXT_FIRE_TIME` 为 NULL、`qrtz_locks` 两行——那是**进程未干净停机**的证据，留着当证据，不是拿来修的）。

## 责任文件范围（认领时在群里先说清）

只执行 SQL 与写本票票面；备份文件落 `backups/`（**不入库**）。不碰 `src/`、`apps/`、`db/`（一个字节都不改）。

## 关键接口与依赖

- 前置票：无（只依赖 `pzd_dev` 账号）
- 后继：17 号票（首次迁移）、12 号票（建新表）都必须在本票之后
- 对外契约：处置完成后 `pzd_master` 里**不应存在任何 `qrtz_%` 表**，12 号票按此前提建表

## 步骤

1. **先备份**：`mysqldump` 只导这 15 张表的结构与数据到 `backups/2026-10-30-pzd-master-qrtz.sql`，算 `SHA256` 并记进票面。备份**不进仓库**（`backups/` 不入库）。
2. **确认无活进程**：`SELECT INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL FROM qrtz_scheduler_state;`
   现状（2026-10-30 老登实测）：只有一行 `AUTO` / `LAST_CHECKIN_TIME=2026-10-28 09:06:56` / `CHECKIN_INTERVAL=7500`ms，最后一次心跳距今约 48 小时。**判据：时间差必须大于 `CHECKIN_INTERVAL` 的 10 倍，否则停止本票并报告。**
3. **留证**：把半死状态原样记进票面（`heartbeat` 触发器 `TRIGGER_STATE=WAITING` 且 `NEXT_FIRE_TIME`/`PREV_FIRE_TIME` 均为 NULL；`qrtz_locks` 压着 `STATE_ACCESS`、`TRIGGER_ACCESS` 两行）。
4. **按名单逐个 drop**（禁止用 `qrtz%` 通配符）：`qrtz_blob_triggers`、`qrtz_calendars`、`qrtz_cron_triggers`、`qrtz_execution_history`、`qrtz_fired_triggers`、`qrtz_job_details`、`qrtz_job_status`、`qrtz_locks`、`qrtz_misfire_history`、`qrtz_paused_job_grps`、`qrtz_paused_trigger_grps`、`qrtz_scheduler_state`、`qrtz_simple_triggers`、`qrtz_simprop_triggers`、`qrtz_triggers`。
5. **回查**：`SHOW TABLES LIKE 'qrtz%'` 必须为 0；`SHOW TABLES` 前后差异只能有这 15 张。

## 验收标准

- [ ] 备份文件存在，`SHA256` 记进票面，且**不在仓库里**（`git status` 看不到它）。
- [ ] 备份可还原：导入到一个临时库后 `SHOW TABLES LIKE 'qrtz%'` 数出 15 张（贴输出），临时库用完即删。
- [ ] 心跳证据贴原文，且 `LAST_CHECKIN_TIME` 与操作时刻之差 > 10 × `CHECKIN_INTERVAL`。
- [ ] 处置后 `SHOW TABLES LIKE 'qrtz%'` 为 0，且 `pzd_master` 其余表未变（前后 `SHOW TABLES` 差异只有这 15 张）。
- [ ] 未用通配符 drop，`pzd_master` 之外的库未动。
- [ ] 半死状态证据与处置结论写进票面。

## 验证入口

```bash
# 备份与指纹
mysqldump -h 127.0.0.1 -P 3306 -u pzd_dev -p --databases pzd_master --tables qrtz_... > backups/2026-10-30-pzd-master-qrtz.sql
sha256sum backups/2026-10-30-pzd-master-qrtz.sql
# 心跳与前后表清单
mysql -h 127.0.0.1 -u pzd_dev -p -e "SELECT INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL FROM pzd_master.qrtz_scheduler_state;"
mysql -h 127.0.0.1 -u pzd_dev -p -e "SHOW TABLES FROM pzd_master LIKE 'qrtz%';"
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、执行时间。`-p` 后不写口令，口令从环境变量或已 gitignore 的连接串文件取，**任何口令不得出现在票面与提交里**。

## 恢复方式

按票面记的备份文件与 `SHA256` 还原：`mysql -u pzd_dev -p pzd_master < backups/2026-10-30-pzd-master-qrtz.sql`。还原后回查 15 张表齐全。V1 无生产数据，还原只是保险动作。

## 任务映射

- worktree：不适用（只执行 SQL 与写票面，须先在群里报范围）
- 分支：`chore/37-reset-legacy-quartz-tables`（票面改动）
- 提交：实施时填写（完整 SHA）
