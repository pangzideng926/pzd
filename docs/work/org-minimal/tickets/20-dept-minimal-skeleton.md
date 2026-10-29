状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 06 合入后认领；只做最小骨架，不做维护界面
交付状态：待收尾
实施条件：待满足：06 合入（前置票）

# 20 最小组织骨架（提前切片，为数据范围与用户表单解锁部门）

## 目标

让数据范围五档与用户表单的部门字段有真实数据可用，而不必等完整组织模块。

## 需求依据

- [architecture.md](../../../architecture.md) §4 Organization、§7.2 数据范围
- [spec.md](../spec.md) （platform-identity AC-10；本条为跨里程碑提前切片）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) 原 M6.1 子集，按张一一口径单开

## 范围与非范围

- 做：租户库 `depts` 表（`Id`/`ParentId`/`Name`/`LeaderId`/`Path`/`Level`）、`users.DeptId`、种子一条根部门、`IOrgQuery.GetDeptAndDescendantsAsync(userId)` 只读查询。
- 不做：不做部门增删改界面、不做岗位、不做主管层级维护、不做组织树拖拽。**本票不是 M6.1 的全部**。

## 责任文件范围（认领时在群里先说清）

`db/tenant/` 新增建表脚本、`src/Pzd.Organization/**` 与 `src/Pzd.Organization.Contracts/**`（仅最小骨架）、`tests/Pzd.IntegrationTests/Organization/**`。

## 关键接口与依赖

- 前置票：06
- 对外契约：`IOrgQuery` 签名被 24（数据范围）与 33（用户表单部门选择）号票依赖。

## 步骤

1. 落 `depts` 建表 SQL 与根部门种子。
2. 建最小 Organization 模块与 `IOrgQuery`。
3. 实现 `GetDeptAndDescendantsAsync`（递归或 Path 前缀）。
4. 集成测试：三级部门能取到全部下级；跨租户取不到。

## 验收标准

- [ ] `depts` 表与根部门种子存在，`users` 有 `DeptId`。
- [ ] `IOrgQuery.GetDeptAndDescendantsAsync` 返回自身与全部下级。
- [ ] 跨租户查询返回空，不泄漏。
- [ ] 本票不含任何部门维护端点（`/api/depts` 的写操作不存在）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Organization
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚代码与 SQL；测试库重建。

## 任务映射

- worktree：实施时填写（`.worktree/20/`）
- 分支：实施时填写（`feat/20-dept-minimal-skeleton`）
- 提交：实施时填写（完整 SHA）
