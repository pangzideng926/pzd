#!/usr/bin/env pwsh
# 票 03 一键验收。
#
# 两种模式：
#   -Mode aspire  （默认，票面口径）起 src/Pzd.AppHost，由 Aspire 编排 API 与 Vite。
#                 验收命令就是 `pwsh -NoProfile -File scripts/verify-apphost.ps1`。
#   -Mode api     兜底：不编排，直接起 src/Pzd.Api 并单独拉一个 pnpm dev。
#                 用于 Aspire DCP 完全起不来的受限环境；不是票面口径，结论要注明是兜底模式。
#
# 用法：
#   pwsh -NoProfile -File scripts/verify-apphost.ps1
#   pwsh -NoProfile -File scripts/verify-apphost.ps1 -SkipWeb
#   pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode api
#
# 连接串只经环境变量注入（ConnectionStrings__mysql / ConnectionStrings__redis），脚本不回显密码；
# 缺凭据时 /health/ready 会是 Degraded，脚本按失败处理并提示怎么补。

[CmdletBinding()]
param(
    [ValidateSet("api", "aspire")]
    [string]$Mode = "aspire",
    [ValidateSet("Auto", "Healthy", "Degraded")]
    [string]$Expect = "Auto",
    [int]$TimeoutSeconds = 240,
    [string]$ApiBaseUrl = "http://127.0.0.1:5080",
    [switch]$SkipWeb,
    # 跳过 local.env 并清空连接串：本机用来复现「无实例 → Degraded」这条票面验收。
    # CI 本来就没有 local.env，不加这个开关也是 Degraded。
    [switch]$NoCredentials
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

if ($NoCredentials) {
    # 复现无实例路径：不读 local.env，也不让外部环境残留的连接串漏进来。
    $env:ConnectionStrings__mysql = $null
    $env:ConnectionStrings__redis = $null
}
else {
    Import-LocalEnv (Join-Path $repoRoot "local.env")
}

function Get-Env([string]$name) {
    return [Environment]::GetEnvironmentVariable($name, "Process")
}

# 显式注入的连接串优先；否则按 local.env 的键拼出来（密码不回显）。
# -NoCredentials 时两个都不拼：否则 redis 会靠默认值拼出 127.0.0.1:6379，
# 本机 Memurai 在跑就会变成「redis=Healthy / mysql=Degraded」的混合态，验不出整条降级路径。
if ($NoCredentials) {
    Write-Host "[info] -NoCredentials：跳过硬编码默认值，mysql/redis 连接串都留空。" -ForegroundColor DarkGray
}
elseif (-not (Get-Env "ConnectionStrings__mysql")) {
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

if (-not $NoCredentials -and -not (Get-Env "ConnectionStrings__redis")) {
    $redisHost = if (Get-Env "PZD_REDIS_HOST") { Get-Env "PZD_REDIS_HOST" } else { "127.0.0.1" }
    $redisPort = if (Get-Env "PZD_REDIS_PORT") { Get-Env "PZD_REDIS_PORT" } else { "6379" }
    [Environment]::SetEnvironmentVariable("ConnectionStrings__redis", "$redisHost`:$redisPort", "Process")
}

if (-not (Get-Env "ConnectionStrings__mysql")) {
    Write-Host "[warn] ConnectionStrings__mysql 未设置：本次按无实例处理，走 Degraded 断言。" -ForegroundColor Yellow
    Write-Host "       要把这条路验成 Healthy，往 local.env 的 PZD_MYSQL_PASSWORD 填密码，或先导出 ConnectionStrings__mysql。" -ForegroundColor Yellow
}

# 票 03 ④：有实例时必须 Healthy（只由真跑实例的脚本断言），无实例时必须 Degraded 且 200。
# Auto 按凭据有无决定；CI 无凭据跑同一条命令就是验降级路径，不需要额外参数。
$effectiveExpect = $Expect
if ($Expect -eq "Auto") {
    $effectiveExpect = if (Get-Env "ConnectionStrings__mysql") { "Healthy" } else { "Degraded" }
}
Write-Host "期望 /health/ready 模式：$effectiveExpect" -ForegroundColor Cyan

$logDir = Join-Path $repoRoot "artifacts"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

function Stop-ProcessTree {
    param([int]$ProcessId)

    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-ProcessTree -ProcessId $_.ProcessId }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Remove-StaleDcpState {
    # 受限令牌环境下 DCP 偶发「apiserver 从未就绪」（EnsureKubernetesAsync 2 分钟超时），
    # 上一次被强杀的 dcp.exe / 通知套接字 / 会话目录就是最常见的诱因，启动前先清掉。
    Get-Process dcp, Aspire.Dashboard -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $env:TEMP -Filter "aspire-dcp*" -Directory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $env:LOCALAPPDATA "dcp\dcp-notify-sock-*") -Force -ErrorAction SilentlyContinue
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

    # Aspire 的 DCP 会在 TEMP 下建会话目录（kubeconfig、日志套接字）。受限令牌写系统 TEMP 会被
    # UnauthorizedAccessException 拒掉、宿主启动即崩（"Access to the path ... is denied."），
    # 所以把 TEMP/TMP 指到工作区内已 gitignore 的 .aspire-tmp/ 再启动 AppHost。
    $tempDir = Join-Path $repoRoot ".aspire-tmp"
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
    [Environment]::SetEnvironmentVariable("TEMP", $tempDir, "Process")
    [Environment]::SetEnvironmentVariable("TMP", $tempDir, "Process")
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

$failures = [System.Collections.Generic.List[string]]::new()

# 启动 + 等 /health/live：aspire 模式允许重试一次（先清 DCP 残留），把「受限令牌下 DCP 偶发不就绪」
# 与真正的启动失败区分开；两次都起不来才算失败，且把第二次的宿主日志留给人看。
$maxAttempts = if ($Mode -eq "aspire") { 2 } else { 1 }
$hostProcess = $null
$live = $null
$startError = $null

for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    if ($Mode -eq "aspire") { Remove-StaleDcpState }
    if ($attempt -gt 1) { Write-Host "[retry] 第 $attempt 次启动（已清理 DCP 残留）" -ForegroundColor Yellow }

    $hostProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $target, "--no-build") `
        -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
        -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

    try {
        $live = Wait-ForUrl "$ApiBaseUrl/health/live" $TimeoutSeconds
        break
    }
    catch {
        $startError = $_.Exception.Message
        $live = $null
        Stop-ProcessTree -ProcessId $hostProcess.Id
        Start-Sleep -Milliseconds 800
    }
}

if (-not $live) {
    $failures.Add($startError)
    if (Test-Path "$logPath.err") {
        Write-Host "---- 宿主 stderr 尾部 ----" -ForegroundColor Yellow
        Get-Content "$logPath.err" -Tail 20 | ForEach-Object { Write-Host $_ }
    }
    if ($Mode -eq "aspire") {
        Write-Host "如仍失败，可用兜底口径复跑：pwsh -NoProfile -File scripts/verify-apphost.ps1 -Mode api" -ForegroundColor Yellow
    }
}

try {
    if ($live) {
        Write-Host "[ok] /health/live 200（免租户）"

    $ready = Invoke-WebRequest -Uri "$ApiBaseUrl/health/ready" -TimeoutSec 10 -SkipHttpErrorCheck
    $readyBody = $ready.Content | ConvertFrom-Json

    if ($ready.StatusCode -ne 200) { $failures.Add("/health/ready 返回 $($ready.StatusCode)，期望 200（Degraded 也必须 200）") }

    # 票 03 老登②：有实例时根状态必须是 Healthy，无实例时必须是 Degraded（两者都要 200）。
    # 这两条只能由真跑实例的脚本分别断言；单元测试只守 200 + 免租户 + JSON 结构，
    # 禁止「Healthy 和 Degraded 都算过」那种断言（本票唯一的假绿风险点）。
    if ($readyBody.status -ne $effectiveExpect) {
        $failures.Add("/health/ready 根状态是 '$($readyBody.status)'，期望 $effectiveExpect")
    }
    else { Write-Host "[ok] /health/ready 根状态 = $effectiveExpect" }

    foreach ($check in @("mysql", "redis")) {
        $status = $readyBody.checks.$check.status
        if ($status -ne $effectiveExpect) {
            $failures.Add("/health/ready 的 $check 是 '$status'，期望 $effectiveExpect")
        }
        else { Write-Host "[ok] /health/ready $check = $effectiveExpect" }
    }

    # 票 03 ④：降级日志必须真有出处（不接受「代码里应该会打」）；降级路径下把原文贴出来。
    # aspire 模式下 API 的 stdout 由 DCP 接管，不会落到 AppHost 的日志文件里，
    # 所以警告文本这条证据由 `-Mode api` 取证；这里只做状态断言。
    if ($effectiveExpect -eq "Degraded") {
        $degradedLines = Select-String -Path $logPath -Pattern "Degraded mode:" -SimpleMatch -ErrorAction SilentlyContinue
        if ($degradedLines) {
            Write-Host "---- 启动期降级日志（$($degradedLines.Count) 行）----" -ForegroundColor Yellow
            $degradedLines | ForEach-Object { Write-Host "  $($_.Line.Trim())" }
        }
        elseif ($Mode -eq "api") {
            $failures.Add("启动日志里没有 'Degraded mode:' 警告行（$logPath），④ 缺证据")
        }
        else {
            Write-Host "[note] aspire 模式下 API 日志由 DCP 接管；降级警告原文用 -Mode api -NoCredentials 取证。" -ForegroundColor DarkGray
        }
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
}
catch {
    $failures.Add($_.Exception.Message)
    if (Test-Path "$logPath.err") {
        Write-Host "---- 宿主 stderr 尾部 ----" -ForegroundColor Yellow
        Get-Content "$logPath.err" -Tail 20 | ForEach-Object { Write-Host $_ }
    }
}
finally {
    if ($hostProcess) { Stop-ProcessTree -ProcessId $hostProcess.Id }
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
Write-Host "验收通过（/health/ready 期望模式：$effectiveExpect）：/health/live 200、/health/ready 根与 mysql/redis 明细均为 $effectiveExpect、/api/version 三字段、豁免之外仍 400$([bool]$SkipWeb ? '' : '、前端 127.0.0.1:5199 SPA 外壳')" -ForegroundColor Green
exit 0
