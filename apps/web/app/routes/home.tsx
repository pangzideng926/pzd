export function meta() {
  return [{ title: "PZD 后台管理系统" }];
}

/**
 * 票 02 的占位页：只证明 monorepo、路由与构建链路通。
 * 真正的登录页与后台外壳在步骤 4（票 03 之后的前端外壳票）替换。
 */
export default function Home() {
  return (
    <main style={{ fontFamily: "system-ui, sans-serif", padding: "2rem" }}>
      <h1>PZD 后台管理系统</h1>
      <p>前端骨架已就位。登录页与后台外壳将在步骤 4 接入。</p>
    </main>
  );
}
