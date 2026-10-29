状态：进行中
主写入者：PZD-实现Bot-3
更新日期：2026-10-29 22:10 Asia/Shanghai
下一步：审计 4 项必修已返工（`e0cf96f`），等老登按新 SHA 复审
交付状态：待收尾
实施条件：已具备（01 `0f25d16` 于 2026-10-29 合入 main）

# 05 SharedKernel 共享内核

## 目标

提供 `Entity` / `AggregateRoot` / `ValueObject` / `DomainEvent` / `Result` / `Error` / `IRepository`，让业务模块不再重复定义基类。

## 需求依据

- [architecture.md](../../../architecture.md) §3.3、§2、§3.2
- [spec.md](../spec.md) §4「分层与领域」；本票没有独立 AC，出口在 13 号票 AC-01
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.1

## 范围与非范围

- 做：`src/Pzd.SharedKernel`：抽象基类、值对象基类（按值相等、不可变）、领域事件收集、`Result`/`Result<T>`/`Error`（错误码 `模块.实体.原因`）、`IRepository<T>` 接口。
- 不做：不引 SqlSugar、不引 ASP.NET Core、不引 DI 容器；不写业务实体。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.SharedKernel/**`、`tests/Pzd.SharedKernel.UnitTests/**`。不碰其他项目。

## 关键接口与依赖

- 前置票：01
- 对外契约：全部模块的唯一基类来源；`Result`/`Error` 的形状决定 09 号票的 ProblemDetails 映射，改动需通知 09。**合入前必须冻结 `Entity`/`AggregateRoot`/`ValueObject` 的公开签名（此后只增不改）**，06 号票要按这份签名并行开工，冻结后在群里公告。

## 步骤

1. 建基类与值对象，确保 Domain 无任何框架引用。
2. 实现 `Result`/`Error`，错误码格式非法时抛 `ArgumentException`（决定见下节 seam）。
3. 单测覆盖：值对象相等与不可变、`Result` 成功/失败分支、领域事件收集顺序。
4. 冻结基类公开签名并在群里公告。
5. `dotnet test` 贴输出与通过数。

## 测试 seam（已确认，未经确认不写测试）

S1 `ValueObject` 按值相等：两个相等的值对象相等且哈希一致，且不可变。抓到：把值相等写成引用相等；抓不到：领域规则合法性。
S2 `Result`/`Result<T>` 成功与失败分支，`Error` 错误码格式校验。抓到：失败分支丢 code、错误码不校验格式；抓不到：HTTP 映射（09 号票的事）。
S3 `AggregateRoot` 领域事件收集：顺序、去重、清空。抓到：事件乱序、重复分发；抓不到：08 的投递与幂等语义。
S4 依赖边界自证：`Pzd.SharedKernel` 的包引用不含 SqlSugar / ASP.NET Core。**只自证包引用，真正的分层守卫在 13 号票，S4 不替代它。**

**已定（2026-10-29）**：`Error` 的错误码格式非法时**抛 `ArgumentException`**，不返回失败的 `Result`。依据 §8.1「预期失败用 Result，意外才抛异常」——错误码是代码里写死的字面量，写错属于开发期 bug，早失败才能被单测直接断言。校验放在 `Error` 的构造/静态工厂里。

每个 seam 至少一条正例与一条反例；只测外部可观察行为，不测私有字段与内部实现。

## 验收标准

- [x] `Pzd.SharedKernel` 的依赖中不含 SqlSugar 与 ASP.NET Core（`DependencyBoundaryTests` 断言程序集引用）。
- [x] 值对象按值相等且不可变，有单测覆盖（相等/不等/异类型/运算符/无 public setter）。
- [x] `Result` 成功与失败两个分支各有单测（含 `Result<T>` 与空错误守卫）。
- [x] 错误码格式非法时**抛 `ArgumentException`**，单测断言异常类型且消息里含那个非法码（决定见上节）。
- [x] 证据贴出实际通过的用例数（>0），`0 tests` 不算通过（现为 `Total: 32, Failed: 0`，直跑 exe）。
- [x] 基类公开签名已冻结并在群里公告；审计指出 `Error` 公告与实现不一致后，公告已按实际形状修正并重新公告 06/09。

## 验证入口

```bash
dotnet test tests/Pzd.SharedKernel.UnitTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 冻结的公开签名（2026-10-29，此后只增不改）

06 号票按这份签名并行开工：

```csharp
public abstract class Entity
{
    public long Id { get; protected set; }
}

public abstract class AggregateRoot : Entity
{
    protected AggregateRoot();                               // 子类构造会调用；只读视图在这一步就建好
    public IReadOnlyList<IDomainEvent> DomainEvents { get; }  // 缓存的只读视图：反复访问返回同一实例，不新建包装
    public void Raise(IDomainEvent domainEvent);
    public void ClearDomainEvents();
}

public interface IDomainEvent { }

public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();
    public static bool operator ==(ValueObject? left, ValueObject? right);
    public static bool operator !=(ValueObject? left, ValueObject? right);
}

public sealed record Error
{
    // 参数名固定 code / message；「模块.实体.原因」格式非法时抛 ArgumentException
    public Error(string code, string message);
    public string Code { get; }
    public string Message { get; }
    // 契约限制（有意为之，06/09 按此实现）：没有 Deconstruct；不能用 with 改属性值（get-only，with { Code = … } 是 CS0200）；
    // 参数名固定 code/message，不能用 Code/Message 命名参数。
    // 为什么不改成 record 主构造：主构造的参数校验只在构造那一次生效，with 走复制构造、不重跑属性初始化器，
    // 校验写在属性初始化器里会被 with 绕过；要让 with 也受约束得把校验放进 init 访问器（属性不再纯只读、两条路径校验位置不同易漏）。
    // 判据是校验写在哪，不是 class 还是 record。
}

public class Result
{
    public Error? Error { get; }
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public static Result Success();
    public static Result Failure(Error error);
}

public sealed class Result<T> : Result
{
    public T Value { get; }                                  // 失败分支取值抛 InvalidOperationException
    public static Result<T> Success(T value);                // 引用类型的成功值允许为 null，骨架层不做非空强约定，调用方自行校验
    public static new Result<T> Failure(Error error);
}

public interface IRepository<TAggregate> where TAggregate : AggregateRoot
{
    Task<TAggregate?> FindAsync(long id, CancellationToken cancellationToken = default);
    void Add(TAggregate aggregate);
    void Remove(TAggregate aggregate);
}
```

**唯一预留的例外**：`Entity.Id` 现在是 `protected set`，赋值方是 Infrastructure 的 `EntityService`。若 06 发现 SqlSugar 无法通过反射写入受保护 setter，由 06 决定是否改成 `internal set` 并把变更回报本票；其余签名不动。`IRepository` 故意没有 `Save`——提交归 UoW（08 号票）。

**`Raise` 的去重语义**：按引用（`ReferenceEquals`）去重——同一个事件实例加两次只留一条，**值相同但不同实例会收集两条**。08 号票做投递幂等时按这条来，别指望这里去重。

## 证据（L1）

### R1 红灯（先于实现，编译期缺实现）

```text
$ dotnet build tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
TestTypes.cs(4,64): error CS0246: 未能找到类型或命名空间名"ValueObject"
TestTypes.cs(18,31): error CS0246: 未能找到类型或命名空间名"AggregateRoot"
TestTypes.cs(28,74): error CS0246: 未能找到类型或命名空间名"IDomainEvent"
    3 个错误
```

### R2 绿灯

```text
$ dotnet build tests/Pzd.SharedKernel.UnitTests/Pzd.SharedKernel.UnitTests.csproj
已成功生成。 0 个错误

$ ./Pzd.SharedKernel.UnitTests.exe            # 等价于 dotnet test tests/Pzd.SharedKernel.UnitTests
=== TEST EXECUTION SUMMARY ===
   Pzd.SharedKernel.UnitTests  Total: 33, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.115s
   # 本段是 R2 当时的 24；补测后为 33（见 R3/R4）
[exe exit=0]
```

覆盖 seam：S1（值对象相等/不等/异类型/运算符）、S2（`Result`/`Result<T>` 成功失败 + `Error` 非法码 7 组）、S3（事件顺序/去重/清空/空集合/空参数/状态只经方法变更）、S4（包引用无 SqlSugar 与 ASP.NET Core）。

### R3 补测（李总独立复核后，2026-10-29）

李总用变异验证抓到真缺口：删掉 `ValueObject.Equals` 里的 `other.GetType() != GetType()` 后，当时 24 个用例**仍全绿**——原用例比较的是 `Money` 与 `string`，走 `as ValueObject → null` 分支，从没进「两个不同值对象子类」的路径。

补测（只动测试文件）：

| 缺口 | 补的用例 |
|---|---|
| 分量相同但类型不同应不相等 | `MoneyTwin` 类型 + `分量相同但类型不同的值对象不相等` |
| AC2「不可变」零断言 | `值对象子类不暴露可写属性`（反射断言无 public setter） |
| `Result<T>.Failure(null)` 未覆盖 | `带值结果的失败也不能用空错误` |
| `Error` 只测空段、没测纯空白段 | `Identity. .NotFound`、`Identity.User. `、`   ` 三组 |

变异自证（自己压一遍，不只信补测用例能编译）：

```text
# 变体：删掉 other.GetType() != GetType()
Total: 30, Errors: 0, Failed: 1
[FAIL] Pzd.SharedKernel.UnitTests.ValueObjectTests.分量相同但类型不同的值对象不相等

# 还原后
Total: 30, Errors: 0, Failed: 0
$ git diff --stat src/        # 空输出，实现无残留改动
```

### R4 审计返工（老登 run-03 结论，2026-10-29）

四条必修全部处理：

| 项 | 处理 |
|---|---|
| high：`Error` 公告与实现不一致 | **改公告**为实际形状：显式构造 + get-only 属性，明确无 `Deconstruct`、不支持 `with`、参数名 `code`/`message`；理由写进签名块（校验不能被 `with` 绕过）。实现未动。 |
| medium：`DomainEvents` 可强转回 `List` 绕过封装 | 改为 `_domainEvents.AsReadOnly()`，签名不变；新增用例 `领域事件集合不能被调用方强转后增删` |
| medium：`ValueObject` 注释「顺序无关」与 `SequenceEqual` 相反 | 注释改为「按顺序逐项比较，两侧顺序必须一致」 |
| medium：票面缺交接四要素 | 验收勾选、任务映射、遗留、下一步本段一并补齐 |

**为什么 `Error` 不用 record 主构造**（实证，2026-10-29，三种写法各跑一遍）：record 主构造的参数校验只在构造那一次生效；`with` 走复制构造、不重跑属性初始化器，所以**校验写在属性初始化器里会被 `with` 绕过**（实测 `with { Code = "坏码" }` 得到 `坏码`）。要让 `with` 也受约束，校验必须放进 `init` 访问器（写法更绕，属性也不再是纯只读，且构造与 `with` 两条路径的校验位置不同，容易漏）。综合成本，本票选显式构造 + get-only：校验写一次，`with` 直接编译不过（CS0200）。

判据是**校验写在哪**，不是 class 还是 record——不要据此得出「record 天生不安全」的结论。

三条建议项也顺手处理：`状态只能通过方法变更` 改名 `改单号方法生效` 并新增 `聚合属性不对外暴露setter`（反射断言 `Number` 无 public setter）；`[InlineData("")]` 的恒真断言拆成独立的 `空错误码抛ArgumentException`；csproj 注释改引 §3.2/§5.2。

自压一次新改动的变异：

```text
# 变体 A：DomainEvents 改回 => _domainEvents;
Total: 32, Errors: 0, Failed: 1
[FAIL] Pzd.SharedKernel.UnitTests.AggregateRootTests.领域事件集合不能被调用方强转后增删

# 还原后
Total: 32, Errors: 0, Failed: 0
$ git diff --stat src/        # 除注释与 AsReadOnly 外无其他改动
```

### `dotnet test` 本体（环境性 `0 tests`，按票面四项要求）

① 错误原文与退出码：

```text
$ dotnet test tests/Pzd.SharedKernel.UnitTests --filter ValueObjectTests
System.UnauthorizedAccessException: Access to the path is denied.
   at System.IO.Pipes.NamedPipeClientStream.TryConnect(...)
测试运行摘要: 失败!   总计: 0
```

② 替代执行方式：直接运行测试 exe，`Total: 33, Failed: 0`，退出码 0（见 R2）。
③ **`dotnet test` 本体未运行**：沙箱拒绝 MTP 命名管道，需在无管道限制的环境补跑一次。
④ 剩余风险：`dotnet test --filter` 这条验证入口在本机无效，只能用 exe 等价替代；真正的门禁由 04 号票的 CI 承担。

## 遗留

- `dotnet test` 本体在本机未运行（MTP 命名管道被沙箱拒绝，`总计: 0`、退出码 `-532462766`）；已用直跑 exe 等价替代，需在无管道限制环境或 04 的 CI 补跑一次。
- 「不可变」断言只守 `Money` 这个示例类型，别的值对象子类偷偷加 setter 抓不到——这层归 13 号票的分层守卫。
- S4 只做包引用的正向自证，没有独立反例；真正的分层守卫与反例在 13 号票，S4 不替代它。
- `Error` 无 `Deconstruct`、不能用 `with` 改属性值：有意为之，校验必须跑在构造里；完整理由与三种写法的实测见 [决策笔记](../../../../.agents/notes/implemented/architecture/2026-10-29-error-record-contract.md)。

## 恢复方式

删除新增项目；无数据影响。

## 任务映射

- worktree：`.worktree/05/`
- 分支：`feat/05-shared-kernel`
- 提交：`e0cf96fdd01fdb43dce6a3cd547d5b1540bf4b8e`（审计返工）；此前实现 `c43c3ea07e53a8df6217597b80b02f57fd4c1383`、补测 `00c7a7ba44315bbb8c7411e565cf621d56a45e24`、措辞修正 `567531000c3e5f3e5eb667f6d1da923fda3f5913`、签名块闭环见本分支顶端
