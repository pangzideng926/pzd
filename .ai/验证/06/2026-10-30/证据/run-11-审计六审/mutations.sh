#!/usr/bin/env bash
# 06 六审独立变异：验证五审两条修复是否真被用例钉住。
set -uo pipefail

WT="/e/pzd/.worktree/audit-06g"
OUT="/e/pzd/.tmp/2026-10-30-06六审/mutation-results.txt"
cd "$WT" || exit 1
export PZD_TEST_MYSQL_CONNECTION="$(cat .local/mysql-test-connection.txt)"

run_persist() {
  local label="$1"
  set -o pipefail
  dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj 2>&1 | tail -3
  local build_exit=${PIPESTATUS[0]}
  echo "[$label] BUILD_EXIT=$build_exit" | tee -a "$OUT"
  if [ "$build_exit" -ne 0 ]; then
    echo "[$label] 构建失败，按群规不算红，需重做" | tee -a "$OUT"
    return 1
  fi
  (cd tests/Pzd.IntegrationTests/bin/Debug/net10.0 && ./Pzd.IntegrationTests.exe -class "*PersistenceTests" > "/e/pzd/.tmp/2026-10-30-06六审/$label.log" 2>&1)
  local test_exit=$?
  echo "[$label] TEST_EXIT=$test_exit  $(grep -a 'Total:' "/e/pzd/.tmp/2026-10-30-06六审/$label.log" | head -1 | sed 's/^[[:space:]]*//')" | tee -a "$OUT"
  grep -a "\[FAIL\]" "/e/pzd/.tmp/2026-10-30-06六审/$label.log" | sed 's/^[[:space:]]*/    /' | tee -a "$OUT"
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
echo "== 基线 ==" | tee -a "$OUT"
run_persist "M0-baseline"

echo "== M-1：Update 的 WHERE 去掉活跃条件（只留恢复的 =1）==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        if (isDeletedColumn is not null)
        {
            where += kind == WriteKind.Restore
                ? $" AND `{isDeletedColumn.DbColumnName}`=1"
                : $" AND `{isDeletedColumn.DbColumnName}`=0";
        }' \
'        if (isDeletedColumn is not null && kind == WriteKind.Restore)
        {
            where += $" AND `{isDeletedColumn.DbColumnName}`=1";
        }' | tee -a "$OUT"
run_persist "M1-no-active-condition"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== M-2：affected==0 不分型（IsRecycledAsync 恒 false）==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        return count > 0;' \
'        return count < 0;' | tee -a "$OUT"
run_persist "M2-no-classification"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== M-3：Update 也回写内存实体的删除四列 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        PersistenceAudit.ApplyUpdateStamp(entity, stamp, touchDeleteColumns: kind != WriteKind.Update);' \
'        PersistenceAudit.ApplyUpdateStamp(entity, stamp, touchDeleteColumns: true);' | tee -a "$OUT"
run_persist "M3-touch-delete-columns"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== 复原核对 ==" | tee -a "$OUT"
git status --porcelain src tests | tee -a "$OUT"
