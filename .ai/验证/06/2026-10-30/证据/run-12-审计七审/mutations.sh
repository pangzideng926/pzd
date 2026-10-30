#!/usr/bin/env bash
# 06 七审独立变异：验证四种返回值与规范化是否真被用例钉住。
set -uo pipefail

WT="/e/pzd/.worktree/audit-06h"
OUT="/e/pzd/.tmp/2026-10-30-06七审/mutation-results.txt"
cd "$WT" || exit 1
export PZD_TEST_MYSQL_CONNECTION="$(cat .local/mysql-test-connection.txt)"

run_persist() {
  local label="$1"
  set -o pipefail
  dotnet build tests/Pzd.IntegrationTests/Pzd.IntegrationTests.csproj 2>&1 | tail -3
  local build_exit=${PIPESTATUS[0]}
  echo "[$label] BUILD_EXIT=$build_exit" | tee -a "$OUT"
  if [ "$build_exit" -ne 0 ]; then
    echo "[$label] 构建失败，按群规不算红" | tee -a "$OUT"
    return 1
  fi
  (cd tests/Pzd.IntegrationTests/bin/Debug/net10.0 && ./Pzd.IntegrationTests.exe -class "*PersistenceTests" > "/e/pzd/.tmp/2026-10-30-06七审/$label.log" 2>&1)
  local test_exit=$?
  echo "[$label] TEST_EXIT=$test_exit  $(grep -a 'Total:' "/e/pzd/.tmp/2026-10-30-06七审/$label.log" | head -1 | sed 's/^[[:space:]]*//')" | tee -a "$OUT"
  grep -a "\[FAIL\]" "/e/pzd/.tmp/2026-10-30-06七审/$label.log" | sed 's/^[[:space:]]*/    /' | tee -a "$OUT"
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

echo "== M-A：三态不分型（一律当成活跃行）==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        return code switch
        {
            0 => MissKind.NoRow,
            2 => MissKind.Recycled,
            _ => MissKind.Active,
        };' \
'        return code switch
        {
            _ => MissKind.Active,
        };' | tee -a "$OUT"
run_persist "MA-no-classification"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== M-B：只对 Update 分型（SoftDelete 回落版本冲突）==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'            if (miss == MissKind.Recycled && kind != WriteKind.Restore)' \
'            if (miss == MissKind.Recycled && kind == WriteKind.Update)' | tee -a "$OUT"
run_persist "MB-update-only"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== M-C：Update 不规范化（stamp 退回按实体原值算）==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'        var stamp = kind == WriteKind.Update
            ? PersistenceAudit.ActiveUpdateStamp(options.CurrentUserId())
            : PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted, clearDeleted);' \
'        var stamp = PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted, clearDeleted);' | tee -a "$OUT"
run_persist "MC-no-normalize"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== 复原核对 ==" | tee -a "$OUT"
git status --porcelain src tests | tee -a "$OUT"
