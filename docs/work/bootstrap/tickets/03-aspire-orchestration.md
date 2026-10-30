状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-30 08:56 Asia/Shanghai
下一步：数据源侧已复核完整（`pzd_dev` 账号与权限边界、库名口径 `pzd_master`、启动期校验双向、Redis 四项真语义）；只剩在有权限终端跑一次 `dotnet run --project src/Pzd.AppHost` 拿四个资源 Healthy——EventLog 墙与数据源无关
交付状态：待收尾
实施条件：已具备 01、02（`0f25d16`／`f13b946`）与外部 MySQL 账号（`pzd_test@localhost`，2026-10-30 到位）；**待满足**：一处能跑 AppHost 的执行环境（EventLog 可写，或用 Docker）——这道门与连接串无关，已用连接串复跑确认

# 03 Aspire 编排：一条命令拉起 MySQL / Redis / API / Web

## 目标

让开发机一条命令启动 MySQL、Redis、API、Web 四个资源，连接串由 Aspire 注入。

## 需求依据

- [architecture.md](../../../architecture.md) §1、§18 步骤 1
- [spec.md](../spec.md) AC-01、AC-04
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.3

## 范围与非范围

- 做：`src/Pzd.AppHost`：声明 MySQL、Redis、API（dotnet）、Web（vite dev），连接串注入 Api；**MySQL / Redis 支持「容器优先、外部实例回落」，用 AppHost 参数切换**——本机实测无 Docker / Podman，回落用 MySQL80 `8.0.46` 与 Memurai（Windows 上的 Redis 兼容服务，`127.0.0.1:6379`）；开发期 Vite 代理 `/api`、`/hubs` 到后端；README 写那一条命令与两种来源的切换方式。
- 不做：不做生产部署、不做容器镜像发布、不初始化数据库内容。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.AppHost/**`；改 `apps/web` 的 dev server 代理配置与根 `README.md`。不碰其他 `src/` 项目。

## 关键接口与依赖

- 前置票：01、02
- 对外契约：为全体提供唯一启动入口；连接串只经 AppHost 注入，不写进 `appsettings`。

## 步骤

1. 建 AppHost 项目并声明四个资源（镜像版本固定），MySQL / Redis 带「容器优先、外部实例回落」参数。
2. 把连接串以环境变量注入 Api，`appsettings` 里不出现密码。
3. 配 Vite 代理 `/api`、`/hubs`。
4. 跑一条命令，贴四个资源 Healthy 的输出；回落路径下 MySQL / Redis 的连接串从环境变量取，进阶说明写进 README（不写凭据）。

## 验收标准

- [ ] 容器可用时：单条命令后四个资源全部 Healthy，退出码 0，贴实际输出。
- [ ] 容器不可用时（本机现状）：用外部 MySQL `8.0.46` 与 Memurai 回落跑通，四个资源全部 Healthy，贴实际输出——**这两个都是真服务，所以本路径可以完整验收，不再按「部分验证」处理**。
- [ ] 认领人必须自行复核两个外部实例的真实性与版本（`redis-cli PING` + 一次 `SET`/`GET` + 一次 `SUBSCRIBE`/`PUBLISH` 对 Memurai，`SELECT VERSION()` 对 MySQL），贴命令与输出。后两项是硬要求：§6.3 的多节点广播与 §13 的分布式锁都依赖真 Redis 语义，只 PING 通不算兼容。张一一 2026-10-29 报的 Memurai `8.2.10` 只作线索，不复核就当未验证。
- [ ] 版本差异写进遗留：本机 `8.0.46` / Memurai `8.2.10` 与容器版 `mysql:8.4` / `redis:7.x` 的差异必须列明，并在有容器的环境（CI）再复核一次；未复核前不得声称版本一致性已验证。
- [ ] README 写清两种来源怎么切换，且不出现任何凭据。
- [ ] `appsettings*.json` 中无明文连接串与密码。
- [ ] 前端 dev 下访问 `/api/health` 由代理转给后端，浏览器无 CORS 报错（健康端点可由 16 号票提供，未就绪时用一个临时 echo 端点证明代理生效并在遗留里写明）。
- [ ] 包版本偏离必须在票面写清：为修 `NU1902`（`Aspire.Hosting.MySql` 传递依赖 `MySqlConnector 2.3.1`）而显式提升版本，属于让票能跑通的最小必要动作，但必须①在票面写明改了哪个包、从哪版到哪版、原因；②确认没有其他项目仍钉旧版；③不夹带无关升级。
- [ ] 实跑被环境阻断时（例如本沙箱里 AppHost 启动即崩：`Cannot open log for source '.NET Runtime'` → `Win32Exception (5): 拒绝访问` → 退出码 `-532462766`，Windows EventLog 源需要管理员权限），按「未运行」四项记：错误原文与退出码、替代验证（build 通过等）、需在有权限的普通终端补跑的明确命令、剩余风险。**在四个资源真正 Healthy 之前，不得把本票标为已完成。**

## 验证入口

```bash
aspire run（或 README 中的单条命令）
curl -i http://localhost:<web>/api/health
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚 AppHost 项目与代理配置；数据库实例为容器，直接删除即可。

## 任务映射

- worktree：`E:/pzd/.worktree/03/`
- 分支：`feat/03-aspire-orchestration`
- 提交：见本段下方「实施进展」的被测 SHA

## 实施进展（2026-10-29 21:58，PZD-实现Bot-1）

**已落地**

- `src/Pzd.AppHost`（Aspire 13.6.4）：`mysql`（`mysql:8.4` 容器 + `pzd-master` 库）、`redis`（`redis:7.4` 容器）、`api`（`src/Pzd.Api` 项目）、`web`（Vite 开发服务器，pnpm）四个资源；连接串只经 AppHost 注入，`appsettings` 里没有密码。
- 「容器优先、外部实例回落」用配置切换：`External:MySql` / `External:Redis` 给了值就用外部实例（`AddConnectionString`），不给就起容器；只有走容器时才 `WaitFor` 数据源。
- `apps/web/vite.config.ts`：`/api`、`/hubs` 代理到 Aspire 注入的 `services__api__http__0`（未在 Aspire 下启动时回落 `http://127.0.0.1:5080`），端口读 `PORT`。
- 编译证据：`dotnet build Pzd.slnx` → `已成功生成。0 个警告 0 个错误`，退出码 0。

**两道环境门（导致本轮拿不到「四个资源 Healthy」）**

1. 本机无 Docker / Podman → 容器路径起不来，只能走回落路径，而回落需要外部 MySQL 连接串。
2. 本沙箱下 AppHost 直接崩在日志初始化，与 03 代码无关：

```text
$ timeout 60 dotnet run --project src/Pzd.AppHost --no-build
Unhandled exception. System.AggregateException: ... (Cannot open log for source '.NET Runtime'. You may not have write access.)
System.ComponentModel.Win32Exception (5): 拒绝访问
❌ 发生意外错误: The AppHost process exited unexpectedly with exit code -532462766
```

即 Windows EventLog 源需要管理员写权限，受限执行环境里拿不到；这是执行环境限制，和 01 那张票的「命名管道被拒」同类。

**给维护者的最小动作**（任选一，普通终端执行）

```bash
cd E:/pzd/.worktree/03 && dotnet run --project src/Pzd.AppHost
# 或先给回落连接串再跑
# External__MySql="Server=127.0.0.1;Port=3306;Database=pzd_master;User ID=...;Password=..." dotnet run --project src/Pzd.AppHost
```

期望：仪表板里 `mysql`/`redis`/`api`/`web` 四个资源 Healthy，Web 端点打开后 `/api/health` 可经代理回后端。

**其他留痕**

- 传递依赖 `MySqlConnector 2.3.1` 有已知漏洞（`NU1902`，被警告即错误抬成失败），已在 `Directory.Packages.props` 显式提升到 `2.6.2`。
- `AddProject` 用路径重载而非生成的 `Pzd_Api` 类型，避免 AppHost 与 Api 的源生成耦合。
- 实测本机 `dotnet new install Aspire.ProjectTemplates`（13.6.4）已装到本机模板库，属环境改动，非仓库改动。

### 「未运行」四要素（按票面环境条款记）

1. **错误原文与真实退出码**：经 `dotnet run` 的 CLI 包装时看到 `Unhandled exception. System.AggregateException: ... (Cannot open log for source '.NET Runtime'. You may not have write access.)` → `System.ComponentModel.Win32Exception (5): 拒绝访问` → `The AppHost process exited unexpectedly with exit code -532462766`，`RUN_EXIT=2`。**这条是二次故障**——直跑 exe 后真正的根因露出来了，见下节「环境阻断的真正根因」
2. **替代执行方式的实际结果**：`dotnet build Pzd.slnx` → `已成功生成。0 个警告 0 个错误`，退出码 0（证明代码可编译、接线类型正确，但**不证明资源能起**）
3. **明确记录**：四个资源 Healthy **未运行**，需在**允许修改 NTFS ACL 的普通终端**补跑一次（沙箱禁 `SetSecurityInfo`，见下节）；Docker 与外部 MySQL 连接串二者有其一即可
4. **剩余风险**：`WithPnpm`/`AddViteApp` 的 Vite 端口与端点注入、`WithReference(api)` 的服务发现环境变量名（`services__api__http__0`）都只在运行时才暴露，编译通过不代表代理真通；回落路径的数据源已复核（Redis 四项真语义、MySQL `pzd_dev` 账号与权限边界，见下文「回落链路复核」）；库名口径已定为 `pzd_master` 并有启动期校验兜底

**四个资源真 Healthy 之前不标完成**，本票维持「进行中／待收尾」。

### 环境阻断的真正根因（2026-10-30 13:35–13:38，逐层剥出来的）

先前记的「EventLog 要管理员权限」是**症状，不是根因**。三次实验把它剥清楚了：

| # | 做法 | 结果 |
|---|---|---|
| 1 | 直跑 `Pzd.AppHost.exe`，不经 `dotnet run` 的 CLI 包装 | 看到被掩埋的两条原始异常：`UnauthorizedAccessException: Access to the path 'C:\Users\…\Temp\aspire-dcp….ktn' is denied`（`Directory.CreateTempSubdirectory`），以及 `SetSecurityInfo … 未授权` |
| 2 | `Logging__EventLog__LogLevel__Default=None` + `TMP`/`TEMP` 指向 `E:\pzd\.worktree\03\.local\tmp` | EventLog 报错消失、`%TEMP%` 那层被绕过：**DCP 真的起来了**（`Starting DCP with arguments: start-apiserver …`、`Started monitoring process {"PID": …}`），进程不再立即崩溃（`timeout` 到期才被杀，`RC=124`） |
| 3 | 让它跑 90 秒后查现场 | `dcp.exe` 进程已不在、没有任何 `LISTENING` 端口；Aspire 仍停在「等 DCP 就绪」，日志不再推进 |

**最终那道墙是 `System.Security.AccessControl.Win32.SetSecurityInfo` 被拒**（`UnauthorizedAccessException: Attempted to perform an unauthorized operation`）。Aspire 起编排要先建 socket 目录再给它设 ACL，两个调用点死在同一处：

- `Aspire.Shared.SocketPermissionHelper.CreateWindowsDirectory`（auxiliary backchannel）
- `Aspire.Hosting.Dcp.DcpHost.CreateLoggingSocket`（编排日志）

本沙箱禁止修改 NTFS ACL，所以这不是「多设个环境变量」能绕过去的：DCP 通信端点建不起来 → Aspire 永远等不到 DCP 就绪 → 四资源不可能 Healthy。

**给补跑的人两条**：

1. 别先怀疑 EventLog。它在普通终端上本来就可写；真跑不起来时先看 `%TEMP%` 与 ACL，别被那条 `Win32Exception (5)` 带偏。
2. 想复现我这套诊断：`Logging__EventLog__LogLevel__Default=None`，并把 `TMP`/`TEMP` 指到可写目录，能把被 EventLog 吞掉的原始异常逼出来。

### 包版本偏离（老何要求：改了什么、还有谁钉旧版、有没有夹带）

- **改了什么**：`Directory.Packages.props` 新增 `<PackageVersion Include="MySqlConnector" Version="2.6.2" />`，`src/Pzd.AppHost/Pzd.AppHost.csproj` 显式 `<PackageReference Include="MySqlConnector" />` 覆盖传递依赖。
- **为什么**：`Aspire.Hosting.MySql 13.6.4` 传递依赖 `MySqlConnector 2.3.1`，该版本有已知漏洞（`NU1902`，GHSA-473q-m89c-ghf8），被 `TreatWarningsAsErrors` 抬成构建失败；除非降级 Aspire，否则只能显式提升。
- **还有谁钉旧版**：全仓库 `grep MySqlConnector` 只有上两处（一处 `Directory.Packages.props`、一处 AppHost csproj），其余项目未直接引用；`Pzd.Api` 目前不引数据库包。
- **有没有夹带无关升级**：没有。本票新增包只有 `Aspire.Hosting.{AppHost,MySql,Redis,JavaScript} 13.6.4` 与 `MySqlConnector 2.6.2`，其余项目依赖零变动。

## 回落链路复核（2026-10-30 08:52–08:56，PZD-实现Bot-1）

维护者给出外部 MySQL 账号（`pzd_test@localhost`）后，本段只做复核，不改代码。凭据不出现在票面、日志与任何提交里。

### Redis / Memurai：四项真语义全部通过

```text
$ memurai-cli -h 127.0.0.1 -p 6379 ping
PONG

$ memurai-cli ... set pzd:verify:key "fallback-ok"   → OK
$ memurai-cli ... get pzd:verify:key                 → fallback-ok

$ memurai-cli ... subscribe pzd:verify:chan &        （订阅端）
$ memurai-cli ... publish pzd:verify:chan "pubsub-ok"
PUBLISH_RECEIVERS=1
订阅端收到：subscribe / pzd:verify:chan / 1 / message / pzd:verify:chan / pubsub-ok

$ memurai-cli ... info server
redis_version:8.2.10   memurai_version:8.2.0   tcp_port:6379
```

验证键已清理（`DEL pzd:verify:key` → `exists=0`）。老何要求的「`PING` 之外还要 `SET`/`GET` 与 `SUBSCRIBE`/`PUBLISH`」到此满足，不是只 PING 通。

### MySQL：账号可连，但库名口径不一致（本段最重要的发现）

```text
$ mysql -h 127.0.0.1 -P 3306 -u pzd_test -e "SELECT VERSION(), CURRENT_USER()"
8.0.46    pzd_test@localhost

$ SHOW DATABASES;
information_schema  mysql  performance_schema  pzd_master  sys
```

- 本机库名是 **`pzd_master`（下划线）**，里面有 15 张 `qrtz_*` 表，`CREATE_TIME` 全是 `2026-10-27 17:46:45`——是清空前那版实现留下的 Quartz 表，**属既有数据，不是本票建的**
- **账号分工（老何 2026-10-30 定，两个账号不许合一）**：`pzd_test` 是**测试账号**，只用在集成测试（36 号票建删 `pzd_test_%` 临时库），不给任何 `ON *.*`；应用运行与回落路径用 `pzd_dev`（`pzd_master`.* + `pzd_t_%`.*，含 DDL）。**已按 `pzd_dev` 复核，证据见下文「pzd_dev 复核」**
- **`pzd-master`（连字符）不存在**：``USE `pzd-master` `` → `ERROR 1049 (42000): Unknown database 'pzd-master'`
- 而回落路径注入的连接串键由资源名决定，资源名是 `pzd-master`（Aspire 禁止下划线，会报 `ASPIRE006`）
- 所以**回落连接串必须自带 `Database=pzd_master`**。维护者这次给的串是 `Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=…`，**不含 `Database=`**

这条不会让「四个资源 Healthy」失败（`AddConnectionString` 型资源不做库级健康检查），但会在 06 之后第一次真正读写库时暴露。

**口径已定（老何，2026-10-30）：采用方案 1——回落连接串显式带 `Database=pzd_master`。** 依据不是权衡，是架构书 §6.1 原文就写 `pzd_master`（平台库）与 `pzd_t_{code}`（租户库）；方案 2 改库名属对架构的偏离，要为它迁走 15 张旧表不值。判据一句话：**键名可以有连字符，库名不行。**

两选留档：

1. 回落连接串统一带 `Database=pzd_master`（根 README 的示例已按此写，运维侧要补齐）——**采纳**
2. 把库名统一成 `pzd-master`、与容器路径一致——未采纳，代价是迁走现有 15 张表，且偏离 §6.1

### 启动期校验：缺 `Database=` 直接起不来（已实现）

只在 README 写一句挡不住下一次踩坑，所以在 `src/Pzd.AppHost/AppHost.cs` 加了一道启动期校验：回落连接串里没有 `Database=`（也认 `Initial Catalog=`）就抛 `InvalidOperationException`，消息给出正确写法。

**这道校验能真验**，它放在 `builder.Build().Run()` 之前，比 EventLog 崩溃更早触发：

```text
$ External__MySql="Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=…"     # 缺 Database=
Unhandled exception. System.InvalidOperationException: 回落路径的 MySQL 连接串缺少 Database=…
   at Program.<Main>$(String[] args) in E:\pzd\.worktree\03\src\Pzd.AppHost\AppHost.cs:line 18
（这条输出里没有 EventLog 报错）

$ External__MySql="…;Database=pzd_master"                                       # 带库名
System.InvalidOperationException: Cannot open log for source '.NET Runtime'. You may not have write access.
   ---> System.ComponentModel.Win32Exception (5): 拒绝访问
（校验放行，落在既有的 EventLog 墙上）
```

控制台代码页会把中文消息显示成乱码，消息正文以 `AppHost.cs` 源码为准——判据是两个场景**抛出的异常不同**：缺库名抛本票新增的校验、带库名落到 EventLog，说明校验既拦得住、也不误伤正确写法。`dotnet build Pzd.slnx` 0 警告 0 错误。

决策笔记：`.agents/notes/implemented/architecture/2026-10-30-datasource-fallback-database-guard.md`（为什么不在代码里替使用者补 `Database=`、为什么不用健康检查替代）。

### pzd_dev 复核（2026-10-30 09:10，按「两个账号分开」口径）

维护者建好 `pzd_dev` 并收回 `pzd_test` 的全局权限后，改用 `pzd_dev` 的连接串复核，不再拿测试账号代跑：

```text
$ mysql -u pzd_dev -e "SHOW GRANTS FOR CURRENT_USER()"
GRANT USAGE ON *.* TO `pzd_dev`@`localhost`                          ← 没有全局权限
GRANT ALL PRIVILEGES ON `pzd_master`.* TO `pzd_dev`@`localhost`
GRANT ALL PRIVILEGES ON `pzd_t_%`.* TO `pzd_dev`@`localhost`

$ mysql -u pzd_dev -e "USE pzd_master; SELECT DATABASE(), 表数…"
pzd_master    15

$ mysql -u pzd_dev -e "CREATE TABLE mysql.__probe(id INT)"           ← 负向：跨库必须被拒
ERROR 1142 (42000): CREATE command denied to user 'pzd_dev'@'localhost' for table '__probe'

$ mysql -u pzd_test -e "SHOW GRANTS FOR CURRENT_USER()"              ← 已收紧
GRANT USAGE ON *.* TO `pzd_test`@`localhost`
GRANT ALL PRIVILEGES ON `pzd_test_%`.* TO `pzd_test`@`localhost`

$ External__MySql="Server=…;Database=pzd_master;User ID=pzd_dev;…" dotnet run --project src/Pzd.AppHost
（通过 Database= 校验，落到既有的 EventLog 墙）
```

数据源侧到此复核完整：账号、权限边界（正向能进 `pzd_master`、负向跨库被 `1142` 拒）、库名口径、启动期校验双向，四项都验了。剩下唯一未验的仍是「四个资源 Healthy」，卡在 EventLog，与本票改动无关。

同时把分支合并到 main（`12b543f`）：冲突只在 `Pzd.slnx`，两边各加一个项目，都保留。合并后 `dotnet build Pzd.slnx` 0 警告 0 错误，`Pzd.SharedKernel.UnitTests` 直跑 `Total: 33, Failed: 0`。

### `pzd_master` 里那 15 张 Quartz 表的来历（给 17 号票与接手人）

**不是本票建的，本票也没动它们。** 事实如下：

- 15 张表，`CREATE_TIME` 全是 `2026-10-27 17:46:45`，`SCHED_NAME = pzd`，属清空前那版实现（`d34cf66`）的 Quartz.NET 调度器 schema
- **没有活着的调度器在连**：`qrtz_scheduler_state` 只有一行（`INSTANCE_NAME=AUTO`，`CHECKIN_INTERVAL=7500` 毫秒），`LAST_CHECKIN_TIME` 距今约 48 小时
- **`qrtz_locks` 仍压着 `STATE_ACCESS`、`TRIGGER_ACCESS` 两行**，是进程被打断后留下的锁记录

**一个会误导判断的坑：这些时间字段是 .NET ticks（100 纳秒），不是 Unix 时间戳。** 用 `FROM_UNIXTIME(...)` 查只会得到 `NULL`，很容易被读成「字段为空 / 触发器是半死状态」。正确换算是 `ticks / 10^7 - 62135596800` 得到 Unix 秒：

| 字段 | 原始值（ticks） | UTC | 本地（+08:00） |
|---|---|---|---|
| `qrtz_scheduler_state.LAST_CHECKIN_TIME` | `639287464167601504` | 2026-10-28 01:06:56 | 2026-10-28 09:06:56 |
| `qrtz_triggers.PREV_FIRE_TIME`（`heartbeat` / `pzd`） | `639287464200000000` | 2026-10-28 01:07:00 | 2026-10-28 09:07:00 |
| `qrtz_triggers.NEXT_FIRE_TIME`（同上） | `639287464800000000` | 2026-10-28 01:08:00 | 2026-10-28 09:08:00 |

所以那条 `heartbeat` 触发器**不是**「WAITING 却没有下次触发时间」：它上一次 09:07:00、下一次 09:08:00，是 1 分钟间隔的正常心跳，只是进程在 09:06:56 之后没再跑过。

处置不属本票：老何已单开票处理这 15 张旧表，17 号票照那张票的前置执行。本票只留上面这些事实，免得接手人凭空猜。

### 授权比脚本声明的宽（安全提示，只报事实）

```text
GRANT CREATE, DROP ON *.* TO `pzd_test`@`localhost`
GRANT ALL PRIVILEGES ON `pzd\_test\_%`.* TO `pzd_test`@`localhost`
```

`CREATE, DROP ON *.*` 是**全局**权限：`pzd_test` 能在任何库建表、删表，不只是 `pzd_test_%`。实测在 `pzd_master` 建表**成功**；探针表 `__probe` 已 `DROP`，回查该库仍是 15 张原表。这不属本票范围，只把事实报出来——若账号本意是「只能动 `pzd_test_%`」，这条要收紧。

### AppHost 实跑：仍被 EventLog 挡，与连接串无关

```text
Cannot open log for source '.NET Runtime'. You may not have write access.
   ---> System.ComponentModel.Win32Exception (5): 拒绝访问
❌ 发生意外错误: The AppHost process exited unexpectedly with exit code -532462766
```

带上 `External__MySql` / `External__Redis` 重跑，崩在同一处，说明**障碍来自 Windows EventLog 源需要管理员权限，不是数据源**。四个资源 Healthy 继续记「未运行」，本票维持「进行中／待收尾」。

