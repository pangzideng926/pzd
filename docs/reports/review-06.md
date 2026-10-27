# 审计报告：票 06（异常处理 + Result→HTTP）

> 审计：老登｜日期：2026-10-27｜方法：`code-review`（Standards / Spec），合并李总复测证据
> 被测：`dee779d`（初始）→ `3ff40ef`（F-06-1）→ `6e20aaf`（F-06-2）→ `abde749`（F-06-3，终审）
> 依据：[`architecture.md`](../architecture.md) §8.1–§8.4、[`foundation.md`](../specs/foundation.md) §4.4、[`tickets.md`](../specs/tickets.md) 票 06、[`test-report-06.md`](test-report-06.md)（李总）
> **结论：通过（`abde749`，156 条全绿）。** 票 06 主路径（映射全表、`type`/`code`、404/405、生产不泄漏、F7 三条违规）成立；三条收口项全部落地并复核：F-06-1 `3ff40ef`、F-06-2 `6e20aaf`、F-06-3 `abde749`。剩余 3 条 low（`errors` 字典形态覆盖、`Auth.*` 文案与票面不一致、两处提交信息编号笔误）不阻塞关票，建议随手处理。

---

## 1. 基线（我实跑 + 李总复现一致）

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build Pzd.slnx -warnaserror --no-incremental` | 0 警告 0 错误，exit 0 |
| 五个测试工程（终审 `abde749`） | `dotnet run --project tests/<工程>/…` 逐个 | 35+71+30+19+1 = **156 通过，0 失败**，exit 0 |
| 初始基线（`dee779d`） | 同上 | 35+54+26+16+1 = **132 通过，0 失败**，exit 0 |
| 提交范围 | `git show --stat dee779d` | 12 文件（5 实现 + 6 测试 + Program.cs），未越界 |
| 工作树 | `git status --porcelain` | 空（审计时点） |
| 李总复测 | `test-report-06.md` | 132 条全绿；6 个变异各自变红；AC4 探针证实 PascalCase 键原样输出 |

## 2. 票面验收逐条核对

| # | 验收项 | 核对结果 |
|---|---|---|
| 1 | 映射全表 400/401/403/404/409/422/426/503，400 含 `errors` | **通过**：`ErrorMappingTests.MappingTable` 8 条逐条走 HTTP 接缝，断言 status/type/code/header；`errors` 用例另断言 camelCase 键 |
| 2 | `type` kebab、`code` 一致、`traceId` 与响应头/`Activity.Id` 一致 | **通过**：type/code/body↔header 有断言；`3ff40ef` 补 `TraceIds` 两条单测覆盖 `Activity.Id` 分支（F-06-4 关闭） |
| 3 | 未知异常 500、无堆栈/类型名、含 traceId；开发/生产差异 | **通过**：生产环境 HTTP 接缝断言无 `boom`/类型名/`" at "`/`stack`；开发环境处理器级用例仅多 `exceptionType` |
| 4 | FluentValidation 失败 → 400、`errors` camelCase | **基本通过**：camelCase 转换在 `3ff40ef` 修复（PascalCase 入参 + 变异复核）；「FluentValidation 失败 → 400」本轮无真实管线，票面已明确移交步骤 3（§4） |
| 5 | 404/405 框架响应带 `code`/`traceId` | **通过**：两条用例断言 body/header；`3ff40ef` 补 `type`/`title` 断言（F-06-6 关闭） |
| 6 | 不使用「永远 200 + 包装」 | **通过**：`Result`→204、`Result<T>`→200+值，失败走对应状态码；代码无统一包装体 |
| 7 | F7：`FakeApiDependency` → 恰好 3 条违规 | **通过**：`ReverseCaseTests` 断言恰好 3 个类型名，复用生产谓词 `DomainDependencyViolations`；李总去掉 Api 引用变异后该用例变红 |

---

## 3. 发现

### F-06-1 · medium · `errors` 的 key 没有 camelCase 转换（AC4 实测缺口）

- **位置**：[`ProblemDetailsCustomization.cs:33-36`](../../src/Pzd.Api/Errors/ProblemDetailsCustomization.cs#L33-L36)、序列化处 [`ResultHttpExtensions.cs:57-61`](../../src/Pzd.Api/Errors/ResultHttpExtensions.cs#L57-L61)
- **问题**：`errors` 字典原样塞进 `ProblemDetails.Extensions`，写出时用默认 JSON 选项（`DictionaryKeyPolicy` 为 null），key 不会被转换。李总把 validation scenario 的 key 换成 FluentValidation 实际会产生的 `"UserName"` 后，响应体就是 `"errors":{"UserName":[...]}`；现有用例入参本来就写 `userName`（[`TestEndpoints.cs:62`](../../tests/Pzd.Api.UnitTests/TestEndpoints.cs#L62)），所以转不转都绿——和票 04 的 F1 同类：用例自己喂了期望输入。
- **为什么错**：AC4 要求 `errors` 键与前端字段名一致（camelCase），而前端 `setError` 依赖这个键。接入真实 validator 后字段映射会整体落空，且现有测试无法发现。
- **修法（二选一，推荐 A）**：
  - **A. 在 `Create` 归一化 key**：只对 `errors` 的字典键做 camelCase（`JsonNamingPolicy.CamelCase.ConvertName`），保持 ProblemDetails 其它扩展原样，影响面最小；需要处理 `Dictionary<string, string[]>`（FluentValidation 转换后的自然形态）以及可能的 `IReadOnlyDictionary`。
  - **B. 给 problem JSON 单独的 `JsonSerializerOptions.DictionaryKeyPolicy = CamelCase`**：三处写出（`PzdProblemResult`、`StatusCodePages`、`GlobalExceptionHandler`）共用一份选项；注意这会影响该选项下所有字典键，需确认不会误伤其它扩展。
- **必须补的用例**：输入 `errors: { "UserName": [...] }`，断言响应里是 `userName` 且没有 `UserName`。仅在 `TestEndpoints` 里改一行入参不够，要有一条独立的「PascalCase 入参 → camelCase 输出」用例。
- **复核（`3ff40ef`，我实跑）**：**已修**。154 条基线（35+71+28+19+1）全绿；独立 worktree 变异两处：改回原样透传 → 该用例 **1 红**；`TraceIds.Current` 去掉 `Activity` 分支 → `TraceId_prefers_the_running_activity_id` **1 红**。`TestEndpoints` 入参已改为 PascalCase，不再自喂期望值。
- **残余 low（不阻塞）**：`ToCamelCaseKeys` 只覆盖 `IDictionary<string, object?>` 与 `IDictionary<string, string[]>`；若传 `ReadOnlyDictionary<string, string[]>`（票 12 之后很自然）或 `Dictionary<string, List<string>>`，会走 `_ => errors` **静默不转换**。建议改用非泛型 `System.Collections.IDictionary` 重建以覆盖所有字典形态；另外 camelCase 后若同时存在 `UserName`/`userName`，`ToDictionary` 会抛重复键异常，需要去重策略。

### F-06-2 · medium（跨 05）· 租户 400/403 响应缺 `traceId` / `X-Trace-Id`，统一 ProblemDetails 没覆盖中间件手写路径

- **位置**：[`TenantResolutionMiddleware.cs:121-146`](../../src/Pzd.Infrastructure/Tenancy/TenantResolutionMiddleware.cs#L121-L146)，对照 [`ProblemDetailsCustomization.cs:21-39`](../../src/Pzd.Api/Errors/ProblemDetailsCustomization.cs#L21-L39)
- **问题**：`WriteProblemAsync` 手写 ProblemDetails，只塞 `type`/`title`/`status`/`code`，**没有 `traceId` 扩展，也没有 `X-Trace-Id` 响应头**；票 05 的两条 403 用例只断言 `type`/`code`，所以一直没暴露。
- **为什么错**：架构 §8.4 的响应格式包含 `traceId`，foundation §4.4 也写「统一补 traceId、code」。租户停用/过期是最需要排障的 403 之一，缺 traceId 时日志与响应无法关联；而且票 06 的「统一」只覆盖了 `Pzd.Api` 内部路径，中间件是第二个实现，两处 `ToKebabCase`（[`TenantResolutionMiddleware.cs:149`](../../src/Pzd.Infrastructure/Tenancy/TenantResolutionMiddleware.cs#L149) 与 [`ProblemDetailsCustomization.cs:54`](../../src/Pzd.Api/Errors/ProblemDetailsCustomization.cs#L54)）已经重复，后续必然漂移。
- **修法（一次做完）**：
  1. 在 `WriteProblemAsync` 补 `traceId = Activity.Current?.Id ?? context.TraceIdentifier` 与 `X-Trace-Id` 响应头；
  2. 把 kebab 转换收成**一份**共享实现（放 `Pzd.Infrastructure` 公共静态：Api 引用 Infrastructure 合法，Infrastructure 不能反向引用 Api），Api 侧委托过去；
  3. 在现有 `Disabled_tenant_returns_403_tenant_disabled` 里补 body `traceId` 与 header 断言（Expired 同理）。
- **归属说明**：这条是票 05 的遗留，我上一轮审计漏了，本票一并收口；只改这一个方法与两条断言，不重开 05 全部工作。
- **复核（`6e20aaf`，我实跑）**：**已修**。新增 [`ProblemDetailsWriter`](../../src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs) 作为唯一实现（`Create`/`MapStatusCode`/`Customize`/`WriteAsync`/`ToKebabCase`/`TraceIds`），中间件短路与 `ToHttpResult`/异常处理/框架兜底全部改走它，Api 侧 `ProblemDetailsCustomization.cs` 删除；`grep` 确认 `src` 里 `ToKebabCase` 只剩一份。154 条全绿；独立 worktree 变异「删掉 `Create` 里的 traceId」→ **14 条红**（7 中间件 + 多条 API，比旺总自述的 10 条更多，因为映射表用例也断言 traceId），守护成立。

### F-06-3 · medium · 框架 401/403/415 会落成 `System.Unexpected` +「服务器内部错误。」

- **位置**：[`ProblemDetailsSetup.cs:36-41`](../../src/Pzd.Api/Errors/ProblemDetailsSetup.cs#L36-L41)、[`CommonErrors.cs:8-12`](../../src/Pzd.Api/Errors/CommonErrors.cs#L8-L12)
- **问题**：`UseStatusCodePages` 只特判 404/405，其余 4xx/5xx 一律 `CommonErrors.Unexpected`。一个没有 body 的 401（认证 Challenge 的典型形态）会被补成 `status=401` + `code=System.Unexpected` + `title=服务器内部错误。`；403 同理。
- **为什么错**：与架构 §8.3「未登录 401、无权限 403」语义冲突；步骤 3 一上 `[Authorize]`/登录/权限策略就暴露，前端按 `code` 分支会把 401 当系统错误。
- **修法**：`CommonErrors` 补 401/403 两条（415 可顺手），`switch (statusCode)` 按状态码选错误；补一条「测试端点返回空 body 401 → 对应 code 且带 traceId」的用例。老何已在票面把这条纳入本轮，定为 `Auth.Unauthorized` / `Auth.Forbidden`。
- **命名一致性（low）**：现有框架错误码是 `Http.NotFound` / `Http.MethodNotAllowed`，`Auth.*` 是第二种平台前缀；将来若真有 `Auth` 模块，`Auth.Unauthorized` 会与模块错误码混在一起。建议改用 `Http.Unauthorized` / `Http.Forbidden` 与既有前缀对齐，或在 foundation 里明确「`Http.*` = 框架层、`Auth.*` = 认证层」。两种都能接受，但要在实现前定死，别等代码写完再改。
- **复核（`abde749`，我实跑）**：**已修**。`CommonErrors` 加 `Auth.Unauthorized`/`Auth.Forbidden`，`UseStatusCodePages` 按 401/403 映射；测试端点加 `status401`/`status403`（`Results.StatusCode`，无 body），用例断言 status/content-type/code/type/title/`traceId`/`X-Trace-Id` 全对齐。156 条全绿；独立 worktree 变异「删掉 401 映射」→ **1 红**，守护成立。
- **文案与票面不一致（low）**：票面 `F-06-3 口径` 写的是 `"未认证或认证已失效。"` / `"没有权限执行该操作。"`，代码是 `"未登录或会话已过期。"` / `"没有权限访问该资源。"`。不影响行为，但票面既是契约就应对齐——建议老何把票面改成代码文案（或反之），别留两套。另外 401/403 的 `type` 精确值已由用例钉死（`https://pzd/errors/auth.unauthorized` / `.../auth.forbidden`），李总担心的「转两层」不会发生，且实现上只有一份 kebab。

### F-06-4 · low · `traceId` 与 `Activity.Id` 的一致性没有直接断言（与李总 low 相同）

- **位置**：[`ErrorMappingTests.cs:51-53`](../../tests/Pzd.Api.UnitTests/Errors/ErrorMappingTests.cs#L51-L53)、[`UnexpectedExceptionTests.cs:35-36`](../../tests/Pzd.Api.UnitTests/Errors/UnexpectedExceptionTests.cs#L35-L36)
- **问题**：只断言 `body.traceId == X-Trace-Id`。两个值同源于 `TraceIds.Current`，所以即使它退化成「永远返回 `TraceIdentifier`」，用例仍全绿——验收项 2 的「与 `Activity.Id` 一致」未被守护。李总独立得到同一结论。
- **修法**：加一条 `TraceIds.Current` 单测：手工 `Activity` 启动时返回 `Activity.Id`，未启动时回落到 `TraceIdentifier`。不需要 HTTP 接缝。

### F-06-5 · low · 提交信息称「修掉架构夹具命名空间被误写」，与 git 历史不符

- **位置**：`dee779d` 提交信息；[`ArchitectureTestAssembly.cs`](../../tests/Pzd.ArchitectureTests/ArchitectureTestAssembly.cs)
- **问题**：`git log --follow` 显示该文件自 `50ac4ec` 起命名空间一直是 `Pzd.SharedKernel.Domain`，`057f64a`、`dee779d` 都只做了追加，**没有任何一次提交把它写成 `Pzd.ArchitectureTests`**。而且反向用例断言的是「恰好 3 条」（`057f64a` 时是恰好 2 条），命名空间错了会直接红，不存在「空跑通过」。
- **为什么错**：审计与交接会引用一个不存在的历史缺陷；如果是本次编辑过程中自己引入又修掉的，应如实写成「过程中自查修正」。
- **修法**：更正报告/交接措辞即可，不改代码；若确有另一处夹具，请给出路径与提交 hash。
- **复核（`6e20aaf`）**：**已关闭**。旺总在提交说明里明确「与 git 历史不符，实际只新增 `FakeApiDependency`，属表述错误」，并按我的要求更正；不需要代码改动。

### F-06-6 · low · 404/405 用例没断言 `type`/`title`（与李总 low 相同）

- **位置**：[`ErrorMappingTests.cs:94-128`](../../tests/Pzd.Api.UnitTests/Errors/ErrorMappingTests.cs#L94-L128)
- **问题**：两条用例只断言 `code`/`traceId`；`type` 与 `title` 未断言。李总探针确认当前行为正确（`http.not-found`、405 的 `Allow` 头在），但将来 `ToKebabCase`/`CommonErrors` 改动不会被这两个用例抓住。
- **修法**：补 `type` 断言（`https://pzd/errors/http.not-found`、`https://pzd/errors/http.method-not-allowed`）与 `Allow` 头存在性断言。

---

## 4. 票面口径问题（归老何，不是旺总返工）

**AC4 的前半段「FluentValidation 失败 → 400」目前没有对应实现**：仓库无 FluentValidation 引用、无 Application 工程（`src/` 只有 SharedKernel/Infrastructure/Api），现有用例是手工构造 `Error`。即使 F-06-1 补上 camelCase 转换，「校验失败 → Validation Result」这条链路仍未被验证。建议二选一：

- **推荐**：票面改成「`errors` 字典透传 + key 统一 camelCase；FluentValidation 接入属步骤 3」，并在步骤 3 新开「Application 层 + FluentValidation pipeline」票；
- 或者本轮引入 Application 工程与 FluentValidation 做真实管线（明显超出本轮范围，不建议）。

无论选哪个，旺总的报告里都不要写成「FluentValidation 已验证」。

**票面已闭环（`48eb3f7` / `b71e6e4`）**：老何把三条收口项写进票 06，编号以本报告为准，并新增 [`tickets.md`](../specs/tickets.md) 「步骤 3 移交清单」（第 1 项即 FluentValidation 管道 + Application 层校验），验收口径与实现范围不再含糊。

---

## 5. 终审结论（`abde749`）

- **票 06 实现审计：通过。** 映射表、type/code、404/405、生产不泄漏、F7 三条违规成立；F-06-1（`3ff40ef`）、F-06-2（`6e20aaf`）、F-06-3（`abde749`）全部落地，156 条全绿。
- **我复核的变异**：errors 原样透传 → 1 红；`TraceIds` 去掉 `Activity` 分支 → 1 红；删 traceId → 14 红；删 401 映射 → 1 红。四条守护都不是空转。
- **剩余 low（不阻塞，建议随手）**：
  1. `ToCamelCaseKeys` 只认 `IDictionary<string, object?>` / `IDictionary<string, string[]>`，`ReadOnlyDictionary` 等形态会静默不转换（见 F-06-1 残余）；
  2. `Auth.*` 文案与票面「F-06-3 口径」不一致（见 F-06-3）；
  3. `6e20aaf` 提交信息/两处代码注释把 F-06-2 写成 F-06-1（编号以本报告为准），后续按内容/编号对齐即可。
- **关票动作**：李总按 `abde749` 冒烟 → 老何回填票 06 状态与文案 low → 老卫提交 `review-06.md`/`review-07.md`/两份测试报告。
- **口径**：AC4 的「FluentValidation 失败 → 400」已由老何改为「本轮只验证 `errors` 透传 + camelCase」，管道移交步骤 3（「步骤 3 移交清单」第 1 项）；报告与交接不得写成「管道已验证」。
