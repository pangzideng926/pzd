import type { Config } from "@react-router/dev/config"

export default {
  // SPA：架构 §1/§14.1 要求 ssr: false，产物只有 build/client，由 Pzd.Api 托管（§14.5）
  ssr: false,
} satisfies Config
