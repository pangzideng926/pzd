# Agent Note: 后端骨架与构建约定

Status: implemented

## Problem

仓库此前只有架构文档和协作规则，没有任何可编译代码，因此「每个新项目自动继承统一编译设置」这条按 §16.3 属于硬要求、却没有任何落地载体。逐个 csproj 复制 `Nullable`、警告即错误、包版本会随项目增多而漂移；版本号手改文件也不符合 §15.1「版本来自 git tag」。测试侧同样没有约定：.NET 10 SDK 下 xUnit v2 模板生成的工程既不是 §16.1 要求的 v3，`dotnet test` 的 runner 也不再默认走 Microsoft.Testing.Platform（MTP），若不一次定死，后续每张票都会各自踩一遍。

## Decision

**SDK 与编译约定集中一处**：`global.json` 锁 SDK `10.0.402`（`rollForward: latestFeature`），`Directory.Build.props` 统一 `Nullable`、`ImplicitUsings`、`TreatWarningsAsErrors`、`MinVerTagPrefix=v`，并把 MinVer 作为 `PackageReference` 注入所有项目；csproj 只声明目标框架与项目身份。

**包版本集中在 `Directory.Packages.props`**：启用 `ManagePackageVersionsCentrally`，csproj 一律不写包版本。

**包源锁定在仓库内**：根 `NuGet.config` 用 `<clear/>` 后只留 `nuget.org`。原因是中央包管理与「多包源」并存会触发 `NU1507`，而 `TreatWarningsAsErrors` 会把它抬成错误，本机多一个私有源就整树构建失败；把源写进仓库，构建结果才与机器无关。

**版本号来自 git tag**：MinVer 产出 `AssemblyInformationalVersion`（形如 `0.1.1-alpha.0.259+ed4e656…`），`Pzd.Api` 在 `GET /` 与启动日志里透出「版本 + buildId」，作为本里程碑的版本链证明。

**测试栈定 xUnit v3 + MTP**：测试工程为 `OutputType=Exe`（v3 硬要求），`global.json` 写 `test.runner = Microsoft.Testing.Platform` 让 `dotnet test` 走 MTP；工程内保留 `TestingPlatformDotnetTestSupport`（.NET 9 时代开关）。`Pzd.ArchitectureTests` 在本票只放一条用例，断言 Api 程序集的 `InformationalVersion` 存在、语义版本形态正确、且不等于 MSBuild 默认 `1.0.0`。

**0 用例是红灯，不是空转绿灯**（实测）：直跑测试产物加 `-class NoSuchTests` 得到 `Total: 0` 且**退出码 0**。所以「空壳工程通过 `dotnet test`」这件事本身不证明任何断言成立，至少一条真用例是底线。

## Alternatives considered

- **沿用 `dotnet new xunit` 默认栈（xUnit v2 + Verst）** — 最强理由：模板零摩擦、`dotnet test` 行为最稳、团队都熟。没选，是因为 §16.1 指定 xUnit v3，且 .NET 10 SDK 已不再支持 VSTest 目标跑 MTP，晚迁一次要动全部测试工程。
- **包源保留本机全部源、改用 `packageSourceMapping`** — 最强理由：不剥夺私有源，能同时用镜像。没选，是因为本票只需要 `nuget.org` 上那几个包，单源最省事；真要私有源时再加映射，代价只是改这一个文件。
- **版本号写死在 csproj / `Directory.Build.props`** — 最强理由：不依赖 git 历史，打包环境不需要 `.git`。没选，是因为 §15.1 明确要求版本来自 tag，MinVer 在无 tag 时也退化为 `0.0.0-alpha.0`，可接受。
- **架构测试工程保持真正 0 用例（严格照 spec §5）** — 最强理由：不把无关断言塞进架构测试，职责最干净。没选，是因为 0 用例走的正是上面实测到的「退出码 0 但没有断言」路径，等于给本票的绿灯留了一个假绿口子。

## Consequences

- **收益**：新项目零配置继承统一编译语义；包版本单点维护；构建结果不再被机器级包源污染；版本号与 buildId 可从运行中的进程直接读出，后续前端版本检测（§15.2）有后端对应物；`TreatWarningsAsErrors` 让 `NU1507` 这类配置问题在本地就暴露。
- **代价**：仓库内 `<clear/>` 会屏蔽开发机的私有源，需要私有源时必须改 `NuGet.config`；`global.json` 与测试工程里两代 MTP 开关并存，普通终端确认 `dotnet test` 可用后应删掉一条；架构测试工程里这条版本断言最终要迁给 13 号票或专门的版本测试。
- **重访信号**：出现需要私有 NuGet 源的包、CI 上 `dotnet test` 与本地行为不一致、MinVer 与 tag 规则冲突（例如预发布 tag）、或 13 号票落地架构规则后需要重新划分测试工程职责时，重评本决策。

## Verification

```bash
dotnet build Pzd.slnx                                  # 期望：0 警告 0 错误，退出 0
git grep -nE 'PackageReference[^>]*\bVersion\s*=' -- '*.csproj'   # 期望：无命中（退出 1）
./tests/Pzd.ArchitectureTests/bin/Debug/net10.0/Pzd.ArchitectureTests.exe   # 期望 Total>=1
dotnet build Pzd.slnx -p:MinVerSkip=true               # 证红：版本回落到 1.0.0，用例失败
```

`GET /` 的 `buildId` 必须等于被测提交 SHA，可用来排除「跑的不是这条提交」。
