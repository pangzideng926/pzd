# Agent Note: CI 门禁入口与「0 用例」防线

Status: implemented

## Problem

01 落地的测试栈是 Microsoft.Testing.Platform（MTP），实测在 **0 个用例时退出码仍是 0**；02 的复跑又暴露 turbo 的 `pnpm test` 默认是**缓存回放**——退出码 0、`pass 2` 照打，测试进程根本没跑。两种假绿共同依赖同一个前提：CI 只看退出码。而仓库此前没有任何 CI，门禁全靠人肉跑命令、靠票面留痕，结果与「哪台机器跑的」绑在一起，不可复现。

## Decision

- **单一入口**：`.github/workflows/ci.yml`，两个 job——`backend`（`dotnet restore` → `dotnet build`（警告即错误）→ `dotnet test`）与 `frontend`（`pnpm install --frozen-lockfile` → `pnpm build --force`）。
- **平台按假设 A1（GitHub Actions），尚未确认**。工作流是平台口径的唯一落点：改 GitLab CI 只替换这一个文件，其他票引用的验收命令不变。
- **架构测试步自己数用例数**：解析摘要里的 `Total:` 或 `总计:`，解析不到或 ≤ 0 一律判失败，不依赖退出码。
- **前端 build 强制 `--force`**，不吃 turbo 缓存回放。
- **SDK 版本只在 workflow 里写一次**，与 `global.json` 的 `sdk.version` 对齐，不动 `global.json`（那属 01 范围）。

## Alternatives considered

- **只依赖退出码** — 最强理由：最简单，不需要解析测试框架输出，将来换框架也不用改 CI。没选，是因为两次实测已经证明 0 用例与缓存回放都能退出 0，依赖退出码等于把假绿制度化。
- **用 MTP 的 `--minimum-expected-tests`** — 最强理由：由框架自己保证「至少跑了 N 个用例」，比文本解析稳，不怕摘要格式变化。没选，是因为本机 MTP 4.0.2 实测报 `error: unknown option: --minimum-expected-tests`、退出码 3，装了不能用；等框架版本支持再换回来。
- **CI 里直接跑测试 exe，不用 `dotnet test`** — 最强理由：绕开 MTP 与 `dotnet test` 的命名管道握手，本机那种 `NamedPipeClientStream` 被拒的环境也能跑通。没选，是因为它把产物路径与目标框架写死，跨 runner 的构建布局差异会让路径很脆；`dotnet test` 才是产线语义上的入口。
- **把断言逻辑抽成 `scripts/ci/assert-test-count.sh`** — 最强理由：能本地单测，四个场景可以进仓库当回归用例。暂未选，是因为票面的责任文件范围只列了 CI 配置与 `db/`，新增 `scripts/` 属范围外；改用「从 YAML 抽出 `run` 块再本地模拟」拿到了同样的红绿证据，没有扩大范围。范围放宽后应该搬过去。
- **GitHub Actions 与 GitLab CI 各写一份** — 最强理由：不赌平台，哪边确认都能立刻用。没选，是因为两份必然分叉，门禁脚本一旦分叉就是两套真相；口径未定时写一份可替换的，比写两份平行省维护。

## Consequences

- **收益**：门禁不再依赖「谁在哪台机器跑过」；0 用例与缓存回放这两种已知假绿在 CI 侧被堵死；平台口径确认后只需换一个文件。
- **代价**：多一层文本解析，MTP 摘要格式一改就得跟着改；`dotnet-version` 与 `global.json` 是两处真相，升级 SDK 要同时改；前端用例没进门禁（票面三步不含 `pnpm test`）。
- **重访信号**：MTP 支持 `--minimum-expected-tests` 后改用框架原生断言；CI 首次真跑时摘要格式与本地不一致；前端出现必须挡住的行为变更，需要把 `pnpm test --force` 加进 `frontend` job；平台口径改判为 GitLab CI。

## Verification

```bash
pnpm dlx js-yaml .github/workflows/ci.yml    # 解析成功，退出码 0
```

断言逻辑用假 `dotnet` 模拟四种场景，全部符合预期：

```text
Total: 1 + 退出码 0      → 通过
Total: 0 + 退出码 0      → 失败（抓 0 用例假绿）
Total: 2 且 Failed: 1    → 失败（退出码原样传出）
总计: 2（中文摘要）      → 通过
```

CI 本体仍是「未运行」，需平台口径确认后由维护者触发一次并回填。
