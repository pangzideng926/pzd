状态：进行中
主写入者：PZD-实现Bot-3
更新日期：2026-10-30 08:55 Asia/Shanghai
下一步：绿灯已回填 R2（外部真库 8.0.46，3/3 通过）；等李总独立验证后转老登审计
交付状态：待测试（代码与绿灯证据齐；`dotnet test` 本体仍受沙箱限制，见 R1）
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

## 关键接口与依赖

- 前置票：01
- 对外契约：后续所有集成测试（06–32）复用此 fixture；**隔离语义为「每用例独立库」**，06 的真提交、08 的一次 Command 一次提交与 Outbox 回滚语义都建立在这个前提上，不允许改成事务回滚。MySQL 版本以本票固定的 tag 为准，03 出来后比对。

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

- [ ] `dotnet test tests/Pzd.IntegrationTests --filter SmokeTests` 连真 MySQL 完成建表、插入、查询，通过数 >0。
- [ ] 工程不引用任何 `Pzd.*` 项目（`dotnet list reference` 输出为空可证）。
- [ ] 每个用例独立库，重复运行结果一致，失败后无残留库。
- [ ] 容器可用时按 `mysql:8.4` 起库；容器不可用时回落外部真库并成功跑通冒烟。两种来源各贴一次实际输出；本机做不到的那一种写「未运行＋原因」，不算通过。
- [ ] 回落外部真库时，实测版本（本机 `8.0.46`）与 `mysql:8.4` 的差异写进本票遗留，并在有容器的环境复核一次（未复核前不算版本一致性已验证）。
- [ ] 不存在 InMemory 替身路径，也没有用事务回滚冒充隔离（审查确认）。

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

fixture 因此实现两种来源，判定写在 `MySqlServerFixture.InitializeAsync`：

1. 容器可用 → 按票面固定 tag `mysql:8.4` 起库；
2. 调用方给出外部连接串 → 回落外部真库（本机为 `8.0.46`）。

外部连接串只从两处读，都不入库：

- 环境变量 `PZD_TEST_MYSQL_CONNECTION`
- 仓库根 `.local/mysql-test-connection.txt`（`/.local/` 已加进 `.gitignore`）

格式：`Server=127.0.0.1;Port=3306;User ID=<可建库账号>;Password=<口令>;`

**剩余风险**

- 本机只有 `8.0.46`，`mysql:8.4` 分支未在本机运行过；按 AC 要求，有容器的环境需复核一次，复核前版本一致性不算已验证。
- 外部真库是共享实例：用例库名固定 `pzd_test_` + GUID，句柄释放即 `DROP DATABASE`，失败路径同样清理；但仍需专用测试账号，不用业务账号。

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

## 恢复方式

删除工程与 fixture；容器与测试库直接销毁；回落外部真库时，fixture 建出的临时库同样必须删干净，失败路径也要清。

## 任务映射

- worktree：`.worktree/36/`
- 分支：`test/36-integration-test-fixture`
- 代码与 R1 红灯提交：`dfb5bfbda61280f4d5b7d68a9650ce4fb0cf1953`
- R2 绿灯回填提交：本文件所在分支顶端，推送后由接手方按 `git log -1 --format=%H` 取完整 SHA

## 交接：实现 → 测试（李总）

1. **目标**：证明 fixture 能在真 MySQL 上「建独立库 → 建表 → 插入 → 查询」跑通，并把 R1 红灯翻成绿灯；不证明业务表语义。
2. **提交**：分支 `test/36-integration-test-fixture`，代码 `dfb5bfbda61280f4d5b7d68a9650ce4fb0cf1953`，证据回填为分支顶端提交。
3. **验证**：`dotnet build` 0 警告 0 错误；直跑 `Total: 3, Errors: 0, Failed: 0`、exe 退出 0；无误口令反向验证 `Total: 1, Failed: 1` 退出 1；服务端无残库、`VERSION()` = `8.0.46`。输出原文见 R2。
4. **遗留**：
   - `dotnet test` 本体本机未运行（沙箱拒命名管道，`总计: 0`），需在无管道限制环境补跑一次；李总若也受限，直跑 exe 可作为替代，但必须写「本体未运行」。
   - 容器路径（`mysql:8.4`）本机无法验证，仅验证了外部真库回落；装了 Docker 的机器需再跑一次确认容器分支。
   - Redis 不在本票范围（本机由 Memurai `8.2.10` 提供），需要 Redis 的票自行接入。
