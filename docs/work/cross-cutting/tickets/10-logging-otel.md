状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 06 合入后认领
交付状态：待收尾
实施条件：待满足：06 合入（前置票）

# 10 Serilog + OpenTelemetry 与日志上下文

## 目标

每条日志都带租户、用户、TraceId，慢 SQL 有告警，敏感字段被脱敏。

## 需求依据

- [architecture.md](../../../architecture.md) §9.2
- [spec.md](../spec.md) AC-07
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.6

## 范围与非范围

- 做：Serilog 配置（开发 Aspire Dashboard / 生产 Seq）、OTLP 导出、`LogContext` 推 `TenantId`/`UserId`/`TraceId`/`ClientIp`、`UseSerilogRequestLogging` 一行摘要、SqlSugar AOP（Debug 完整 SQL、>500ms Warning、`OnError` 带参数）、脱敏 destructuring policy。
- 不做：不做业务日志表与审计界面（期外）；不做前端错误上报（期外）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/Logging/**`、`src/Pzd.Api/Logging/**`、`tests/Pzd.IntegrationTests/Logging/**`。不碰 `src/Pzd.Infrastructure/Tenancy`（只读它的 context）。

## 关键接口与依赖

- 前置票：06
- 对外契约：日志字段名 `TenantId`/`UserId`/`TraceId` 固定，供运维检索。

## 步骤

1. 接 Serilog 与 OTel，配置两个 sink。
2. 中间件推 `LogContext`，接 SqlSugar AOP。
3. 写脱敏策略并单测（password/token/手机号/身份证号）。
4. 集成测试断言日志行包含三个字段（可用测试 sink 捕获）。

## 验收标准

- [ ] 每次请求一行摘要日志。
- [ ] 日志行含 `TenantId`、`UserId`、`TraceId`。
- [ ] 超过 500ms 的 SQL 记 Warning。
- [ ] 脱敏策略对四类字段生效，有单测。
- [ ] OTLP 导出在开发环境下可见 trace。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Logging
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚配置与代码；无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/10/`）
- 分支：实施时填写（`feat/10-logging-otel`）
- 提交：实施时填写（完整 SHA）
