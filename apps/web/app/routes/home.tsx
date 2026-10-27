import { API_PACKAGE } from "@pzd/api";
import { REALTIME_PACKAGE } from "@pzd/realtime";
import { UI_PACKAGE } from "@pzd/ui";

export function meta() {
  return [{ title: "PZD 后台管理系统" }];
}

/**
 * 票 02 的占位页：只证明 monorepo、路由与构建链路通。
 * 真正的登录页与后台外壳在步骤 4（票 03 之后的前端外壳票）替换。
 *
 * 这里真实 import 三个 workspace 包，证明包关系不是「只在 package.json 里声明」——
 * 老登的 low 项「web 声明了 ui/api/realtime 但零 import」由此关闭。
 */
export default function Home() {
  return (
    <main style={{ fontFamily: "system-ui, sans-serif", padding: "2rem" }}>
      <h1>PZD 后台管理系统</h1>
      <p>前端骨架已就位。登录页与后台外壳将在步骤 4 接入。</p>
      <ul aria-label="workspace 包连通性">
        <li>{UI_PACKAGE}</li>
        <li>{API_PACKAGE}</li>
        <li>{REALTIME_PACKAGE}</li>
      </ul>
    </main>
  );
}
