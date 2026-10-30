# Agent Note: Error 用显式构造而不是 record 主构造

Status: implemented

## Problem

领域错误对象要在构造时校验错误码格式（「模块.实体.原因」三段非空）：错误码是代码里写死的字面量，写错属于开发期 bug，必须早失败而不是变成一条运行期失败结果。它同时要按值相等，才配得上 record 语义。

这两件事在 C# record 里有个坑：主构造上的参数校验只在构造那一次生效。

## Decision

`Pzd.SharedKernel.Error` 是 `public sealed record Error`，但用**显式构造函数 + get-only 属性**，没有主构造：

```csharp
public Error(string code, string message);   // 格式非法抛 ArgumentException
public string Code { get; }
public string Message { get; }
```

对依赖它的 06、09 号票，这是契约：

- 没有 `Deconstruct`（CS8129）
- 不能用 `with` 改属性值（get-only，`with { Code = … }` 是 CS0200）
- 参数名固定 `code` / `message`，不能写 `new Error(Code: …, Message: …)`（CS1739）
- 值相等与 `ToString` 仍由 record 语义保留

## Alternatives considered

- **record 主构造 + 属性初始化器校验** — 最强理由：写法最短，`new Error(…)` 一个表达式。没选，是因为 `with` 走复制构造、不重跑属性初始化器，实测 `v1 with { Code = "坏码" }` 直接得到 `坏码`，校验被绕过。
- **record 主构造 + init 访问器校验** — 最强理由：`with` 会重新调用 init 访问器，实测拦得住（报「错误码格式非法」）。没选，是因为属性不再是纯只读，且构造与 `with` 两条路径的校验位置不同，后续维护容易只改一处。

## Consequences

- 判据是**校验写在哪**，不是 class 还是 record：写属性初始化器 → `with` 绕过；写 init 访问器 → 拦得住；get-only → `with` 编译不过。不要据此得出「record 天生不安全」。
- 将来真需要「可 `with` 且受校验约束」的类型时，把校验放进 init 访问器，并在测试里同时覆盖构造与 `with` 两条路径。
- 契约原文写在 [05 号票](../../../../docs/work/cross-cutting/tickets/05-shared-kernel.md) 的冻结签名块，06/09 按那份实现。

## Evidence

2026-10-29 用一次性工程实测三种写法（主构造 + 属性初始化器校验 / 主构造 + init 访问器校验 / 纯主构造），结果见上面。三方独立复现：主写入者、测试侧、审计侧各跑一遍，结论一致。复核记录见 [05 复审结论](../../../../.ai/验证/05/2026-10-29/证据/run-06-审计复审/结论.md)。
