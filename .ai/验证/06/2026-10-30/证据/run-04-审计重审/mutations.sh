#!/usr/bin/env bash
# 06 重审（213753a）独立变异：每处改动必须让 PersistenceTests 变红
set -uo pipefail
cd /e/pzd/.worktree/audit-06b || exit 1
LOG=/e/pzd/.tmp/2026-10-30-audit-06b/mutation-results.txt
: > "$LOG"

suite() {
  ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe \
    -class Pzd.IntegrationTests.PersistenceTests > /tmp/mut.log 2>&1
  local exit_code=$?
  local line
  line=$(grep -E "^   Pzd.IntegrationTests  Total:" /tmp/mut.log | tail -1)
  echo "  exit=$exit_code | $line"
  return "$exit_code"
}

build_or_die() {
  dotnet build Pzd.slnx > /tmp/build.log 2>&1
  local exit_code=$?
  echo "  BUILD_EXIT=$exit_code"
  if [ "$exit_code" -ne 0 ]; then
    grep -E "error" /tmp/build.log | head -3
    echo "  ！！构建失败，变异结果不可用"
    return 1
  fi
  return 0
}

judge() {
  local name=$1
  if grep -qE "Failed: [1-9]" /tmp/mut.log; then
    echo "$name 判定: 红 ✅" | tee -a "$LOG"
  else
    echo "$name 判定: 绿 ❌（用例没抓住）" | tee -a "$LOG"
  fi
  grep -E "\[FAIL\]" /tmp/mut.log | sed -E 's/^ *//' | head -3 | tee -a "$LOG"
}

echo "=== 基线 ===" | tee -a "$LOG"
build_or_die | tee -a "$LOG"
suite | tee -a "$LOG"

python3() { python "$@"; }

# M1 去掉租户谓词
p1=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace(\'            where += $" AND `{tenantColumn.DbColumnName}`=@tenantId";\',"            // mutated")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M1 去掉租户谓词 ===" | tee -a "$LOG"
patch() { python3 -c "$1"; }
patch "$p1"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M1
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# M2 把 CreatedAt/CreatedBy 放回 SET
p2=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace(\'        PersistenceOptions.Columns.CreatedAt,\\n        PersistenceOptions.Columns.CreatedBy,\\n\',\'\')\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M2 创建审计回到 SET ===" | tee -a "$LOG"
patch "$p2"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M2
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# M3 不拦物理删除
p3=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ni=s.index("                case DataFilterType.DeleteByObject:")\nj=s.index("            }",i)\ns=s[:i]+"                case DataFilterType.DeleteByObject:\\n                    break;\\n"+s[j:]\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M3 不拦 DeleteByObject ===" | tee -a "$LOG"
patch "$p3"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M3
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

# M4 软删除不写 DeletedAt/DeletedBy
p4=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace(\'            PersistenceOptions.Columns.DeletedAt when deleting => stamp.UpdatedAt,\\n            PersistenceOptions.Columns.DeletedBy when deleting => stamp.UpdatedBy,\\n\',\'\')\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M4 软删除不写 DeletedAt/DeletedBy ===" | tee -a "$LOG"
patch "$p4"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M4
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# M5 冲突前就落实体
p5=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace(\'        var affected = await db.Ado.ExecuteCommandAsync(sql, parameters, cancellationToken).ConfigureAwait(false);\',\'        PersistenceAudit.ApplyUpdateStamp(entity, stamp, markDeleted);\\n        var affected = await db.Ado.ExecuteCommandAsync(sql, parameters, cancellationToken).ConfigureAwait(false);\')\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M5 冲突前就落实体 ===" | tee -a "$LOG"
patch "$p5"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M5
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# M6 忽略 worker 配置
p6=$'import io\np="src/Pzd.Infrastructure/PersistenceIdentity.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("if (options.WorkerId is { } workerId && options.DatacenterId is { } datacenterId)","if (false)")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M6 忽略 worker 配置 ===" | tee -a "$LOG"
patch "$p6"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M6
git checkout -- src/Pzd.Infrastructure/PersistenceIdentity.cs

# M7 markDeleted 语义退化成 deleting（复现我 Q1 发现的混用）
p7=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), markDeleted)","PersistenceAudit.ComputeUpdateStamp(entity, options.CurrentUserId(), deleting)")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M7 ComputeUpdateStamp 传 deleting ===" | tee -a "$LOG"
patch "$p7"; build_or_die | tee -a "$LOG" && suite | tee -a "$LOG"; judge M7
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

echo "=== 还原复核 ===" | tee -a "$LOG"
build_or_die | tee -a "$LOG"
suite | tee -a "$LOG"
git status --short | tee -a "$LOG"
