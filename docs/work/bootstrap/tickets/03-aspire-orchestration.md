状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-29 21:58 Asia/Shanghai
下一步：代码已落地且编译通过；实跑被两道环境门挡住（本机无 Docker、本沙箱 EventLog 无写权限），需维护者普通终端跑一次或给外部 MySQL 连接串
交付状态：待收尾
实施条件：已具备（01 `0f25d16`、02 `f13b946` 于 2026-10-29 合入 main）；**待满足**：可运行 Aspire AppHost 的环境，或外部 MySQL 连接串（两者任一即可实跑，详见「实施进展」）

# 03 Aspire 编排：一条命令拉起 MySQL / Redis / API / Web

## 目标

让开发机一条命令启动 MySQL、Redis、API、Web 四个资源，连接串由 Aspire 注入。

## 需求依据

- [architecture.md](../../../architecture.md) §1、§18 步骤 1
- [spec.md](../spec.md) AC-01、AC-04
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.3

## 范围与非范围

- 做：`src/Pzd.AppHost`：声明 MySQL、Redis、API（dotnet）、Web（vite dev），连接串注入 Api；**MySQL / Redis 支持「容器优先、外部实例回落」，用 AppHost 参数切换**（本机实测无 Docker / Podman，只有 MySQL80 `8.0.46`）；开发期 Vite 代理 `/api`、`/hubs` 到后端；README 写那一条命令与两种来源的切换方式。
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
4. 跑一条命令，贴四个资源 Healthy 的输出。

## 验收标准

- [ ] 容器可用时：单条命令后四个资源全部 Healthy，退出码 0，贴实际输出。
- [ ] 容器不可用时（本机现状）：用外部 MySQL `8.0.46` 与外部 Redis 回落跑通，贴实际输出；**缺 Redis 之类无法回落的资源要写明「未运行＋原因」，此时本票不算完整通过，只算部分验证**。
- [ ] README 写清两种来源怎么切换，且不出现任何凭据。
- [ ] `appsettings*.json` 中无明文连接串与密码。
- [ ] 前端 dev 下访问 `/api/health` 由代理转给后端，浏览器无 CORS 报错（健康端点可由 16 号票提供，未就绪时用一个临时 echo 端点证明代理生效并在遗留里写明）。

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

