状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-29 22:06 Asia/Shanghai
下一步：第一段已交付（`db/` 骨架 + `NuGet.config` 复核 + 根 README 约定）；CI 工作流待平台确认后补第二个提交，那一步记「未运行」
交付状态：待收尾（CI 两条验收未运行期间，整张票不标完成）
实施条件：01 已合入（`0f25d16`）；第一段与平台无关，已具备；**待满足**：CI 平台口径（假设 A1 默认 GitHub Actions）

# 04 仓库约定与 CI 骨架

## 目标

把提交规范、目录骨架与 CI 骨架固定下来，让后续每张票有统一的检查入口。

## 需求依据

- [architecture.md](../../../architecture.md) §16.3、§15.1
- [spec.md](../spec.md) AC-06、AC-07
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.4

## 范围与非范围

- 做：`db/master/`、`db/tenant/` 目录与 README（只写放置与命名规则）；CI 配置（构建 + 架构测试 + 前端 build）；提交信息规范写进 `README` 或 `CLAUDE.md` 之前先写进 `.ai/` 已有规则文件的引用说明；**复核根 `NuGet.config` 的单源锁定**——它已由 01 号票引入（本机用户级配了 `nuget.org` + `SJ` 两个源，中央包管理下触发 `NU1507` 被警告即错误抬成失败），本票只复核并在 README 写明用法，不重复引入、不删除。
- 不做：不写任何 SQL；不做发布流水线（§15.4 另立）；不改 `.ai/` 下导入自上游的通用规则；不改 01 号票已提交的 `NuGet.config` 内容（只复核与文档化）。

## 责任文件范围（认领时在群里先说清）

新建 `db/master/README.md`、`db/tenant/README.md`、CI 配置目录（`.github/workflows/` 或 `.gitlab-ci.yml` 二选一）；只读复核根 `NuGet.config`。不碰 `src/`、`apps/`。

## 关键接口与依赖

- 前置票：01
- 对外契约：CI 提供唯一门禁入口；后续票的验证命令沿用这里的任务名。

## 步骤

1. 建 `db/` 两个目录与 README，写清版本化 SQL 命名规则与「先迁移后部署」。
2. 写 CI：restore/build/test（架构测试）+ 前端 install/build。
3. 在本仓库真实跑一次，贴运行记录链接或输出。
4. 若 CI 无法在本地触发，明确记「未运行」与原因，不得写成通过。

## 验收标准

- [x] `db/master/`、`db/tenant/` 存在且 README 写清命名与放置规则。
- [ ] CI 配置包含构建、架构测试、前端 build 三步。**（未运行：等 CI 平台确认，本段未提交）**
- [ ] CI 在本仓库真实运行一次（未授权则记「未运行」并说明）。**（未运行：同上，不在本段范围）**
- [x] CI 不打印任何凭据。**（本段无 CI 文件；约定已写进根 README，落地时按此检查）**
- [x] 根 `NuGet.config` 的单源锁定在 README 有说明，且本票未改动 01 已提交的内容。
- [x] 干净环境 `dotnet restore` 不出现 `NU1507`（若本机用户级 NuGet 源未清理，需在无该配置的环境复核并在票里写明）。

## 验证入口

```bash
act -l（本地查看工作流）或直接触发 CI 后贴运行结果
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除 CI 配置与 `db/` 骨架即可。

## 任务映射

- worktree：`E:/pzd/.worktree/04/`
- 分支：`chore/04-repo-conventions-ci`
- 提交：见下方「交接」段的被测 SHA

## 交接：实现 → 审计（第一段：与平台无关的部分）

### 1. 目标

交付 `db/` 骨架与仓库约定（提交信息、NuGet 单源、迁移顺序），复核根 `NuGet.config`；CI 工作流留到平台确认后的第二个提交。

### 2. 提交

- 分支：`chore/04-repo-conventions-ci`（已推送）
- 提交：见本轮回复与票面 `git log -1`；基线 `bc9a93b`
- 作者：`PZD-实现Bot-1 <impl-bot-1@pzd.local>`

### 3. 验证（L1）

```text
$ dotnet restore Pzd.slnx
已还原 Pzd.ArchitectureTests / Pzd.Api
RESTORE_EXIT=0        grep -c NU1507 → 0

$ git grep -nE 'CREATE DATABASE|IDENTIFIED BY' -- db/
（无命中：迁移文件不含运维语句与凭据）

$ ls db/master db/tenant
db/master: README.md    db/tenant: README.md
```

### 4. 遗留

1. **CI 三步（build / 架构测试 / 前端 build）未写**：票面要求等平台拍板（假设 A1 默认 GitHub Actions）。本段故意不写，避免按错平台返工；确认后补第二个提交，届时本票才能标完成。**CI 两条验收处于「未运行」期间，整张票不得标完成，也不得标「待审计」**——本段只交付与平台无关的那半。
2. **`dotnet restore` 是在本机用户级 NuGet 仍配着 `nuget.org` + `SJ` 两个源的环境下跑通的**（仓库 `NuGet.config` 的 `<clear/>` 生效），所以「干净环境」那条严格说只验了「仓库配置能覆盖机器配置」这一面；无该用户配置的环境待一次复核。
3. **根 README 与 03 号票重叠**：03 也在建根 `README.md`（启动命令与数据源切换）。两边合入时会 add/add 冲突，需按「03 的启动段 + 04 的约定段」合并，我负责解。
4. 迁移执行器未定（consensus §4 待决 2），命名规则已按「不依赖执行器」写，选型变化不需要改这些文件。

