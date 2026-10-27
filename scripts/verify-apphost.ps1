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
    # 票 03 审计 low：原来丢弃了 PZD_REDIS_PASSWORD，有密码的实例一律连不上。
    $redisPassword = Get-Env "PZD_REDIS_PASSWORD"
    $redisConnection = if ($redisPassword) { "$redisHost`:$redisPort,password=$redisPassword" } else { "$redisHost`:$redisPort" }
    [Environment]::SetEnvironmentVariable("ConnectionStrings__redis", $redisConnection, "Process")
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

# F-03-A11：本次运行的唯一标记。子进程（dotnet run / AppHost）继承进程环境变量，
# AppHost 再把 PZD_RUN_ID 透传给 api 资源，API 从 /api/version 的 runId 原样回显。
# 它只在本次进程树里有，同一 hash 的残留实例不会有——这是脚本判定「200 归属」的唯一依据。
$runId = [guid]::NewGuid().ToString("N")
[Environment]::SetEnvironmentVariable("PZD_RUN_ID", $runId, "Process")
Write-Host "本次运行标记 runId=$runId" -ForegroundColor Cyan

function Stop-ProcessTree {
    param([int]$ProcessId)

    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-ProcessTree -ProcessId $_.ProcessId }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

function Remove-StaleDcpState {
    # 受限令牌环境下 DCP 偶发「apiserver 从未就绪」（EnsureKubernetesAsync 2 分钟超时），
    # 上一次被强杀的 dcp.exe / 通知套接字 / 会话目录就是最常见的诱因，启动前先清掉。
    # 只清**属于本仓库**的 dcp：无差别杀全机 dcp 会误伤其他会话正在跑的实例（F-03-A2 low）。
    Get-CimInstance Win32_Process -Filter "Name='dcp.exe' OR Name='Aspire.Dashboard.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($repoRoot) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    Get-ChildItem -Path $env:TEMP -Filter "aspire-dcp*" -Directory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $env:LOCALAPPDATA "dcp\dcp-notify-sock-*") -Force -ErrorAction SilentlyContinue
}

function Wait-ForUrl {
    param([string]$Url, [int]$TimeoutSeconds, [System.Diagnostics.Process]$ProcessToWatch)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = ""

    while ((Get-Date) -lt $deadline) {
        # 宿主自己崩掉时要立刻失败：否则残留 API 顶着端口，脚本会把「别人的 200」当成自己的结果
        # （F-03-A1：导出 Outbox__MaxAttempts=0 让宿主秒崩，脚本仍全绿）。
        if ($ProcessToWatch -and $ProcessToWatch.HasExited) {
            throw "启动的宿主进程已退出（exit code $($ProcessToWatch.ExitCode)），$Url 不是它提供的。"
        }

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
}

$failures = [System.Collections.Generic.List[string]]::new()

# 启动 + 等 /health/live：aspire 模式允许重试一次（先清 DCP 残留），把「受限令牌下 DCP 偶发不就绪」
# 与真正的启动失败区分开；两次都起不来才算失败，且把第二次的宿主日志留给人看。
$maxAttempts = if ($Mode -eq "aspire") { 2 } else { 1 }
$hostProcess = $null
$live = $null
$startError = $null

# 验票前先排除两种「不是代码问题」的假红，否则很容易把别人的进程或没装依赖当成本票缺陷：
#   1) 端口被别人的实例占着（strictPort 会正确拒绝，但验出来的是别人实例的结果）；
#   2) 前端没装依赖（AddViteApp 不跑 install，node_modules 缺失时 web 资源秒失败、连 node 进程都没有）。
function Assert-PortFree {
    param([int]$Port)

    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { return }

    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) {
        $proc = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        throw "端口 $Port 已被占用（pid $($listener.OwningProcess) $($proc.ProcessName)）。先关掉占用进程再跑，否则验到的是别人实例的结果。"
    }
}

function Get-PortListenerPid {
    param([int]$Port)

    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { return $null }

    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener) { return [int]$listener.OwningProcess }
    return $null
}

function Test-ProcessDescendsFrom {
    # F-03-A1 的正面证据：端口不仅要「启动前空闲」，监听它的进程还必须是从这次启动的宿主派生出来的。
    # 只靠预检 + 200 仍可能被中途抢占的进程骗过，这里沿 ParentProcessId 走上去核对血缘。
    param([int]$ChildPid, [int]$AncestorPid)

    $current = $ChildPid
    for ($depth = 0; $depth -lt 12 -and $current -gt 0; $depth++) {
        if ($current -eq $AncestorPid) { return $true }
        $info = Get-CimInstance Win32_Process -Filter "ProcessId=$current" -ErrorAction SilentlyContinue
        if (-not $info) { return $false }
        $current = [int]$info.ParentProcessId
    }
    return $false
}

try {
    Assert-PortFree ([uri]$ApiBaseUrl).Port
    if (-not $SkipWeb) { Assert-PortFree 5199 }

    if ($Mode -eq "aspire") {
        $webNodeModules = Join-Path $repoRoot "apps/web/node_modules"
        if (-not (Test-Path $webNodeModules)) {
            throw "apps/web/node_modules 不存在：AddViteApp 不会自动 install，前端会秒失败。先执行 pnpm install（或从已装好的副本复用）再跑。"
        }
    }
}
catch {
    Write-Host "[preflight] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "[preflight] 这是环境/占用问题，不是本票缺陷：清掉占用或装好依赖后重跑。" -ForegroundColor Yellow
    exit 2
}

# 票 03 审计 F-03-A2：脚本原来只带 --no-build 却从不构建，干净副本第一次跑必失败、
# 工作区有未提交改动时会验到过期二进制（报告 hash 与二进制对不上）。启动前显式构建。
Write-Host "构建 $target（-warnaserror）"
& dotnet build $target -warnaserror --nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) {
    # 构建失败是本票缺陷（产品/代码问题），按老何的三档归 1，不占环境预检的 2。
    Write-Host "[build] 构建失败（$target）：先修编译再验票。" -ForegroundColor Red
    exit 1
}

# F-03-B1：预检在前、启动在后。原来 api 模式在预检之前就把 pnpm dev 拉起来了，
# 于是 `Assert-PortFree 5199` 必然命中脚本自己的前端，报「端口已被占用（pid … node）」并 exit 2
# ——完整路径（含前端的那条）从一开始就不可能通过，还被误读成环境问题。
# 前端单独起一个进程，等价于 Aspire 里的 AddViteApp（本模式不编排，只保证同一条命令能验到前端）。
if ($Mode -ne "aspire" -and -not $SkipWeb) {
    $webLog = Join-Path $logDir "verify-web.log"
    Write-Host "同一起动 web：pnpm --filter @pzd/web dev（日志 $webLog）"
    # Windows 上 pnpm 是 .cmd 垫片，Start-Process 必须点名 pnpm.cmd，直接写 "pnpm" 会报「不是有效的 Win32 应用」。
    $webProcess = Start-Process -FilePath "pnpm.cmd" -ArgumentList @("--filter", "@pzd/web", "dev") `
        -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
        -RedirectStandardOutput $webLog -RedirectStandardError "$webLog.err"
}

for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    if ($Mode -eq "aspire") { Remove-StaleDcpState }
    if ($attempt -gt 1) { Write-Host "[retry] 第 $attempt 次启动（已清理 DCP 残留）" -ForegroundColor Yellow }

    $hostProcess = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $target, "--no-build") `
        -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
        -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

    try {
        $live = Wait-ForUrl "$ApiBaseUrl/health/live" $TimeoutSeconds $hostProcess
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

        # F-03-A1/A12：api 模式下 200 必须来自本次启动的进程树（残留实例顶端口时最容易踩；
        # 取不到监听 pid 也要判失败，不能让 `-and` 短路到 else 打印一个查不到的假事实）。
        # aspire 模式下 5080 由 DCP 代理持有，父链到不了本次宿主——那是「证据取错对象」（A11），
        # 所以 aspire 不走进程树，改由 /api/version 回显的本次运行标记判定（见下方 runId 断言）。
        if ($Mode -eq "api") {
            $listenerPid = Get-PortListenerPid ([uri]$ApiBaseUrl).Port
            if (-not $listenerPid) {
                $failures.Add("$($ApiBaseUrl) 取不到监听者 pid，无法确认 200 是否来自本次实例（宿主 pid $($hostProcess.Id)）")
            }
            elseif (-not (Test-ProcessDescendsFrom -ChildPid $listenerPid -AncestorPid $hostProcess.Id)) {
                $failures.Add("$($ApiBaseUrl) 的监听者 pid $listenerPid 不属于本次启动的进程树（宿主 pid $($hostProcess.Id)），200 不是本次实例给的")
            }
            else {
                Write-Host "[ok] $ApiBaseUrl 由本次启动的进程树监听（pid $listenerPid）"
            }
        }
        else {
            Write-Host "[note] aspire 模式：5080 由 DCP 代理持有，归属改用 /api/version 的 runId 判定（A11）。" -ForegroundColor DarkGray
        }

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

    # F-03-A11：归属证据。runId 由本脚本在启动前生成并注入进程环境，API 原样从 /api/version 回显；
    # 一比对就能区分「本次启动的进程」与「同一 hash 的残留实例」（后者拿不到这个值）。
    # 老何的口径：buildId 只证明版本相同，不能当归属；取不到证据也必须判失败。
    if ([string]::IsNullOrWhiteSpace($versionBody.runId)) {
        $failures.Add("/api/version 没有 runId：API 没收到本次运行的 PZD_RUN_ID，无法证明应答来自本次启动（F-03-A11）")
    }
    elseif ($versionBody.runId -ne $runId) {
        $failures.Add("/api/version 的 runId '$($versionBody.runId)' 与本次运行的 '$runId' 不符：5080 上是别的实例（同 hash 的残留也会这样）")
    }
    else {
        Write-Host "[ok] /api/version runId 与本次运行一致（$runId）：应答来自本次启动的进程"
    }

    # 豁免之外仍要有租户：无租户访问 / 必须 400 Tenant.NotFound。
    $rootProbe = Invoke-WebRequest -Uri "$ApiBaseUrl/" -TimeoutSec 10 -SkipHttpErrorCheck
    if ($rootProbe.StatusCode -ne 400) { $failures.Add("/ 返回 $($rootProbe.StatusCode)，期望 400（豁免只覆盖三个端点）") }
    else { Write-Host "[ok] / 无租户仍 400" }

    if (-not $SkipWeb) {
        try {
            # aspire 模式下前端是宿主的子资源：宿主崩了就别再等 240 秒。
            $watch = if ($Mode -eq "aspire") { $hostProcess } else { $null }
            $web = Wait-ForUrl "http://127.0.0.1:5199/" $TimeoutSeconds $watch
            if ($web.Content -notmatch 'lang="zh-CN"') { $failures.Add("前端首屏缺 SPA 标记，可能不是 React Router 外壳") }
            else {
                # F-03-A10（李总建议，与 F-03-A1 同一口径）：5199 的 200 也必须来自本次启动的进程树。
                # 只断言「200 + SPA 标记」时，别人在这端口上跑的 dev 会让前端腿假绿。
                # 三条互斥判定，不用 -and 串联：任何一环「无法确认」都算失败，宁可假红不可假绿
                # （原实现把「取不到 pid」短路进 else 并打印「属本次进程树」，是主动断言假事实）。
                $webAncestor = if ($Mode -eq "aspire") { $hostProcess.Id } elseif ($webProcess) { $webProcess.Id } else { $null }
                $webListenerPid = Get-PortListenerPid 5199

                if (-not $webAncestor) {
                    $failures.Add("前端 127.0.0.1:5199 无法确定本次启动的祖先进程（Mode=$Mode），不能证明 200 来自本次实例")
                }
                elseif (-not $webListenerPid) {
                    $failures.Add("前端 127.0.0.1:5199 取不到监听者 pid，无法确认 200 是否来自本次实例（祖先 pid $webAncestor）")
                }
                elseif (-not (Test-ProcessDescendsFrom -ChildPid $webListenerPid -AncestorPid $webAncestor)) {
                    $failures.Add("前端 127.0.0.1:5199 的监听者 pid $webListenerPid 不属于本次启动的进程树（祖先 pid $webAncestor），200 不是本次实例给的")
                }
                else {
                    # 归属成立之后再扫前端进程自己的 stderr：李总那次假绿的直接痕迹是
                    # `Port 5199 is already in use` / `ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL`，
                    # 只看 200 会被同端口的旧实例骗过去。
                    $webErr = Join-Path $logDir "verify-web.log.err"
                    $portClash = if ($Mode -eq "api" -and (Test-Path $webErr)) {
                        Select-String -Path $webErr -Pattern "already in use", "EADDRINUSE", "ERR_PNPM" -SimpleMatch -ErrorAction SilentlyContinue
                    }

                    if ($portClash) {
                        $failures.Add("前端进程自己的 stderr 里有端口冲突/启动失败痕迹（$($portClash[0].Line.Trim())），200 来自别的实例")
                    }
                    elseif ($Mode -eq "api" -and $webProcess -and $webProcess.HasExited) {
                        $failures.Add("前端进程已退出（exit code $($webProcess.ExitCode)），5199 上的 200 不是它给的")
                    }
                    else {
                        Write-Host "[ok] 前端 127.0.0.1:5199 200 且是 SPA 外壳（监听者 pid $webListenerPid 属本次进程树，stderr 无端口冲突）"
                    }
                }
            }
        }
        catch {
            # 前端起不来时直接把 web/vite/pnpm 相关行打出来：宿主日志是排查 web 资源的唯一入口，
            # 否则每次都要等人手翻 artifacts。
            $failures.Add($_.Exception.Message)
            if (Test-Path $logPath) {
                Write-Host "---- 宿主日志里与前端相关的行（web/vite/pnpm）----" -ForegroundColor Yellow
                $lines = Select-String -Path $logPath -Pattern "web", "vite", "pnpm", "EACCES", "ENOENT" -SimpleMatch -ErrorAction SilentlyContinue
                if ($lines) { $lines | Select-Object -Last 15 | ForEach-Object { Write-Host "  $($_.Line.Trim())" } }
                else { Write-Host "  （没有匹配行，宿主日志末尾见下）" -ForegroundColor DarkGray }
                Get-Content $logPath -Tail 10 | ForEach-Object { Write-Host "  $($_.Trim())" }
            }
        }
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
