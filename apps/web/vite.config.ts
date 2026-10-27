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
    port: 5173,
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
