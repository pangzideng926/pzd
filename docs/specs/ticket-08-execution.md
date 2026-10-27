# 票 08 执行细化与票 09 开工检查

> 给旺总：按 `executing-plans` 分段执行；本文件细化现有票 08，不新增票、不改变既定团队接力。只写规格与验收，不提供实现代码。每段先红后绿，再提交；实现签名与包版本由开发核实，不以本文建议文件名代替技术评审。

**Goal:** 让请求日志、遥测、异步审计与安全保护各有可证伪的端到端证据。

**Architecture:** 沿用 API 组合根、Infrastructure 横切实现、SharedKernel 最小抽象。复用现有租户、错误响应、测试认证接缝，不引入业务模块或登录流程。

**Tech Stack:** .NET 10、Serilog、OpenTelemetry、ASP.NET Core Antiforgery/Data Protection、System.Threading.Channels；版本由开发核对兼容性后写入 CPM，不在此猜版本。

**Spec:** [票 08/09](./tickets.md)、[foundation §4.7/4.8](./foundation.md)、[架构 §7.3/9/11](../architecture.md)。核对基线：`7c5f411`。

## 0. 前置、边界与共同门禁

- 08 硬依赖仍为 06（已通过），不是新增依赖 03；调度安排在 03 收口后执行，以免并行改 API 组合根和争抢端口。
- 只改本票横切能力；不改架构文档，不实现登录/权限、审计落库、业务 SQL、前端外壳。
- 新增包集中写 `Directory.Packages.props`，各 csproj 只声明引用；Infrastructure 不引用 API；SharedKernel 不引入 Web/Serilog/OTel。
- 先协调停止自己持有的宿主再整解构建。不得杀同机其他任务的进程。5080/5199 的真实探测与李总排他协调。
- 复用 `tests/Pzd.Api.UnitTests/Tenancy/PzdApiFactory.cs`、测试认证与测试端点；测试端点只在测试宿主注册，不能把探针写端点暴露到生产。
- 现有 Factory 会清日志提供方：新建可注入 sink/reader/消费者的测试接缝，证明生产注册链仍执行，不能只测一个脱离宿主的假日志器。
- 保留 03 行为：免租户端点、缺资源 200 + Degraded、启动期每资源一条 Warning、轮询不重复刷框架 Warn。换 Serilog 后须重新验证类别过滤；不机械保留或删除某一行 ClearProviders，验的是行为。
- 日志/审计/遥测不记录真实凭据、Cookie、Authorization、X-XSRF-TOKEN 或完整连接串。测试只用明显虚构的哨兵敏感值。
- 下列命令从仓库根执行；每段记录被测 hash、失败用例、修后退出码、测试汇总，不能只写“绿了”。

```powershell
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
dotnet run --project tests/Pzd.Infrastructure.UnitTests/Pzd.Infrastructure.UnitTests.csproj
dotnet run --project tests/Pzd.ArchitectureTests/Pzd.ArchitectureTests.csproj
dotnet build Pzd.slnx -warnaserror
```

## 1. 请求摘要 + 上下文 + 脱敏（覆盖验收 1、3）

**文件职责**
- 新建 `src/Pzd.Infrastructure/Observability/SerilogRegistration.cs`：注册控制台结构化输出、请求摘要、类别级过滤；不接生产 Seq 部署。
- 新建 `LogContextMiddleware.cs`（同目录）：读取已解析租户/认证信息，推入 TenantId/UserId/TraceId/ClientIp，确保请求结束后作用域释放；不能信任任意 X-Forwarded-For。
- 新建 `SensitiveDataDestructuring.cs`（同目录）：统一处理敏感字段、嵌套对象、集合与字典，不修改业务对象；安全字段保留可诊断信息。
- 修改 `src/Pzd.Api/Program.cs`、`appsettings.json` 与相关 csproj/CPM：组合注册。请求摘要应覆盖租户拒绝及异常路径；注意摘要写出时仍能取得上下文，不能在作用域已释放后得到全空属性。
- 新建 `tests/Pzd.Api.UnitTests/Observability/RequestLoggingTests.cs`、`SensitiveDataTests.cs` 与测试用 RecordingLogSink。

**执行与验收**
- [ ] 先写红测：正常请求、租户 400、异常 500 各恰好一条“请求完成摘要”；不是要求全系统只产生一条日志。四个上下文字段存在；匿名/免租户字段允许 null，不能伪造用户/租户 ID。
- [ ] 并发 A/B 租户请求断言摘要互不串值；TraceId 与错误响应关联；未授权转发头不改变 ClientIp。
- [ ] 输入虚构 password/token/手机号/身份证号，含大小写键、嵌套对象、字典和集合，断言序列化后的整条日志不含原值，安全字段仍在。只测顶层匿名对象不够。
- [ ] 实现最小注册与中间件；不要宣称 destructuring 能自动清理任意已拼接字符串，禁止记录原始请求体、秘密头和连接串异常原文。
- [ ] 跑 API 与架构测试；去掉脱敏策略/上下文填充各应有失败用例；提交本段。

## 2. OTel 有采集、可关闭导出（覆盖验收 2）

**文件职责**
- 新建 `src/Pzd.Infrastructure/Observability/OpenTelemetryRegistration.cs`：service name、traces/metrics、ASP.NET Core/HttpClient/runtime instrumentation，按 `OpenTelemetry:Endpoint` 是否存在启用 OTLP。
- 新建 `tests/Pzd.Api.UnitTests/Observability/OpenTelemetryTests.cs`：内存测试 exporter/reader 或等价 SDK 接缝；隔离全局 listener，释放 provider。
- 修改组合根、配置与 CPM；测试依赖只进测试工程。

**执行与验收**
- [ ] 红测：一个 HTTP 请求和受控 HttpClient 调用产生对应 Activity，service.name 非空；metrics 经测试 reader 收集到 instrumentation 产生的数据，不只断言 DI 有服务。
- [ ] 无 endpoint 时启动成功、无网络导出尝试；有测试 endpoint 时才构造导出链，不能用真实外部收集器作为单测前置。
- [ ] 标签不带秘密或高基数用户输入；测试不依赖外网/时间睡眠碰巧成功。
- [ ] 实现后跑 API 测试并提交。生产 Seq/远程 collector 部署不属本票。

## 3. 审计从端点进入队列再被消费（覆盖验收 4、5）

**文件职责**（均在 `src/Pzd.Infrastructure/Audit/`）
- `WithAuditLogExtensions.cs`：保存模块、动作 metadata 并接过滤器。
- `AuditLogEntry.cs`：不可变快照，含操作人、租户、模块、动作、脱敏参数、结果、耗时、IP、UA；不能持有 HttpContext、请求服务或可变请求 DTO。
- `AuditLogFilter.cs`：围绕端点执行采集成功/失败结果，在请求作用域内完成快照；不吞业务异常。
- `AuditLogChannel.cs`：有界队列，容量来自 Audit:QueueCapacity，提供可观测丢弃计数；请求路径不能 await 等队列空间。
- `AuditLogBackgroundService.cs`：后台批量消费、取消与停机；本轮计数/告警消费，不假装已落库。消费者接口可由开发提炼在同目录，允许测试替换。
- 新建 `tests/Pzd.Api.UnitTests/Audit/AuditPipelineTests.cs`、`AuditBackpressureTests.cs`：测试端点 + 可暂停假消费者。

**执行与验收**
- [ ] 红测：带 metadata 的成功/失败端点各产生对应审计条目；无 metadata 不产生。断言所有票面字段与脱敏参数，不只断言队列长度。
- [ ] 设置容量 1、暂停消费者、依次填满再提交；新增请求仍完成，丢弃计数精确增加，已入队条目不混租户。开发明确选择丢新/丢旧并在报告记录，不把 TryWrite 返回 true 等同于未丢弃。
- [ ] 丢弃须可计数、可诊断，但不能每个请求刷 Warning；用汇总/节流验证持续压力不会产生无界日志。此处不指定 Channel 枚举以代替开发选型。
- [ ] 后台消费失败不能使 HTTP 请求失败；无 DB 模式消费数量与未持久化 Warning 有证据，报告明确“未落库”。
- [ ] 停机取消能结束消费者；排空或剩余丢弃须记录数量，不无限等。批大小/间隔/停机上限由开发定可测配置，并在提交中说明。
- [ ] 跑 API/Infrastructure 测试；用同步信号控制队列，不用固定 sleep；提交本段。

## 4. Data Protection + XSRF 成对校验（覆盖验收 6、8）

**文件职责**
- 新建 `src/Pzd.Infrastructure/Security/DataProtectionSetup.cs`：配置密钥目录与稳定应用隔离标识；默认目录按 foundation，密钥不进仓库。
- Antiforgery 注册/校验可拆为同目录 `AntiforgerySetup.cs`、`AntiforgeryValidationMiddleware.cs`，也可由开发选等价框架接法；修改组合根确保实际覆盖普通 JSON 写端点，而非仅表单绑定自动校验。
- 新建 `tests/Pzd.Api.UnitTests/Security/AntiforgeryTests.cs`、`DataProtectionTests.cs`；测试端点负责发出 request token 与测试写入计数。

**执行与验收**
- [ ] 红测：POST/PUT/PATCH/DELETE 各在缺 header、伪造 token、缺配对 cookie 时 400，写入计数为零；同一身份正确 token + cookie 时才成功。
- [ ] GET/HEAD/OPTIONS 不受写请求 token 检查阻断；测试提供有效租户，不能把 Tenant.NotFound 的 400 当成 XSRF 已验证。健康/版本原有豁免保持。
- [ ] 错误统一走现有 ProblemDetails 出口并含 traceId，不在响应泄露 token；新增错误码按现有前缀登记，不覆盖 Auth.Unauthorized/Forbidden。
- [ ] 区分两类 token：框架配对 cookie 必须 HttpOnly；供 JS 复制到 X-XSRF-TOKEN 的 request token 不能只存在 HttpOnly cookie 内。测试通过测试宿主取 request token；生产发放端点/前端读取流程随步骤 3–4 明确，不借本票新增登录流程。若生产采用可读 request-token cookie，需单独命名和配置，不能降低身份 cookie 的 HttpOnly。
- [ ] SameSite=Lax；生产 Secure，开发 HTTP 可例外。测试生产 cookie 属性需 HTTPS TestServer 地址，不以 HTTP 上没发 cookie 判 token 实现错误。
- [ ] 两个独立 provider/host 使用同一临时密钥目录和应用标识：A 保护的值，B 重建后解保护成功；不同隔离标识不能解开。测试后仅清理由本测试创建的目录，不动用户默认密钥环。
- [ ] 跑 API 测试（包括原有写端点回归，给合法请求补 token，不能全面关闭校验）；提交本段。

## 5. 安全响应头覆盖成功与拒绝路径（覆盖验收 7）

**文件职责**
- 新建 `src/Pzd.Infrastructure/Security/SecurityHeadersMiddleware.cs`：CSP、X-Frame-Options、X-Content-Type-Options；生产 HSTS 走框架机制或等价实现。
- 新建 `tests/Pzd.Api.UnitTests/Security/SecurityHeadersTests.cs`；组合根接线应包住租户拒绝、错误出口。

**执行与验收**
- [ ] 红测：200、租户 400、XSRF 400、异常 500 均有前三个头；不可为空或相互矛盾。开发给出 CSP 实际值和用途，不能用任意通配/unsafe-inline 仅凑测试。
- [ ] Production + HTTPS 非 localhost 测试主机返回 HSTS；Development 不强制 HSTS。不要忽略框架对 HTTP/localhost 的默认排除规则。
- [ ] 本票保护 API 响应；Vite dev server 的 HTML 响应不是该 API 中间件生成，不宣称已保护 SPA 部署入口。
- [ ] 跑 API 测试并提交。

## 6. 一次综合回归与交接

- [ ] 执行 §0 四条命令，确认 06 错误码/traceId、05 租户、07 后台连接边界不回退。
- [ ] 协调空闲端口后执行 `pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire`；健康实例与缺资源分别判定，不能 Healthy/Degraded 两种都当同一用例通过。
- [ ] Serilog 替换后重新检查：每个不可用资源启动 Warning 一条、随后轮询三次数量不增；框架健康检查类别无重复 Warn，但真正 Error 不被屏蔽。
- [ ] 产出测试报告与审计证据：八条验收逐条关联测试名/hash/命令；记录丢弃策略、批量参数、CSP 值、依赖版本。提交后交李总测试，再交老登审计，规格 Bot 不代签通过。

## 步骤 3–4 移交（明确非本票已实现）

1. 审计租户库 audit_logs 建表、批写、失败重试/保留期；当前只有异步消费接缝，不能称可靠持久化。
2. 生产多节点 Data Protection 的 Redis/master 存储、证书保护与共享密钥生命周期；本轮仅本地目录重启复用。
3. 登录/权限/真实 ICurrentUser 身份映射、生产 XSRF request-token 发放及身份切换刷新；前端步骤 4 的自动带头、登录失败处理。
4. 业务 SQL AOP、变更审计、登录日志与管理界面、前端错误上报：按对应业务模块接入，不本轮制造业务端点。
5. Seq/collector 部署与保留策略；本票只保证配置接缝、采集和无 endpoint 不导出。

## 票 09 开工前置核对（不是已放行）

| 条件 | 本次核对 | 开工动作/证据 |
|---|---|---|
| 04 已通过 | 已满足，原审计基线 49ee09a | 保持架构测试通过 |
| 03 通过 | 未满足；7c5f411 已含 F-03-1 修复，aspire/前端/退出清理待李总复测、老登审计 | 不以代码提交代替前置验收 |
| master 建库及 Quartz 表 | 本次未执行、不宣称已建 | 票 09 内用 init-master-db.ps1 幂等建 pzd_master/qrtz_*；需安全注入凭据及 CREATE 权限，权限不足明确阻塞，不能把本机主路径偷换内存 |
| 无业务迁移器 | 仍成立 | A6 允许 Quartz 专用 SQL/初始化脚本，不扩成业务迁移框架 |
| MySQL/Redis | 历史真跑可用；本次未再连 | 09 开工重新探测；MySQL 服务器 ping 不等于 Quartz 表已可用 |
| 端点复用 | API Program.cs 与 Endpoints/ApiVersionInfo.cs 已有实现 | 不按旧票路径另建第二套 version/health 端点；优先扩展现有文件 |
| master 连接语义 | 03 用 ConnectionStrings:mysql 做服务器探活；API 租户 master 使用 ConnectionStrings:master | 开工明确 Quartz 选用的 master 连接及库名，报告只写配置键，禁止靠去掉 Database 来证明 AdoJobStore 可用 |
| 持久化证据 | 待实现 | 同一 JobKey 在重启前后可查询；断言持久化 store 与 qrtz 表，不能仅证明启动又注册了同名任务 |
| 后台租户边界 | 07 已规定显式 tenantId 取连接 | Heartbeat 为平台任务；需要租户库时只调用显式重载，传递取消，不做跨租户共享 scope |

09 硬依赖保持 03、04，不擅自加 08。若消费 08 的日志扩展，开发必须显式说明新增依赖或使用现有 ILogger 接缝。无连接串内存兜底与“已配置但认证/建表失败”必须分开举证，不能静默掩盖错误。

核对结论：08 可按上述五段实现加综合回归接棒；09 暂挂等待 03 验收，其余建库/权限/包版本属于 09 开工核验，不是假定已就绪。
