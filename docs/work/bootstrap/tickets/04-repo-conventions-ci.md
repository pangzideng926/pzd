状态：进行中
主写入者：PZD-实现Bot-1
更新日期：2026-10-29 22:11 Asia/Shanghai
下一步：两段都已交付——第一段 `db/` 骨架 + `NuGet.config` 复核 + 根 README 约定；第二段 `.github/workflows/ci.yml`。CI 真实运行记「未运行」，等平台口径确认与一次真实触发
交付状态：待收尾（CI 两条验收未运行期间，整张票不标完成）
实施条件：01 已合入（`0f25d16`）；**已具备**：按假设 A1 写出 CI 配置；**待满足（不阻塞第二段交付，但阻塞本票标完成）**：CI 平台口径由维护者确认——假设 A1 默认 GitHub Actions，**这一条仍待维护者确认，未划掉**

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

新建 `db/master/README.md`、`db/tenant/README.md`、`.github/workflows/ci.yml`（平台口径唯一落点，**换 GitLab CI 只替换这一个文件**）；只读复核根 `NuGet.config`。不碰 `src/`、`apps/`。

## 关键接口与依赖

- 前置票：01
- 对外契约：CI 提供唯一门禁入口；后续票的验证命令沿用这里的任务名。

## 步骤

1. 建 `db/` 两个目录与 README，写清版本化 SQL 命名规则与「先迁移后部署」。**（已完成）**
2. 写 CI：restore/build/test（架构测试）+ 前端 install/build。**（已完成 `.github/workflows/ci.yml`；架构测试那步额外断言摘要里的用例数 > 0）**
3. 在本仓库真实跑一次，贴运行记录链接或输出。**（未运行：本机无法触发 CI，也未在远端触发过）**
4. 若 CI 无法在本地触发，明确记「未运行」与原因，不得写成通过。**（已按此记，见遗留）**

## 验收标准

- [x] `db/master/`、`db/tenant/` 存在且 README 写清命名与放置规则。
- [x] CI 配置包含构建、架构测试、前端 build 三步。**（已写 `.github/workflows/ci.yml`；YAML 解析退出码 0；架构测试步断言摘要用例数 > 0）**
- [ ] CI 在本仓库真实运行一次（未授权则记「未运行」并说明）。**（未运行：本机不能触发 CI，也未在远端触发；首次真实运行结果待回填，回填前不得视为通过）**
- [x] CI 不打印任何凭据。**（工作流内无密钥、无 `secrets` 引用；NuGet 与 npm 只用公开源）**
- [x] 根 `NuGet.config` 的单源锁定在 README 有说明，且本票未改动 01 已提交的内容。
- [x] 干净环境 `dotnet restore` 不出现 `NU1507`（若本机用户级 NuGet 源未清理，需在无该配置的环境复核并在票里写明）。

## 验证入口

```bash
# 工作流语法（本地已跑）
pnpm dlx js-yaml .github/workflows/ci.yml        # 解析成功、退出码 0

# 断言逻辑（本地已跑：假 dotnet 模拟四种场景，见第二段交接）
#   Total: 1 + 退出码 0        → 通过
#   Total: 0 + 退出码 0        → 失败（0 用例假绿被抓住）
#   Total: 2 且 Failed: 1      → 失败（退出码 1 原样传出）
#   总计: 2（中文摘要）        → 通过

# 真实运行（未运行：平台确认后由维护者触发）
gh workflow run ci.yml                            # 或 push 后看 Actions 页面
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除 CI 配置与 `db/` 骨架即可。

## 任务映射

- worktree：`E:/pzd/.worktree/04/`
- 分支：`chore/04-repo-conventions-ci`
- 提交：`799309c`（第一段）→ `7de6675`（票面钉住「未运行不标完成」）→ 第二段（CI 工作流）见下方交接

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

1. **CI 三步第一段没写**——第二段已补 `.github/workflows/ci.yml`（见下方「交接（第二段）」）。平台口径仍未由维护者确认（假设 A1）。**CI 两条验收处于「未运行」期间，整张票不得标完成，也不得标「待审计」。**
2. **`dotnet restore` 是在本机用户级 NuGet 仍配着 `nuget.org` + `SJ` 两个源的环境下跑通的**（仓库 `NuGet.config` 的 `<clear/>` 生效），所以「干净环境」那条严格说只验了「仓库配置能覆盖机器配置」这一面；无该用户配置的环境待一次复核。
3. **根 README 与 03 号票重叠**：03 也在建根 `README.md`（启动命令与数据源切换）。两边合入时会 add/add 冲突，需按「03 的启动段 + 04 的约定段」合并，我负责解。
4. 迁移执行器未定（consensus §4 待决 2），命名规则已按「不依赖执行器」写，选型变化不需要改这些文件。

## 交接：实现 → 审计（第二段：CI 工作流）

### 1. 目标

按假设 A1（GitHub Actions）写出唯一 CI 入口：后端 restore / build / 架构测试 + 前端 install / build。平台口径**未经维护者确认**，所以工作流做成单文件可替换——改成 GitLab CI 只替换这一个文件，其他票引用的验收命令不变。

### 2. 提交

- 分支：`chore/04-repo-conventions-ci`（已推送）
- 提交：本段提交见 `git log -1`；上一提交 `7de6675`
- 作者：`PZD-实现Bot-1 <impl-bot-1@pzd.local>`
- 新增文件：`.github/workflows/ci.yml`（本段唯一新增文件；未碰 `src/`、`apps/`、`NuGet.config`）
- 决策笔记：`.agents/notes/implemented/process/2026-10-29-ci-gate-and-zero-test-guard.md`（为什么不用 `--minimum-expected-tests`、为什么不抽 `scripts/ci/`）

### 3. 验证（L1）

YAML 交给解析器验，不靠肉眼：

```text
$ pnpm dlx js-yaml .github/workflows/ci.yml > /dev/null
解析成功，退出码 0
```

「架构测试」步的断言逻辑用假 `dotnet` 在本地跑四种场景；被验的脚本是从 YAML 里那段 `run` 块**抽出来**的，不是另写一份：

```text
[A_total1]  exit=0  期望 0   OK
[B_total0]  exit=1  期望 1   OK      ← MTP 0 用例且退出码 0，被断言抓住
[C_failed]  exit=1  期望 1   OK      ← 有用例失败，退出码原样传出
[D_zh_2]    exit=0  期望 0   OK      ← 中文摘要「总计: 2」也认
```

`bash -n` 对该 `run` 块语法检查通过。

**未运行（四要素）**

1. 错误原文与真实退出码：**无报错可贴**——本机不具备触发 GitHub Actions 的条件，也未在远端触发过，属「未发起」而非「发起后失败」，因此没有运行日志。
2. 替代执行结果：`js-yaml` 解析退出码 0；断言逻辑四场景全部符合预期；第一段的 `dotnet restore` 退出码 0、`NU1507` 计数 0。
3. 明确写：**CI 本体未运行，需在平台口径确认后由维护者触发一次并回填运行链接**。
4. 剩余风险：见下方遗留。

### 4. 遗留

1. **平台口径未确认**：假设 A1，待维护者答复。若改 GitLab CI，只替换 `.github/workflows/ci.yml`。
2. **`dotnet-version: '10.0.402'` 在 runner 上能否取到未验证**：取不到会卡在 setup 步。届时只改这一行；动 `global.json` 的 `rollForward` 属 01 的范围，不在本票动。
3. **前端只跑 install + build，没跑 `pnpm test`**：票面三步不含前端测试，所以前端现有用例（`packages/api` 2 条）没进门禁。建议 CI 真跑通后补一步 `pnpm test --force`。
4. **turbo 缓存**：`pnpm build --force` 已绕开缓存回放；CI 里没做缓存持久化，将来若加缓存，必须保留 `--force` 或改配置，否则又会吃到「缓存绿」。
5. **`pnpm install --frozen-lockfile` 在 CI 上未验证**：本地第一段没跑过 pnpm install（本机 `node_modules` 不存在），锁文件与 `package.json` 是否严格一致，要等首次 CI 或一次本地 install 才能确认。

