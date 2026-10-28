# PZD — Agent 协作约定（AGENTS.md）

先读 [docs/architecture.md](docs/architecture.md)，不要猜架构；实现必须遵守 §3 的分层与依赖规则、§18 的实施顺序。

Claude Code 通过 `CLAUDE.md` 里的 `@AGENTS.md` 引用加载本文件；规则只改这里，不要往 `CLAUDE.md` 里加内容。

## 决策笔记规则

本节是 [write-notes-like-deepseek](https://github.com/czm15053/write-notes-like-deepseek) 技能（项目级安装在 `.agents/skills/write-notes-like-deepseek/`）的项目内精简版。**本节与技能原文冲突时以本节为准**；格式细节、归档流程查技能的 `SKILL.md` 与 `references/`。

### 1. 先判要不要写

- **必须写**：改了行为、架构、跨文件契约、流程与工具链、测试策略、落盘/网络/配置格式之一；或者是「选 A 没选 B」这类日后会被重访的取舍。判不准时从严。
- **禁止写**，直接提交：排版格式化、错别字、无歧义重命名、不改行为的样式或依赖补丁、打 tag、单模块内看 diff 即懂的 CRUD 或局部修复。
- 在 PZD 里，偏离 [architecture.md](docs/architecture.md) 的任何做法（换选型、破例跨模块引用、改依赖方向）一律算非平凡。

### 2. 动手前先查

```bash
rg --hidden --glob '!.agents/notes/archived/**' "<关键词>" .agents/notes/
```

- 按意图直切目录：结构看 `implemented/architecture/`，避坑看 `rejected/`。
- 查得到的事实自己查，只把真正要拍板的取舍拿去问用户；要问就一轮列全，每题给推荐答案。

### 3. 写在哪、改哪篇

- 路径：`.agents/notes/{proposed|implemented|rejected|archived}/{feature|bug-fix|simplification|architecture|process|testing}/yyyy-mm-dd-主题.md`。日期是**首次提出日**，之后不改；只有这 6 个类别，没有 `refactor`（行为变了归对应类，没变归 `simplification`）。
- **优先原地更新**：路径、类名、默认值等事实变了，直接改持有该决定的那篇，不追加流水账。
- **决定或理由翻转就新开一篇**并互链，不许把旧篇的 `## Decision` 改成反面。
- 新想法先写 `proposed/`；落地时在**同一个提交**里挪到 `implemented/`，`Status` 改成 `implemented`，`## Proposal` 改成现在时的 `## Decision`，`## Acceptance criteria` / `## Risks` 并进 `## Consequences`。
- 写新篇时当场审计同主题旧篇：无关 / 部分重叠（互链）/ 完全吸收（删或归档）/ 过时提案（转 rejected 或删）。不许留到以后。
- 过时提案转 `Status: rejected — <原因>`，**不许归档**；没有防坑价值的被否记录直接删。归档只用 `archive-agent-note.ts`，归档篇不可再改。

### 4. 怎么写

前三行固定，`Status` 必须与所在文件夹一致：

```markdown
# Agent Note: <标题>

Status: implemented
```

- 正文用简体中文；`Agent Note:`、`Status:` 和节标题保持英文，脚本靠它们核对。
- `implemented` 的骨架：`## Problem` → `## Decision`（现在时）→ 自由节 → `## Alternatives considered` → `## Consequences`，可加 `## Verification`。`proposed` 用 `## Proposal` / `## Acceptance criteria` / `## Risks`。模板在技能的 `templates/`。
- `## Problem` 脱离方案也要成立：什么破了、什么要变、不做会怎样。
- `## Alternatives considered` 必填，只写**真考虑过**的方案，每个先写它最强的理由再否定；确实没有就写「无」，不许编。
- `## Consequences` 同时写收益和代价，写明什么信号出现时要重访；没有基线就不写「更快」「提升」这类相对量词。
- 笔记互相引用用相对 Markdown 链接，不用裸编号。

### 5. 提交前校验

写完或改完任何笔记都必须跑，全绿才能提交；不许以「没配 npm script」为由跳过：

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-agent-note-tree.ts
```

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-agent-note-format.ts
```

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-archived-agent-notes.ts
```

脚本只管结构。写完再按技能的 `references/quality-gate.md` 自检语义，只向用户报缺口和写得好的地方，不超过 5 行。

## 提交

- 提交保持单一主题，提交信息用 `feat|fix|test|docs|chore(scope): …`。
- 笔记与它记录的改动放在**同一个提交**里。
- 技能文件 `.agents/skills/` 与 `skills-lock.json` 入库；不要手改技能文件，升级用 `npx skills` 重新安装。
