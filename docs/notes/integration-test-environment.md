# 集成测试环境约定（本机 MySQL / Redis，2026-10-27 实测）

> 来源：Chief 实测 `127.0.0.1:3306` / `127.0.0.1:6379`（`local.env`，不入库）；时区与驱动部分由旺总用 `MySqlConnector 2.6.2` 实测
> 适用：票 03（AppHost 引用本机实例）、票 09（AdoJobStore / readiness）、票 10（租户隔离集成测试）
> 状态：开发约定；票 10 的验收项以 `docs/specs/tickets.md` 为准，本条只补测试实现约束
> 修订：2026-10-27 修正时区做法（原写 `ServerTimeZone=UTC`，MySqlConnector 2.6.2 不支持该关键字）

## 实例事实

| 项 | 实测值 |
|---|---|
| MySQL | 8.0.46，InnoDB，`max_connections=151`，`@@global.time_zone = @@session.time_zone = SYSTEM`（东八区） |
| MySQL 权限 | `root@localhost` 具备 `GRANT ALL ON *.* WITH GRANT OPTION` → `CREATE/DROP DATABASE` 可用 |
| 字符集 / 排序规则 | 建库后 `utf8mb4` / `utf8mb4_0900_ai_ci`（与架构 §5.3 一致） |
| Redis | Memurai 8.2.10，16 个逻辑库，`PZD_REDIS_TEST_DB=15` 可用 |
| 驱动 | `MySqlConnector 2.6.2`（由 `SqlSugarCore 5.1.4.221` 的 nuspec 强依赖，仓库无 `MySql.Data`） |
| 起点 | 当前实例无任何 `pzd%` 数据库 |

## 测试实现必须遵守的两条

1. **时区不能在连接串里配，也不能依赖服务器默认值**：`@@time_zone` 是 `SYSTEM`（东八区），架构 §5.3 要求存 UTC。
   `MySqlConnector 2.6.2` 实测：
   - `ServerTimeZone=UTC` → `ArgumentException: Option 'servertimezone' not supported.`
   - `ConnectionTimeZone=UTC` → 同样不支持；未知关键字一律抛异常，不会静默忽略。
   - `DateTimeKind=Utc`（`MySqlDateTimeKind.Utc`）**支持但不够**：它只决定读回 `DateTime` 的 `Kind`，不改会话时区。

   正确做法：每次 `Open` 之后执行 `SET time_zone = '+00:00'`，并断言 `SELECT @@session.time_zone` 为 `+00:00`；或在查询里显式 `CONVERT_TZ`。
   **注意连接池**：实测同池复用的第二条连接会话时区又回到 `SYSTEM`（驱动默认 `ConnectionReset` 会重置会话状态），所以不能只在 fixture 初始化时 SET 一次，必须挂在「获取连接」这一步（或评估后显式关掉 `ConnectionReset`）。
   不得写「插入 `DateTime.UtcNow` → 读回相等」这类吃服务器默认时区的断言。
2. **表名大小写不敏感**：Windows 上 `lower_case_table_names=1`，`pzd_it_a.users` 与 `pzd_it_a.Users` 指向同一张表。
   跨租户隔离的证据只能来自**数据行**（A 库只查到 A 的数据、QueryFilter 生效）与**库名**，不得用表名大小写差异证明隔离。

## 与票 10 的关系

- 租户库命名沿用 `pzd_it_a` / `pzd_it_b`（票 10 的 fixture 自动创建与清理）；本机具备建库权限，主路径直接跑真实 MySQL，不需要「无权限」降级分支。
- 若 `local.env` 缺失或实例不可达，票 10 仍按动态 Skip 处理（CI 兜底），Skip 原因写入测试输出。
