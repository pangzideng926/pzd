# PZD

PZD 是一个模块化单体后台系统（.NET 10 + React Router SPA）。仓库目前**只保留架构设计**，实现代码已清空，之后按架构文档重新实现。

- 架构：[docs/architecture.md](docs/architecture.md)（技术选型、分层与依赖规则、多租户、异常处理、实施路线 §18、ADR §19）
- Agent 协作约定：[AGENTS.md](AGENTS.md)：重要改动必须先留决策笔记；`CLAUDE.md` 只是引用它

## 决策笔记

非平凡改动的「为什么这样做、放弃了什么」记在 [`.agents/notes/`](.agents/notes/)，路径为 `{proposed|implemented|rejected|archived}/{类别}/yyyy-mm-dd-主题.md`。规则、模板和提交前自查清单全部在 [AGENTS.md](AGENTS.md)，不依赖外部技能或脚本。

## 历史

清空前的实现（步骤 1–2：工程底座与横切能力，票 01–12）以及测试、审计、交接报告都保存在 git 历史里，最后一个完整提交是 `d34cf66`：

```bash
git show d34cf66 --stat
```
