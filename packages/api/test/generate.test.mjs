import { test } from "node:test"
import assert from "node:assert/strict"
import { mkdtempSync, writeFileSync, readFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { generateClient } from "../scripts/generate.mjs"

function tempSpec(spec) {
  const dir = mkdtempSync(join(tmpdir(), "pzd-api-"))
  const specPath = join(dir, "openapi.json")
  writeFileSync(specPath, JSON.stringify(spec), "utf8")
  return { specPath, outPath: join(dir, "client.ts") }
}

test("从 openapi.json 生成 TS 客户端占位文件", () => {
  const { specPath, outPath } = tempSpec({
    openapi: "3.1.0",
    info: { title: "Pzd API", version: "0.0.0" },
    paths: { "/api/health": { get: { operationId: "getHealth" } } },
  })

  generateClient({ specPath, outPath })

  const output = readFileSync(outPath, "utf8")
  assert.match(output, /Pzd API/, "应写出接口标题，证明读的是这份 openapi.json")
  assert.match(output, /"\/api\/health"/, "应列出 openapi.json 里的路径")
  assert.match(output, /0\.0\.0/, "应写出接口版本")
})

test("openapi.json 缺 paths 时报错而不是静默生成空文件", () => {
  const { specPath, outPath } = tempSpec({ openapi: "3.1.0", info: { title: "Pzd API", version: "0.0.0" } })

  assert.throws(() => generateClient({ specPath, outPath }), /paths/)
})
