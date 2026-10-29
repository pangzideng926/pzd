状态：进行中
主写入者：PZD-实现Bot-2
更新日期：2026-10-29 18:04 Asia/Shanghai
下一步：等维护者点头合入；`git merge main` 后复跑一次构建
交付状态：待收尾
实施条件：已具备

# 02 前端 monorepo 骨架（shadcn react-router 扁平化）

## 目标

建出与后端同级的前端 monorepo：`apps/web` + `packages/{ui,api,realtime}`，Turbo 任务链可跑通。

## 需求依据

- [architecture.md](../../../architecture.md) §1、§14.1
- [spec.md](../spec.md) AC-03
- 拆分依据：[consensus.md](../../pzd-v1/consensus.md) M1.2

## 范围与非范围

- 做：用 shadcn 官方模板初始化（preset `b2oDW9T8C`、`--base radix`、`--template react-router --monorepo`）后扁平化到根；`pnpm-workspace.yaml`、`turbo.json`、根 `package.json` 脚本；`api#generate → web#build` 任务依赖（本票 `api#generate` 读占位 `openapi.json`）。
- 不做：不写业务页面、不做认证与路由守卫（26 号票）、不接真实接口。

## 责任文件范围（认领时在群里先说清）

新建 `package.json`、`pnpm-workspace.yaml`、`turbo.json`、`apps/web/**`、`packages/ui/**`、`packages/api/**`、`packages/realtime/**`。不碰 `src/`、`tests/`、`db/`。

## 关键接口与依赖

- 前置票：无（可立即开工）
- 对外契约：向 26–31 号票提供 `apps/web` 与三个包的骨架；`packages/api` 的生成入口在本票定型。

## 步骤

1. 跑官方模板初始化命令，记录完整命令与输出。
2. 把生成目录扁平化到根，删除多余嵌套，核对与 §2 一致。
3. 配 turbo 任务依赖 `api#generate → web#build`，占位 `openapi.json` 放在包内。
4. `pnpm install && pnpm build` 跑通并贴输出。

## 验收标准

- [x] 根目录 `pnpm install && pnpm build` 成功，退出码 0。
- [x] turbo 日志能看出先 `api#generate` 后 `web#build`。
- [x] 目录结构与 §2 一致，无 `backend/`、`frontend/` 一类嵌套。
- [x] `apps/web` dev 模式能启动并渲染模板首页。

## 验证入口

```bash
pnpm install
pnpm build
pnpm --filter web dev
```

证据按 L1 记到本票：完整命令、真实退出码、关键输出行、被测 SHA。未运行就写「未运行」，不写成通过。

**假绿防线**：用 `--filter` 时，测试类名必须与 filter 的字面一致；证据里必须贴出实际通过的用例数（>0）。跑出 `0 tests` 的退出码 0 一律按失败处理，不算通过。

**环境性 `0 tests`**（不算假绿，但也不等于通过）：若原因是环境限制——例如沙箱拒绝命名管道，.NET MTP 连不回宿主，报 `UnauthorizedAccessException ... NamedPipeClientStream` 并显示 `总计: 0`——证据里必须同时给出四项：①错误原文与真实退出码；②替代执行方式的实际结果（如直接运行测试 exe）；③明确写「`dotnet test` 本体未运行，需在无管道限制的环境补跑一次」；④剩余风险写进遗留。缺一项就按「未运行」处理，不得写成通过。


## 交接（实现 → 测试／审计返工后）

**1. 目标**：修掉审计 `b9abbaa` 指出的两项 high（`ssr:true` 违反 §14.1、模板 Dockerfile 依赖已删除的 `package-lock.json`），并把证据补到可复核：`--force` 实跑、真实红跑日志、完整 SHA、重跑生成物不再弄脏工作树。

**2. 提交**

- 分支：`chore/02-frontend-monorepo`
- 首轮实现：`ce36a281e41dafcf8b610bebbf5357d2d0b89352`
- 审计基线（首轮票面回填）：`b9abbaa8a462c2c2ffccbb47c4dd2336c8fb1be1`
- **本次返工（被测 SHA）**：`f652ffabd44018d4a70ebb7d96d6bcaa468268f3`
- 本票第二次回填：随本分支下一次 docs 提交入库（`git log --oneline -1 -- docs/work/bootstrap/tickets/02-frontend-monorepo.md` 可查完整 SHA）

**3. 验证**（命令、真实退出码、关键输出行，原样粘贴）

安装（`@react-router/serve` 已移除，锁文件随之更新）：

```
$ cd .worktree/02 && pnpm install
Scope: all 5 workspace projects
Done in 240ms using pnpm v12.14.0
INSTALL_EXIT=0
```

构建（`--force` 跳过缓存，行号为日志行）：

```
$ pnpm build --force
8:@workspace/api:generate: cache bypass, force executing 94a156d733d2787a
9:@workspace/api:generate: $ node scripts/generate.mjs
10:@workspace/api:generate: api#generate: .../packages/api/openapi.json -> .../packages/api/src/generated/client.ts
11:web:build: cache bypass, force executing a9c032bb809570d0
68:web:build: SPA Mode: Generated build\client\index.html
70: Tasks:    2 successful, 2 total
71:Cached:    0 cached, 2 total
BUILD_EXIT=0
```

SPA 产物核对（审计要求的 `build/client/index.html`）：

```
$ ls -l apps/web/build/client/index.html
-rw-r--r-- 1 q1887 197609 1405 Oct 29 18:01 apps/web/build/client/index.html
$ grep -c "min-h-svh" apps/web/build/client/assets/*.css
1
```

测试（`--force` 实跑，假绿防线：贴实际通过用例数）：

```
$ pnpm test --force
@workspace/api:test: ℹ pass 2
@workspace/api:test: ℹ fail 0
 Tasks:    1 successful, 1 total
Cached:    0 cached, 1 total
TEST_EXIT=0
```

真红证据（把 `generateClient` 换成不写文件的桩，断言必须失守）：

```
$ node --test "test/*.test.mjs"      # 桩实现
✖ 从 openapi.json 生成 TS 客户端占位文件
✖ openapi.json 缺 paths 时报错而不是静默生成空文件
ℹ pass 0
ℹ fail 2
RED_EXIT=1
$ node --test "test/*.test.mjs"      # 恢复实现
ℹ pass 2
ℹ fail 0
GREEN_EXIT=0
```

类型检查（`--force`，两包都实跑）：

```
$ pnpm typecheck --force
@workspace/ui:typecheck: cache bypass, force executing f7e4e94ee03d71c6
web:typecheck: cache bypass, force executing 22d7b3952d2d7904
 Tasks:    2 successful, 2 total
Cached:    0 cached, 2 total
TC_EXIT=0
```

dev 首页真实渲染（SPA 下服务端只给外壳，用 Chromium 实测 DOM）：

```
$ pnpm exec react-router dev        # 监听 127.0.0.1:5173
$ node check.mjs                    # Playwright chromium，装在临时目录，不是仓库依赖（一次性人工核对，非可复现证据）
status 200
含 Project ready!: true
含 You may now add components: true
Button 数量: 1
可见文本首行: Project ready!
EXIT=0
```

生成物不再脏树（`.gitattributes` 生效）：

```
$ node packages/api/scripts/generate.mjs && git status --porcelain packages/api
（无输出）
```

**4. 遗留**

- **决策笔记与代码不在同一个提交**（首轮笔记 `b9abbaa`、代码 `ce36a28`；返工轮 SPA 笔记 `23cb24e`、代码 `f652ffa`）：这两轮分支都已推送，按规矩不改写历史；**自 03 号票起笔记与代码必须同提交**（`Bot分工 §6`、`决策笔记 §5.8`）。
- **`pnpm test` 只覆盖 `packages/api` 的 2 个用例**，`apps/web` 没有 test 脚本。浏览器级证据是本票临时装的 Playwright 跑的一次性校验，不是仓库依赖；正式 E2E 归 35 号票。
- **`.gitattributes` 只对 `packages/api/src/generated/**` 固定 LF**，没做仓库级 `* text=auto eol=lf`：仓库级归一化会重写 `旺总` 的 .NET 分支全部文件的换行，属于 04 号票「仓库约定」的范围，本票不动。
- **SPA 构建对本机 IPv6 回环敏感**：`ssr:false` 后构建会预渲染 `/`，默认连 `::1`，在禁用 IPv6 回环的环境里报 `Prerender: Request failed for /: connect EACCES ::1`。已在 `apps/web/vite.config.ts` 固定 `server.host: "127.0.0.1"`（A/B 验证：不加报错、加了 `SPA Mode: Generated build\client\index.html`）。
  - 代价：写死回环地址后，局域网 / PDA 联调时别的机器连不上；需要对外暴露时用 `pnpm exec react-router dev --host 0.0.0.0` 覆盖，别改配置里的默认值。Aspire 里的启动方式见 03 号票。
- `build/server/index.js` 仍有 6.21 kB：这是 React Router SPA 预渲染自带的产物，不是 SSR 服务；部署只拷 `build/client`（§14.5），不影响同源部署。
- 包名沿用 `@workspace/*`、`apps/web` 版本号仍硬编码 `0.0.1`（§15.1 要由 MinVer 推导）：前者要改建议单开票，后者归后续版本票。
- 未用依赖与错位依赖留给 28/29 号票整理：`@remixicon/react`、`@turbo/gen` 零引用；`isbot` 在 SPA 模式下也没有引用方（模板 SSR 残留）；TanStack Query 尚未安装；`zod` 装在 `ui` 而不是 `api`。
- `Vite /api`、`/hubs` 代理与根 `README.md` 归 03 号票；`apps/web` 的业务页面与路由守卫归 26 号票。

## 恢复方式

删除本票新增文件，重新 `git checkout` 根配置即可；无数据影响。

## 任务映射

- worktree：`.worktree/02/`
- 分支：`chore/02-frontend-monorepo`
- 提交：`f652ffabd44018d4a70ebb7d96d6bcaa468268f3`（审计返工，被测 SHA）；首轮实现 `ce36a281e41dafcf8b610bebbf5357d2d0b89352`；首轮审计基线 `b9abbaa8a462c2c2ffccbb47c4dd2336c8fb1be1`
- 合并 main：`ee00d28` → 合并提交 `a575036`（作者 `PZD-实现Bot-2`，未落到维护者身份）；合并后复跑 `pnpm build --force` 退出 0、`SPA Mode: Generated build\client\index.html`（1405 字节）、`pnpm test --force` `pass 2 / fail 0`、`Cached: 0`
