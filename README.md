# PZD

模块化单体后台系统：.NET（`src/`）+ pnpm monorepo（`apps/`、`packages/`）。分层与依赖规则见 [docs/architecture.md](docs/architecture.md)，协作与实施流程见 [AGENTS.md](AGENTS.md)。

## 仓库约定

### 提交信息

`feat|fix|test|docs|chore(scope): 简体中文描述`，正文写清「改了什么 / 怎么验证」。署名用各自 Bot 的 git 身份（`PZD-<角色>Bot-<n>`），不加任何 AI 署名行，也不用机器的默认身份。

### NuGet 包源

根 [NuGet.config](NuGet.config) 用 `<clear/>` 后只保留 `nuget.org`。原因是仓库启用中央包管理（`ManagePackageVersionsCentrally`）且 `TreatWarningsAsErrors`，一旦本机配置里存在第二个包源就会触发 `NU1507`，直接被抬成构建错误——把源写进仓库，构建结果才与机器无关。

需要私有源时改这一个文件（或加 `packageSourceMapping`），不要靠修改本机全局配置绕过。

包版本一律写在 `Directory.Packages.props`，csproj 里不写 `Version=`。

### 数据库迁移

- 平台级对象放 `db/master/`，业务表放 `db/tenant/`，两份目录的 README 各写了放置与命名规则（`NNNN_简述.sql`，序号全局递增、永不复用）。
- **先迁移后部署**：结构先行，代码随后；租户侧灰度 → 全量，单租户失败不阻塞其它租户。
- 迁移文件只前滚，不写 down 脚本；不写 `CREATE DATABASE`、账号授权或任何凭据。

### 持续集成

门禁入口是 [.github/workflows/ci.yml](.github/workflows/ci.yml)，两个 job：

- `backend`：`dotnet restore` → `dotnet build`（警告即错误）→ `dotnet test`（架构测试）
- `frontend`：`pnpm install --frozen-lockfile` → `pnpm build --force`

三点要留意：

- **架构测试那步会数摘要里的用例数，为 0 就判失败。** MTP 在 0 个用例时退出码仍是 0，只看退出码会把「根本没跑到测试」当成通过。
- **平台口径是假设（GitHub Actions），尚未确认。** 若改成 GitLab CI，只替换这一个文件，其他票的验证命令不变。
- **`--force` 不能省。** turbo 缓存跨 worktree 共享，不加会直接回放「缓存绿」。

## 目录

| 路径 | 说明 |
|---|---|
| `src/Pzd.Api` | 宿主：端点注册、认证、中间件、静态资源 |
| `apps/web` | React Router 后台前端 |
| `packages/{ui,api,realtime}` | 共享组件、接口生成产物、SignalR 客户端 |
| `db/{master,tenant}` | 版本化 SQL（当前只有放置约定，无 SQL） |
| `docs/work/<feature-id>/` | 规格与票 |
