import subprocess, sys, os

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

WT = r"E:\pzd\.worktree\audit-06i"
WRITER = os.path.join(WT, "src", "Pzd.Infrastructure", "PersistenceWriter.cs")
TEST_EXE = os.path.join(WT, "tests", "Pzd.IntegrationTests", "bin", "Debug", "net10.0", "Pzd.IntegrationTests.exe")

with open(WRITER, "rb") as f:
    orig_bytes = f.read()
text = orig_bytes.decode("utf-8")

A = 'return Enum.TryParse<MissKind>(text, ignoreCase: false, out var kind)'
A2 = 'return Enum.TryParse<MissKind>(text, ignoreCase: false, out var kind) && kind != MissKind.NoRow'
B = '            : throw new InvalidOperationException($"三态查询返回了未知值「{text}」，三种事实必须由这条 SQL 自己说清。");'
B2 = '            : MissKind.Active;'

assert text.count(A) == 1, f"A 命中 {text.count(A)}"
assert text.count(B) == 1, f"B 命中 {text.count(B)}"

with open(WRITER, "wb") as f:
    f.write(text.replace(A, A2).replace(B, B2).encode("utf-8"))

def run(cmd):
    p = subprocess.run(cmd, cwd=WT, capture_output=True)
    return p.returncode, (p.stdout + p.stderr).decode("gbk", errors="replace")

b, bout = run(["dotnet", "build", "Pzd.slnx"])
if b != 0:
    print(f"[M-D NoRow 吞成 Active] BUILD_EXIT={b} 构建失败")
    for line in bout.splitlines():
        if "error" in line.lower():
            print("   " + line.strip())
    line_out = f"[M-D NoRow 吞成 Active] BUILD_EXIT={b} 构建失败，按群规不算红"
else:
    code, out = run([TEST_EXE, "-class", "Pzd.IntegrationTests.PersistenceTests"])
    total = ""
    fails = []
    for line in out.splitlines():
        s = line.strip()
        if "Total:" in s:
            total = s
        if "[FAIL]" in s:
            fails.append(s)
    line_out = f"[M-D NoRow 吞成 Active] TEST_EXIT={code} {total} " + ("| FAILS: " + " ;; ".join(fails) if fails else "| 无失败(变异未被捕获)")
    print(line_out)

with open(WRITER, "wb") as f:
    f.write(orig_bytes)

b, _ = run(["dotnet", "build", "Pzd.slnx"])
code, out = run([TEST_EXE, "-class", "Pzd.IntegrationTests.PersistenceTests"])
total = [s.strip() for s in out.splitlines() if "Total:" in s]
nfail = sum(1 for s in out.splitlines() if "[FAIL]" in s)
print(f"[M-D 还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={nfail}")

with open(r"E:\pzd\.tmp\2026-10-30-misskind-audit\mutation-results.txt", "a", encoding="utf-8") as f:
    f.write(line_out + f"\n[M-D 还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={nfail}\n")
