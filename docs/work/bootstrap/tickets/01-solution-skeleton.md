状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-29 17:58 Asia/Shanghai
下一步：等维护者决定是否合入本分支；代码与认领已在任务分支提交，李总在无管道限制环境复核 dotnet test
交付状态：待收尾
实施条件：已具备

# 01 后端解决方案骨架与公共编译约定

## 目标

建出后端解决方案骨架与公共编译约定，让后续每个项目自动继承统一设置，不再逐个 csproj 复制。

## 需求依据

- [architecture.md](../../../architecture.md) §2、§15.1、§16.3
- [spec.md](../spec.md) AC-02、AC-05
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.1

## 范围与非范围

- 做：`Pzd.slnx`（解决方案文件夹分组）、`global.json` 锁 SDK、`Directory.Build.props`（Nullable、TreatWarningsAsErrors、MinVer）、`Directory.Packages.props` 中央包管理、`src/Pzd.Api` 与 `tests/Pzd.ArchitectureTests` 两个空壳项目。
- 超出原范围但已纳入本票（张一一 2026-10-29 决定）：根 `NuGet.config` 锁单一包源——缺它 `dotnet build` 直接因 `NU1507` 失败，属「一条命令能构建」的底座本身，不挪 04。另有 `global.json` 的 `test.runner = Microsoft.Testing.Platform`，该键写在票内本就新建的文件里，不算新增文件。
- 不做：不建业务模块，不建 SharedKernel，不写任何业务代码，不引数据库包。

## 责任文件范围（认领时在群里先说清）

新建 `Pzd.slnx`、`global.json`、`NuGet.config`、`Directory.Build.props`、`Directory.Packages.props`、`src/Pzd.Api/**`、`tests/Pzd.ArchitectureTests/**`。不碰 `apps/`、`packages/`、`db/`。

## 关键接口与依赖

- 前置票：无（可立即开工）
- 对外契约：无对外业务契约；为 05、11、13 号票提供项目骨架与编译约束。

## 步骤

1. 用 `dotnet new` 建 slnx（含 `src`、`tests` 解决方案文件夹）与两个项目。
2. 写 `Directory.Build.props` 与 `Directory.Packages.props`，把 csproj 里的包版本全部上移。
3. 配 MinVer（无 tag 时版本形如 `0.0.0-alpha.0`），在 Api 里输出该版本以证明生效。
4. 跑 `dotnet build` 与 `dotnet test`，把真实输出贴进票。

## 验收标准

- [x] `dotnet build Pzd.slnx` 成功，退出码 0。
- [x] 全仓库 csproj 中不存在显式 `Version=` 的包引用。
- [x] `.slnx` 用解决方案文件夹分组，目录与 §2 一致。
- [x] Api 启动后 `/` 或启动日志能体现 MinVer 推导出的版本号。

## 验证入口

```bash
dotnet build Pzd.slnx
dotnet test tests/Pzd.ArchitectureTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

删除本票新增文件即可，无数据影响。

## 任务映射

- worktree：`E:/pzd/.worktree/01/`
- 分支：`chore/01-solution-skeleton`
- 提交：`42b0e1b2b92826a8e062c940f80ad2a48539b1ce`

## 交接：实现 → 审计

### 1. 目标

建出可编译的后端解决方案骨架与公共编译约定，证明 MinVer 版本链生效（AC-02、AC-05）。

### 2. 提交

- 分支：`chore/01-solution-skeleton`（已推送 `origin/chore/01-solution-skeleton`）
- 提交：`42b0e1b2b92826a8e062c940f80ad2a48539b1ce`，作者 `PZD-实现Bot-1 <impl-bot-1@pzd.local>`
- 基线：`33ade04d79b331326f518411cba84ba1f79613b8`

### 3. 验证（L1，原始输出见 `.ai/验证/01/2026-10-29/证据/run-01/`）

```text
$ dotnet build Pzd.slnx
  Pzd.Api -> ...\src\Pzd.Api\bin\Debug\net10.0\Pzd.Api.dll
  Pzd.ArchitectureTests -> ...\tests\Pzd.ArchitectureTests\bin\Debug\net10.0\Pzd.ArchitectureTests.dll
已成功生成。
    0 个警告
    0 个错误
EXIT_CODE=0

$ ./tests/Pzd.ArchitectureTests/bin/Debug/net10.0/Pzd.ArchitectureTests.exe
   Pzd.ArchitectureTests  Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.097s
EXIT_CODE=0

$ grep -rn 'Version="' --include=*.csproj .
NONE   （全仓库 csproj 无显式包版本）

$ Pzd.Api.exe --urls http://127.0.0.1:5180  →  GET /
{"name":"Pzd.Api","version":"0.1.1-alpha.0.254","buildId":"33ade04d79b331326f518411cba84ba1f79613b8"}
启动日志：info: Pzd.Api[0] Pzd.Api 0.1.1-alpha.0.254（build 33ade04d79b331326f518411cba84ba1f79613b8）已启动
```

**反假绿证明**：先证红再证绿。`dotnet build Pzd.slnx -p:MinVerSkip=true` 后跑同一条用例：

```text
Assert.NotEqual() Failure: Strings are equal
Expected: Not "1.0.0"
Actual:       "1.0.0"
Pzd.ArchitectureTests  Total: 1, Errors: 0, Failed: 1, Skipped: 0
```

说明「版本来自 git tag」这条断言确实能抓到 MinVer 失效，不是空跑。

**未运行 / 环境限制（如实记录）**：

- `dotnet test tests/Pzd.ArchitectureTests` **在本机沙箱下失败**，与本票代码无关：.NET 10 的 MTP 需要测试进程用命名管道连回宿主，沙箱拒绝管道访问，实测报 `System.UnauthorizedAccessException: Access to the path is denied. at System.IO.Pipes.NamedPipeClientStream.TryConnect`，结果被记为 `总计: 0` 并以非零码退出。替代口是直接运行测试可执行文件（上面第二条命令，Total: 1 通过）。**这条要在无管道限制的环境（李总/审计侧）复核一次 `dotnet test`。**
- CI（AC-07）属 04 号票，本票未涉及；PR 未创建（本机无 `gh` CLI，仓库配置需维护者在 GitHub 上点）。

### 4. 遗留

1. **超出票面文件范围新增的根文件已定性**：`NuGet.config` 按张一一 2026-10-29 的决定收进本票（原因见「范围与非范围」）；`global.json` 的 `test.runner` 键写在本票自有文件内。后续若 04 号票要统一仓库约定，从这里接手即可。
2. **architecture 与实现的一处偏差**：`tests/Pzd.ArchitectureTests` 里有 1 条用例（`ApiVersionTests`），不是 spec §5 说的「空壳 0 用例」。原因有两个：MTP 在 0 用例时以非零码退出（`0 tests` 一律按失败，见上面的假绿防线）；且用一条真断言证明 MinVer 生效比人眼看日志可靠。13 号票接管真实架构规则后，这条可保留或迁走。
3. **测试框架选了 xUnit v3 + MTP**（§16.1 要求），不是模板默认的 xUnit v2。
4. **认领已进任务分支（2026-10-29 17:58 补齐）**：张一一 以调度 Bot 身份把 `docs/work/` 与验证证据推到 main（`b3983ca`）后，本分支合并 main（`f146952`），票面「进行中／PZD-实现Bot-1」与本节证据随本提交进入任务分支。此前 `docs/work/` 未入库，新 worktree 里没有本票，无法只提交本票而不夹带他人未提交内容。
5. 待维护者决定：是否同意合入本分支（Bot分工 §5 要求 PR + 维护者明确同意）。
