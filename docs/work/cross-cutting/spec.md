# cross-cutting 规格：横切能力（M2）

> 基线：2026-10-29（v1）　作者：老何
> 需求依据：[architecture.md](../../architecture.md) §3.2、§3.3、§5.2、§5.3、§6.2、§6.3、§7.3、§8、§9.2、§11.1–§11.5、§13、§16.1
> 拆分依据：[consensus.md](../pzd-v1/consensus.md) M2（M2.1–M2.12）

## 1. 问题

底座能启动之后，每个模块都要各自解决同一批问题：实体怎么定义、数据库怎么连、请求怎么找到租户、一个命令怎么提交、失败怎么返回、日志怎么带上租户与用户、后台任务怎么跑。这些如果让每个模块自己发明，PZD 会变成六个互不兼容的小系统。

M2 的任务是**把这些答案一次性定型到共享层**，让后续模块只写业务。

## 2. 方案

按「共享内核 → 持久化 → 租户 → 事务与消息 → 错误 → 日志 → 安全 → 调度 → 架构守卫 → 通用能力 → Excel → 健康与 OpenAPI」顺序建共享层，并用架构测试把依赖方向焊死。出口是「架构测试通过 + 租户隔离集成测试通过」。

## 3. 用户故事

1. 作为实现 Bot，我想共享内核只提供 `Entity` / `AggregateRoot` / `ValueObject` / `DomainEvent` / `Result` / `Error` / `IRepository`，以便业务模块不用重复定义基类。
2. 作为实现 Bot，我想 Domain 里不允许出现任何 ORM 特性，映射全在 Infrastructure，以便领域层能被单测直接 new 出来。
3. 作为实现 Bot，我想雪花主键对外序列化为字符串，以便前端 JS 不出精度问题。
4. 作为实现 Bot，我想审计字段和软删除由框架统一处理，以便不靠人记得写。
5. 作为实现 Bot，我想一个 Command 只提交一次，领域事件在同一事务内分发、跨模块消息进 Outbox，以便不出现「业务成功了但消息没了」。
6. 作为实现 Bot，我想重复投递的 Outbox 消息只被消费一次，以便重试不制造重复数据。
7. 作为实现 Bot，我想任何请求进来都能拿到 `ITenantContext`，拿不到就明确 403，以便不会静默连到错的库。
8. 作为实现 Bot，我想业务失败返回 `Result`，端点自动映射 ProblemDetails，以便状态码准确、前端能统一处理。
9. 作为运维，我想每条日志都带租户、用户、TraceId，慢 SQL 有告警，密码和手机号被脱敏，以便排查不靠猜。
10. 作为实现 Bot，我想写操作有 CSRF 防护和幂等键，以便重放与伪造都挡在门外。
11. 作为实现 Bot，我想定义任务类就能注册 Quartz 任务，且能按租户扇出执行，以便写一次跑全部租户。
12. 作为实现 Bot，我想上传文件只需拿到预签名地址，路径自动带租户前缀，以便不会串租户。
13. 作为实现 Bot，我想大批量导出走异步生成加通知下载，以便请求不会超时。
14. 作为审计 Bot，我想有一条命令证明分层依赖没被破，以便不用人肉读代码。
15. 作为实现 Bot，我想前端能生成类型安全的客户端，以便接口变更立刻暴露在编译期。
16. 作为运维，我想 /health 能分别反映 master、Redis、对象存储、租户库的状态，以便定位是哪一环挂了。

## 4. 实现决策（约束，不可自行放宽）

**分层与领域**
- 依赖方向：`Pzd.Api → Pzd.<Module> → Pzd.SharedKernel / Pzd.Infrastructure`；模块之间只允许引用对方的 `.Contracts`；`Domain/` 禁止引用 SqlSugar、ASP.NET Core、Infrastructure 和其他模块（§3.2）。
- 聚合状态只通过方法变更、属性 `private set`；仓储接口在 `Domain/`，按聚合整体加载保存；业务失败返回 `Result`，不抛异常。

**两条已定的契约细节（2026-10-29，改这里就是改契约）**

1. **`Result<T>` 的成功值允许 `null`**（引用类型）。骨架层**不加** `ThrowIfNull`，也不把「成功即非空」写进约定；需要非空约束的调用方自己校验并返回失败。理由：骨架票不引入过强约定，但必须写明并用一条用例钉住（`Result<string>.Success(null!)` → `IsSuccess == true`、不抛异常），否则 06 之后每个调用方各猜一套。
2. **`with` 与校验的关系（措辞模板，照抄不要自创）**：record 主构造的参数校验只在构造那一次生效；`with` 走复制构造、**不重跑属性初始化器**，所以校验写在属性初始化器里会被 `with` 绕过。要让 `with` 也受约束，校验必须放进 `init` 访问器（写法更绕、属性也不再是纯只读）。综合成本，本层选**显式构造 + get-only 属性**：校验写一次，`with` 直接编译不过。
   - **禁止**把它简写成「record 天生不安全」或「改成 record 主构造 + init 属性就会绕过校验」——那是把三种写法混成一句，会让后来人误以为「静态工厂 + 无校验 init」是安全的。34 号票写 `CLAUDE.md` 时必须照上面这段。

**冻结签名块的作用域（2026-10-29，回答 05 审计遗留 low 1）**：05 票里的「冻结签名块」**只列下游必须照抄的成员与继承契约**（`Entity`/`AggregateRoot` 的成员、`protected` 构造、事件视图的实例语义），**不追求 API 完整快照**——例如 `ValueObject` 的 `Equals(ValueObject?)`、`GetHashCode()` 属于 `IEquatable<ValueObject>` 的公开成员，下游派生时无需显式知道，因此不在块内，补它反而要重开一轮审计。哪天 06 或 13 真正碰到这两处，一并补进块里。

**持久化**
- 映射全部集中在 Infrastructure（`EntityService`）；Domain 中禁止 `[SugarColumn]`，由架构测试检查。
- 主键雪花 `long`，对外字符串；`CreatedBy/At`、`UpdatedBy/At` 由 AOP 填充；`IsDeleted` + 全局 `QueryFilter`；`Version` 乐观并发冲突 → 409；MySQL 1062 唯一键 → 409。
- 唯一索引必须含 `TenantId`；`utf8mb4_0900_ai_ci`；时间一律存 UTC，展示层转 `Asia/Shanghai`。
- 不使用 CodeFirst 比对，表结构只走 `db/` 版本化 SQL。

**多租户**
- 启动只注册 `ConfigId = "master"`；请求内由 `ITenantDbProvider` 按需 `AddConnection`（加锁双检，并对该连接单独配置 AOP 与过滤器），禁止在单例上 `ChangeDatabase`。
- 已登录取 claim `tid`；匿名端点（登录、验证码）走子域名或 `X-Tenant`。每次请求都校验租户状态与到期，失败 403 `Tenant.Disabled` / `Tenant.Expired`。缓存 key 统一前缀 `t:{tid}:`。
- 禁止跨库事务；跨 master 与租户库的流程拆成可重试步骤。

**事务与消息**
- 一个 Command = 一次 UoW 提交；领域事件在提交前分发；跨模块消息写入 Outbox 后提交，投递语义为至少一次，消费端必须幂等（去重键为消息 Id），重复消费不产生副作用。

**错误**
- `Result` + `ToHttpResult()` + `IExceptionHandler` 链；ProblemDetails 统一带 `traceId` 与 `code`（`模块.实体.原因`）；状态码严格按 §8.3：400/401/403/404/409/422/426/500/503。
- 不使用「永远 200 + code/msg/data」包装。生产环境不返回堆栈。

**日志**
- 业务代码只依赖 `ILogger<T>`，热点用 `[LoggerMessage]`；`LogContext` 推入 `TenantId`、`UserId`、`TraceId`、`ClientIp`；请求摘要一行。
- SqlSugar AOP：Debug 完整 SQL、>500ms Warning、`OnError` 带参数。脱敏覆盖 password、token、手机号、身份证号。

**安全基线**
- Antiforgery，前端从 Cookie 读 token 放 `X-XSRF-TOKEN`；Data Protection 密钥持久化（否则重启后 Cookie 全失效）；安全响应头 CSP/X-Frame-Options/HSTS/X-Content-Type-Options；`RateLimiter`；关键写操作支持 `Idempotency-Key`。

**调度**
- Quartz：master 库 AdoJobStore + 集群 + JSON 序列化，建表 SQL 进 `db/master/`；`[JobDefinition]` 启动扫描注册；`TenantFanOutJob` 基类遍历租户、每租户独立 scope、单租户异常不中断其他；时区与 Misfire 约定写在基类注释里。

**通用能力**
- `IFileStorage`：S3 兼容，路径前缀 `tenant/{tid}/`，预签名直传直下。分布式锁用 `Medallion.Threading`。缓存用 `HybridCache`，领域事件驱动失效。
- Excel：MiniExcel 流式；大数据量异步生成 → 对象存储 → SignalR 通知下载。
- `/health` 分别检查 master、Redis、对象存储、租户库抽样（分项状态，不合并成一个红点）；OpenAPI 3.1 构建时导出 `openapi.json` 供 orval。

## 5. 测试决策

- **强制测试先行**（AGENTS.md 追加清单）：租户隔离与租户解析、UoW + Outbox 投递与幂等。先写失败测试，确认因预期行为失败（不是编译错误），再实现。
- 架构测试用 NetArchTest，断言的是**依赖元数据**（是否引用了程序集/命名空间），不是文件字面内容。
- 集成测试落在 `tests/Pzd.IntegrationTests`，用真实 MySQL；隔离语义由 36 号票定为**每用例独立库**（不用事务回滚，否则测不出真提交语义），镜像 tag 固定 `mysql:8.4`；不用 InMemory 替身。
- 租户隔离的代表性正反例：租户 A 的上下文查不到租户 B 的数据（正例）；不带租户上下文发起数据访问必须失败而不是回退到 master（反例）。
- Outbox 的代表性正反例：同一条消息投递两次只被消费一次（正例）；事务回滚后不留未投递消息（反例）。
- 只测外部可观察行为，不测私有方法、不测内部字段赋值。

## 6. 非目标

- 不做认证与授权（23、24 号票），不建业务实体。
- 不实现业务审计日志表与登录日志（原 M6.6，期外），只提供技术日志与写操作的可扩展点。
- 不做通知渠道、字典、编号规则、回收站（原 M6，期外）。
- 不做备份与单租户恢复（期外，交运维手册）。
- 不做跨租户查询的管理界面。

## 7. 验收标准

- **AC-01** `dotnet test tests/Pzd.ArchitectureTests` 全绿，且能在一个故意违规的临时分支上变红（证明测试真的在检查）。
- **AC-02** 租户隔离集成测试通过：A 租户查不到 B 租户数据；无租户上下文的数据访问明确失败。
- **AC-03** 未登录访问受保护端点 401；租户停用或到期访问 403 且 `code` 为 `Tenant.Disabled` / `Tenant.Expired`；租户库版本不满足时 503 `Tenant.Upgrading`（此条随 22 号票联动）。
- **AC-04** 一次 Command 只提交一次，且事务内产生的领域事件在提交前分发；事务回滚不留 Outbox 记录。
- **AC-05** Outbox 同一条消息投递两次，业务副作用只发生一次。
- **AC-06** 校验失败返回 400 且带 `errors` 字段字典；业务规则失败返回 422；两者响应体都是 ProblemDetails 且含 `traceId` 与 `code`。
- **AC-07** 每条请求日志含 `TenantId`、`UserId`、`TraceId`；慢 SQL（>500ms）出现 Warning；日志中密码、手机号被脱敏。
- **AC-08** 带 Cookie 的写请求缺少 `X-XSRF-TOKEN` 时被拒绝；服务重启后既有登录 Cookie 仍有效（Data Protection 已持久化）。
- **AC-09** 定义好的任务能在 master 库 `qrtz_*` 表中看到，集群内同一时刻只在一个节点触发；`TenantFanOutJob` 对一个自造租户失败不影响其他租户。
- **AC-10** `IFileStorage` 上传路径以 `tenant/{tid}/` 开头；跨租户预签名地址不能读取他租户文件。
- **AC-11** 大数据量 Excel 导出走异步生成，生成完成后经 SignalR 收到下载通知（此条在 30 号票完成后才能端到端验证，先验证到对象存储生成成功）。
- **AC-12** `/health` 分项返回 master、Redis、对象存储、租户库抽样状态；任一依赖断开时对应项为不健康而不是全红。
- **AC-13** `openapi.json` 构建时导出，字段命名与 §8.4 一致（`type`/`title`/`status`/`code`/`traceId`/`errors`）。
- **AC-14** 持久化约定可验证：审计字段自动填充；软删除记录默认不可见；**软删除后可用同一业务键重建**（唯一索引方案必须在票内留痕，禁止用「去掉唯一索引」解决）；并发版本冲突与 MySQL 1062 都能被识别为 409 的来源错误；时间字段入库为 UTC 原值，Infrastructure 不得改变时区语义，出参转换统一在 API 序列化层。

## 8. 假设与待决

- **A1** 集成测试用真实 MySQL；容器化不可用时必须在票里写明替代方案与剩余风险，不能默认 InMemory。
- **A2** 租户库迁移执行器（DbUp / 自研）未定（consensus §4 待决 2），22 号票实施条件因此是「待满足」，不影响 07、08 号票。

## 9. 来源

- [architecture.md](../../architecture.md) 上述章节号
- [consensus.md](../pzd-v1/consensus.md) M2、§4 待决 5
- [AGENTS.md](../../../AGENTS.md)「项目事实」强制测试先行清单
