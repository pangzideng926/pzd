# 票 08 复测报告：可观测性 + 审计 + 安全横切

- 被测提交：`e8f2e2d`（`origin/main`，雨哥 08 提交）
- 复测方式：干净 worktree `C:/tmp/pzd-t08` @ `e8f2e2d`，`local.env` 拷贝一份；跑完即删
- 结论：**测试层面通过**；两条覆盖边界见 §4（不是缺陷，是还没有验证路径）

## 1. 构建与单测

```
dotnet build Pzd.slnx -warnaserror → 0 警告 0 错误（3.3s）
```

| 工程 | Total | Failed | Skipped |
|---|---|---|---|
| Pzd.SharedKernel.UnitTests | 35 | 0 | 0 |
| Pzd.ArchitectureTests | 30 | 0 | 0 |
| Pzd.Infrastructure.UnitTests | 86 | 0 | 0 |
| Pzd.Api.UnitTests | **68** | 0 | 0 |
| Pzd.IntegrationTests | 10 | 0 | 0 |
| **合计** | **229** | **0** | **0** |

集成 10 条是真跑（本机 MySQL 8.0.46，建删 `pzd_it_a/b`，跑完零残留）。与雨哥自述的 229 一致。

## 2. 真实启动取证（我自己的进程，`127.0.0.1:5097`，不给凭据 → 降级）

`/health/live` → **200**，响应头五件套齐全：

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Content-Security-Policy: default-src 'self'; …; frame-ancestors 'none'; base-uri 'self'; form-action 'self'
Referrer-Policy: no-referrer
Strict-Transport-Security: max-age=31536000; includeSubDomains
```

- `/api/version` → `{"version":"0.1.1-alpha.0.128","buildId":"e8f2e2d","buildTime":"…"}`，**三字段、无 runId**（符合 A11 定下的契约：归属只走 `X-Pzd-Run-Id` 响应头，不扩版本体）。
- `/`（无租户）→ **400**。
- `/health/ready` → **Degraded**，`mysql` / `redis` 两条明细均为 `Degraded` + 中文说明。

降级日志（ver 08 换 `RenderedCompactJsonFormatter` 后的实际形态）：

```
[18:20:22 WRN] Degraded mode: MySQL unavailable（mysql 未配置（ConnectionStrings:mysql 为空）。） | {"SourceContext": "Pzd.Api"}
[18:20:22 WRN] Degraded mode: Redis unavailable（redis 未配置（ConnectionStrings:redis 为空）。） | {"SourceContext": "Pzd.Api"}
```

`grep -c "Degraded mode:"` = **2**，按资源名 `MySQL` / `Redis` 可分别命中。**整行相等会失配**（行尾多了 ` | {…}`），票 11 只能按子串 + 资源名匹配——与老登的建议一致，我这边实测复现了这个坑。

请求摘要行一请求一行、字段可解析：

```
[18:20:22 INF] HTTP GET /health/live responded 200 in 2.6855 ms | {"TraceId": "d010420c96aef65173decc026013126d", "ClientIp": "127.0.0.1", "RequestId": "…", "ConnectionId": "…"}
```

## 3. 退出清理

我只杀了自己起的那个 `Pzd.Api.exe`（监听 5097，pid 88572），端口已释放；worktree 已删，无残留。

## 4. 两条覆盖边界（不背书，交给下一票）

1. **CSRF 双层 token 只有单测证据，没有端到端证据**：`AntiforgeryValidationMiddleware` 有 68 条里的单测（缺 token → 400 `Security.Csrf.Invalid`；带 cookie+header → 放行），但当前 `src/Pzd.Api` **没有任何写端点**（`MapPost/MapPut/MapDelete` 全无），真实 HTTP 面打不出这条 400。等登录外壳/写端点落地后必须补一次真请求验证。
2. **没有任何端点发放 `XSRF-TOKEN` cookie**：`src` 里只有 `AntiforgerySetup` 的配置（`Cookie.HttpOnly=false`），没有 `GetAndStoreTokens` 调用点。也就是说写请求现在必然缺 token → 一律 400。这不是 08 的缺陷（发放端点属于登录壳），但**是交接硬依赖**：登录壳落地时必须同时提供 token 发放端点，否则前端所有 POST 全被 400。
3. 审计（`WithAuditLog` / 有界 Channel）同样只有单测：没有写端点就没有真实审计链路可验，改用例时再补。

## 5. 复跑 @ `79bbe36`（雨哥给的新基线；顺带发现一处**不属于 08** 的契约回退）

> 干净副本 `C:/tmp/pzd-t08c` @ `79bbe36`（当前 `origin/main`），`local.env` 就位，跑完即删。

### 5.1 构建与测试（`e8f2e2d` 之后 09 的 Quartz 与 runId 相关单测加了进来，条数变多）

```
dotnet build Pzd.slnx -warnaserror → 0 警告 0 错误（3.3s）
```

| 工程 | Total | Failed | Skipped |
|---|---|---|---|
| SharedKernel | 35 | 0 | 0 |
| Architecture | 30 | 0 | 0 |
| Infrastructure | **97**（原 86） | 0 | 0 |
| Api | **72**（原 68） | 0 | 0 |
| Integration | **11**（原 10） | 0 | 0 |
| **合计** | **245** | **0** | **0** |

集成 11 条真跑（真 MySQL），与雨哥/旺总报的数字一致。

### 5.2 真实启动（`127.0.0.1:5096`，不给凭据）

五条安全头齐全：`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、CSP（含 `frame-ancestors 'none'`）、`Referrer-Policy: no-referrer`、`Strict-Transport-Security: max-age=31536000; includeSubDomains`。降级日志 `Degraded mode: MySQL/Redis unavailable` 两条可 grep。08 的验收点全部仍然成立。

### 5.3 发现：`79bbe36` 的 `/api/version` 仍带 `runId`（**这不是 08 的问题，是 A11 契约在提交态未收口**）

```
GET /api/version →
{"version":"0.1.1-alpha.0.147","buildId":"79bbe36","buildTime":"…","runId":null}
```

四字段、`runId:null`；同一个响应**没有** `X-Pzd-Run-Id` 头。对照我在 `e8f2e2d` 上测的同一端点，当时是**三字段、无 runId**——也就是说「把 runId 从载荷搬到响应头」这个修法**只存在于张一一的工作区，还没落到任何提交里**。

后果：谁拿 `79bbe36` 当契约基线，都会看到票 03 已验证的三字段被污染，以及「未注入时无头」这条不成立。所以：

- **08 的复测结论只对 08 自己的验收点有效**（单测 + 安全头 + 降级日志），对 A11 不背书；
- A11 的收口必须以「提交态」为准：`ApiVersionInfo` 回到三字段 + 有注入时才写 `X-Pzd-Run-Id` 头，两条都要在**同一提交**里可验。

> **效力说明（老登 2026-10-27 指正，我采纳）**：`79bbe36` 上「脚本读载荷 `runId`、API 也发载荷」是**旧契约自洽**，我这次跑绿只能记成「旧路径当时可用」，**不能当作 A11 新契约的通过证据**。新契约（载荷三字段 + `X-Pzd-Run-Id` 头）目前只在工作区、没有 hash，要等张一一原子提交后在带 hash 的树上重跑；且 web 腿还要 `apps/web` 与 AppHost 两头的 `PZD_RUN_ID` 接线，那部分 `79bbe36` 里一行都没有。

> **更正（同日后续）**：上一条「新契约只在工作区」在 `79bbe36` 成立，但 **HEAD `6b67c2d` 已经把它全部提交**：`ApiVersionInfo` 回到三字段、`Program.cs:140` 调 `ApiRunId.Apply(...)` 写头、`ApiRunId.cs`/`ApiRunIdTests.cs`/`ApiRunIdWiringTests.cs` 都在树上、脚本 `:447` 禁载荷 `runId` / `:453` 读头。我用两个进程实测：不注入 → 三字段、无头；`PZD_RUN_ID=probe-head-xyz` → **body 逐字节一致** + `X-Pzd-Run-Id: probe-head-xyz`。所以「HEAD 半新半旧 / 新契约无 hash」这个判断已过期，A11 可以在 `6b67c2d` 上按提交态复核。
