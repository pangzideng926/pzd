import { reactRouter } from "@react-router/dev/vite";
import { defineConfig } from "vite";
import tsconfigPaths from "vite-tsconfig-paths";

/**
 * 开发期把 `/api`、`/hubs` 代理到 `Pzd.Api`（架构 §14.1）。
 * 生产由 `Pzd.Api` 托管 `build/client`，同源部署，不需要代理（§14.5）。
 */
export default defineConfig({
  plugins: [reactRouter(), tsconfigPaths()],
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
