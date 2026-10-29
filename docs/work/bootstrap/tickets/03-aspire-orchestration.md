状态：待实施
主写入者：未认领
更新日期：2026-10-29 21:45 Asia/Shanghai
下一步：可直接认领；回落路径另需一条外部 MySQL 连接串（与 36 同一前提）
交付状态：待收尾
实施条件：已具备 01、02（`0f25d16`／`f13b946`）；待满足一项：外部 MySQL 建库账号（本机 MySQL80 root 无密码进不去，与 36 号票同一门槛）——只影响「容器不可用回落」路径，容器可用时不受阻塞

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

- worktree：实施时填写（`.worktree/03/`）
- 分支：实施时填写（`chore/03-aspire-orchestration`）
- 提交：实施时填写（完整 SHA）
