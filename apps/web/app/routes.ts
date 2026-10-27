import { index, type RouteConfig } from "@react-router/dev/routes";

/**
 * 路由由前端定义（架构 §14.2）。票 02 只有一个占位路由；
 * 登录页（`_auth`）、侧边栏 + 多页签外壳（`_app`）在步骤 4 引入。
 */
export default [index("routes/home.tsx")] satisfies RouteConfig;
