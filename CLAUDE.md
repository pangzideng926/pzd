# PZD — AI 协作约定

先读 [docs/architecture.md](docs/architecture.md)，不要猜架构；实现必须遵守 §3 的分层与依赖规则、§18 的实施顺序。

## 重要改动必须留笔记

使用 [write-notes-like-deepseek](https://github.com/czm15053/write-notes-like-deepseek) 技能（安装：`npx skills add czm15053/write-notes-like-deepseek`），具体写法以技能的 `SKILL.md` 为准。

1. **非琐碎改动先写或更新笔记**：行为、架构、跨文件契约、工具链、测试策略、磁盘/网络/配置格式的变化都算。样式、格式化、不改行为的补丁升级这类机械改动直接提交。
2. **写之前先搜 `.agents/notes/`**：同主题已有笔记就原地更新；新想法放 `proposed/`，随代码落地挪到 `implemented/`；被取代的决策归档并指向替代者，不改写旧笔记。
3. **被否决的方案先写它最强的理由**，再写为什么没选。
4. **提交前跑校验**，失败先修再提交（路径按项目级安装 `.agents/skills/`；装在别处就替换路径）：

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-agent-note-tree.ts
```

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-agent-note-format.ts
```

```bash
npx tsx .agents/skills/write-notes-like-deepseek/scripts/verify-archived-agent-notes.ts
```

笔记路径：`.agents/notes/{proposed|implemented|rejected|archived}/{feature|bug-fix|simplification|architecture|process|testing}/yyyy-mm-dd-主题.md`。

## 提交

提交保持单一主题，提交信息用 `feat|fix|test|docs|chore(scope): …`。
