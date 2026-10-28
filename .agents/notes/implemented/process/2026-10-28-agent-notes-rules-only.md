# Agent Note: 决策笔记去掉外部技能，全靠 AGENTS.md 规则卡控

Status: implemented

## Problem

[仓库重置](2026-10-28-reset-to-architecture-and-agent-notes.md)时，决策笔记接入了 write-notes-like-deepseek 技能：技能文件入库，提交前用 `npx tsx` 跑技能的三个 TypeScript 校验脚本。实际用下来有三个问题：

- 校验依赖 Node 和 `tsx`。agent 所在的会话里找不到 Node，每次提交前都要用户手动在 PowerShell 里跑，然后把输出贴回来。
- `AGENTS.md` 里的规则不是自包含的：模板、语义自检、归档流程都要回头查技能的 `SKILL.md` 和 `references/`，规则分在两处。
- 仓库要跟踪 21 个第三方技能文件和 `skills-lock.json`，而项目实际只用到其中的规则和三个校验脚本。

## Decision

- 删除 `.agents/skills/write-notes-like-deepseek/` 和 `skills-lock.json`，仓库里不再有任何外部技能或校验脚本。
- `AGENTS.md`「决策笔记规则」一节自包含：判定、检索、落位与流转、三种笔记的骨架、归档做法、提交前自查 8 条，全部写在这一个文件里。
- 卡控方式是规则加自查：没有机器检查，agent 每次改了笔记，提交前都要按 `AGENTS.md` §5 逐条核对，并向用户汇报缺口。
- 归档不再使用 SHA-256 封印和 `manifest.json`。只把笔记 `git mv` 到 `archived/`，加一行 `Archived: yyyy-mm-dd`，之后不再修改；用 `git diff --cached -- .agents/notes/archived/` 确认没人改动已归档的笔记。
- 笔记的目录结构、6 个类别、头块格式与之前一致，已有笔记不用迁移。

## Alternatives considered

- **保留技能和它的校验脚本** — 最强理由：格式、目录、归档封印都由脚本机械检查，不依赖 agent 自觉，漏写或写错会直接被拦下来。没选，是因为用户明确要求不引用技能；而且当前会话没有 Node，脚本只能由用户手动跑，等于每次提交都卡在人身上。
- **自己写 Python 校验脚本，再加 git pre-commit 钩子** — 最强理由：本机有 Python 3.12，agent 在会话里就能跑；钩子能在每次提交时自动拦截，不依赖第三方代码。用户先选了这个方案，开工前又改成只靠规则，所以没有实施。
- **在上面的基础上再加 GitHub Actions** — 最强理由：本地绕过钩子也会在远端被拦下来。没选，理由同上；仓库目前只有一个人在用，CI 的维护成本暂时不值得。

## Consequences

- **收益**：规则只有 `AGENTS.md` 一处，不用在规则和技能文档之间来回查。提交前不需要 Node，agent 自己就能做完全部检查。仓库不跟踪任何第三方文件。
- **代价**：没有任何东西能机械地拦住格式错误、缺节、`Status` 和文件夹对不上、断链，或者有人改了已归档的笔记，全靠 agent 遵守规则、用户在评审时把关。归档篇失去了哈希封印，「只增不改」只能靠 `git diff` 自查和 git 历史追溯。
- **重访信号**：只要出现一次以下情况，就改用自写校验脚本加 pre-commit 钩子（上面第二个备选）：提交进 main 的笔记结构不合规（缺节、`Status` 与文件夹不一致、断链），或者已归档的笔记被修改。

## Verification

- `git ls-files .agents/` 只列出 `.agents/notes/` 下的笔记，没有 `.agents/skills/`；仓库里也没有 `skills-lock.json`。
- 用下面的命令在 `AGENTS.md`、`README.md`、`.agents/notes/` 里搜，除了本篇和上一篇追溯来源的文字，没有其他命中：

  ```bash
  rg -n "write-notes-like-deepseek|npx tsx|skills-lock|\.agents/skills" AGENTS.md README.md .agents/notes/
  ```
