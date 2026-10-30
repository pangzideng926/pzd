状态：已完成
主写入者：PZD-实现Bot-3
更新日期：2026-10-30 09:32 Asia/Shanghai
下一步：已并入 main（合并提交 `869e745`；代码对象 `8c4033b`、最终候选 `698ea6b`，老登重审通过；分支顶端 `511139d` 仅文档差异）。合并结果复跑：`dotnet build` 0 警告 0 错误、`Pzd.IntegrationTests.exe` 走外部真库 → `Total: 8, Errors: 0, Failed: 0`、退出 0
交付状态：已交付（容器分支本机未运行、`dotnet test` 本体仍受沙箱限制，见遗留）
实施条件：已具备（01 `0f25d16` 于 2026-10-29 合入 main）；本机容器不可用，见「环境与替代方案」

# 36 集成测试库 fixture（真 MySQL）

## 目标

给所有集成测试一个能连真库的载体，避免每张票各自造一套 fixture，或退化成 InMemory 假通过。

## 需求依据

- [architecture.md](../../../architecture.md) §16.1
- [spec.md](../spec.md) AC-01、AC-02（测试载体，无单独 AC）
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) 补票：M2 的前置基础设施，§18 无对应步骤

## 范围与非范围

- 做：`tests/Pzd.IntegrationTests` 工程与 fixture：**只引 xUnit + 数据库驱动 / Testcontainers，不引任何 `Pzd.*` 项目**；**两种库来源，容器优先、外部真库回落**——①容器可用时按固定 tag `mysql:8.4` 起库；②容器不可用时回落外部真库（**本机实测无 Docker / Podman，只有 MySQL80 `8.0.46` 跑在 3306**），连接串与凭据从环境变量取、不入库、不写进仓库；**用例隔离方式定为「每用例独立库」**，不用事务回滚。
- 不做：不写任何业务断言（06 及后续票写）；不覆盖前端 E2E（35 号票）；不做性能测试；不引任何 `Pzd.*` 项目（引了就退回，见 AC）。

## 责任文件范围（认领时在群里先说清）

新建 `tests/Pzd.IntegrationTests/**`（工程与 `Fixtures/**`）。不碰 `src/`。

返工后该目录下的文件：`Fixtures/MySqlServerFixture.cs`、`Fixtures/TestDatabase.cs`、`AssemblyFixtures.cs`（程序集级 fixture 注册）、`SmokeTests.cs`、`FixtureGuardTests.cs`（删库护栏）、`SourceSelectionTests.cs`（来源优先级与不可用指引）。

## 关键接口与依赖

- 前置票：01
- 对外契约：后续所有集成测试（06–32）复用此 fixture；**隔离语义为「每用例独立库」**，06 的真提交、08 的一次 Command 一次提交与 Outbox 回滚语义都建立在这个前提上，不允许改成事务回滚。MySQL 版本以本票固定的 tag 为准，03 出来后比对。
- **程序集级 fixture 共享的是实例，不是库**（老何 2026-10-30 定）：一个测试程序集只起一个 MySQL（容器模式下也只起一个容器），但每个用例仍必须自己 `CreateDatabaseAsync` 拿独立库。谁图快让整个程序集共用一个库，隔离就破了——08 的「一次 Command 一次提交」会立刻测不出来。

## 步骤

1. 建 xUnit 工程：**只引 xUnit + 数据库驱动 / Testcontainers，不引任何 `Pzd.*` 项目**，这样 36 只依赖 01，可与 05 真正并行。
2. 实现 fixture：容器可用 → 按 `mysql:8.4` 起库；容器不可用 → 连外部真库（本机 `8.0.46`）。两种来源都必须每用例建独立库、用完销毁、失败不留残留；来源判定写在 fixture 里，两者都不可用时给出明确错误，**不许静默跳过用例**。
3. 写一个自证的冒烟测试（类名 `SmokeTests`：建表 → 插入 → 查询）。
4. 贴命令、通过数与关键输出行。

## 测试 seam（已确认，未经确认不写测试）

S1 fixture 对外只暴露「建一个独立库、给出连接串、用完销毁」。抓到：容器能起、有建库权限、重复运行不撞库、失败后无残留库；抓不到：任何业务表语义。
S2 `SmokeTests` 走一次建表 → 插入 → 查询。抓到：驱动、连接串、`utf8mb4` 真的通；抓不到：并发与事务。

本票只新增这两个 seam，业务表语义由 06 及后续票在 fixture 上追加。

## 验收标准

判定一律按本票「假绿防线」：贴实际通过数、`-class` 字面与类名一致、`0 tests` 退出码 0 按失败算。返工后全量 8 条通过（3 条冒烟 + 2 条护栏 + 3 条来源判定）。

- [x] `dotnet test tests/Pzd.IntegrationTests --filter SmokeTests` 连真 MySQL 完成建表、插入、查询，通过数 >0。
      —— 替代执行方式实跑 `Total: 8, Errors: 0, Failed: 0` 退出 0（见 R2/R2b）；`dotnet test` **本体未运行**（沙箱拒命名管道，`总计: 0`），按「环境性 0 tests」四项记录在 R1，需在无管道限制环境补跑一次。
- [x] 工程不引用任何 `Pzd.*` 项目（`dotnet list reference` 输出为空可证）。
- [x] 每个用例独立库，重复运行结果一致，失败后无残留库。
      —— 跑完 `SHOW DATABASES LIKE 'pzd\_test\_%'` 无输出行；护栏用例另证「释放句柄不会误删非测试库」。
- [ ] 容器可用时按 `mysql:8.4` 起库；容器不可用时回落外部真库并成功跑通冒烟。两种来源各贴一次实际输出；本机做不到的那一种写「未运行＋原因」，不算通过。
      —— **未运行（容器分支）**：本机无 Docker，只有外部真库分支有实际输出（R2）。来源优先级本身已用纯函数用例钉死（`容器可用时即使配了外部连接串也走容器`），但**「容器真的起得来」本机无法验证**，故本条不勾。
- [ ] 回落外部真库时，实测版本（本机 `8.0.46`）与 `mysql:8.4` 的差异写进本票遗留，并在有容器的环境复核一次（未复核前不算版本一致性已验证）。
      —— 差异已写进遗留；**有容器环境复核未运行**，本条不勾。
- [x] 不存在 InMemory 替身路径，也没有用事务回滚冒充隔离（审查确认）。
      —— 隔离靠每用例独立库 + 句柄释放 `DROP DATABASE`，无事务回滚包装；来源只有「容器 / 外部真库」两种，没有第三种替身。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter SmokeTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 环境与替代方案（spec A1 落地）

本机实测（2026-10-29 21:40）：**无 Docker、无 Podman、无 colima**（都不在 PATH，Testcontainers 连 `npipe://./pipe/docker_engine` 超时）；
有 **MySQL80 `8.0.46`** 服务跑在 `127.0.0.1:3306`（服务 Running、端口可连），但 `root` 无密码登录被拒。

fixture 因此实现两种来源，判定集中在 `MySqlServerFixture.ChooseSource`（纯函数，可被用例钉住）：

1. 容器可用 → 按票面固定 tag `mysql:8.4` 起库（**优先**，AC-04）；
2. 容器不可用 → 回落外部真库（本机为 `8.0.46`）；
3. 两者都不可用 → 抛 `InvalidOperationException`，外层是中文指引（说清两条出路），内层才是 Testcontainers 的英文原因。

**测试类 fixture 形态（06–32 一律照此写）**：程序集级 `[assembly: AssemblyFixture(typeof(MySqlServerFixture))]`，注册在 `tests/Pzd.IntegrationTests/AssemblyFixtures.cs`，测试类构造注入即可，**不要用 `IClassFixture`**——它是每个测试类一个实例，容器模式下等于每个类各起一个 MySQL 容器，几十个类会线性放大。已确认：张一一（admin，2026-10-30，理由是比 collection fixture 少一层排序问题）、老何（2026-10-30，并补「共享实例不等于共享库」）。

外部连接串只从两处读，都不入库：

- 环境变量 `PZD_TEST_MYSQL_CONNECTION`
- 仓库根 `.local/mysql-test-connection.txt`（`/.local/` 已加进 `.gitignore`）

格式：`Server=127.0.0.1;Port=3306;User ID=<可建库账号>;Password=<口令>;`

**剩余风险**

- 本机只有 `8.0.46`，`mysql:8.4` 分支未在本机运行过；按 AC 要求，有容器的环境需复核一次，复核前版本一致性不算已验证。
- **无 Docker 的机器每次跑测试要先付约 10 秒容器探测**（`Build()` 连 `npipe://./pipe/docker_engine` 超时后才回落外部库）。这是容器优先的代价，实测 5 条用例 0.3s → 8 条用例 10.4s；若以后觉得慢，先改 AC-04 再说，别在代码里偷偷反转优先级。
- 外部真库是共享实例：用例库名固定 `pzd_test_` + GUID，句柄释放即 `DROP DATABASE`，失败路径同样清理；但仍需专用测试账号，不用业务账号。`pzd_test` 已按 spec 收紧为只有 `pzd_test_%` 权限（全局 CREATE/DROP 已收回），护栏 + 权限两道防线并存，不互相替代。

**本机其它依赖**（不属于本票 fixture 范围，供 03 等票参考）：Redis 由 **Memurai `8.2.10`** 提供，服务 `Memurai` 为 Running、`127.0.0.1:6379` 实测可连。36 的 fixture 只负责 MySQL 载体，不代理 Redis；需要 Redis 的票自行接入。

## 证据（进行中）

### R1 红灯：本机两种来源都不可用（预期失败，不是缺陷）

编译：

```text
$ dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
Pzd.IntegrationTests -> ...\bin\Debug\net10.0\Pzd.IntegrationTests.dll
已成功生成。 0 个警告 0 个错误
```

直接运行测试 exe（等价于 `--filter SmokeTests`，规避 MTP 管道限制）：

```text
$ ./Pzd.IntegrationTests.exe -class "Pzd.IntegrationTests.SmokeTests"
DotNet.Testcontainers.Builders.DockerUnavailableException : Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'.
=== TEST EXECUTION SUMMARY ===
   Pzd.IntegrationTests  Total: 3, Errors: 0, Failed: 3, Skipped: 0, Not Run: 0
[exe exit=1]
```

`dotnet test` 本体（按票面「环境性 0 tests」四项要求记录）：

```text
$ dotnet test tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj --filter SmokeTests
System.UnauthorizedAccessException: Access to the path is denied.
   at System.IO.Pipes.NamedPipeClientStream.TryConnect(...)
测试运行摘要: 失败!   总计: 0
```

**`dotnet test` 本体未运行**：沙箱拒绝 MTP 命名管道（`总计: 0` 按未运行算），需在无管道限制的环境补跑一次。

### R2 绿灯：外部真库 `8.0.46`（回落路径，容器本机不可用）

来源：维护者建的 `pzd_test@localhost` 账号，连接串写在 `E:\pzd\.worktree\36\.local\mysql-test-connection.txt`（被 `.gitignore` 挡住，不进版本库）。`PZD_TEST_MYSQL_CONNECTION` 环境变量本机未设，实际走的是本地文件回落。

编译：

```text
$ dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj
Pzd.IntegrationTests -> E:\pzd\.worktree\36\tests\Pzd.IntegrationTests\bin\Debug\net10.0\Pzd.IntegrationTests.dll
已成功生成。 0 个警告 0 个错误
```

直接运行测试 exe（等价于 `--filter SmokeTests`）：

```text
$ ./Pzd.IntegrationTests.exe -class "Pzd.IntegrationTests.SmokeTests"
xUnit.net v3 In-Process Runner v4.0.2+0c7aa4991f (64-bit .NET 10.0.13)
  Discovering: Pzd.IntegrationTests
  Discovered:  Pzd.IntegrationTests
  Finished:    Pzd.IntegrationTests
=== TEST EXECUTION SUMMARY ===
   Pzd.IntegrationTests  Total: 3, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 0.371s
[exe exit=0]
```

通过数 `3 > 0`，类名与 `-class` 字面一致（`Pzd.IntegrationTests.SmokeTests`），不是 0 用例假绿。

**证明这 3 条真的连了外部真库，不是空转**——两条旁证：

1. 跑完服务端无残留库，且服务端版本就是 8.0.46（fixture 建的库用完都删干净了）：

   ```text
   $ mysql -h 127.0.0.1 -u pzd_test -N -e "SHOW DATABASES LIKE 'pzd\_test\_%'; SELECT VERSION();"
   8.0.46
   ```

   `SHOW DATABASES` 无输出行 = 没有任何 `pzd_test_*` 残库。

2. 反向验证：把 `PZD_TEST_MYSQL_CONNECTION` 故意写成错口令，必须红在外部连接上：

   ```text
   $ PZD_TEST_MYSQL_CONNECTION="Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=definitely-wrong-pw;" \
       ./Pzd.IntegrationTests.exe -class "Pzd.IntegrationTests.SmokeTests" -method "...库默认字符集是utf8mb4"
   Class fixture type 'Pzd.IntegrationTests.Fixtures.MySqlServerFixture' threw in InitializeAsync
   ---- MySqlConnector.MySqlException : Access denied for user 'pzd_test'@'localhost' (using password: YES)
   === TEST EXECUTION SUMMARY ===
      Pzd.IntegrationTests  Total: 1, Errors: 0, Failed: 1, Skipped: 0, Not Run: 0, Time: 0.188s
   [exe exit=1]
   ```

   说明连接串确实被读取并用于连接外部真库（环境变量优先级也一并验到了），不存在「没读到配置就静默回落容器」的假绿。

`dotnet test` 本体：仍受沙箱限制未运行，与 R1 同一原因（`System.UnauthorizedAccessException` + `NamedPipeClientStream`，`总计: 0`），**本票所有绿灯结论均来自直跑 exe**，剩余风险见「环境与替代方案」。

### R2b 返工证据（审计 6 项返工后，代码 `8c4033b613518103952e5526596e24a40edc21eb`）

全量 8 条（3 条冒烟 + 2 条护栏 + 3 条来源判定），构建 0 警告 0 错误：

```text
$ ./Pzd.IntegrationTests.exe
=== TEST EXECUTION SUMMARY ===
   Pzd.IntegrationTests  Total: 8, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 10.399s
[exe exit=0]
```

**high：容器不可用时的中文指引**（把连接串移走、本机无 Docker）——返工前这里只有 Testcontainers 英文栈：

```text
$ ./Pzd.IntegrationTests.exe -class "Pzd.IntegrationTests.SmokeTests" -method "...库默认字符集是utf8mb4"
---- System.InvalidOperationException : 容器不可用（镜像 mysql:8.4）：Docker is either not running or misconfigured. Please ensure that Docker is running and that the endpoint is properly configured.
-------- DotNet.Testcontainers.Builders.DockerUnavailableException : Failed to connect to Docker endpoint at 'npipe://./pipe/docker_engine'.
=== TEST EXECUTION SUMMARY ===
   Pzd.IntegrationTests  Total: 1, Failed: 1
[exe exit=1]
```

**medium 1：来源优先级改成容器优先（AC-04）**，判定抽成纯函数 `ChooseSource`，三条用例钉住：容器可用且有连接串 → `Container`；容器不可用 → `External`；两者都无 → 抛带中文指引的 `InvalidOperationException`（断言同时检查指引三条出路与内层原始原因）。

**medium 2：删库护栏 + 变异自证**

| 变异 | 改动 | 结果 |
|---|---|---|
| M1 | 摘掉 `DropDatabaseAsync` 里的 `EnsureTestDatabaseName(name)` 调用 | `FixtureGuardTests` `Total: 2, Failed: 1`，退出 1（落成 `1044/1142` 这种不明所以的库错误） |
| M2 | 让前缀校验恒真（`if (false)`） | `Total: 2, Failed: 2`，退出 1（`Assert.Throws() Failure: No exception was thrown` / `Exception type was not an exact match`） |

还原后**先 build 再跑**，8/8 绿（上轮审计记过我「先还原漏重构建」的顺序错误，这次没再犯）。护栏抛的是 `ArgumentException`，用例断言就是这一个精确类型，没有用 `ThrowsAnyAsync`——拒绝原因必须说得清。

**medium 5：分支同步** `git merge origin/main`（落后 13 个提交），`.gitignore` 与 `Pzd.slnx` 两处冲突取并集，合完 `git rev-list --count HEAD..origin/main` = `0`，上面的 8/8 就是在合并后的树上跑出来的。

**low 三条**：`IClassFixture` 换成程序集级 `AssemblyFixture`（见「环境与替代方案」里的约定）；`Database = string.Empty` 和 `Build()` 进 `try` 两处都写了「为什么」注释，防止后人当冗余删掉；`pzd_chk_*` 探针残库由李总认领并已清，本次跑完 `pzd_test_%` 残库 **0 个**。

## 恢复方式

删除工程与 fixture；容器与测试库直接销毁；回落外部真库时，fixture 建出的临时库同样必须删干净，失败路径也要清。

## 任务映射

- worktree：`.worktree/36/`
- 分支：`test/36-integration-test-fixture`
- R1 红灯提交（初版）：`dfb5bfbda61280f4d5b7d68a9650ce4fb0cf1953`
- R2 绿灯回填提交：`8ca97a9dcc0cb012aa83df8cd9f5dea233f06ae7`
- 合并 `origin/main`：`cb8b2fe`（落后 13 个提交，`.gitignore`/`Pzd.slnx` 取并集）
- **审计返工代码提交（送审对象）**：`8c4033b613518103952e5526596e24a40edc21eb`
- 票面返工记录提交：`698ea6b2b8b8eba8cb48dbd4aad8ba223ff445fd`
- 审计通过后再次合并 `origin/main`：`98a3c1752b08f5f38683985cbb0dbd900602c0a9`（落后 4 个提交，**无冲突**；`git diff 698ea6b HEAD -- tests Directory.Packages.props Pzd.slnx` 空输出，合并后重跑 `Total: 8, Errors: 0, Failed: 0` 退出 0）

## 交接：实现 → 审计（老登，返工后第二轮）

1. **目标**：证明审计 6 项已闭环——容器优先（AC-04）、删库护栏、验收勾选、完整 SHA、分支同步、fixture 形态定死；不重开「外部真库能不能跑通」这个已通过的结论。
2. **提交**：分支 `test/36-integration-test-fixture`，代码 `8c4033b613518103952e5526596e24a40edc21eb`（在 `cb8b2fe` 合并后的树上）。
3. **验证**（原文都在 R2b）：`dotnet build` 0 警告 0 错误；全量 `Total: 8, Errors: 0, Failed: 0` 退出 0；连接串移走 + 无 Docker 时抛带中文指引的 `InvalidOperationException`；护栏变异 M1 `Failed: 1`、M2 `Failed: 2` 均退出 1，还原后先 build 再跑回 8/8；合并后 `rev-list HEAD..origin/main` = `0`。
4. **遗留**：
   - **容器分支本机未运行**（无 Docker）：`mysql:8.4` 起库、版本一致性都未验证，AC 第 4/5 条因此不勾。
   - **无 Docker 机器每次跑测试多付约 10 秒**容器探测（8 条用例 0.4s → 10.4s）。这是容器优先的代价，要提速得先改 AC-04，不许在代码里反转优先级。
   - `dotnet test` 本体仍受沙箱限制未运行（`Access denied` + 命名管道，`总计: 0`），需无管道限制环境补跑一次。
   - **fixture 形态已定**：程序集级 `AssemblyFixture`（张一一、老何 2026-10-30 确认），`IClassFixture` 明确排除；同时钉住「共享实例不等于共享库」，每用例仍独立库。若日后要改为 collection fixture，只动 `AssemblyFixtures.cs` 与各测试类声明，不动 fixture 本体。
   - 本机 `pzd_master` 里仍有 15 张 `qrtz_*` 旧表（不是本票产物），17 号票迁移前需要先定处理方式。

## 遗留（不阻塞合入；后续票引用本票时从这里取，不必翻正文）

| # | 事项 | 谁 | 触发时机 |
|---|---|---|---|
| 1 | 容器分支（`mysql:8.4`）从未在本机运行，AC 第 4/5 条未勾，版本一致性未验证 | 有 Docker 的环境 | 首次在有 Docker 的机器上跑集成测试时 |
| 2 | `dotnet test` 本体受沙箱限制未运行（`Access denied` + 命名管道，`总计: 0`），本票结论均来自直跑 exe | 无管道限制环境 | 合入后首次在普通终端跑 CI/本地全量时 |
| 3 | 无 Docker 的机器每次多付约 10 秒容器探测；提速需先改 AC-04，不许反转优先级 | 后续票认领人 | 觉得慢的时候 |
| 4 | `ChooseSource` 抛出的指引文案有双句号（`…engine'.` 拼 `。请安装` → `'.。请安装`），建议 `TrimEnd('.')` | 触碰 fixture 的人 | 下一次改 `MySqlServerFixture` 时（含修第 5 条） |
| 5 | `TestDatabase` 构造是 `public`，护栏只加在 `DropDatabaseAsync` 上；收紧成 `internal` 更严（同程序集内测试仍可调） | 触碰 fixture 的人 | 同上 |
| 6 | `pzd_master` 有 15 张 `qrtz_*` 旧表（`d34cf66` 遗物），不属本票范围 | 37 号票 | 已在 37 处理 |
