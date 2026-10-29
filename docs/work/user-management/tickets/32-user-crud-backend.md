状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 09、24 合入后认领；本票定标杆的代码形状
交付状态：待收尾
实施条件：待满足：09、24 合入（前置票）

# 32 用户管理后端全链路（标杆模块）

## 目标

把一条完整业务链路走通：聚合 + 仓储 + Command/Query + 端点 + 权限码 + 数据范围，作为后续模块的模板。

## 需求依据

- [architecture.md](../../../architecture.md) §3.1、§4 Identity、§7.2、§9.3
- [spec.md](../spec.md) AC-01–AC-08
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M5.1

## 范围与非范围

- 做：用户聚合（用户名唯一、角色分配走方法、停用/删除走领域方法）、仓储、Create/Update/Disable/ResetPassword/Delete 命令、分页查询（CQRS 投影）、端点挂 `system:user:*` 权限码、数据范围过滤、`IAuditWriter` 扩展点位。
- 不做：不做通知（原 M6.5 期外）、不做业务审计日志表（原 M6.6 期外）、不做导入导出、不做回收站恢复界面、不做批量操作。

## 责任文件范围（认领时在群里先说清）

`src/Pzd.Identity/Domain/**`、`Application/**`、`Infrastructure/**`、`Endpoints/UserEndpoints.cs`、`tests/Pzd.Identity.UnitTests/**`、`tests/Pzd.IntegrationTests/Users/**`。

## 关键接口与依赖

- 前置票：09、24
- 对外契约：本票的代码形状（命名、目录、错误码、端点风格）是 34 号票写进 `CLAUDE.md` 的事实来源，后续模块照抄。

## 步骤

1. **先写失败的单测**：重复用户名应返回 `Result.Failure`；停用用户不能登录。
2. 建聚合与仓储，实现命令与查询。
3. 端点挂权限码与数据范围过滤。
4. 集成测试覆盖 403/422/409 三类反例。

## 验收标准

- [ ] 分页查询支持关键字、排序、分页，参数来自查询串。
- [ ] 新建重复用户名返回 422 且 `errors.userName` 有值，库里无第二条记录。
- [ ] 改角色后该用户 `/api/me` 权限码变化。
- [ ] 停用用户登录被拒，重新启用可登录。
- [ ] 软删除后列表不可见，同名可再次新建。
- [ ] 并发编辑后提交者收到 409。
- [ ] 无 `system:user:delete` 调删除端点 403。
- [ ] 数据范围「仅本人」只返回自己。

## 验证入口

```bash
dotnet test tests/Pzd.Identity.UnitTests
dotnet test tests/Pzd.IntegrationTests --filter Users
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚代码与建表脚本；测试库重建。

## 任务映射

- worktree：实施时填写（`.worktree/32/`）
- 分支：实施时填写（`feat/32-user-crud-backend`）
- 提交：实施时填写（完整 SHA）
