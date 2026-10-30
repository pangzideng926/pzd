#!/usr/bin/env bash
# 06 三审（dddcb58）独立变异：每处改动必须让 PersistenceTests 变红
set -uo pipefail
cd /e/pzd/.worktree/audit-06d || exit 1
LOG=/e/pzd/.tmp/2026-10-30-audit-06d/mutation-results.txt
: > "$LOG"

build_or_die() {
  dotnet build Pzd.slnx > /tmp/build.log 2>&1
  local code=$?
  echo "  BUILD_EXIT=$code" | tee -a "$LOG"
  if [ "$code" -ne 0 ]; then
    grep -E "error" /tmp/build.log | head -2 | tee -a "$LOG"
    echo "  ！！构建失败，本轮结果作废" | tee -a "$LOG"
    return 1
  fi
  return 0
}

suite_and_judge() {
  local name=$1
  ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe \
    -class Pzd.IntegrationTests.PersistenceTests > /tmp/mut.log 2>&1
  local code=$?
  grep -E "^   Pzd.IntegrationTests  Total:" /tmp/mut.log | tail -1 | tee -a "$LOG"
  echo "  exit=$code" | tee -a "$LOG"
  if [ "$code" -ne 0 ] && grep -qE "Failed: [1-9]" /tmp/mut.log; then
    echo "$name 判定: 红 ✅" | tee -a "$LOG"
    grep -E "\[FAIL\]" /tmp/mut.log | sed -E 's/^ *//' | head -4 | tee -a "$LOG"
  else
    echo "$name 判定: 绿 ❌（用例没抓住）" | tee -a "$LOG"
  fi
}

patched() {
  python -c "$1" && build_or_die && suite_and_judge "$2"
}

echo "=== 基线 ===" | tee -a "$LOG"
build_or_die && suite_and_judge "基线"

p1=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("            PersistenceOptions.Columns.IsDeleted => stamp.MarkDeleted,","            PersistenceOptions.Columns.IsDeleted => (bool?)column.PropertyInfo.GetValue(entity) ?? false,")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N1 IsDeleted 用实体值而非语义 ===" | tee -a "$LOG"
patched "$p1" N1
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

p2=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("        if (clearDeleted)\\n        {\\n            return new UpdateStamp(now, currentUserId, MarkDeleted: false, DeletedToken: 0L, DeletedAt: null, DeletedBy: 0L);\\n        }\\n\\n","")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N2 恢复不清 token ===" | tee -a "$LOG"
patched "$p2" N2
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

p3=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("                ApplyTenantOnInsert(entity, options);","                break;")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N3 去掉插入租户校验 ===" | tee -a "$LOG"
patched "$p3" N3
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

p4=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("                if (ReadLong(entity.EntityValue!, PersistenceOptions.Columns.Version) <= 0)","                if (false)")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N4 去掉旁路版本防御 ===" | tee -a "$LOG"
patched "$p4" N4
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

p5=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("                    if (IsHardDeleteAllowed(entity, options.HardDeleteTypes))","                    if (true)")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N5 物理删除白名单恒放行 ===" | tee -a "$LOG"
patched "$p5" N5
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

p6=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("        var markDeleted = kind switch\\n        {\\n            WriteKind.SoftDelete => true,\\n            WriteKind.Update => ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted),\\n            _ => false,\\n        };","        var markDeleted = ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted);")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== N6 SoftDeleteAsync 不算软删除 ===" | tee -a "$LOG"
patched "$p6" N6
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "=== 还原复核 ===" | tee -a "$LOG"
build_or_die && suite_and_judge "还原"
git status --short | tee -a "$LOG"
