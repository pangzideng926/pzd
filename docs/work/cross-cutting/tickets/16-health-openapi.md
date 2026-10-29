状态：待实施
主写入者：未认领
更新日期：2026-10-29 18:05 Asia/Shanghai
下一步：等 07 合入后认领
交付状态：待收尾
实施条件：待满足：07 合入（前置票）

# 16 健康检查与 OpenAPI 导出

## 目标

运维能看出是哪一环挂了；前端能拿到稳定的 `openapi.json` 生成客户端。

## 需求依据

- [architecture.md](../../../architecture.md) §13
- [spec.md](../spec.md) AC-12、AC-13
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M2.12

## 范围与非范围

- 做：`/health` 分项检查 master、Redis、对象存储、租户库抽样；OpenAPI 3.1 + Scalar 界面；构建时导出 `openapi.json` 到 `packages/api` 可读的位置。
- 不做：不做监控告警接入；不做 API 版本化（§17 期外）。

## 责任文件范围（认领时在群里先说清）

新建 `src/Pzd.Api/Health/**`、`src/Pzd.Api/OpenApi/**`；改 `apps/web` 或 `packages/api` 的生成输入路径（只改路径配置）。不碰业务模块。

## 关键接口与依赖

- 前置票：07
- 对外契约：`openapi.json` 的产出路径与文件名固定，28 号票的 orval 配置依赖它。

## 步骤

1. 实现分项健康检查。
2. 配 OpenAPI 3.1 与构建时导出。
3. 手工停一个依赖，确认对应项不健康而不是全红。
4. 贴 `/health` 输出与 `openapi.json` 存在证明。

## 验收标准

- [ ] `/health` 分项返回 master、Redis、对象存储、租户库抽样状态。
- [ ] 停掉 Redis 后仅该项不健康。
- [ ] 构建产出 `openapi.json`，路径固定。
- [ ] ProblemDetails 字段与 §8.4 一致地出现在 openapi 中。

## 验证入口

```bash
dotnet run --project src/Pzd.Api
curl -s http://localhost:<port>/health
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

## 恢复方式

回滚代码与导出配置。

## 任务映射

- worktree：实施时填写（`.worktree/16/`）
- 分支：实施时填写（`feat/16-health-openapi`）
- 提交：实施时填写（完整 SHA）
