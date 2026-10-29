状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 06 合入后认领
交付状态：待收尾
实施条件：待满足：06 合入（前置票）

# 14 通用能力（文件存储 / 分布式锁 / 缓存封装）

## 目标

文件存储、分布式锁、缓存三个高频能力只实现一次，路径与 key 自动带租户隔离。

## 需求依据

- [architecture.md](../../../architecture.md) §13
- [spec.md](../spec.md) AC-10
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.10

## 范围与非范围

- 做：`IFileStorage`（S3 兼容，`tenant/{tid}/` 前缀，预签名直传直下）；分布式锁（`Medallion.Threading`）；`HybridCache` 封装（`t:{tid}:` 前缀 + 领域事件失效）。
- 不做：不做上传界面；不做对象存储生命周期策略；不做 Excel（15 号票）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Infrastructure/Storage/**`、`Storage/Locking/**`、`Storage/Caching/**`、`tests/Pzd.IntegrationTests/Storage/**`。不碰 `src/Pzd.Infrastructure/Tenancy`。

## 关键接口与依赖

- 前置票：06
- 对外契约：`IFileStorage` 签名被 15、33 号票依赖；缓存封装被 24 号票权限缓存依赖。

## 步骤

1. 实现 `IFileStorage` 并强制租户前缀。
2. 封装分布式锁（Redis 与 MySQL 二选一可配）。
3. 封装缓存：前缀、失效、领域事件钩子。
4. 集成测试：跨租户预签名地址互不可读；锁互斥；缓存按前缀失效。

## 验收标准

- [ ] 上传路径以 `tenant/{tid}/` 开头。
- [ ] A 租户的预签名地址无法读取 B 租户文件。
- [ ] 分布式锁在同一 key 上互斥，超时释放。
- [ ] 缓存 key 带 `t:{tid}:` 前缀，领域事件可触发失效。
- [ ] 对象存储不可用时降级行为明确（抛错而不是静默成功）。

## 验证入口

```bash
dotnet test tests/Pzd.IntegrationTests --filter Storage
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。

## 恢复方式

回滚代码；对象存储测试桶可删。

## 任务映射

- worktree：实施时填写（`.worktree/14/`）
- 分支：实施时填写（`feat/14-generic-capabilities`）
- 提交：实施时填写（完整 SHA）
