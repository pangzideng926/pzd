状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 01 合入后认领
交付状态：待收尾
实施条件：待满足：01 合入（前置票）

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

- [ ] `Pzd.SharedKernel` 的依赖中不含 SqlSugar 与 ASP.NET Core。
- [ ] 值对象按值相等且不可变，有单测覆盖。
- [ ] `Result` 成功与失败两个分支各有单测。
- [ ] 错误码格式非法时**抛 `ArgumentException`**，单测断言异常类型且消息里含那个非法码（决定见上节）。
- [ ] 证据贴出实际通过的用例数（>0），`0 tests` 不算通过。
- [ ] 基类公开签名已冻结并在群里公告。

## 验证入口

```bash
dotnet test tests/Pzd.SharedKernel.UnitTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除新增项目；无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/05/`）
- 分支：实施时填写（`feat/05-shared-kernel`）
- 提交：实施时填写（完整 SHA）
