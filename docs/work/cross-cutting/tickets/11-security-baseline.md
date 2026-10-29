状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 01 合入后认领
交付状态：待收尾
实施条件：待满足：01 合入（前置票）

# 11 安全基线（Antiforgery / Data Protection / 响应头 / 限流 / 幂等键）

## 目标

写操作有 CSRF 防护、会话在重启与多节点下不丢、安全响应头默认开启、关键写操作可幂等。

## 需求依据

- [architecture.md](../../../architecture.md) §7.3
- [spec.md](../spec.md) AC-08
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.7

## 范围与非范围

- 做：Antiforgery（Cookie 取 token → 请求头 `X-XSRF-TOKEN`）、Data Protection 密钥持久化（Redis/master，生产证书保护）、安全响应头（CSP/X-Frame-Options/HSTS/X-Content-Type-Options）、内置 `RateLimiter`、`Idempotency-Key` 过滤器。
- 不做：不做登录与验证码（23 号票）；不做 WAF 或网关配置。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Api/Security/**`、`tests/Pzd.IntegrationTests/Security/**`。不碰 `src/Pzd.Identity`。

## 关键接口与依赖

- 前置票：01
- 对外契约：前端 mutator 需要读取 CSRF Cookie 名与头名，本票里定死并写进 README，28 号票照抄。

## 步骤

1. 开启 Antiforgery 并暴露 token 端点/Cookie。
2. 持久化 Data Protection 密钥。
3. 加安全响应头中间件与限流策略。
4. 实现 `Idempotency-Key`（同键重放返回首次结果）。

## 验收标准

- [ ] 写请求缺 `X-XSRF-TOKEN` 被拒（400/403）。
- [ ] 服务重启后既有 Cookie 仍有效（密钥已持久化）。
- [ ] 响应头含四类安全头。
- [ ] 超限请求返回 429。
- [ ] 同 `Idempotency-Key` 重复提交只产生一次副作用。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Security
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚配置；Redis 中的密钥可清空重生成（会使既有 Cookie 失效，需在票里说明）。

## 任务映射

- worktree：实施时填写（`.worktree/11/`）
- 分支：实施时填写（`feat/11-security-baseline`）
- 提交：实施时填写（完整 SHA）
