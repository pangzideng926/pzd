#!/usr/bin/env bash
# 06 审计变异：每个变异必须让 PersistenceTests 变红，否则说明用例没钉住行为
set -u
cd /e/pzd/.worktree/audit-06 || exit 1
LOG=/e/pzd/.tmp/2026-10-30-audit-06/mutation-results.txt
: > "$LOG"
run_suite() {
  ./tests/Pzd.IntegrationTests/bin/Debug/net10.0/Pzd.IntegrationTests.exe \
    -class Pzd.IntegrationTests.PersistenceTests 2>&1 | grep -E "^   Pzd.IntegrationTests  Total:"
}
build() {
  dotnet build Pzd.slnx 2>&1 | grep -E "个错误"
}
patch() { python -c "$1"; }

echo "=== 基线 ===" | tee -a "$LOG"
build | tee -a "$LOG"
run_suite | tee -a "$LOG"

# M1：UpdateAsync 去掉版本条件
p1=$'import io\np="src/Pzd.Infrastructure/PersistenceWriter.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace(\'WHERE `{primary.DbColumnName}`=@id AND `{version.DbColumnName}`=@version\',\'WHERE `{primary.DbColumnName}`=@id\')\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M1 更新 WHERE 去掉 Version ===" | tee -a "$LOG"
patch "$p1"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M1 判定: 红（被抓住）" | tee -a "$LOG" || echo "M1 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceWriter.cs

# M2：UTC 换成本地时间
p2=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("var now = DateTime.UtcNow;","var now = DateTime.Now;")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M2 时间改成本地时间 ===" | tee -a "$LOG"
patch "$p2"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M2 判定: 红（被抓住）" | tee -a "$LOG" || echo "M2 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

# M3：软删除时不写 DeletedToken
p3=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("""                if (ReadBool(entity.EntityValue, PersistenceOptions.Columns.IsDeleted))\n                {\n                    entity.SetValue(ReadLong(entity.EntityValue, PersistenceOptions.Columns.Id));\n                }""","""                _ = entity;""")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M3 软删除不写 DeletedToken ===" | tee -a "$LOG"
patch "$p3"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M3 判定: 红（被抓住）" | tee -a "$LOG" || echo "M3 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

# M4：不登记软删除过滤
p4=$'import io\np="src/Pzd.Infrastructure/PersistenceSoftDelete.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("        foreach (var type in softDeleteTypes)","        foreach (var type in Array.Empty<Type>())")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M4 不登记软删除过滤 ===" | tee -a "$LOG"
patch "$p4"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M4 判定: 红（被抓住）" | tee -a "$LOG" || echo "M4 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceSoftDelete.cs

# M5：插入不设版本初值
p5=$'import io\np="src/Pzd.Infrastructure/PersistenceAudit.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("""            case PersistenceOptions.Columns.Version:\n                entity.SetValue(1);\n                break;""","""            case PersistenceOptions.Columns.Version:\n                break;""")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M5 插入不设 Version=1 ===" | tee -a "$LOG"
patch "$p5"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M5 判定: 红（被抓住）" | tee -a "$LOG" || echo "M5 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceAudit.cs

# M6：1062 分类失效
p6=$'import io\np="src/Pzd.Infrastructure/PersistenceErrors.cs"\ns=io.open(p,encoding="utf-8").read()\ns=s.replace("if (current is MySqlException { Number: DuplicateEntryErrorNumber })","if (current is MySqlException { Number: 999999 })")\nio.open(p,"w",encoding="utf-8",newline="\\n").write(s)'
echo "=== M6 1062 分类失效 ===" | tee -a "$LOG"
patch "$p6"
build | tee -a "$LOG"
m=$(run_suite | tee -a "$LOG"); grep -q "Failed: [1-9]" <<<"$m" && echo "M6 判定: 红（被抓住）" | tee -a "$LOG" || echo "M6 判定: 绿（用例没抓住！）" | tee -a "$LOG"
git checkout -- src/Pzd.Infrastructure/PersistenceErrors.cs

echo "=== 还原后复核 ===" | tee -a "$LOG"
build | tee -a "$LOG"
run_suite | tee -a "$LOG"
git status --short | tee -a "$LOG"
