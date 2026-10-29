状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 01 合入后认领；CI 平台口径见假设 A1
交付状态：待收尾
实施条件：待满足：01 合入；CI 平台待维护者确认（默认 GitHub Actions）

# 04 仓库约定与 CI 骨架

## 目标

把提交规范、目录骨架与 CI 骨架固定下来，让后续每张票有统一的检查入口。

## 需求依据

- [architecture.md](../../../architecture.md) §16.3、§15.1
- [spec.md](../spec.md) AC-06、AC-07
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.4

## 范围与非范围

- 做：`db/master/`、`db/tenant/` 目录与 README（只写放置与命名规则）；CI 配置（构建 + 架构测试 + 前端 build）；提交信息规范写进 `README` 或 `CLAUDE.md` 之前先写进 `.ai/` 已有规则文件的引用说明。
- 不做：不写任何 SQL；不做发布流水线（§15.4 另立）；不改 `.ai/` 下导入自上游的通用规则。

## 责任文件范围（认领时在群里先说清）

新建 `db/master/README.md`、`db/tenant/README.md`、CI 配置目录（`.github/workflows/` 或 `.gitlab-ci.yml` 二选一）。不碰 `src/`、`apps/`。

## 关键接口与依赖

- 前置票：01
- 对外契约：CI 提供唯一门禁入口；后续票的验证命令沿用这里的任务名。

## 步骤

1. 建 `db/` 两个目录与 README，写清版本化 SQL 命名规则与「先迁移后部署」。
2. 写 CI：restore/build/test（架构测试）+ 前端 install/build。
3. 在本仓库真实跑一次，贴运行记录链接或输出。
4. 若 CI 无法在本地触发，明确记「未运行」与原因，不得写成通过。

## 验收标准

- [ ] `db/master/`、`db/tenant/` 存在且 README 写清命名与放置规则。
- [ ] CI 配置包含构建、架构测试、前端 build 三步。
- [ ] CI 在本仓库真实运行一次（未授权则记「未运行」并说明）。
- [ ] CI 不打印任何凭据。

## 验证入口

```bash
act -l（本地查看工作流）或直接触发 CI 后贴运行结果
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

删除 CI 配置与 `db/` 骨架即可。

## 任务映射

- worktree：实施时填写（`.worktree/04/`）
- 分支：实施时填写（`chore/04-repo-conventions-ci`）
- 提交：实施时填写（完整 SHA）
