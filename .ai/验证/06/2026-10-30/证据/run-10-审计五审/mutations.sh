#!/usr/bin/env bash
# 06 五审独立变异：验证四审两条 medium 的修复是否真被用例钉住。
set -uo pipefail

WT="/e/pzd/.worktree/audit-06f"
OUT="/e/pzd/.tmp/2026-10-30-06五审/mutation-results.txt"
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
  (cd tests/Pzd.IntegrationTests/bin/Debug/net10.0 && ./Pzd.IntegrationTests.exe -class "*PersistenceTests" > "/e/pzd/.tmp/2026-10-30-06五审/$label.log" 2>&1)
  local test_exit=$?
  echo "[$label] TEST_EXIT=$test_exit  $(grep -a 'Total:' "/e/pzd/.tmp/2026-10-30-06五审/$label.log" | head -1 | sed 's/^[[:space:]]*//')" | tee -a "$OUT"
  echo "[$label] 红用例（按字节取，名字可能是 GBK）：" | tee -a "$OUT"
  grep -a "\[FAIL\]" "/e/pzd/.tmp/2026-10-30-06五审/$label.log" | sed 's/^[[:space:]]*/    /' | tee -a "$OUT"
  perl -ne 'print "    译文: $1\n" if /PersistenceTests\.([^ ]+) \[FAIL\]/ && ($1) =~ /\xe4|\xe5|\xe6|\xe7|\xe8|\xe9/' "/e/pzd/.tmp/2026-10-30-06五审/$label.log" >> "$OUT" 2>/dev/null
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

echo "== M-A：去掉插入侧 IsDeleted=true 拒绝 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceAudit.cs \
'                if (ReadBool(entity.EntityValue!, PersistenceOptions.Columns.IsDeleted))
                {
                    throw new InvalidOperationException(
                        $"插入「{entity.EntityName}」时实体带 IsDeleted=true：插入入口只写活跃记录。"
                        + "要删除请先 PersistenceWriter.InsertAsync 再调用 PersistenceWriter.SoftDeleteAsync，"
                        + "否则会写出「删除态但 DeletedToken=0」的幽灵行，它挡住唯一索引却在应用里查不到。");
                }

' '' | tee -a "$OUT"
run_persist "MA-insert-allows-deleted"
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

echo "== M-B：Update 重新写删除四列 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'            // Update 一个删除列都不写：它们只能由软删除 / 恢复入口改写（06 四审 medium 2 / C10）。
            if (kind == WriteKind.Update && DeleteColumns.Contains(column.PropertyName, StringComparer.Ordinal))
            {
                continue;
            }

' '' | tee -a "$OUT"
run_persist "MB-update-writes-delete-cols"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== M-C：Update 不拒删除态实体 ==" | tee -a "$OUT"
py src/Pzd.Infrastructure/PersistenceWriter.cs \
'            if (ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted))
            {
                throw new InvalidOperationException(
                    $"类型「{typeof(T).Name}」的这条记录处于删除态，不能用 UpdateAsync 更新："
                    + "删除态只能经 PersistenceWriter.SoftDeleteAsync 标记、经 PersistenceWriter.RestoreAsync 恢复。");
            }

' '' | tee -a "$OUT"
run_persist "MC-update-allows-deleted"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "== 复原核对 ==" | tee -a "$OUT"
git status --porcelain src tests | tee -a "$OUT"
