import { reactRouter } from "@react-router/dev/vite"
import tailwindcss from "@tailwindcss/vite"
import { defineConfig } from "vite"

// Aspire 注入的后端地址（03 号票）：开发期 /api、/hubs 走 Vite 代理到 Api，前后端同源，
// 这是 Cookie 认证不触发 CORS 的前提（architecture.md §7.1）。未在 Aspire 下启动时回落到本机默认端口。
const apiTarget = process.env.services__api__http__0 ?? "http://127.0.0.1:5080"

export default defineConfig({
  resolve: { tsconfigPaths: true },
  plugins: [tailwindcss(), reactRouter()],
  // 固定回环地址：SPA 预渲染默认连 ::1，在禁用 IPv6 回环的环境里会 EACCES 构建失败；
  // 同时让开发服务器监听 127.0.0.1，本地与 CI 都用 IPv4 访问。
  // 端口由 Aspire 通过 PORT 环境变量指定，单独跑 pnpm dev 时用 Vite 默认值。
  server: {
    host: "127.0.0.1",
    port: process.env.PORT ? Number(process.env.PORT) : undefined,
    proxy: {
      "/api": { target: apiTarget, changeOrigin: true },
      "/hubs": { target: apiTarget, changeOrigin: true, ws: true },
    },
  },
})
