import { reactRouter } from "@react-router/dev/vite";
import { defineConfig } from "vite";
import tsconfigPaths from "vite-tsconfig-paths";

// `types` 里只有 vite/client（没有 @types/node）：vite 配置本身就跑在 Node 里，
// 取值没问题，只是类型不自带，这里显式声明一下。
declare const process: { env: Record<string, string | undefined> };

/**
 * F-03-A11：本次运行的唯一标记，由启动方注入 `PZD_RUN_ID`，dev server 原样作为响应头回显。
 *
 * 为什么 aspire 模式下不能靠进程血缘：AppHost 起的 web 经 DCP 编排，父链是
 * `node → dcp.exe → dcp.exe → 已退出`，够不到本次宿主。为什么也不能靠命令行路径：
 * 同目录的上一轮残留实例路径完全一致，它只证明「来自这个目录」，不证明「来自本次启动」。
 * 只有启动方每轮新生成、残留实例拿不到的值，才能证明「这个 200 是本次实例给的」。
 *
 * 未注入时不加任何响应头——生产构建与日常 `pnpm dev` 不该多出一个内部标识（与 API 同一条原则）。
 */
const runId = process.env.PZD_RUN_ID?.trim() ?? "";

/**
 * F-03-A5 诊断锚点（临时探针，A5 结案后删除）。
 *
 * 为什么非要在这里打：AppHost 那行 `[pzd-diag] env` 读的是 `snapshot.EnvironmentVariables`，
 * 那是**资源声明**的环境，不等于 vite **进程**的真实环境——supervisor 运行时塞进去的值
 * 不会出现在快照里。所以「快照里没有 NODE_ENV」不能反证「进程里没有」。
 * 这一行是 vite 进程自己的值，单一变量、不含任何秘密，符合「不做整包打印」的约束。
 */
console.log(`[pzd-web] NODE_ENV=${process.env.NODE_ENV ?? "<未设置>"}`);

/**
 * 把标记加在**每一个** dev server 响应上。
 *
 * 不能用 `server.headers`：它只作用于 Vite 自身那层中间件，而 `/`（SPA 文档）是
 * React Router 的文档中间件应答的，实测 `/@vite/client` 带上了头、`/` 却没有——
 * 而脚本要验的正是 `/`（200 + `lang="zh-CN"`）。所以改用最前面的一道中间件直接
 * `setHeader`，后续任何 handler 复用同一个 res 都会带上它。
 */
const runIdHeaderPlugin = {
  name: "pzd-run-id-header",
  configureServer(server: {
    middlewares: {
      use: (handler: (req: unknown, res: { setHeader: (name: string, value: string) => void }, next: () => void) => void) => void;
    };
  }) {
    if (!runId) {
      return;
    }

    // configureServer 里直接 use 的中间件排在 Vite 内部中间件之前，React Router 的
    // 文档中间件也在其后注册，因此 `/` 的响应同样带这个头。
    server.middlewares.use((_req, res, next) => {
      res.setHeader("X-Pzd-Run-Id", runId);
      next();
    });
  },
};

/**
 * 开发期把 `/api`、`/hubs` 代理到 `Pzd.Api`（架构 §14.1）。
 * 生产由 `Pzd.Api` 托管 `build/client`，同源部署，不需要代理（§14.5）。
 */
export default defineConfig({
  plugins: [runIdHeaderPlugin, reactRouter(), tsconfigPaths()],
  server: {
    // 票 02：端口与绑定写死，不靠默认值漂移。
    // host 显式绑 127.0.0.1：默认 "localhost" 在 Windows 上只监听 ::1，而 ::1 在部分环境
    // （含本机 sandbox，WSAEACCES）连不出去，探测会假红；绑 IPv4 后
    // `http://localhost:5199/` 与 `http://127.0.0.1:5199/` 两种探测都能通。
    host: "127.0.0.1",
    port: 5199,
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://127.0.0.1:5080",
        changeOrigin: false,
      },
      "/hubs": {
        target: "http://127.0.0.1:5080",
        changeOrigin: false,
        ws: true,
      },
    },
  },
});
