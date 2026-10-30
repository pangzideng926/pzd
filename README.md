# PZD

模块化单体后台系统：.NET（`src/`）+ pnpm monorepo（`apps/`、`packages/`），仓库分层与依赖规则见 [docs/architecture.md](docs/architecture.md)，协作与实施流程见 [AGENTS.md](AGENTS.md)。

## 一条命令启动

```bash
dotnet run --project src/Pzd.AppHost
```

AppHost 会把四个资源一起拉起并在仪表板里显示健康状态：`mysql`（MySQL 8.4 容器，库 `pzd-master`）、`redis`（Redis 7.4 容器）、`api`（`src/Pzd.Api`）、`web`（`apps/web` 的 Vite 开发服务器，pnpm）。连接串由 AppHost 注入 Api，仓库里不存密码。

开发期 `/api`、`/hubs` 由 Vite 代理到后端（见 `apps/web/vite.config.ts`），所以前后端同源，Cookie 认证不会触发 CORS。

### 数据源两种来源

默认起容器（需要本机可用 Docker / Podman）。没有容器运行时，用外部实例回落——给 AppHost 传两个连接串即可，配了就不再起容器：

```bash
External__MySql="Server=127.0.0.1;Port=3306;Database=pzd_master;User ID=pzd_dev;Password=<口令>" \
External__Redis="127.0.0.1:6379" \
dotnet run --project src/Pzd.AppHost
```

连接串只走环境变量或 AppHost 配置，不写进 `appsettings.json`，也不入库。

**`Database=` 不能省，库名要和实际实例对齐。** 上面写的是 `pzd_master`（下划线），注意两条路径的库名不同：

- 容器路径：由 `AddDatabase("pzd-master")` 生成，库名是 `pzd-master`
- 回落路径：库名由连接串的 `Database=` 决定，本机实例上是 `pzd_master`，**没有** `pzd-master`

资源名必须是 `pzd-master`（含下划线会触发 `ASPIRE006`），但那只是连接串的**键名**，不决定库名。回落时若照抄容器库名，会直接得到 `ERROR 1049 (42000): Unknown database 'pzd-master'`——这是实测的。

## 目录

| 路径 | 说明 |
|---|---|
| `src/Pzd.Api` | 宿主：端点注册、认证、中间件、静态资源 |
| `src/Pzd.AppHost` | Aspire 编排，唯一启动入口 |
| `apps/web` | React Router 后台前端 |
| `packages/{ui,api,realtime}` | 共享组件、接口生成产物、SignalR 客户端 |
| `db/{master,tenant}` | 版本化 SQL |
| `docs/work/<feature-id>/` | 规格与票 |

## 状态

骨架阶段：`Pzd.Api` 目前只有一个 `/` 端点，返回 MinVer 从 git tag 推导的版本与 buildId。业务模块、认证、数据库迁移尚未实现。
