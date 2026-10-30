#!/usr/bin/env bash
# 06 四审独立变异：验证两条新防线是否真被用例钉住。用法：bash mutations.sh
set -uo pipefail

WT="/e/pzd/.worktree/audit-06e"
OUT="/e/pzd/.tmp/2026-10-30-06四审/mutation-results.txt"
cd "$WT" || exit 1

export PZD_TEST_MYSQL_CONNECTION="$(cat .local/mysql-test-connection.txt)"

run_persist() {
  local label="$1"
  set -o pipefail
  dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj 2>&1 | tail -4
  local build_exit=${PIPESTATUS[0]}
  echo "[$label] BUILD_EXIT=$build_exit" | tee -a "$OUT"
  if [ "$build_exit" -ne 0 ]; then
    echo "[$label] 构建失败，按群规不算红，需重做" | tee -a "$OUT"
    return 1
  fi
  (cd tests/Pzd.IntegrationTests/bin/Debug/net10.0 && ./Pzd.IntegrationTests.exe -class "*PersistenceTests" > "/e/pzd/.tmp/2026-10-30-06四审/$label.log" 2>&1)
  local test_exit=$?
  local total
  total=$(iconv -f GBK -t UTF-8 "/e/pzd/.tmp/2026-10-30-06四审/$label.log" | grep -a "Total:" | head -1)
  echo "[$label] TEST_EXIT=$test_exit  $total" | tee -a "$OUT"
  echo "[$label] 红用例：" | tee -a "$OUT"
  iconv -f GBK -t UTF-8 "/e/pzd/.tmp/2026-10-30-06四审/$label.log" | grep -a "\[FAIL\]" | sed 's/^/    /' | tee -a "$OUT"
}

py() { python - "$@" <<'PYEOF'
import sys, io
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
data = io.open(path, encoding='utf-8').read()
if old not in data:
    print("PATCH_MISS: 未找到目标文本")
    sys.exit(2)
io.open(path, 'w', encoding='utf-8', newline='').write(data.replace(old, new, 1))
print("PATCH_OK")
PYEOF
}

: > "$OUT"
echo "== 基线（未变异）==" | tee -a "$OUT"
run_persist "M0-baseline"

# --- M-A：把 high 1 退回上一版「只拦 Version<=0」 ---
echo "== M-A：UpdateByObject 退回只拦 Version<=0 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceAudit.cs \
'                    throw new InvalidOperationException(
                        $"类型「{entity.EntityName}」的更新必须走 PersistenceWriter.UpdateAsync / SoftDeleteAsync / RestoreAsync："
                        + "直接 db.Updateable 不带版本条件与租户谓词，会把整行搬到别的租户、抹掉创建审计，并让 Version 失去意义。");' \
'                    if (ReadLong(entity.EntityValue!, PersistenceOptions.Columns.Version) <= 0)
                    {
                        throw new InvalidOperationException("版本小于等于 0，拒绝旁路更新。");
                    }

                    break;' | tee -a "$OUT"
run_persist "MA-version-only"
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

# --- M-B：整块删掉回收站误用校验（high 2） ---
echo "== M-B：去掉 UpdateAsync 的回收站误用校验 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        // 回收站里的记录只能经 RestoreAsync 复活。走 UpdateAsync 把 IsDeleted 改回 false 的话，
        // DeletedToken 会带着主键一起写回，那条就成了「活跃但占着已删 token」的记录，
        // 唯一索引当场失效、同名活跃记录可以有两条（06 三返复验 P6）。
        if (kind == WriteKind.Update
            && !markDeleted
            && ReadLong(info, entity, PersistenceOptions.Columns.DeletedToken) != 0)
        {
            throw new InvalidOperationException(
                $"类型「{typeof(T).Name}」的这条记录仍在回收站里（{PersistenceOptions.Columns.DeletedToken} 不为 0），"
                + "不能通过 UpdateAsync 改回活跃；请调用 PersistenceWriter.RestoreAsync。");
        }

' '' | tee -a "$OUT"
run_persist "MB-no-guard"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# --- M-C：把校验极性反过来（!=0 改成 ==0），看正路会不会被误伤 ---
echo "== M-C：校验极性反转 DeletedToken == 0 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'            && ReadLong(info, entity, PersistenceOptions.Columns.DeletedToken) != 0)' \
'            && ReadLong(info, entity, PersistenceOptions.Columns.DeletedToken) == 0)' | tee -a "$OUT"
run_persist "MC-inverted"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== 复原核对 ==" | tee -a "$OUT"
git status --porcelain src tests | tee -a "$OUT"
echo "(除 AuditRound4Probes.cs 探针外应为空)" | tee -a "$OUT"
