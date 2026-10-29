状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 07、11 合入后认领
交付状态：待收尾
实施条件：待满足：07、11 合入（前置票）

# 23 认证：Cookie 登录与会话

## 目标

租户内用户能登录、登出、保持会话；暴力破解与撞库被挡。

## 需求依据

- [architecture.md](../../../architecture.md) §7.1、§7.3
- [spec.md](../spec.md) AC-06、AC-07
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M3.5

## 范围与非范围

- 做：ASP.NET Core Identity + 同源 Cookie（`HttpOnly`/`Secure`/`SameSite=Lax`）、登录/登出端点、图形验证码、失败锁定、密码强度、会话存 Redis、按 scheme 组织认证（为 Bearer 预留）、开发期 Vite 代理保持同源。
- 不做：不做忘记密码与短信找回（期外）；不做双因素认证；不做平台超管登录页（期外）。

## 责任文件范围（认领时在群里先说清）

`src/Pzd.Identity/**`（认证部分）、`src/Pzd.Api/Auth/**`、`db/tenant/` 下 users 表脚本、`tests/Pzd.IntegrationTests/Auth/**`。

## 关键接口与依赖

- 前置票：07、11
- 对外契约：登录/登出端点路径与 Cookie 名被 26 号票前端依赖；`/api/me`（25 号票）依赖登录后 claim 形状。

## 步骤

1. **先写失败测试**：未登录访问受保护端点应 401（当前实现前必然失败）。
2. 建 users 表与 Identity 集成，配 Cookie 策略。
3. 实现验证码与失败锁定。
4. 会话入 Redis，登出使会话失效。

## 验收标准

- [ ] 正确凭据登录返回 200 且 Set-Cookie 含 HttpOnly/Secure/SameSite=Lax。
- [ ] 登出后原 Cookie 访问受保护端点 401。
- [ ] 连续失败达阈值后即使密码正确也被拒绝，锁定有到期时间。
- [ ] 验证码错误返回 400 且不增加失败计数。
- [ ] 会话在服务重启后仍有效（Redis 会话）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Auth
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚代码与 users 表脚本（测试库重建）。

## 任务映射

- worktree：实施时填写（`.worktree/23/`）
- 分支：实施时填写（`feat/23-authentication`）
- 提交：实施时填写（完整 SHA）
