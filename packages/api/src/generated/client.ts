// 由 packages/api/scripts/generate.mjs 生成，勿手改。
// 输入：openapi.json　来源：Pzd API 0.0.0-placeholder
// TODO(28 号票)：替换为 orval 生成的 client、Query hooks 与 zod schema。

export const apiTitle = "Pzd API"
export const apiVersion = "0.0.0-placeholder"
export const apiPaths = [
  "/api/health",
] as const

export type ApiPath = (typeof apiPaths)[number]
