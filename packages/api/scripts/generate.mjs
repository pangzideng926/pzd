import { readFileSync, writeFileSync, mkdirSync } from "node:fs"
import { dirname, resolve } from "node:path"
import { fileURLToPath } from "node:url"

const here = dirname(fileURLToPath(import.meta.url))

/**
 * 读 openapi.json，生成 TS 客户端占位文件。
 * 本票（02）只保证「generate 读的是这条 openapi.json」这条链路是通的；
 * 真正的 orval 客户端、Query hooks 与 zod schema 由 28 号票替换这里的实现。
 */
export function generateClient({ specPath, outPath }) {
  const spec = JSON.parse(readFileSync(specPath, "utf8"))
  if (!spec.paths || typeof spec.paths !== "object") {
    throw new Error(`openapi.json 缺少 paths：${specPath}`)
  }

  const title = spec.info?.title ?? "Pzd API"
  const version = spec.info?.version ?? "0.0.0"
  const paths = Object.keys(spec.paths).sort()
  const pathList = paths.map((path) => `  ${JSON.stringify(path)},`).join("\n")

  const output = `// 由 packages/api/scripts/generate.mjs 生成，勿手改。
// 输入：openapi.json　来源：${title} ${version}
// TODO(28 号票)：替换为 orval 生成的 client、Query hooks 与 zod schema。

export const apiTitle = ${JSON.stringify(title)}
export const apiVersion = ${JSON.stringify(version)}
export const apiPaths = [
${pathList}
] as const

export type ApiPath = (typeof apiPaths)[number]
`

  mkdirSync(dirname(outPath), { recursive: true })
  writeFileSync(outPath, output, "utf8")
}

const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)
if (isMain) {
  const specPath = resolve(here, "..", "openapi.json")
  const outPath = resolve(here, "..", "src", "generated", "client.ts")
  generateClient({ specPath, outPath })
  console.log(`api#generate: ${specPath} -> ${outPath}`)
}
