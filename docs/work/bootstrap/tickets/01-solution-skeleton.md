状态：已完成
主写入者：PZD-实现Bot-1
更新日期：2026-10-29 21:25 Asia/Shanghai
下一步：已并入 main（合并提交 `0f25d16`；代码对象 `e40a7cf`、最终候选 `972b21a`，维护者授权 PZD-调度Bot 于 2026-10-29 21:20 合入）。遗留：`dotnet test` 本体仍需维护者在普通终端补跑一次，`总计: 0` 一律按未运行算
交付状态：已交付
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
- 超出原范围但已纳入本票（张一一 2026-10-29 决定，架构偏离待维护者认可）：根 `NuGet.config` 锁单一包源——缺它 `dotnet build` 直接因 `NU1507` 失败，属「一条命令能构建」的底座本身，不挪 04。另有 `global.json` 的 `test.runner = Microsoft.Testing.Platform`，该键写在票内本就新建的文件里，不算新增文件。
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

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除本票新增文件即可，无数据影响。

## 任务映射

- worktree：`E:/pzd/.worktree/01/`
- 分支：`chore/01-solution-skeleton`
- 提交：被测 SHA `e40a7cf42c7193b612fd642e9833dcac061fad71`（审计通过的最终候选，见 [run-05 审计重审结论](../../../../.ai/验证/01/2026-10-29/证据/run-05-审计重审/结论.md)）；此前 `ed4e656` 是同一份代码的前一候选，`git diff --stat ed4e656 e40a7cf -- src tests` 为空

## 交接：实现 → 审计（第 3 版：审计通过）

**审计结论：通过**（老登 @ `e40a7cf42c7193b612fd642e9833dcac061fad71`，独立干净 worktree 重跑；原始输出与判定见 [run-05 结论](../../../../.ai/验证/01/2026-10-29/证据/run-05-审计重审/结论.md)）。run-03 提出的三项必改已全部落实；按审计要求把被测 SHA 从 `ed4e656` 更新为 `e40a7cf`，使「必要检查适用于最终候选」闭环，此项不重开审计。

**合并 main 后复跑**（`ee00d28` 入库证据与群规之后，合并提交 `86e5b66c5fbcb86b5cc3adf1ddaf611acfbc4766`）：

```text
$ git diff --stat e40a7cf HEAD -- src tests
（空：代码/测试相对审计候选零改动）

$ dotnet build Pzd.slnx      → 已成功生成。0 个警告 0 个错误，EXIT_CODE=0
$ Pzd.ArchitectureTests.exe  → Total: 1, Errors: 0, Failed: 0，EXE_EXIT=0
```

## 交接明细（第 2 版，回应 run-03「需修改」）

### 1. 目标

回应审计三项必改：证据与被测 SHA 错位、AC-02 验收命令不足以支撑结论、缺决策笔记。代码未改，结论重新绑定到合并 main 后的新候选 `ed4e656`。

### 2. 提交

- 分支：`chore/01-solution-skeleton`（已推送 `origin/chore/01-solution-skeleton`）
- 被测 SHA：`ed4e65692fb4b4caa84baa61f140ccdec4ac5a44`（合并 main `1bfd85e` 之后的提交，已含 36 张票重生成与 `.agents/notes` 入库）
- 历史：基线 `33ade04` → 骨架 `42b0e1b` → 认领 `161af51` → 票面补写 `424e14e` → 合并 main `ed4e656`
- 本次修复提交：票面证据与决策笔记（`git diff --stat` 可证无 `src/`、`tests/` 改动）

### 3. 验证（L1，原始输出见 `.ai/验证/01/2026-10-29/证据/run-04-修复后复核/`）

**AC-01 构建**

```text
$ dotnet build Pzd.slnx
已成功生成。
    0 个警告
    0 个错误
EXIT_CODE=0
```

**AC-02 中央包管理（按审计要求换成带标签名的命令）**

```text
$ git grep -nE 'PackageReference[^>]*\bVersion\s*=' -- '*.csproj'
GREP_EXIT=1   （无命中，退出 1 是 grep 的正常「未匹配」）
```

**AC-03 `.slnx` 分组**：`Pzd.slnx` 内只有 `/src/`（`src/Pzd.Api/Pzd.Api.csproj`）与 `/tests/`（`tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj`）两个解决方案文件夹，与 §2 一致。

**AC-04 版本链（证据绑定被测 SHA）**

```text
# 被测 SHA = ed4e65692fb4b4caa84baa61f140ccdec4ac5a44
$ GET http://127.0.0.1:5181/
{"name":"Pzd.Api","version":"0.1.1-alpha.0.259","buildId":"ed4e65692fb4b4caa84baa61f140ccdec4ac5a44"}
HTTP=200
```

`buildId` 精确等于被测 SHA，排除「跑的不是这条提交」。run-01 那条 `33ade04` 的 Api 证据是**提交前工作树**的构建，不再作为 AC-04 依据。

**测试断言**

```text
$ ./tests/Pzd.ArchitectureTests/bin/Debug/net10.0/Pzd.ArchitectureTests.exe
   Pzd.ArchitectureTests  Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.109s
EXE_EXIT=0
```

**证红（反假绿）**：`dotnet build Pzd.slnx -p:MinVerSkip=true` 后同一条用例失败 `Expected: Not "1.0.0" / Actual: "1.0.0"`，`Failed: 1`；正常重建恢复绿。

**0 用例退出码（补测，回应审计「无实测文件」）**

```text
$ Pzd.ArchitectureTests.exe -class NoSuchTests
   Pzd.ArchitectureTests  Total: 0
ZERO_EXIT=0
```

结论修正：MTP 直跑在 0 用例时**退出码是 0**，所以「空壳工程 `dotnet test` 绿」不证明任何断言成立——这正是本票保留一条真用例的理由，也印证假绿防线「必须贴实际通过数」的要求。

**`dotnet test` 本体：未运行（环境项，四项齐全）**

1. 错误原文与退出码：`System.UnauthorizedAccessException: Access to the path is denied. at System.IO.Pipes.NamedPipeClientStream.TryConnect` → MTP 退出码 `-532462766`（Git Bash 显示 127），摘要 `总计: 0`
2. 替代执行：直跑测试产物 `Total: 1, Failed: 0`（上面第三条命令）
3. `dotnet test tests/Pzd.ArchitectureTests` **本体未运行，需在无管道限制的环境补跑一次**（李总、老登与我三台机器复现同一条拒绝；李总实测同环境 pwsh 自建命名管道正常，说明限制只在 MTP 的会话管道）
4. 剩余风险见遗留第 2 条

**测试阶段结论引用**：李总在 `424e14e` 上做过独立复核（构建 0 警告 0 错误、无显式 `Version=`、`/` 的 `buildId` 与 SHA 一致、测试 `Total: 1 / Failed: 0`），结论 `通过`；原始日志在 `.ai/验证/01/2026-10-29/证据/run-02-独立复核/`。

### 4. 遗留

1. **架构偏离待维护者认可**：根 `NuGet.config` 不在 `architecture.md §2` 根目录清单里。张一一 已定为归 01，但按 Bot分工 §1，调度 Bot 不能替维护者做架构决定。请维护者认可并让 §2 补上这个文件，或改归 04。
2. **`dotnet test` 本体仍未运行**：三台机器同一条命名管道拒绝，判环境项；需普通终端补跑一次后，才能删掉「未运行」这句。
3. **`AGENTS.md` 已过时**：「仓库目前只有架构文档和协作规则，没有实现代码」被本票推翻，只有维护者能改。
4. **`/` 是临时口**：§15.1 的正式契约是 `GET /api/version`（含版本、buildId、构建时间），本票只证明版本链可读，后续票不得把 `/` 当契约沿用。
5. **两代 MTP 开关并存**：`global.json` 的 `test.runner` 与测试工程的 `TestingPlatformDotnetTestSupport` 同时存在，普通终端确认 `dotnet test` 可用后应删掉后者。
6. **署名瑕疵**：合并提交 `f146952` 的作者是本机默认身份 `登登登`，不是 `PZD-实现Bot-1`，只带入他人 `docs/work/`、无代码内容；分支已推送，按「已推送分支不许强推」未改写历史，留审计判定。
7. **未采纳的一条建议**：「断言 buildId 等于给定期望 SHA」需要在测试里注入期望值（环境变量或生成文件），会把测试与运行环境耦合；当前用运行验证核对 `buildId == SHA`（AC-04）已覆盖「跑错提交」的风险。若 13 号票要做版本契约测试，可在那时用注入方式实现。

