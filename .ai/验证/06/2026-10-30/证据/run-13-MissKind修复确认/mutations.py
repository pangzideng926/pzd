import subprocess, sys, io, os, shutil

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

WT = r"E:\pzd\.worktree\audit-06i"
WRITER = os.path.join(WT, "src", "Pzd.Infrastructure", "PersistenceWriter.cs")
TEST_EXE = os.path.join(WT, "tests", "Pzd.IntegrationTests", "bin", "Debug", "net10.0", "Pzd.IntegrationTests.exe")

def read(p):
    with io.open(p, encoding="utf-8") as f:
        return f.read()

def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)

def run(cmd, cwd=WT):
    p = subprocess.run(cmd, cwd=cwd, capture_output=True)
    out = (p.stdout + p.stderr).decode("gbk", errors="replace")
    return p.returncode, out

def build():
    code, out = run(["dotnet", "build", "Pzd.slnx"])
    return code

def tests():
    code, out = run([TEST_EXE, "-class", "Pzd.IntegrationTests.PersistenceTests"])
    total = ""
    fails = []
    for line in out.splitlines():
        s = line.strip()
        if "Total:" in s:
            total = s
        if "[FAIL]" in s:
            fails.append(s)
    return code, total, fails

MUTATIONS = [
    ("[M-A 恢复实体自报守卫加回去]",
     '        ArgumentNullException.ThrowIfNull(options);\n\n        return WriteAsync(db, entity, options, WriteKind.Restore, cancellationToken);',
     '        ArgumentNullException.ThrowIfNull(options);\n\n        var info = db.EntityMaintenance.GetEntityInfo<T>();\n        if (!ReadBool(info, entity, PersistenceOptions.Columns.IsDeleted))\n        {\n            throw new InvalidOperationException("mutant-guard");\n        }\n\n        return WriteAsync(db, entity, options, WriteKind.Restore, cancellationToken);'),

    ("[M-B NotInTrash 合并成 RecordInTrash]",
     'return PersistenceErrors.NotInTrash();',
     'return PersistenceErrors.RecordInTrash();'),

    ("[M-C 三态字符串 Recycled/Active 对调]",
     "WHEN {deletedSum}>0 THEN 'Recycled' ELSE 'Active' END",
     "WHEN {deletedSum}>0 THEN 'Active' ELSE 'Recycled' END"),

    ("[M-D NoRow 吞成 Active]",
     'return Enum.TryParse<MissKind>(text, ignoreCase: false, out var kind)\n            ? kind',
     'return Enum.TryParse<MissKind>(text, ignoreCase: false, out var kind) && kind != MissKind.NoRow\n            ? kind\n            : MissKind.Active'),
]

orig = read(WRITER)
results = []

for name, old, new in MUTATIONS:
    cnt = orig.count(old)
    if cnt != 1:
        results.append(f"{name} SKIP 锚点命中 {cnt} 次，变异无效")
        print(results[-1])
        continue
    write(WRITER, orig.replace(old, new))
    b = build()
    if b != 0:
        results.append(f"{name} BUILD_EXIT={b} 构建失败，按群规不算红")
        write(WRITER, orig)
        print(results[-1])
        continue
    code, total, fails = tests()
    results.append(f"{name} TEST_EXIT={code} {total} " + ("| FAILS: " + " ;; ".join(fails) if fails else "| 无失败(变异未被捕获)"))
    write(WRITER, orig)
    print(results[-1])

# 还原后复跑
b = build()
code, total, fails = tests()
print(f"[还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={len(fails)}")

with io.open(r"E:\pzd\.tmp\2026-10-30-misskind-audit\mutation-results.txt", "w", encoding="utf-8") as f:
    f.write("\n".join(results) + f"\n[还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={len(fails)}\n")
