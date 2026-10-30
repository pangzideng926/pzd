# 本机建 pzd_test 专用账号，并写好集成测试用的连接串文件。
# 只做三件事：建账号 / 授权 / 写本机连接串（.local 已被 .gitignore 忽略，不进版本库）。
# 口令由脚本随机生成；root 口令只在进程内用一次，不落盘、不出现在命令行参数里。

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$mysqlExe = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
if (-not (Test-Path -LiteralPath $mysqlExe)) {
    Write-Host ('[x] mysql.exe not found at: ' + $mysqlExe) -ForegroundColor Red
    exit 1
}

$chars = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'.ToCharArray()
$builder = New-Object System.Text.StringBuilder
foreach ($i in 1..20) {
    $idx = Get-Random -Minimum 0 -Maximum $chars.Count
    [void]$builder.Append($chars[$idx])
}
$pzdPw = $builder.ToString()

Write-Host 'MySQL root password (input hidden): ' -NoNewline -ForegroundColor Cyan
$secure = Read-Host -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$rootPw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

$lines = @(
    ('CREATE USER IF NOT EXISTS ''pzd_test''@''localhost'' IDENTIFIED BY ''{0}'';' -f $pzdPw)
    ('ALTER USER ''pzd_test''@''localhost'' IDENTIFIED BY ''{0}'';' -f $pzdPw)
    'GRANT ALL PRIVILEGES ON `pzd\_test\_%`.* TO ''pzd_test''@''localhost'';'
    'GRANT CREATE, DROP ON *.* TO ''pzd_test''@''localhost'';'
    'FLUSH PRIVILEGES;'
)
$sqlText = $lines -join [Environment]::NewLine

$env:MYSQL_PWD = $rootPw
try {
    $out = $sqlText | & $mysqlExe -h 127.0.0.1 -u root --default-character-set=utf8mb4 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[x] CREATE USER failed. Raw output:' -ForegroundColor Red
        Write-Host ([string]::Join([Environment]::NewLine, $out))
        Write-Host '[i] Check the root password, and that the MySQL80 service is running.' -ForegroundColor Yellow
        exit 1
    }
}
finally {
    $env:MYSQL_PWD = $null
    $rootPw = $null
}

$env:MYSQL_PWD = $pzdPw
try {
    $check = 'SELECT VERSION();' | & $mysqlExe -h 127.0.0.1 -u pzd_test --default-character-set=utf8mb4 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[x] pzd_test self-check failed:' -ForegroundColor Red
        Write-Host ([string]::Join([Environment]::NewLine, $check))
        exit 1
    }
    Write-Host '[ok] pzd_test login works, version:' -ForegroundColor Green
    Write-Host ([string]::Join([Environment]::NewLine, $check))
}
finally {
    $env:MYSQL_PWD = $null
}

$conn = ('Server=127.0.0.1;Port=3306;User ID=pzd_test;Password=' + $pzdPw + ';')
foreach ($dir in @('E:\pzd\.local', 'E:\pzd\.worktree\36\.local')) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $file = Join-Path $dir 'mysql-test-connection.txt'
    Set-Content -LiteralPath $file -Value $conn -Encoding ASCII
    Write-Host ('[ok] connection string written: ' + $file) -ForegroundColor Green
}

Write-Host ''
Write-Host '[done] Say "db ready" in the group. No need to share the password.' -ForegroundColor Green
