# Agent Note: 仓库重置为只保留架构文档，决策改用 Agent Notes 记录

Status: implemented

## Problem

步骤 1–2（票 01–12）实现期间，决策理由散落在 README、交接记录和十几份测试/审计报告里，文档总量约 6800 行，超过实现代码约 5600 行。同一个结论在多处重复，改一处就要同步三处，已经出现过 README 自相矛盾的情况（一处写主仓能跑 aspire，另一处写需要修 ACL）。

用户决定重新落地实现。如果沿用原来的记录方式，新一轮会重复同样的问题：AI 只能看到代码「怎么跑」，看不到「为什么这样、放弃了什么」，而这些理由又埋在过程报告里，难以检索。

## Decision

- 仓库只跟踪 `docs/architecture.md`、`README.md`、`AGENTS.md`、`CLAUDE.md`、`.editorconfig`、`.gitignore`，以及决策笔记和技能文件。实现代码、工程配置、`docs/specs`、`docs/notes`、`docs/reports` 全部删除；清空前最后一个完整提交是 `d34cf66`。
- `docs/architecture.md` 保持原样，仍然是上游架构的唯一来源。
- 决策记录采用 write-notes-like-deepseek 技能：项目级安装在 `.agents/skills/write-notes-like-deepseek/`，用 `skills-lock.json` 锁定来源；笔记放在 `.agents/notes/`。
- 项目内执行的是 `AGENTS.md`「决策笔记规则」一节里的精简版规则，与技能原文冲突时以 `AGENTS.md` 为准；`CLAUDE.md` 只有一行 `@AGENTS.md`，供 Claude Code 加载。
- 根目录没有 `package.json`，校验直接用 `npx tsx` 调用技能的三个 `verify-*.ts` 脚本，不经过 npm script。

## Alternatives considered

- **保留架构 + 规格 + 票，只删报告** — 最强理由：`docs/specs/foundation.md` 和 `tickets.md` 已经把步骤 1–2 拆成了可直接执行的票，重新实现时能省下拆票的时间。没选，是因为用户明确只保留架构文档；票面写的是旧实现的验收锚点和 hash，代码删掉后这些锚点全部失效，留着反而会误导。
- **按规范手写笔记，不安装技能** — 最强理由：不需要下载、运行第三方代码，仓库也不会多一份外部文件。没选，是因为用户选择用 npx 安装；装上后格式、目录和归档封印都由脚本机械检查，不靠人自觉。
- **在新分支上清理，确认后再合并** — 最强理由：main 上的状态可以随时回看，出错不影响主线。没选，是因为用户选择直接在 main 上提交；旧实现在 git 历史里随时可以恢复。

## Consequences

- **收益**：重新实现时只有一份架构文档作为依据，不存在旧报告和新代码互相矛盾的问题。每个非平凡决策都有固定位置，能按目录和关键词检索，格式由脚本保证。
- **代价**：步骤 1–2 的实现和踩坑记录不再出现在工作树里，只能翻 `d34cf66` 之前的历史，例如低完整性标签导致 `dotnet test` 握手失败、`react-router` 默认 `entry.server` 的 `abort` TDZ 崩溃。重新实现时如果再碰到这些问题，需要先去历史里查。
- **代价**：票 13（Platform 入口）当时还没提交的改动，按用户决定一并丢弃，无法恢复。
- **重访信号**：如果重新实现时反复回头翻旧历史里的同一类坑，就应该把它整理成 `rejected/` 或 `implemented/` 下的笔记，而不是继续依赖 git 历史。

## Verification

- `git ls-files` 只列出上述文件，加上 `.agents/` 下的笔记与技能文件、`skills-lock.json`。
- 在仓库根目录跑 `AGENTS.md`「提交前校验」里的三条 `npx tsx` 命令，全部通过。
