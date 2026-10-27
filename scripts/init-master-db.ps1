#!/usr/bin/env pwsh
# 票 09：幂等初始化 master 库并建 Quartz AdoJobStore 表。
#
# 只做三件事：
#   1) 幂等创建 pzd_master（utf8mb4/utf8mb4_unicode_ci）；
#   2) 若 QRTZ_* 表尚不存在，执行 db/master/quartz_tables.sql 建表；
#   3) 若表已存在，校验 15 张表齐全后直接跳过（不 DROP、不重建 —— 票面验收要求
#      「重启 API 后 HeartbeatJob 仍在」，任何删除都会销毁那份持久化证据）。
#
# 安全约束（票 09）：
#   - 只建/改 pzd_master 这一个库，库名带 pzd_ 前缀校验，不碰其他库；
#   - 凭据只从环境变量或仓库根 local.env 读取，连接串与密码一律不回显、不入日志。
#
# 退出码（与 scripts/verify-apphost.ps1 同口径）：
#   0 = 就绪（新建或已存在）；1 = 执行失败；2 = 环境/凭据缺失等预检受阻。

[CmdletBinding()]
param(
    # 覆盖 master 库名，默认取 PZD_MYSQL_MASTER_DB，再缺省为 pzd_master。
    [string]$MasterDb = "",
    [int]$ConnectTimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sqlPath = Join-Path $repoRoot "db/master/quartz_tables.sql"

# Quartz 4.x 的 QRTZ_* 官方表清单（tables_mysql_innodb.sql），用于幂等校验与事后核对。
$expectedTables = @(
    "QRTZ_JOB_DETAILS", "QRTZ_TRIGGERS", "QRTZ_SIMPLE_TRIGGERS", "QRTZ_CRON_TRIGGERS",
    "QRTZ_SIMPROP_TRIGGERS", "QRTZ_BLOB_TRIGGERS", "QRTZ_CALENDARS", "QRTZ_PAUSED_TRIGGER_GRPS",
    "QRTZ_PAUSED_JOB_GRPS", "QRTZ_FIRED_TRIGGERS", "QRTZ_SCHEDULER_STATE", "QRTZ_LOCKS",
    "QRTZ_EXECUTION_HISTORY", "QRTZ_MISFIRE_HISTORY", "QRTZ_JOB_STATUS"
)

function Import-LocalEnv {
    param([string]$Path)

    if (-not (Test-Path $Path)) { return }

    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ($line -eq "" -or $line.StartsWith("#")) { return }
        $eq = $line.IndexOf("=")
        if ($eq -lt 1) { return }
        $key = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1).Trim()
        if ($value -eq "") { return }
        [Environment]::SetEnvironmentVariable($key, $value, "Process")
    }
}

function Get-Env([string]$name) {
    return [Environment]::GetEnvironmentVariable($name, "Process")
}

# MySqlConnector 的 DLL 直接在 NuGet 缓存里取，不依赖 mysql 命令行客户端（本机没有）。
# 选中与目标框架最接近的 lib 目录；版本取缓存里最高的。
function Resolve-MySqlConnectorAssembly {
    $root = Join-Path $env:USERPROFILE ".nuget/packages/mysqlconnector"
    if (-not (Test-Path $root)) { return $null }

    $versionDirs = Get-ChildItem -LiteralPath $root -Directory |
        Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
        Sort-Object { [version]$_.Name } -Descending

    foreach ($versionDir in $versionDirs) {
        foreach ($tfm in @("net10.0", "net9.0", "net8.0", "net6.0", "netstandard2.1")) {
            $candidate = Join-Path $versionDir.FullName "lib/$tfm/MySqlConnector.dll"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    return $null
}

# Add-Type 只加载 MySqlConnector.dll 本身是不够的：它的类型初始化器会去找
# Microsoft.Extensions.Logging.Abstractions（程序集版本 8.0.0.0），pwsh 默认加载上下文里没有，
# 于是报 The type initializer for 'MySqlConnectorLoggingConfiguration' threw an exception。
# 这里先把 8.x 的两份抽象程序集从 NuGet 缓存显式 LoadFrom，再加载 MySqlConnector。
function Import-MySqlConnector {
    param([string]$DllPath)

    $dependencies = @(
        @{ Package = "microsoft.extensions.logging.abstractions"; File = "Microsoft.Extensions.Logging.Abstractions.dll" },
        @{ Package = "microsoft.extensions.dependencyinjection.abstractions"; File = "Microsoft.Extensions.DependencyInjection.Abstractions.dll" }
    )

    foreach ($dependency in $dependencies) {
        $packageRoot = Join-Path $env:USERPROFILE ".nuget/packages/$($dependency.Package)"
        if (-not (Test-Path $packageRoot)) { continue }

        $versionDir = Get-ChildItem -LiteralPath $packageRoot -Directory |
            Where-Object { $_.Name -match '^8\.\d+(\.\d+)+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1
        if (-not $versionDir) { continue }

        foreach ($tfm in @("net8.0", "netstandard2.0")) {
            $dependencyPath = Join-Path $versionDir.FullName "lib/$tfm/$($dependency.File)"
            if (Test-Path $dependencyPath) {
                [void][System.Reflection.Assembly]::LoadFrom($dependencyPath)
                break
            }
        }
    }

    Add-Type -Path $DllPath
}

# MySqlConnector 没有 MySql.Data 的 MySqlScript：这里自己把脚本切成单条语句，
# 再在同一连接上逐条执行（SET @var / PREPARE / EXECUTE 需要共享连接的用户变量）。
# 切分必须认识单引号、双引号、反引号与 -- / # 行注释，否则 DROP 清单里带逗号的字符串会被切坏。
function Split-SqlStatements {
    param([string]$Sql)

    $statements = [System.Collections.Generic.List[string]]::new()
    $current = [System.Text.StringBuilder]::new()
    $quote = [char]0
    $inLineComment = $false

    for ($i = 0; $i -lt $Sql.Length; $i++) {
        $ch = $Sql[$i]

        if ($inLineComment) {
            if ($ch -eq "`n") {
                $inLineComment = $false
                [void]$current.Append($ch)
            }
            continue
        }

        if ($quote -ne [char]0) {
            [void]$current.Append($ch)
            if ($ch -eq '\' -and $i + 1 -lt $Sql.Length) {
                $i++
                [void]$current.Append($Sql[$i])
                continue
            }
            if ($ch -eq $quote) {
                # '' / "" / `` 是转义，不是结束
                if ($i + 1 -lt $Sql.Length -and $Sql[$i + 1] -eq $quote) {
                    $i++
                    [void]$current.Append($Sql[$i])
                    continue
                }
                $quote = [char]0
            }
            continue
        }

        if ($ch -eq "'" -or $ch -eq '"' -or $ch -eq '`') {
            $quote = $ch
            [void]$current.Append($ch)
            continue
        }

        if ($ch -eq '#' -or ($ch -eq '-' -and $i + 1 -lt $Sql.Length -and $Sql[$i + 1] -eq '-')) {
            $inLineComment = $true
            continue
        }

        if ($ch -eq ';') {
            $statement = $current.ToString().Trim()
            if ($statement) { $statements.Add($statement) }
            [void]$current.Clear()
            continue
        }

        [void]$current.Append($ch)
    }

    $tail = $current.ToString().Trim()
    if ($tail) { $statements.Add($tail) }

    return $statements
}

function New-ConnectionString {
    param([string]$Database)

    # AllowUserVariables：quartz_tables.sql 用 SET @DropDb / PREPARE 这类用户变量。
    $builder = [MySqlConnector.MySqlConnectionStringBuilder]::new()
    $builder.Server = Get-Env "PZD_MYSQL_HOST"
    $builder.Port = [uint32](Get-Env "PZD_MYSQL_PORT")
    $builder.UserID = Get-Env "PZD_MYSQL_USER"
    $builder.Password = Get-Env "PZD_MYSQL_PASSWORD"
    $builder.AllowUserVariables = $true
    $builder.ConnectionTimeout = [uint32]$ConnectTimeoutSeconds
    if ($Database) { $builder.Database = $Database }
    return $builder.ConnectionString
}

Import-LocalEnv (Join-Path $repoRoot "local.env")

if (-not (Test-Path $sqlPath)) {
    Write-Host "[fail] 找不到建表脚本：$sqlPath" -ForegroundColor Red
    exit 1
}

$masterDb = if ($MasterDb) { $MasterDb } elseif (Get-Env "PZD_MYSQL_MASTER_DB") { Get-Env "PZD_MYSQL_MASTER_DB" } else { "pzd_master" }
$hostName = if (Get-Env "PZD_MYSQL_HOST") { Get-Env "PZD_MYSQL_HOST" } else { "127.0.0.1" }
$portText = if (Get-Env "PZD_MYSQL_PORT") { Get-Env "PZD_MYSQL_PORT" } else { "3306" }
$user = Get-Env "PZD_MYSQL_USER"
$password = Get-Env "PZD_MYSQL_PASSWORD"

# 预检：凭据缺失或库名越界都归 2（环境受阻），避免把「没配凭据」报成产品缺陷。
if (-not $user -or -not $password) {
    Write-Host "[preflight] 缺少 MySQL 凭据：请在仓库根 local.env 填 PZD_MYSQL_USER / PZD_MYSQL_PASSWORD（该文件不入库）。" -ForegroundColor Red
    exit 2
}

if ($masterDb -notmatch '^pzd_[a-z0-9_]+$') {
    Write-Host "[preflight] 库名「$masterDb」不符合 pzd_ 前缀约束：本脚本只允许操作 pzd_* 库。" -ForegroundColor Red
    exit 2
}

$assemblyPath = Resolve-MySqlConnectorAssembly
if (-not $assemblyPath) {
    Write-Host "[preflight] 在 NuGet 缓存里找不到 MySqlConnector.dll：先 dotnet restore 再跑。" -ForegroundColor Red
    exit 2
}

Write-Host "初始化 $masterDb（$hostName`:$portText，用户 $user，凭据来自 local.env/环境变量）"
Import-MySqlConnector -DllPath $assemblyPath

# ---- 建库（幂等）----
try {
    $serverConnectionString = New-ConnectionString -Database $null
    $connection = [MySqlConnector.MySqlConnection]::new($serverConnectionString)
    $connection.Open()

    $createDb = $connection.CreateCommand()
    $createDb.CommandText = "CREATE DATABASE IF NOT EXISTS ``$masterDb`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
    [void]$createDb.ExecuteNonQuery()
    Write-Host "[ok] 库 $masterDb 就绪（CREATE DATABASE IF NOT EXISTS）"
}
catch {
    Write-Host "[fail] 连接或建库失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host "       检查 MySQL 是否在跑、凭据是否有效、账号是否有 CREATE 权限。" -ForegroundColor Yellow
    exit 1
}
finally {
    if ($connection) { $connection.Dispose() }
}

# ---- 查现有 Quartz 表 ----
function Get-QuartzTables {
    param([string]$Database)

    $connection = [MySqlConnector.MySqlConnection]::new((New-ConnectionString -Database $Database))
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT TABLE_NAME FROM information_schema.TABLES
WHERE TABLE_SCHEMA = @db AND TABLE_NAME LIKE 'QRTZ\_%'
"@
        [void]$command.Parameters.AddWithValue("@db", $Database)
        $reader = $command.ExecuteReader()
        $tables = [System.Collections.Generic.List[string]]::new()
        while ($reader.Read()) { $tables.Add($reader.GetString(0)) }
        $reader.Dispose()
        return $tables
    }
    finally {
        $connection.Dispose()
    }
}

try {
    $existing = @(Get-QuartzTables -Database $masterDb)
}
catch {
    Write-Host "[fail] 读取 information_schema 失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if ($existing.Count -gt 0) {
    $missing = @($expectedTables | Where-Object { $existing -notcontains $_ })
    if ($missing.Count -eq 0) {
        Write-Host "[skip] $masterDb 已有全部 $($existing.Count) 张 QRTZ_* 表：幂等跳过，不删表、不重建（保留已注册任务）"
        exit 0
    }

    # 半成品 schema（部分表在、部分表缺）不自动 DROP：那会把已注册的任务一起删掉，
    # 与「重启后仍在」的验收直接冲突。留给人来定，脚本只报缺什么。
    Write-Host "[fail] $masterDb 里的 Quartz 表不完整（已存在 $($existing.Count) 张），缺：$($missing -join ', ')" -ForegroundColor Red
    Write-Host "       脚本不会自动 DROP 半成品 schema（会丢已注册任务）；确认可弃后手工清理再用本脚本重建。" -ForegroundColor Yellow
    exit 1
}

# ---- 建表 ----
try {
    $statements = @(Split-SqlStatements -Sql (Get-Content -LiteralPath $sqlPath -Raw))
    $connection = [MySqlConnector.MySqlConnection]::new((New-ConnectionString -Database $masterDb))
    $connection.Open()
    try {
        $index = 0
        foreach ($statement in $statements) {
            $index++
            $command = $connection.CreateCommand()
            $command.CommandText = $statement
            $command.CommandTimeout = 60
            try {
                [void]$command.ExecuteNonQuery()
            }
            catch {
                Write-Host "[fail] 建表失败（第 $index/$($statements.Count) 条语句）：$($_.Exception.Message)" -ForegroundColor Red
                Write-Host "       语句开头：$($statement.Substring(0, [Math]::Min(120, $statement.Length)))" -ForegroundColor Yellow
                exit 1
            }
        }
    }
    finally {
        $connection.Dispose()
    }
    Write-Host "[ok] 已执行 db/master/quartz_tables.sql（$($statements.Count) 条语句）"
}
catch {
    Write-Host "[fail] 建表失败：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$created = @(Get-QuartzTables -Database $masterDb)
$stillMissing = @($expectedTables | Where-Object { $created -notcontains $_ })
if ($stillMissing.Count -gt 0) {
    Write-Host "[fail] 建表后仍缺：$($stillMissing -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "[ok] $masterDb 的 $($created.Count) 张 QRTZ_* 表齐全"
Write-Host "下一步：起 API 后查 QRTZ_JOB_DETAILS 应能看到 HeartbeatJob；重启后再查一次仍在，即为持久化证据。"
exit 0
