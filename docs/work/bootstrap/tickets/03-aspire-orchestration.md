状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-30 08:56 Asia/Shanghai
下一步：连接串已到位，回落链路两侧数据源已复核（Redis 四项真语义、MySQL `pzd_test` 账号可连）；只剩 AppHost 实跑被 EventLog 挡住，需维护者在有权限的终端跑一次 `dotnet run --project src/Pzd.AppHost`；库名口径（`pzd_master` vs `pzd-master`）待定
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

1. **错误原文与真实退出码**：`Unhandled exception. System.AggregateException: ... (Cannot open log for source '.NET Runtime'. You may not have write access.)` → `System.ComponentModel.Win32Exception (5): 拒绝访问` → `The AppHost process exited unexpectedly with exit code -532462766`，`RUN_EXIT=2`
2. **替代执行方式的实际结果**：`dotnet build Pzd.slnx` → `已成功生成。0 个警告 0 个错误`，退出码 0（证明代码可编译、接线类型正确，但**不证明资源能起**）
3. **明确记录**：四个资源 Healthy **未运行**，需在 EventLog 可写、且具备 Docker 或外部 MySQL 连接串的终端补跑一次
4. **剩余风险**：`WithPnpm`/`AddViteApp` 的 Vite 端口与端点注入、`WithReference(api)` 的服务发现环境变量名（`services__api__http__0`）都只在运行时才暴露，编译通过不代表代理真通；回落路径的数据源已在 2026-10-30 单独复核（Redis 四项真语义全通、MySQL 账号可连，见下文「回落链路复核」），但**库名口径仍有缺口**——AppHost 注入的连接串键取资源名 `pzd-master`，本机实际库是 `pzd_master`，回落连接串必须自带 `Database=`

**四个资源真 Healthy 之前不标完成**，本票维持「进行中／待收尾」。

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
- **`pzd-master`（连字符）不存在**：``USE `pzd-master` `` → `ERROR 1049 (42000): Unknown database 'pzd-master'`
- 而回落路径注入的连接串键由资源名决定，资源名是 `pzd-master`（Aspire 禁止下划线，会报 `ASPIRE006`）
- 所以**回落连接串必须自带 `Database=pzd_master`**。维护者这次给的串是 `Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=…`，**不含 `Database=`**

这条不会让「四个资源 Healthy」失败（`AddConnectionString` 型资源不做库级健康检查），但会在 06 之后第一次真正读写库时暴露。改法两选，需先定口径，**本段不动代码**：

1. 回落连接串统一带 `Database=pzd_master`（根 README 的示例已这样写，运维侧要补齐）
2. 或把 `db/master` 对应的库名统一成 `pzd-master`，与容器路径一致——代价是要迁走现有 15 张表

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

