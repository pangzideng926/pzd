/**
 * `@pzd/realtime` — SignalR 客户端封装（架构 §10、§14.1）。
 *
 * 票 02 只建立包骨架；连接与自动重连在步骤 4 的前端外壳里接入，
 * `@microsoft/signalr` 依赖届时再加，避免本票引入未使用的运行时依赖。
 */
export const REALTIME_PACKAGE = "@pzd/realtime" as const;
