# 测试报告：票 06（异常处理 + Result→HTTP）

> 测试：李总｜日期：2026-10-27｜票面：[`docs/specs/tickets.md`](../specs/tickets.md) 票 06（`# 06:` 起）
> 被测提交：`dee779d`（`feat(api): 票 06 异常处理 + Result→HTTP（132 条全绿）`）
> 结论：**7 条验收中 6 条通过；AC4（`errors` 键 camelCase）行为未达成，现有用例用已 camelCase 的入参，测不出转换缺失**。建议返工后再进审计（由审计定级）

## 1. 基线（干净树 `dee779d`）

- `git status --porcelain` → 空；`git log --oneline -1` → `dee779d`。
- `dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误。
- 逐工程 `dotnet run`（本工作区 Low IL 下 `dotnet test` 不可用，见 test-report-01-04 §9）：
  - `Pzd.SharedKernel.UnitTests` Total: 35，Failed 0
  - `Pzd.Infrastructure.UnitTests` Total: 54，Failed 0
  - `Pzd.Api.UnitTests` Total: 26，Failed 0
  - `Pzd.ArchitectureTests` Total: 16，Failed 0
  - `Pzd.IntegrationTests` Total: 1，Failed 0
  - 合计 **132 条全绿**，与旺总自述一致（116 → 132，新增 16 条）。

## 2. 票面验收项 → 证据

| # | 验收项 | 用例 / 证据 | 结果 |
|---|---|---|---|
| 1 | 映射全表 HTTP 逐条：400（含 `errors`）、401、403、404、409、422、426、503 | `ErrorMappingTests.Result_maps_to_the_expected_status_and_problem_details` × 8（Theory 表，逐条断言 status/`code`/`type`/`title`/`traceId` 与 `X-Trace-Id`） | 通过 |
| 2 | `type=https://pzd/errors/{kebab-code}`；`code`=`Error.Code`；`traceId` 与响应头/`Activity.Id` 一致 | 同上 8 条断言 `type`（含多段码 `identity.user.name-duplicated`）与 `code`；`traceId` 断言与 `X-Trace-Id` 相等 | 通过（`Activity.Id` 分支未覆盖，见 §5） |
| 3 | 未知异常 → 500，无堆栈无类型名，含 `traceId`；开发/生产差异有测试 | `Unhandled_exception_returns_500_without_leaking_details_in_production`（HTTP 接缝，断言 body 不含 `boom`/类型名/` at `/`stack`，无 `exceptionType`）+ 处理器级 3 条（生产/开发差异，开发仅 `exceptionType`） | 通过 |
| 4 | FluentValidation 失败 → 400，`errors` 键 camelCase | `Validation_failure_carries_the_errors_dictionary_with_camel_case_keys` | **未达成**，见 F-06-1 |
| 5 | 404/405 框架路径同样带 `code`/`traceId` | `Framework_404_carries_code_and_traceId`、`Framework_405_carries_code_and_traceId`（经 `UseStatusCodePages`） | 通过；探针另确认 405 保留 `Allow`、`type=https://pzd/errors/http.method-not-allowed`，404 `type=…/http.not-found` |
| 6 | 不使用「永远 200 + {code,msg,data}」包装 | `Successful_non_generic_result_is_204_without_body`（204 且空 body）+ `Successful_generic_result_is_200_with_the_value`（body 精确等于 `"pong"`，无包装字段） | 通过 |
| 7 | F7 补测：`FakeApiDependency` → 反向用例断言恰好 3 条违规 | `ArchitectureTestAssembly.FakeApiDependency` 引用 `Pzd.Api.Errors.GlobalExceptionHandler`；`ReverseCaseTests` 用生产规则断言**恰好** 3 个类型名（Fake/FakeInfrastructureDependency/FakeApiDependency） | 通过（变异证实，见 §3-G） |

## 3. 变异复测（`git archive dee779d` 临时副本，工作区未动）

| # | 变异 | 期望 | 实际 |
|---|---|---|---|
| A | `ErrorType.ClientTooOld` 映射改为 400 | 426 用例红 | `ErrorMappingTests…(clienttooold)` **1 failed**，exit 1 |
| B | `GlobalExceptionHandler` 的 `IsDevelopment()` 改为恒真 | 生产不泄漏用例红 | `Unhandled_exception…production` + `Production_environment…` **2 failed**，exit 1 |
| C | `ToKebabCase` 退化为整体小写（不再按大写拆段） | `type` 断言红 | 6 failed（多段/含大写段全部命中），exit 1 |
| D | `ProblemDetailsCustomization` 不再透传 `errors` | validation 用例红 | `Validation_failure_carries…` **1 failed**，exit 1 |
| E | 框架 405 映射改为 `Http.NotFound` | 405 用例红 | `Framework_405_carries_code_and_traceId` **1 failed**，exit 1 |
| G | 去掉 `FakeApiDependency` 对 `Pzd.Api` 的引用 | F7 反向用例红 | `ReverseCaseTests…` **1 failed**，exit 1；说明 3 条违规确实由该类型贡献 |

六个变异全部被现有用例抓住，映射表与 F7 断言不是空转。

## 4. 缺陷

### errors camelCase · medium · `errors` 字典的 key 不做转换，AC4 实际未达成

> 复测状态：**已在 `3ff40ef` 修复并通过冒烟**（见 §7）；本条保留为问题记录。
> 编号说明：本条目在我的首测里编号 F-06-1；老登 review-06 改编号后，F-06-1 指租户 `traceId`。按老何的口径统一用内容称呼，避免对错条目。

**期望**：`errors` 出口统一 camelCase——PascalCase 键（FluentValidation 属性名的形态）进来，响应里是 `userName`。（本轮尚无 FluentValidation 管道，见票面「FluentValidation 管线落步骤 3」。）

**实际**：`ProblemDetailsCustomization.Create` 把 `Error.Metadata["errors"]` 原样塞进扩展字段（`ProblemDetailsCustomization.cs:33-36`），`PzdProblemResult.ExecuteAsync` 用 `WriteAsJsonAsync(problem, options: null, …)` 序列化；该路径的 JSON 选项不应用 `DictionaryKeyPolicy`，PascalCase 键**原样输出**。

**复现**（已实测，副本内加探针后运行）：

```powershell
# 在 TestEndpoints 的 validation scenario 中把 key 从 "userName" 改为 "UserName"（模拟 FluentValidation 的 PascalCase 属性名）
dotnet run --project tests/Pzd.Api.UnitTests/Pzd.Api.UnitTests.csproj
```

响应体（节选）：

```json
{"type":"https://pzd/errors/identity.user.name-duplicated","title":"用户名已存在","status":400,
 "code":"Identity.User.NameDuplicated","traceId":"…","errors":{"UserName":["用户名已存在"]}}
```

即 `UserName` 未转成 `userName`。现有用例 `Validation_failure_carries_the_errors_dictionary_with_camel_case_keys` 的入参 key 本身就是 `"userName"`（`TestEndpoints.cs:62`），因此**无论是否做转换都会绿**——属于「用例自己重拼期望输入」一类（票 04 的 F1 同类），不能证明 AC4。

**影响**：票 03/02 真接 FluentValidation 后，前端拿到的是 PascalCase 字段名，与契约不符；届时才发现需要返工错误输出层。

**建议修法**（二选一，改动都很小）：
1. 在 `Create` 里把 `errors` 的键统一转成 camelCase（`JsonNamingPolicy.CamelCase.ConvertName`）后再放入扩展；
2. 或给 `PzdProblemResult` / 全局 JSON 选项显式设置 `DictionaryKeyPolicy = JsonNamingPolicy.CamelCase`（与 MVC 行为对齐）。

**必须同时补用例**：入参用 PascalCase（如 `"UserName"`），断言响应里是 `"userName"` 且不存在 `"UserName"`。探针原文可用于照搬：

```csharp
Assert.True(errors.TryGetProperty("userName", out _), body);
Assert.False(errors.TryGetProperty("UserName", out _), body);
```

## 5. 不阻塞的缺口（low）

- **traceId 的 `Activity.Id` 分支未被用例覆盖**：`TraceIds.Current` 实现为 `Activity.Current?.Id ?? context.TraceIdentifier`，但测试只断言 body 与响应头一致（两者同源，必然一致）。TestServer 请求里 `Activity.Current` 通常为 null，实际走的是 `TraceIdentifier`。当前一致性有保证，只要将来在 body 之外单独取 `Activity.Id` 比对就会缺证据；补一条「起 `Activity` 后断言 traceId == `Activity.Current.Id`」即可。
- **`MapStatusCode` 未定义 `ErrorType` 的兜底（500）无用例**：当前 `_ => 500`，没有枚举外的输入，属防御性分支，可留到后续票。
- 404/405 用例只断言 `code`/`traceId`，未断言 `type`/`title`；本次探针确认行为正确（`http.not-found` / `http.method-not-allowed`、`Allow` 头保留），可顺手补进用例。

## 6. 结论

- 基线、映射全表、异常处理、框架路径、无包装、F7 补测均按 `dee779d` 独立复核通过；六个变异全部被抓住，旺总自述的验证可复现。
- **AC4 建议返工**：errors camelCase 是行为缺口而非措辞问题，补转换 + 一条 PascalCase 入参用例即可；改完我按新 hash 冒烟，再交审计。
- 其余项可进审计；测试侧已给出失败用例与复现命令，不阻塞其它票序（票 07 可并行）。
- 老登 review-06 另有租户中间件 `traceId`（老登口径 F-06-1）与框架裸 401/403（F-06-3）两条必修/顺带项，复审时我会一并验证：票 06 返工后按新 hash 跑 400/403 中间件响应的 `traceId`/`X-Trace-Id`、`errors` PascalCase 入参、框架 401/403 映射 `Auth.Unauthorized`/`Auth.Forbidden`。

## 7. 返工冒烟：`3ff40ef`（errors camelCase）

> `git worktree` 干净副本 @ `3ff40ef`（只含 `ProblemDetailsCustomization.cs` + `ErrorMappingTests.cs` 两个文件）。

- 基线：`dotnet build Pzd.slnx -warnaserror --no-incremental` → 0 警告 0 错误；35 + 71 + 28 + 19 + 1 = **154 条全绿**。
- 用例已按「不再喂期望输入」重写：`TestEndpoints` 的 validation scenario 传 `"UserName"`，用例断言响应里 `userName` 存在、`UserName` 不存在。
- 变异：把 `problem.Extensions["errors"] = ToCamelCaseKeys(errors)` 改回原样透传 → `Validation_failure_turns_PascalCase_keys_into_camel_case` **1 failed**，守护成立。
- 顺手补的两条 low 也确认有效：404/405 断言 `type`/`title`；`TraceIds` 两条单元用例分别覆盖「有 `Activity.Current`」与「回退 `TraceIdentifier`」。
- **结论：errors camelCase（老登口径 F-06-1）冒烟通过，可关。**

### low（新观察，不阻塞）：转换只认两种字典值类型

`ToCamelCaseKeys` 只匹配 `IDictionary<string, object?>` 与 `IDictionary<string, string[]>`；把探针里的 errors 值换成 `Dictionary<string, List<string>>` 后，键又原样透传（同名用例 1 failed）。FluentValidation 适配层若产出 `List<string>` 值或只实现 `IReadOnlyDictionary` 的包装类型就会漏转。建议改为在序列化层设 `DictionaryKeyPolicy = JsonNamingPolicy.CamelCase`（递归生效、与形状无关），或在 `Create` 里用非泛型 `IDictionary` 归一化；步骤 3 真接 FluentValidation 时需要顺手确认产出形状。

### 仍未进入本提交

`3ff40ef` 只动两文件，**F-06-2（租户 400/403 补 `traceId`/`X-Trace-Id`、kebab 收一份）与 F-06-3（框架裸 401/403 → `Auth.Unauthorized`/`Auth.Forbidden`）尚未提交**，工作区里是未落盘改动。这两项验完、按新 hash 冒烟后才能进审计。

## 8. 返工冒烟：`6e20aaf`（租户 traceId + ProblemDetails 收一份）

> 干净副本 @ `6e20aaf`（8 文件：`ProblemDetailsCustomization.cs` 从 Api 移到 Infrastructure 并扩成 `ProblemDetailsWriter.cs`，中间件、`ResultHttpExtensions`、`GlobalExceptionHandler`、`ProblemDetailsSetup` 改为共用）。

- 基线：build 0 警告 0 错误；35 + 71 + 28 + 19 + 1 = **154 条全绿**。
- 单一实现：`grep -rn "ToKebabCase" src/` 只剩 `src/Pzd.Infrastructure/Errors/ProblemDetailsWriter.cs` 一份（Api 删除原 `ProblemDetailsCustomization.cs`，中间件删掉自己的转换）。
- 租户 400/403 断言已补齐（不再只靠手工拼 body）：4 条中间件用例 + 3 条 API 接缝用例断言 `problem.TraceId` 非空且 **等于 `X-Trace-Id` 响应头**；`type` 精确为 `https://pzd/errors/tenant.not-found` / `tenant.disabled` / `tenant.expired`。
- 变异（副本内，已还原）：
  - `WriteAsync` 不写 `X-Trace-Id` 头 → 中间件 **4 failed**（400/403/过期边界），API 接缝 **9 failed**（含 3 条租户用例与映射全表）；
  - `ToKebabCase` 不按 `.` 分段 → **4 failed**，其中含专门的错误码 → kebab URL 映射用例。
- **结论：F-06-2（租户 traceId + kebab 收一份）冒烟通过，可关。**

### F-06-3 状态

老何已在 `74b0145` 定口径（只动空 body 的状态码路径，404/405 不动）；截至本次冒烟，**`401/403 → Auth.Unauthorized/Auth.Forbidden` 的实现与用例仍在工作区、未提交**。验的时候我会按老何的口径断言 `type` 精确为 `https://pzd/errors/auth.unauthorized` / `auth.forbidden`（单次 kebab，不再二次转换），并确认 404/405 仍是 `Http.NotFound` / `Http.MethodNotAllowed`。

## 9. 返工冒烟：`abde749`（框架裸 401/403 → Auth.*）

> 干净副本 @ `abde749`（含 3ff40ef、6e20aaf、abde749 三次返工；`abde749` 本身只改 `CommonErrors` + `ProblemDetailsSetup` + 用例/测试端点）。

- 基线：build 0 警告 0 错误；35 + 71 + 30 + 19 + 1 = **156 条全绿**（Api 28 → 30，新增 401/403 两条 Theory 用例）。
- 行为：仅 `UseStatusCodePages`（空 body 状态码路径）新增 `401 → Auth.Unauthorized`、`403 → Auth.Forbidden`；404/405 仍是 `Http.NotFound` / `Http.MethodNotAllowed`，与老何口径一致。
- 用例：`Framework_401_and_403_are_mapped_to_auth_problem_details` 逐条断言状态码、`application/problem+json`、`code`、`type`（精确 `https://pzd/errors/auth.unauthorized` / `auth.forbidden`，单次 kebab）、`title` 非空、body `traceId` 与 `X-Trace-Id` 相等。
- 变异（副本内，已还原）：
  - 去掉 401 映射 → `status401` **1 failed**；
  - 403 映射改回 `CommonErrors.Unexpected` → `status403` **1 failed**。

## 10. 票 06 三项返工冒烟总表（李总）

| 项 | 提交 | 基线 | 关键变异 | 结论 |
|---|---|---|---|---|
| F-06-1 errors camelCase | `3ff40ef` | 154 绿 | 回退原样透传 → 1 红 | 通过 |
| F-06-2 租户 400/403 traceId + kebab 收一份 | `6e20aaf` | 154 绿 | 不写 `X-Trace-Id` → 4+9 红；kebab 不分段 → 4 红 | 通过 |
| F-06-3 框架裸 401/403 → Auth.* | `abde749` | 156 绿 | 去掉 401/403 映射 → 各 1 红 | 通过 |

- 累积态 `abde749`：build 0/0、156 条全绿；`src`/`tests` 干净。
- **测试侧结论：票 06 三项返工全部冒烟通过，可进审计**（另有 low 观察：`ToCamelCaseKeys` 只认两种字典值类型，见 §7；不阻塞，建议步骤 3 接 FluentValidation 时确认产出形状）。
- 票 07 的 F-07-1/F-07-2（代码检查可绕过、`DomainEventDispatcher` 零覆盖）在 `74b0145` 已列为必修，等旺总返工后我再按新 hash 复测。
