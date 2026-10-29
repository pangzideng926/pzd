状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 05 合入后认领
交付状态：待收尾
实施条件：待满足：05 合入（前置票）

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

- 前置票：05
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

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Errors
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚项目改动；无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/09/`）
- 分支：实施时填写（`feat/09-exception-problemdetails`）
- 提交：实施时填写（完整 SHA）
