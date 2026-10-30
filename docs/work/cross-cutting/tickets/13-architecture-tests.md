状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 05、06 合入后认领
交付状态：待收尾
实施条件：待满足：05、06 合入（前置票）

# 13 架构测试（分层依赖守卫）

## 目标

用机器守住依赖方向，不靠人肉读代码：Domain 不碰 ORM 与框架，模块之间只引 `.Contracts`。

## 需求依据

- [architecture.md](../../../architecture.md) §3.2、§16.1
- [spec.md](../spec.md) AC-01
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.9

## 范围与非范围

- 做：NetArchTest：`Domain/` 禁引 SqlSugar、ASP.NET Core、Infrastructure 与其他模块；`Application/` 禁引 SqlSugar；模块间只允许引用对方 `.Contracts`。每条规则配一个会变红的反例证明。
- 不做：不做代码风格检查、不做覆盖率门槛。

## 责任文件范围（认领时在群里先说清）

改 `tests/Pzd.ArchitectureTests/**`。不碰 `src/`。

## 关键接口与依赖

- 前置票：05、06
- 对外契约：CI 门禁入口：`dotnet test tests/Pzd.ArchitectureTests`。

## 步骤

1. 写规则并跑绿。
2. 临时加一处违规引用，确认测试变红，然后撤回（贴两次输出）。
3. 把命令写进 CI（04 号票的工作流若已存在则追加）。

## 验收标准

- [ ] 架构测试全绿。
- [ ] 故意违规时测试变红，有前后两次输出作证。
- [ ] 覆盖三类规则：Domain 依赖、Application 依赖、模块跨引。
- [ ] 追加一条守卫（2026-10-30 加）：`Pzd.IntegrationTests.Fixtures` 命名空间不得引用 `Pzd.*` 业务程序集（`NetArchTest` 按命名空间断言），防止有人在 `Fixtures/` 里直接引 `Pzd.Infrastructure` 绕过 36 号的载体契约。
- [ ] 已接入 CI 门禁。

## 验证入口

```bash
dotnet test tests/Pzd.ArchitectureTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚测试项目改动。

## 任务映射

- worktree：实施时填写（`.worktree/13/`）
- 分支：实施时填写（`test/13-architecture-tests`）
- 提交：实施时填写（完整 SHA）
