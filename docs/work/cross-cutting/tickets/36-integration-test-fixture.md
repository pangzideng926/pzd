状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 01 合入后认领；这是本批第 36 张票，按依赖先于 06 执行
交付状态：待收尾
实施条件：待满足：01 合入（前置票）

# 36 集成测试库 fixture（真 MySQL）

## 目标

给所有集成测试一个能连真库的载体，避免每张票各自造一套 fixture，或退化成 InMemory 假通过。

## 需求依据

- [architecture.md](../../../architecture.md) §16.1
- [spec.md](../spec.md) AC-01、AC-02（测试载体，无单独 AC）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) 补票：M2 的前置基础设施，§18 无对应步骤

## 范围与非范围

- 做：`tests/Pzd.IntegrationTests` 工程与 fixture：**只引 xUnit + 数据库驱动 / Testcontainers，不引任何 `Pzd.*` 项目**；MySQL 镜像 tag 由本票固定（`mysql:8.4`），不等 03；**用例隔离方式定为「每用例独立库」**，不用事务回滚；连接串来自配置/环境变量，不硬编码。
- 不做：不写任何业务断言（06 及后续票写）；不覆盖前端 E2E（35 号票）；不做性能测试；不引任何 `Pzd.*` 项目（引了就退回，见 AC）。

## 责任文件范围（认领时在群里先说清）

新建 `tests/Pzd.IntegrationTests/**`（工程与 `Fixtures/**`）。不碰 `src/`。

## 关键接口与依赖

- 前置票：01
- 对外契约：后续所有集成测试（06–32）复用此 fixture；**隔离语义为「每用例独立库」**，06 的真提交、08 的一次 Command 一次提交与 Outbox 回滚语义都建立在这个前提上，不允许改成事务回滚。MySQL 版本以本票固定的 tag 为准，03 出来后比对。

## 步骤

1. 建 xUnit 工程：**只引 xUnit + 数据库驱动 / Testcontainers，不引任何 `Pzd.*` 项目**，这样 36 只依赖 01，可与 05 真正并行。
2. 实现 fixture：按固定 tag `mysql:8.4` 起库、每用例建独立库、用完销毁、失败不留残留。
3. 写一个自证的冒烟测试（类名 `SmokeTests`：建表 → 插入 → 查询）。
4. 贴命令、通过数与关键输出行。

## 测试 seam（已确认，未经确认不写测试）

S1 fixture 对外只暴露「建一个独立库、给出连接串、用完销毁」。抓到：容器能起、有建库权限、重复运行不撞库、失败后无残留库；抓不到：任何业务表语义。
S2 `SmokeTests` 走一次建表 → 插入 → 查询。抓到：驱动、连接串、`utf8mb4` 真的通；抓不到：并发与事务。

本票只新增这两个 seam，业务表语义由 06 及后续票在 fixture 上追加。

## 验收标准

- [ ] `dotnet test tests/Pzd.IntegrationTests --filter SmokeTests` 连真 MySQL 完成建表、插入、查询，通过数 >0。
- [ ] 工程不引用任何 `Pzd.*` 项目（`dotnet list reference` 输出为空可证）。
- [ ] 每个用例独立库，重复运行结果一致，失败后无残留库。
- [ ] 镜像 tag 固定为 `mysql:8.4`；03 合入后再比对，不一致改 tag 重跑冒烟并贴新输出。
- [ ] 不存在 InMemory 替身路径，也没有用事务回滚冒充隔离（审查确认）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter SmokeTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

删除工程与 fixture；容器与测试库直接销毁。

## 任务映射

- worktree：实施时填写（`.worktree/36/`）
- 分支：实施时填写（`test/36-integration-test-fixture`）
- 提交：实施时填写（完整 SHA）
