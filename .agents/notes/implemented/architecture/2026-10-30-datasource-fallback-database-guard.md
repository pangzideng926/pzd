# Agent Note: 回落路径的库名口径与启动期校验

Status: implemented

## Problem

03 号票的 AppHost 支持「容器优先、外部实例回落」。两条路径的库名来源完全不同，而这件事在代码里看不出来：

- 容器路径：库名由 `AddDatabase("pzd-master")` 生成，**就是** `pzd-master`
- 回落路径：库名由外部连接串的 `Database=` 决定，AppHost 不参与

资源名必须避开下划线（`ASPIRE006`），所以键名是 `pzd-master`；而架构书 §6.1 的库名是 `pzd_master`（平台库）与 `pzd_t_{code}`（租户库），一律下划线。

二者叠在一起就产生了一个只在运行期才暴露的空档：连接串**不带** `Database=` 时，驱动没有默认库，AppHost 照样起、资源照样显示正常，直到第一次真正读写库才炸成 `ERROR 1049 (42000): Unknown database 'pzd-master'`。本机实测：`pzd-master` 不存在、`pzd_master` 存在（含 15 张清空前的 `qrtz_*` 表）。

## Decision

- **库名一律下划线**，以架构 §6.1 为准：平台库 `pzd_master`、租户库 `pzd_t_{code}`。
- **回落连接串必须显式带 `Database=pzd_master`**；资源名 `pzd-master` 只作连接串的键名，不决定库名。
- **AppHost 启动即校验**：`External:MySql` 有值但整串里既没有 `Database=` 也没有 `Initial Catalog=` 时，抛 `InvalidOperationException`，消息里直接给正确写法。
- 校验放在 `builder.Build().Run()` 之前，刻意让它在任何运行时故障（例如本机 EventLog 不可写）之前触发，这样这道校验本身是**可验证**的，不需要等环境修好。

## Alternatives considered

- **只写文档，不加校验** — 最强理由：AppHost 是编排层，不该替使用者校验连接串格式；文档已经写了正确写法。没选，是因为这次踩坑的正是「照抄资源名当库名」，而 README 示例原本写的恰恰是 `Database=pzd-master`——文档自己就在教错。只靠文档等于把同一个坑留给下一个人。
- **在代码里替连接串补 `Database=pzd_master`** — 最强理由：使用者什么都不用管，缺了就补上，最省事。没选，是因为回落路径要能指向任意外部实例，库名可能是 `pzd_t_xxx` 或别的测试库；替使用者做主会把「配置写错」变成「静默连到另一个库」，比报错更危险。
- **加健康检查，连不上就标 Unhealthy** — 最强理由：把问题暴露在仪表板上，最直观。没选，是因为健康检查在容器就绪之后才跑，那时 AppHost 已经起来了，错误仍会被当成「环境问题」；而且本票目前拿不到 Healthy 实跑环境，加了也验证不了。
- **把库名统一成 `pzd-master`（连字符），两条路径就一致了** — 最强理由：一份名字，容器与回落行为完全相同，不用记两条规则。没选，是因为它偏离架构 §6.1 的明文（`pzd_master` / `pzd_t_{code}`），且要迁走本机 15 张既有表；偏离架构得维护者点头，为这点收益不值。

## Consequences

- **收益**：库名口径从「靠人记」变成「启动即拦」；错误信息本身带正确写法，不需要去翻文档；校验可在无 Docker、EventLog 不可写的环境里验证，不依赖跑通四个资源。
- **代价**：AppHost 多了一处硬编码的库名提示（`pzd_master`），架构改名要同步改这里；只校验「有没有 `Database=`」，不校验库名拼写对不对（校验不了，实例上可能合法存在任意库名）。
- **重访信号**：回落路径开始支持多个平台库（多实例、多 schema）时，这条单库名提示需要重做；或架构 §6.1 的库名规则变动；或容器路径改为不带连字符的资源名（例如 Aspire 放宽了 `ASPIRE006`）。

## Verification

```bash
cd E:/pzd/.worktree/03 && dotnet build Pzd.slnx      # 0 警告 0 错误

# 缺 Database=：应在 AppHost.cs 的校验处抛 InvalidOperationException，且不出现 EventLog 报错
External__MySql="Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=…" \
  dotnet run --project src/Pzd.AppHost --no-build

# 带 Database=：校验放行，落到既有的 EventLog 墙（本机限制，与本改动无关）
External__MySql="…;Database=pzd_master" \
  dotnet run --project src/Pzd.AppHost --no-build
```

判据是两个场景抛出的**异常不同**；控制台代码页会把中文消息显示成乱码，消息正文以源码为准。
