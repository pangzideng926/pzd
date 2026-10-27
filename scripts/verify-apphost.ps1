#!/usr/bin/env pwsh
# 票 03 一键验收。
#
# 两种模式：
#   -Mode api     （默认）直接起 src/Pzd.Api，探测三个免租户端点 + 前端首屏。
#                 本沙箱里 Aspire 的 DCP 编排无法启动（见 foundation §9 偏差记录），所以默认走这条。
#   -Mode aspire  起 src/Pzd.AppHost，由 Aspire 编排 API 与 Vite。CI / 有权限的机器上用这条。
#
# 用法：
#   pwsh -NoProfile -File scripts/verify-apphost.ps1
#   pwsh -NoProfile -File scripts/verify-apphost.ps1 -SkipWeb
#   pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode aspire
#
# 连接串只经环境变量注入（ConnectionStrings__mysql / ConnectionStrings__redis），脚本不回显密码；
# 缺凭据时 /health/ready 会是 Degraded，脚本按失败处理并提示怎么补。

[CmdletBinding()]
param(
    [ValidateSet("api", "aspire")]
    [string]$Mode = "api",
    [int]$TimeoutSeconds = 240,
    [string]$ApiBaseUrl = "http://127.0.0.1:5080",
    [switch]$SkipWeb
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

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

Import-LocalEnv (Join-Path $repoRoot "local.env")

function Get-Env([string]$name) {
    return [Environment]::GetEnvironmentVariable($name, "Process")
}

# 显式注入的连接串优先；否则按 local.env 的键拼出来（密码不回显）。
if (-not (Get-Env "ConnectionStrings__mysql")) {
    $hostName = if (Get-Env "PZD_MYSQL_HOST") { Get-Env "PZD_MYSQL_HOST" } else { "127.0.0.1" }
    $port = if (Get-Env "PZD_MYSQL_PORT") { Get-Env "PZD_MYSQL_PORT" } else { "3306" }
    $user = if (Get-Env "PZD_MYSQL_USER") { Get-Env "PZD_MYSQL_USER" } else { "root" }
    $password = Get-Env "PZD_MYSQL_PASSWORD"
    $master = if (Get-Env "PZD_MYSQL_MASTER_DB") { Get-Env "PZD_MYSQL_MASTER_DB" } else { "pzd_master" }

    if ($password) {
        [Environment]::SetEnvironmentVariable(
            "ConnectionStrings__mysql",
            "Server=$hostName;Port=$port;User ID=$user;Password=$password;Database=$master;",
            "Process")
    }
}

if (-not (Get-Env "ConnectionStrings__redis")) {
    $redisHost = if (Get-Env "PZD_REDIS_HOST") { Get-Env "PZD_REDIS_HOST" } else { "127.0.0.1" }
    $redisPort = if (Get-Env "PZD_REDIS_PORT") { Get-Env "PZD_REDIS_PORT" } else { "6379" }
    [Environment]::SetEnvironmentVariable("ConnectionStrings__redis", "$redisHost`:$redisPort", "Process")
}

if (-not (Get-Env "ConnectionStrings__mysql")) {
    Write-Host "[warn] ConnectionStrings__mysql 未设置：/health/ready 会是 Degraded，本次判失败。" -ForegroundColor Yellow
    Write-Host "       把密码写进 local.env 的 PZD_MYSQL_PASSWORD，或先导出 ConnectionStrings__mysql。" -ForegroundColor Yellow
}

$logDir = Join-Path $repoRoot "artifacts"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Stop-ProcessTree {
    param([int]$ProcessId)

    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-ProcessTree -ProcessId $_.ProcessId }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Wait-ForUrl {
    param([string]$Url, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ""

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec 5 -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) { return $response }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 750
    }

    throw "等待 $Url 超时（${TimeoutSeconds}s），最后一次错误：$lastError"
}

if ($Mode -eq "aspire") {
    $target = "src/Pzd.AppHost/Pzd.AppHost.csproj"
    $logPath = Join-Path $logDir "verify-apphost.log"
    Write-Host "模式 aspire：dotnet run --project $target（日志 $logPath）"
}
else {
    $target = "src/Pzd.Api/Pzd.Api.csproj"
    $logPath = Join-Path $logDir "verify-api.log"
    # 直接起 API 时没有 Aspire 的 WithHttpEndpoint，端口靠 ASPNETCORE_URLS 定死，否则会用默认 5000。
    [Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", $ApiBaseUrl, "Process")
    Write-Host "模式 api：dotnet run --project $target（$ApiBaseUrl，日志 $logPath）"

    # 前端单独起一个进程，等价于 Aspire 里的 AddViteApp（本模式不编排，只保证同一条命令能验到前端）。
    if (-not $SkipWeb) {
        $webLog = Join-Path $logDir "verify-web.log"
        Write-Host "同一起动 web：pnpm --filter @pzd/web dev（日志 $webLog）"
        # Windows 上 pnpm 是 .cmd 垫片，Start-Process 必须点名 pnpm.cmd，直接写 "pnpm" 会报「不是有效的 Win32 应用」。
        $webProcess = Start-Process -FilePath "pnpm.cmd" -ArgumentList @("--filter", "@pzd/web", "dev") `
            -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
            -RedirectStandardOutput $webLog -RedirectStandardError "$webLog.err"
    }
}

$hostProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $target, "--no-build") `
    -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
    -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

$failures = [System.Collections.Generic.List[string]]::new()

try {
    $live = Wait-ForUrl "$ApiBaseUrl/health/live" $TimeoutSeconds
    Write-Host "[ok] /health/live 200（免租户）"

    $ready = Invoke-WebRequest -Uri "$ApiBaseUrl/health/ready" -TimeoutSec 10 -SkipHttpErrorCheck
    $readyBody = $ready.Content | ConvertFrom-Json

    if ($ready.StatusCode -ne 200) { $failures.Add("/health/ready 返回 $($ready.StatusCode)，期望 200（Degraded 也必须 200）") }

    foreach ($check in @("mysql", "redis")) {
        $status = $readyBody.checks.$check.status
        if ($status -ne "Healthy") { $failures.Add("/health/ready 的 $check 是 '$status'，期望 Healthy") }
        else { Write-Host "[ok] /health/ready $check = Healthy" }
    }

    $version = Invoke-WebRequest -Uri "$ApiBaseUrl/api/version" -TimeoutSec 10 -SkipHttpErrorCheck
    $versionBody = $version.Content | ConvertFrom-Json

    if ([string]::IsNullOrWhiteSpace($versionBody.version)) { $failures.Add("/api/version 缺 version") }
    if ([string]::IsNullOrWhiteSpace($versionBody.buildId)) { $failures.Add("/api/version 缺 buildId") }
    if (-not $versionBody.buildTime) { $failures.Add("/api/version 缺 buildTime") }
    if ($failures.Count -eq 0) { Write-Host "[ok] /api/version $($versionBody.version)+$($versionBody.buildId)" }

    # 豁免之外仍要有租户：无租户访问 / 必须 400 Tenant.NotFound。
    $rootProbe = Invoke-WebRequest -Uri "$ApiBaseUrl/" -TimeoutSec 10 -SkipHttpErrorCheck
    if ($rootProbe.StatusCode -ne 400) { $failures.Add("/ 返回 $($rootProbe.StatusCode)，期望 400（豁免只覆盖三个端点）") }
    else { Write-Host "[ok] / 无租户仍 400" }

    if (-not $SkipWeb) {
        $web = Wait-ForUrl "http://127.0.0.1:5199/" $TimeoutSeconds
        if ($web.Content -notmatch 'lang="zh-CN"') { $failures.Add("前端首屏缺 SPA 标记，可能不是 React Router 外壳") }
        else { Write-Host "[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳" }
    }
}
catch {
    $failures.Add($_.Exception.Message)
    if (Test-Path "$logPath.err") {
        Write-Host "---- 宿主 stderr 尾部 ----" -ForegroundColor Yellow
        Get-Content "$logPath.err" -Tail 20 | ForEach-Object { Write-Host $_ }
    }
}
finally {
    Stop-ProcessTree -ProcessId $hostProcess.Id
    if ($webProcess) { Stop-ProcessTree -ProcessId $webProcess.Id }
    Start-Sleep -Milliseconds 500
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "验收失败：" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "验收通过：/health/live、/health/ready（mysql+redis Up）、/api/version、豁免边界$([bool]$SkipWeb ? '' : '、前端首屏')" -ForegroundColor Green
exit 0
