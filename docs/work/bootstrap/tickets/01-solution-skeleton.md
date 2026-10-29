状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：可直接认领开工
交付状态：待收尾
实施条件：已具备

# 01 后端解决方案骨架与公共编译约定

## 目标

建出后端解决方案骨架与公共编译约定，让后续每个项目自动继承统一设置，不再逐个 csproj 复制。

## 需求依据

- [architecture.md](../../../architecture.md) §2、§15.1、§16.3
- [spec.md](../spec.md) AC-02、AC-05
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.1

## 范围与非范围

- 做：`Pzd.slnx`（解决方案文件夹分组）、`global.json` 锁 SDK、`Directory.Build.props`（Nullable、TreatWarningsAsErrors、MinVer）、`Directory.Packages.props` 中央包管理、`src/Pzd.Api` 与 `tests/Pzd.ArchitectureTests` 两个空壳项目。
- 不做：不建业务模块，不建 SharedKernel，不写任何业务代码，不引数据库包。

## 责任文件范围（认领时在群里先说清）

新建 `Pzd.slnx`、`global.json`、`Directory.Build.props`、`Directory.Packages.props`、`src/Pzd.Api/**`、`tests/Pzd.ArchitectureTests/**`。不碰 `apps/`、`packages/`、`db/`。

## 关键接口与依赖

- 前置票：无（可立即开工）
- 对外契约：无对外业务契约；为 05、11、13 号票提供项目骨架与编译约束。

## 步骤

1. 用 `dotnet new` 建 slnx（含 `src`、`tests` 解决方案文件夹）与两个项目。
2. 写 `Directory.Build.props` 与 `Directory.Packages.props`，把 csproj 里的包版本全部上移。
3. 配 MinVer（无 tag 时版本形如 `0.0.0-alpha.0`），在 Api 里输出该版本以证明生效。
4. 跑 `dotnet build` 与 `dotnet test`，把真实输出贴进票。

## 验收标准

- [ ] `dotnet build Pzd.slnx` 成功，退出码 0。
- [ ] 全仓库 csproj 中不存在显式 `Version=` 的包引用。
- [ ] `.slnx` 用解决方案文件夹分组，目录与 §2 一致。
- [ ] Api 启动后 `/` 或启动日志能体现 MinVer 推导出的版本号。

## 验证入口

```bash
dotnet build Pzd.slnx
dotnet test tests/Pzd.ArchitectureTests
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

删除本票新增文件即可，无数据影响。

## 任务映射

- worktree：实施时填写（`.worktree/01/`）
- 分支：实施时填写（`chore/01-solution-skeleton`）
- 提交：实施时填写（完整 SHA）
