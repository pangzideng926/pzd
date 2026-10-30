import subprocess, os, sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

conn = {}
with open(r"E:\pzd\.local\mysql-test-connection.txt", encoding="utf-8") as f:
    raw = f.read().strip()
for part in raw.replace("\r", "").replace("\n", "").split(";"):
    if "=" not in part:
        continue
    k, v = part.split("=", 1)
    conn[k.strip()] = v.strip()

mysql = r"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"
args = [
    mysql,
    "-h", conn.get("Server", "localhost"),
    "-P", conn.get("Port", "3306"),
    "-u", conn.get("User ID", "root"),
    "-N", "-B",
    "-e", "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME LIKE 'pzd_test_%'",
]
env = dict(os.environ)
env["MYSQL_PWD"] = conn.get("Password", "")

p = subprocess.run(args, capture_output=True, env=env)
out = p.stdout.decode("utf-8", errors="replace").strip()
rows = [r for r in out.splitlines() if r.strip()]
print(f"MYSQL_EXIT={p.returncode}")
print(f"RESIDUAL_DB_COUNT={len(rows)}")
for r in rows:
    print("  " + r)
if p.returncode != 0:
    print("STDERR: " + p.stderr.decode("utf-8", errors="replace")[:500])
