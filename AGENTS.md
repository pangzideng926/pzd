# PZD — Agent 协作约定（AGENTS.md）

先读 [docs/architecture.md](docs/architecture.md)，不要猜架构；实现必须遵守 §3 的分层与依赖规则、§18 的实施顺序。

Claude Code 通过 `CLAUDE.md` 里的 `@AGENTS.md` 引用加载本文件；规则只改这里，不要往 `CLAUDE.md` 里加内容。

## Bot 分工

### 1. 角色

| 角色 | git 身份（作者与提交者） | 职责 | 禁止 |
|---|---|---|---|
| **维护者**（用户本人） | 用户自己的 git 身份 | 拍板架构和一切偏离 [architecture.md](docs/architecture.md) 的做法；修改 `AGENTS.md`；决定是否合并到 `main` | — |
| **调度 Bot** | `PZD-调度Bot <dispatch-bot@pzd.local>` | 按架构 §18 把步骤拆成票，分配给实现 Bot，跟踪进度；把需要维护者拍板的问题一轮汇总给维护者 | 写实现代码；替维护者做架构决定 |
| **实现 Bot** | `PZD-实现Bot-<n> <impl-bot-<n>@pzd.local>`，`<n>` 从 1 开始，每个实例一个编号 | 认领一张票，在自己的分支里实现；代码和决策笔记放在同一个提交；交接时附上验证命令和实际输出 | 同时认领两张票；碰别人认领的票；直接提交到 `main` |
| **审计 Bot** | `PZD-审计Bot <audit-bot@pzd.local>` | 在实现 Bot 交来的提交上**独立重跑**验证，给出「通过 / 不通过」和证据 | 修改实现代码；把审计过程写进仓库 |

- 一个会话只扮演一个角色，开场第一句说明自己是哪个角色、哪个身份。
- 角色之间的权限不能借用：调度 Bot 不能因为「顺手」就写代码，审计 Bot 发现问题只能退回，不能自己修。
- 打了 `maintainer` 标签的票，以及任何需要偏离架构文档的事项，所有 Bot 都只能等维护者回复，不许自行决定。

### 2. 票与认领

- 票用 GitHub Issues。一张票只做一件事，写清目标、验收标准，以及证明它完成的验证命令。
- 认领：在票下留言「认领：<身份名>」。别人认领过的票不能抢，要换人先问对方或调度 Bot。
- 一张票对应一个分支、一个 PR。分支从最新的 `main` 拉，命名为 `feat|fix|docs|chore/<票号>-<主题>`；同步用 `git merge main`，已推送的分支不许 rebase 或强推。

### 3. 一个工作树同一时间只允许一个写入方

- 每个实现 Bot 在自己的 `git worktree` 里工作，不许和别的 Bot 共用一个目录写代码。
- 主工作树和 `main` 只接受合并，任何 Bot 都不在上面直接写代码。
- 开工前先跑 `git status`。看到不属于自己的未提交改动就停下，报告给维护者，不许删、不许覆盖、不许顺手提交。

### 4. 交接

任何一次交接（调度 → 实现、实现 → 审计、审计 → 维护者）都写在票或 PR 的评论里，必须包含下面四项，缺一项接手方直接退回：

1. **目标**：这一步要证明或完成什么。
2. **提交**：完整的 commit hash，以及所在分支。
3. **验证**：跑了什么命令、实际输出的关键行，原样粘贴，不许转述。
4. **遗留**：没做完、没验证到、需要谁拍板的事项；没有就写「无」。

### 5. 完成与合并

- 「完成」只认验证命令的实际输出，不认「看起来没问题」。
- 审计 Bot 必须在实现 Bot 给出的那个 commit 上重跑；commit 之后又有改动，就要重新审计。
- 审计结论写在 PR 评论里，不进仓库。仓库里只留决策笔记，也就是「为什么这样做、放弃了什么」。
- 合并到 `main` 需要维护者在 PR 或对话里明确同意；合并用 merge commit，合并后删除分支。

## 决策笔记规则

本节自包含，不依赖任何外部技能或脚本。**没有机器检查，全靠这里的规则**：每条都是硬性要求，提交前必须按 §5 逐条自查。

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

- 路径：`.agents/notes/{proposed|implemented|rejected|archived}/{feature|bug-fix|simplification|architecture|process|testing}/yyyy-mm-dd-主题.md`。
  - 日期是**首次提出日**，之后不改；主题用小写英文加连字符。
  - 只有这 6 个类别，没有 `refactor`：行为变了归对应类，没变归 `simplification`。
- **优先原地更新**：路径、类名、默认值等事实变了，直接改持有该决定的那篇，不追加流水账。
- **决定或理由翻转就新开一篇**并互链，不许把旧篇的 `## Decision` 改成反面。
  - 部分取代：两篇都留，互相加相对链接，旧篇只更新仍然成立的事实。
  - 完全取代：新篇接管旧篇全部独特的理由、备选和后果，改完所有指向旧篇的链接后删除旧篇；旧篇仍有独立参考价值才归档。
- 新想法先写 `proposed/`；落地时在**同一个提交**里挪到 `implemented/`：`Status` 改成 `implemented`，`## Proposal` 改成现在时的 `## Decision`，`## Acceptance criteria` / `## Risks` 并进 `## Consequences`，删掉计划段。
- 写新篇时当场审计同主题旧篇，逐篇归为：无关 / 部分重叠（互链）/ 完全吸收（删或归档）/ 过时提案（转 rejected 或删）。不许留到以后。
- 过时提案转 `Status: rejected — <一句话原因>`，**不许归档**；没有防坑价值的被否记录直接删。
- **归档**：只对已完成、以后参考价值低的 `implemented` 笔记做。用 `git mv` 挪到 `archived/<同类别>/`，在 `Status: implemented` 下一行插入 `Archived: yyyy-mm-dd`。归档后**永不再改**，指向替代者的链接写在新篇里，不写进归档篇。

### 4. 怎么写

正文用简体中文；`Agent Note:`、`Status:`、`Archived:` 和节标题保持英文，方便检索。前三行固定，`Status` 必须与所在文件夹一致。

**implemented**（`## Decision` 一律现在时，不许出现 `## Proposal` / `## Plan` / `## Acceptance criteria` 这类计划标题）：

```markdown
# Agent Note: <标题>

Status: implemented

## Problem

<什么破了、什么要变、不做会怎样；删掉 Decision 后这段仍然成立>

## Decision

<已落地的事实。可在后面加契约、Schema、包拓扑等自由节>

## Alternatives considered

- **<备选>** — 最强理由：<…>。没选，是因为<具体条件>。

## Consequences

- **收益**：<…>
- **代价**：<…>
- **重访信号**：<出现什么情况必须重新评估>

## Verification

<可选：哪条路径、跑什么命令能证明这个决定仍然生效>
```

**proposed**：头块写 `Status: proposed`，骨架为 `## Problem` → `## Proposal`（可用将来时）→ 自由节 → `## Alternatives considered` → `## Acceptance criteria`（什么可观察状态算完成）→ `## Risks`（风险与已知取舍）。

**rejected**：头块写 `Status: rejected — <一句话原因>`，正文保持被否时的 proposed 原样，不改写成反面。

内容要求：

- `## Alternatives considered` 必填，只写**真考虑过**的方案，每个先写它最强的理由再否定，否定理由落到具体条件；确实没有就写「无」，不许编。
- `## Consequences` 收益和代价都要写；简化类必须写已知上限和重访信号；没有基线就不写「更快」「提升」这类相对量词。
- 笔记互相引用用相对 Markdown 链接，不用裸编号；一个事实只在一篇里讲透，其他篇链过去。

### 5. 提交前自查（每次必做）

改了任何笔记，提交前逐条核对，有一条不满足就先修再提交：

1. 路径是 `.agents/notes/<生命周期>/<6 类之一>/yyyy-mm-dd-主题.md`，日期是首次提出日。
2. 第 1 行是 `# Agent Note: <标题>`，第 3 行是 `Status: …`，状态与所在文件夹一致；`rejected` 带原因，归档篇紧跟一行 `Archived: yyyy-mm-dd`。
3. 必备节齐全且顺序正确；`implemented` 里没有计划类标题，`Decision` 是现在时。
4. `## Alternatives considered` 里每个备选都先写了最强理由；没有编造的选项。
5. `## Consequences` 同时有收益和代价。
6. 笔记里的相对链接都能打开：先列出所有相对链接（不含 `http`、`#` 开头的），再逐个确认目标文件存在，路径相对笔记自己所在的目录：

   ```bash
   rg -o '\]\([^)h#][^)]*\)' .agents/notes/
   ```

7. 没有改动 `archived/` 下的任何文件：

   ```bash
   git diff --cached --name-only -- .agents/notes/archived/
   ```

   输出必须为空。只有新归档的那一次提交例外，且只能是新增文件。

8. 笔记和它记录的代码改动在同一个提交里。

自查完向用户汇报，不超过 5 行：只说缺口（每条附具体修法）和写得好的地方。

## 提交

- 提交保持单一主题，提交信息用 `feat|fix|test|docs|chore(scope): …`。
- 笔记与它记录的改动放在**同一个提交**里。
- **署名**：每个 Bot 用「Bot 分工」§1 里自己的 git 身份作为作者和提交者，例如：

  ```bash
  git -c user.name="PZD-实现Bot-1" -c user.email="impl-bot-1@pzd.local" commit
  ```

  不加任何 `Co-Authored-By` 行，也不用工具默认的 AI 署名；本条优先于工具自带的署名设置。维护者自己的提交用维护者自己的身份。
