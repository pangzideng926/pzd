# bootstrap 规格：工程底座（M1）

> 基线：2026-10-29（v1）　作者：老何
> 需求依据：[architecture.md](../../architecture.md) §1、§2、§14.1、§15.1、§16.3
> 拆分依据：[consensus.md](../pzd-v1/consensus.md) M1（M1.1–M1.4）
> 范围口径：张一一 2026-10-29 确认的「§18 只做步骤 1–5」

## 1. 问题

仓库现在只有架构文档和协作规则，没有一行可运行代码：没有解决方案、没有前端工程、没有把 MySQL / Redis / API / Vite 一起拉起来的编排。后续每个模块都无处落地，也没法用机器验证「Domain 不许引用 SqlSugar」这类架构约定。

## 2. 方案

建立后端 .NET 解决方案骨架、前端 pnpm monorepo（shadcn react-router 模板扁平化到根目录）、Aspire 编排，以及仓库级约定（中央包管理、版本号、提交规范、CI 骨架）。完成后任何人 clone 下来，一条命令启动空壳 API + 前端 + 数据库。

## 3. 用户故事

1. 作为实现 Bot，我想 clone 后一条命令启动 API / MySQL / Redis / 前端，以便立刻验证改动，而不是先花半天配环境。
2. 作为实现 Bot，我想新增项目时自动继承 Nullable、警告即错误、中央包版本，以便不在每个 csproj 里重复配置。
3. 作为实现 Bot，我想改完前端立刻在浏览器看到结果，以便走通先失败后通过的循环。
4. 作为审计 Bot，我想有一条命令跑构建与架构测试，以便判断提交是否可用。
5. 作为维护者，我想版本号由 git tag 推导，以便发布不靠手改文件。
6. 作为实现 Bot，我想开发期 /api 与 /hubs 走 Vite 代理，以便前后端同源（Cookie 认证的前提，§7.1）。
7. 作为实现 Bot，我想数据库目录先有明确放置约定，以便写 SQL 时不用问人放哪。

## 4. 实现决策

- **扁平根结构**（§2）：`Pzd.slnx`、`src/`、`tests/`、`apps/`、`packages/`、`db/` 全在根，分组交给 `.slnx` 解决方案文件夹，不建 `backend/`、`frontend/`、`BuildingBlocks/`、`Modules/`。
- `global.json` 锁 SDK；`Directory.Build.props` 统一 Nullable、`TreatWarningsAsErrors`、MinVer；`Directory.Packages.props` 中央包管理，**csproj 不写版本号**。
- 前端用 shadcn 官方模板初始化（preset `b2oDW9T8C`、`--base radix`、`--template react-router --monorepo`）后扁平化；包为 `apps/web`、`packages/ui`、`packages/api`、`packages/realtime`。
- Turbo 任务依赖 `api#generate → web#build`；本里程碑 `api#generate` 可以先读一个占位 `openapi.json`，真实导出在 16 号票。
- Aspire AppHost 编排 MySQL、Redis、API、Web；连接串由 Aspire 注入，不写死在 `appsettings`。
- `db/master/`、`db/tenant/` 先建目录与 README（只说明放置与命名规则，不含 SQL）。
- 约束：本里程碑不写任何业务代码，不建 SharedKernel（05 号票），不实现认证与迁移。

## 5. 测试决策

- 本里程碑的验收物是「能不能启动」，用实跑证明，不用单测堆数量。
- 启动证明必须给出完整命令、真实退出码和关键输出行（例如 Aspire 资源全部 Healthy），按 L1 记录到票。
- 架构测试项目此时是空壳：只要求能编译、跑出 0 个用例；真正的分层规则在 13 号票。
- 不测试目录结构的字面值（路径易变），改由审查核对是否与 §2 一致。

## 6. 非目标

- 不建业务模块、不建 SharedKernel、不做分层落地。
- 不实现认证、数据库迁移、OpenAPI 真实导出、健康检查。
- 不做生产部署、容器化发布与回滚（§15.4，另立）。
- 不初始化任何数据库内容。

## 7. 验收标准

- **AC-01** 干净 clone 后执行 README 里那一条命令，MySQL、Redis、API、Web 四个资源全部启动，退出码 0。
- **AC-02** `dotnet build Pzd.slnx` 成功，且全仓库 csproj 中不存在显式 `Version=` 的包引用（中央包管理生效）。
- **AC-03** 根目录 `pnpm install && pnpm build` 成功，turbo 按 `api#generate → web#build` 顺序执行。
- **AC-04** 前端 dev 模式访问 `/api/health` 由 Vite 代理到后端并返回响应，浏览器控制台无 CORS 报错。
- **AC-05** 目录结构与 §2 一致，`.slnx` 用解决方案文件夹分组。
- **AC-06** 提交信息符合 `feat|fix|test|docs|chore(scope): …`，签名用 Bot 自己的 git 身份，无 AI 署名。
- **AC-07** CI 配置存在并在本仓库真实跑过一次（未授权跑 CI 时记「未运行」并说明，不能写成通过）。

## 8. 假设与待决

- **A1（待确认）** CI 平台按 GitHub Actions 写（远端是 GitHub）。维护者若选 GitLab CI，只改 04 号票这一处。
- **A2** 开发机具备 global.json 锁定的 .NET SDK 与 Node/pnpm；版本以文件为准，不在此写死。

## 9. 来源

- [architecture.md](../../architecture.md) §1、§2、§14.1、§15.1、§16.3
- [consensus.md](../pzd-v1/consensus.md) M1、§4 待决 5、8
- [存储映射](../../../.ai/规则/存储映射.md)、[四阶段技能](../../../.ai/规则/四阶段技能.md)
