import type { Config } from "@react-router/dev/config";

/**
 * 架构 §14.1：后台在登录之后，不做 SSR，`ssr: false` 走 SPA 模式；
 * `react-router build` 产出 `build/client`，由 `Pzd.Api` 的 wwwroot 托管（§14.5）。
 */
export default {
  ssr: false,
} satisfies Config;
