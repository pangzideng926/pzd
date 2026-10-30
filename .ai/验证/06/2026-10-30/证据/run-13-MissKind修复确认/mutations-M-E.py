import subprocess, sys, os

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

WT = r"E:\pzd\.worktree\audit-06i"
WRITER = os.path.join(WT, "src", "Pzd.Infrastructure", "PersistenceWriter.cs")
TEST_EXE = os.path.join(WT, "tests", "Pzd.IntegrationTests", "bin", "Debug", "net10.0", "Pzd.IntegrationTests.exe")

with open(WRITER, "rb") as f:
    orig_bytes = f.read()
text = orig_bytes.decode("utf-8")

A = ('        if (tenantColumn is not null)\r\n'
     '        {\r\n'
     '            parameters.Add(new SugarParameter("@tenantId", currentTenantId));')
A2 = ('        if (tenantColumn is not null && kind != WriteKind.Restore)\r\n'
      '        {\r\n'
      '            parameters.Add(new SugarParameter("@tenantId", currentTenantId));')

print(f"anchor_count={text.count(A)}")
assert text.count(A) == 1

with open(WRITER, "wb") as f:
    f.write(text.replace(A, A2).encode("utf-8"))

def run(cmd):
    p = subprocess.run(cmd, cwd=WT, capture_output=True)
    return p.returncode, (p.stdout + p.stderr).decode("gbk", errors="replace")

b, bout = run(["dotnet", "build", "Pzd.slnx"])
if b != 0:
    print(f"[M-E 仅 Restore 丢租户条件] BUILD_EXIT={b} 构建失败")
    for line in bout.splitlines():
        if "error" in line.lower():
            print("   " + line.strip())
    out_line = f"[M-E 仅 Restore 丢租户条件] BUILD_EXIT={b} 构建失败，按群规不算红"
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
    out_line = f"[M-E 仅 Restore 丢租户条件] TEST_EXIT={code} {total} " + ("| FAILS: " + " ;; ".join(fails) if fails else "| 无失败 → Restore 跨租户无用例守")
    print(out_line)

with open(WRITER, "wb") as f:
    f.write(orig_bytes)

b, _ = run(["dotnet", "build", "Pzd.slnx"])
code, out = run([TEST_EXE, "-class", "Pzd.IntegrationTests.PersistenceTests"])
total = [s.strip() for s in out.splitlines() if "Total:" in s]
nfail = sum(1 for s in out.splitlines() if "[FAIL]" in s)
print(f"[M-E 还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={nfail}")

with open(r"E:\pzd\.ai\验证\06\2026-10-30\证据\run-13-MissKind修复确认\mutation-E-missing.txt", "w", encoding="utf-8") as f:
    f.write(out_line + f"\n[M-E 还原复跑] BUILD_EXIT={b} TEST_EXIT={code} {total} FAILS={nfail}\n")
