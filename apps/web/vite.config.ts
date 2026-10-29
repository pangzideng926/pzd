import { reactRouter } from "@react-router/dev/vite"
import tailwindcss from "@tailwindcss/vite"
import { defineConfig } from "vite"

export default defineConfig({
  resolve: { tsconfigPaths: true },
  plugins: [tailwindcss(), reactRouter()],
  // 固定回环地址：SPA 预渲染默认连 ::1，在禁用 IPv6 回环的环境里会 EACCES 构建失败；
  // 同时让开发服务器监听 127.0.0.1，本地与 CI 都用 IPv4 访问。
  server: { host: "127.0.0.1" },
})
