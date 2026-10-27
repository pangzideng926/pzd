/**
 * `@pzd/api` — orval 生成产物的落点（架构 §14.1）。
 *
 * 票 02 只建立包骨架；真正的生成流程在前后端契约可用后接入：
 * `Pzd.Api` 导出 `openapi.json` → orval 生成 client / TanStack Query hooks / zod schema，
 * 自定义 mutator 负责错误处理、CSRF 头与版本头。
 */
export const API_PACKAGE = "@pzd/api" as const;
