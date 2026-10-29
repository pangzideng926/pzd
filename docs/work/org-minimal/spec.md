# org-minimal 规格：最小组织骨架（跨里程碑提前切片）

> 基线：2026-10-29（v1）　作者：老何
> 需求依据：[architecture.md](../../architecture.md) §4 Organization、§7.2 数据范围
> 拆分依据：[consensus.md](../pzd-v1/consensus.md) M6.1 的**子集**；按张一一 2026-10-29 口径「步骤 5 遇到强依赖时单开最小依赖票」

## 1. 为什么有这个 feature

原 M6（Organization + System）整块在**期外**，但 V1 范围内有两处硬依赖组织数据：

1. 授权模型的数据范围五档里，「本部门」「本部门及下级」需要有部门表与用户主部门（[platform-identity AC-10](platform-identity/spec.md)）。
2. 用户管理表单的部门字段需要有部门可选（[user-management AC-01](user-management/spec.md) 关联）。

按 §18 顺序硬把 M6 提前会让期外工作渗进来，所以只切出**读得出部门层级**的最小集合，其余（维护界面、岗位、主管层级维护）继续期外。

## 2. 范围

- 租户库 `depts` 表：`Id`、`TenantId`、`ParentId`、`Name`、`LeaderId`、`Path`、`Level`、审计与软删除字段。
- `users.DeptId`。
- 一条根部门种子，随租户库迁移初始化。
- `Pzd.Organization` 与 `Pzd.Organization.Contracts` 最小模块，只提供只读 `IOrgQuery`：取某部门的全部下级、取某用户的部门链。

## 3. 非范围

- 部门增删改端点与维护界面、拖拽排序。
- 岗位、用户主部门调整、主管层级维护。
- `IOrgQuery` 之外的任何 Organization 能力。
- 审批流里的主管解析（M7，期外）。

## 4. 决策

- 层级用 `Path`（祖先路径串）+ `Level` 冗余表达，避免每次递归查库；`GetDeptAndDescendantsAsync` 用 `Path` 前缀匹配。
- `IOrgQuery` 必须是只读契约，放在 `.Contracts`，供 Identity 与 Workflow 引用，不允许 Identity 引用 Organization 的实现。
- 本切片不提供写操作，所以不需要权限码与审计扩展；等 M6 立项时再补。

## 5. 验收标准

- **AC-01** `depts` 表与根部门种子存在，`users` 有 `DeptId`，租户库迁移可重复执行。
- **AC-02** `IOrgQuery.GetDeptAndDescendantsAsync(deptId)` 返回自身与全部下级，跨层级正确。
- **AC-03** 跨租户查询返回空，不泄漏他租户部门。
- **AC-04** 本切片不暴露任何部门写端点（`POST/PUT/DELETE /api/depts` 不存在）。

## 6. 来源

- [architecture.md](../../architecture.md) §4、§7.2
- [consensus.md](../pzd-v1/consensus.md) M6.1
- 张一一 2026-10-29 口径：强依赖单开最小依赖票，不把整个 M6 拉进来
