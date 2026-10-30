状态：待实施
主写入者：未认领
更新日期：2026-10-29 21:40 Asia/Shanghai
下一步：等 05、36 合入后认领
交付状态：待收尾
实施条件：待满足：05、36 合入（05 给 `Result`/`Error`，36 给集成测试载体）

# 09 异常处理链与 ProblemDetails 映射

## 目标

业务失败返回 `Result`，端点自动映射 ProblemDetails；状态码准确、响应体统一带 `traceId` 与 `code`。

## 需求依据

- [architecture.md](../../../architecture.md) §8.1–§8.5
- [spec.md](../spec.md) AC-06
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.5

## 范围与非范围

- 做：FluentValidation pipeline、`result.ToHttpResult()`、`IExceptionHandler` 链、`AddProblemDetails` 定制（补 `traceId`、`code`）、§8.3 状态码映射、错误文案集中在各模块 `Errors` 类。
- 不做：不写业务错误码（各模块自己写）；不做前端拦截（28 号票）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/Errors/**`、验证 pipeline、`src/Pzd.Api/Middleware/**`、`tests/Pzd.IntegrationTests/Errors/**`。不碰 `src/Pzd.SharedKernel`。

## 关键接口与依赖

- 前置票：05、36（36 是集成测试载体：本票验证跑在 `tests/Pzd.IntegrationTests` 上）
- 对外契约：响应体形状（`type`/`title`/`status`/`code`/`traceId`/`errors`）被 28、33 号票依赖，不得改。

## 步骤

1. 实现 `Result → IResult` 映射与状态码表。
2. 实现校验 pipeline 与 `errors` 字典。
3. 实现全局异常处理器（生产不返回堆栈）。
4. 集成测试覆盖 400/422/500 三类与 `traceId` 存在性。

## 验收标准

- [ ] 校验失败 400 且 `errors` 为字段字典。
- [ ] 业务规则失败 422。
- [ ] 未知异常 500 且不含堆栈。
- [ ] 四类响应都含 `traceId` 与 `code`。
- [ ] 不使用「永远 200 + code/msg/data」包装。
- [ ] 写失败必须分型映射，不许压成同一种失败（2026-10-30 加，来源：06 五审 medium / P8、六审 high、合并后复验）。06 侧要能区分五种情形，**全部以返回值为信号，不得靠捕获异常区分**（张一一裁定：异常只留给调用方用错 API，业务态一律返回 `PersistenceFailure`）：`Persistence.Record.VersionConflict`（版本被别人改掉）、`Persistence.Record.NotFound`（记录不存在或不属于当前租户）、`Persistence.Record.InTrash`（记录在回收站，不能直接更新）、`Persistence.Record.NotInTrash`（恢复的目标不在回收站）、`Persistence.Record.DuplicateKey`（唯一键冲突）。09 要给出互不相同的 `code`；版本冲突、在回收站、不在回收站都取 409 但 `code` 与文案必须不同——「在回收站」写清「请先恢复」，**不用 410**（记录可恢复，410 语义是不可恢复）。**前置**：本条落地前 06 必须先把五种情形分开（06 六审 high：当时前两种都返回 `VersionConflict`、「在回收站」与「无租户上下文」抛同一个裸 `InvalidOperationException`；合并后复验又发现 `RestoreAsync` 依赖「实体自称已删」，DTO 形态的恢复请求会被抛成 500）；06 未分开前 09 不得自行猜测分类。理由：把「已删」映射成「并发冲突」会让前端提示「请刷新重试」，而重载永远读不到这条记录（06 票面 `:258` 已为存量 `Version=0` 行记过同一个坑）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Errors
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚项目改动；无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/09/`）
- 分支：实施时填写（`feat/09-exception-problemdetails`）
- 提交：实施时填写（完整 SHA）
