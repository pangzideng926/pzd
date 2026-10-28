#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TempRoot,
    [string]$ScriptRoot = (Join-Path $PSScriptRoot '../scripts')
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
if (-not [IO.Path]::IsPathFullyQualified($TempRoot)) { throw 'TempRoot 必须绝对。' }
$collector = [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Invoke-Verification.ps1'))
$probe = [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Test-ExecutionEnvironment.ps1'))
foreach ($path in $collector, $probe) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw '待测验证工具不存在。' } }
$runRoot = Join-Path $TempRoot ('verification-tools-' + [Guid]::NewGuid().ToString('N'))
$working = Join-Path $runRoot 'working'
$outputs = Join-Path $runRoot 'outputs'
$logs = Join-Path $runRoot 'harness'
foreach ($directory in $working, $outputs, $logs) { [void][IO.Directory]::CreateDirectory($directory) }
$assertions = [Collections.Generic.List[string]]::new()
$runs = [Collections.Generic.List[object]]::new()
$skipped = [Collections.Generic.List[string]]::new()
$pwsh = (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source
$utf8 = [Text.UTF8Encoding]::new($false)
function Assert-True([bool]$Value, [string]$Name) {
    if (-not $Value) { throw "断言失败：$Name；合成夹具保留于 $runRoot" }
    $assertions.Add($Name)
}
function Write-Fixture([string]$Name, [string]$Text) {
    $path = Join-Path $working $Name
    [IO.File]::WriteAllText($path, $Text, $utf8)
    return $path
}
function Invoke-Tool([string]$Name, [hashtable]$Parameters, [int]$ExpectedExit = 0, [string]$Script = $collector, [hashtable]$EnvironmentOverrides = @{}) {
    $payload = @{ script = $Script; parameters = $Parameters } | ConvertTo-Json -Depth 8 -Compress
    $base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
    $command = "`$a = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$base64')) | ConvertFrom-Json -AsHashtable; `$p = `$a.parameters; & `$a.script @p; exit `$LASTEXITCODE"
    $psi = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8
    $psi.StandardErrorEncoding = $utf8
    foreach ($key in $EnvironmentOverrides.Keys) { $psi.Environment[$key] = $EnvironmentOverrides[$key] }
    foreach ($arg in @('-NoProfile', '-NonInteractive', '-EncodedCommand', [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command)))) { $psi.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw '合成工具测试超时。' }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $number = $runs.Count
        [IO.File]::WriteAllText((Join-Path $logs "$number.stdout.json"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $logs "$number.stderr.log"), $stderr, $utf8)
        Assert-True ($process.ExitCode -eq $ExpectedExit) "$Name：真实退出码 $ExpectedExit"
        Assert-True ([string]::IsNullOrWhiteSpace($stderr)) "$Name：包装器不回显异常正文"
        $report = $stdout | ConvertFrom-Json -Depth 30
        $runs.Add([ordered]@{ name = $Name; exitCode = $process.ExitCode; outputDirectory = $Parameters.OutputDirectory; harnessStdout = "$number.stdout.json" })
        return $report
    } finally { $process.Dispose() }
}
function Parameters([string]$Name, [string]$Program, [string[]]$Arguments, [string[]]$Inputs = @()) {
    return @{ Executable = $Program; ArgumentList = $Arguments; WorkingDirectory = $working; OutputDirectory = (Join-Path $outputs $Name); InputFile = $Inputs; TimeoutSeconds = 20; TimeZone = 'Asia/Shanghai' }
}
$echoScript = Write-Fixture 'echo.ps1' @'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::Out.Write(($args | ConvertTo-Json -Compress))
[Console]::Error.Write('中文错误输出')
exit 0
'@
$failScript = Write-Fixture 'fail.ps1' "[Console]::Out.Write('failed'); exit 23"
$sleepScript = Write-Fixture 'sleep.ps1' "[Console]::Out.Write('started'); Start-Sleep -Seconds 30"
$changeScript = Write-Fixture 'change.ps1' @'
param([string]$Path)
[IO.File]::AppendAllText($Path, 'changed', [Text.UTF8Encoding]::new($false))
exit 0
'@
$inputFile = Write-Fixture 'input.txt' "初始`n"
$binaryScript = Write-Fixture 'binary.ps1' @'
$bytes = [byte[]]@(0, 255, 195, 40, 10)
$stream = [Console]::OpenStandardOutput()
$stream.Write($bytes, 0, $bytes.Length)
exit 0
'@
$literal = '中文 空格 $() ` ; "quoted"'
$ok = Invoke-Tool '成功与字面参数' (Parameters 'success' $pwsh @('-NoProfile', '-NonInteractive', '-File', $echoScript, $literal, '第二项') @($echoScript, $inputFile))
Assert-True ($ok.complete -and $ok.childExitCode -eq 0 -and $ok.inputs.unchanged -eq $true) '成功命令记录真实返回与输入一致'
$rawArguments = [IO.File]::ReadAllText($ok.stdout.path, $utf8) | ConvertFrom-Json
Assert-True ($rawArguments[0] -ceq $literal -and $rawArguments[1] -ceq '第二项') '参数通过 ArgumentList 按字面传递'
Assert-True ([IO.File]::ReadAllText($ok.stderr.path, $utf8) -ceq '中文错误输出') 'stderr 中文原始 UTF8 保留'
Assert-True ($ok.executable.path -eq $pwsh -and $ok.executable.sha256 -and $ok.identity -and $ok.timeZone -eq 'Asia/Shanghai') '记录实际程序身份版本和时区'
Assert-True (Test-Path -LiteralPath (Join-Path $outputs 'success/result.json')) '结果 JSON 留存'
$binary = Invoke-Tool '保留原始输出字节' (Parameters 'binary' $pwsh @('-NoProfile', '-File', $binaryScript) @($binaryScript))
Assert-True ([Convert]::ToHexString([IO.File]::ReadAllBytes($binary.stdout.path)) -eq '00FFC3280A') 'stdout 不按文本重新编码'
$failed = Invoke-Tool '子进程退出 23' (Parameters 'failed' $pwsh @('-NoProfile', '-File', $failScript) @($failScript)) 23
Assert-True ($failed.childExitCode -eq 23 -and $failed.collectorExitCode -eq 0 -and $failed.outcome -eq 'failed') '原生失败不映射为 1'
$unknown = Invoke-Tool '未声明输入' (Parameters 'unknown' $pwsh @('-NoProfile', '-Command', 'exit 0'))
Assert-True ($null -eq $unknown.inputs.unchanged -and $unknown.outcome -eq 'unverified') '没有输入清单不声称指纹一致'
$changed = Invoke-Tool '运行期间输入变化' (Parameters 'changed' $pwsh @('-NoProfile', '-File', $changeScript, $inputFile) @($inputFile)) 2
Assert-True ($changed.childExitCode -eq 0 -and -not $changed.complete -and $changed.inputs.unchanged -eq $false) '变化阻止复用但保留子进程成功码'
$timedParameters = Parameters 'timeout' $pwsh @('-NoProfile', '-File', $sleepScript) @($sleepScript)
$timedParameters.TimeoutSeconds = 1
$timed = Invoke-Tool '超时' $timedParameters 124
Assert-True ($timed.timedOut -and -not $timed.complete -and $timed.outcome -eq 'timed-out') '超时明确记录且只终止启动的进程树'
$missing = Invoke-Tool '缺少输入文件' (Parameters 'missing-input' $pwsh @('-NoProfile', '-Command', 'exit 0') @((Join-Path $working 'absent.txt'))) 2
Assert-True ($null -eq $missing.childExitCode -and -not $missing.complete) '缺输入不开始验证'
$absentInput = Join-Path $working 'second-absent.txt'
$partialMissing = Invoke-Tool '多个输入中一项缺失' (Parameters 'partial-input' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile, $absentInput)) 2
Assert-True ($null -eq $partialMissing.childExitCode -and @($partialMissing.inputs.requested).Count -eq 2 -and @($partialMissing.inputs.before).Count -eq 1) '缺一项输入保留原清单和已取得指纹，且子进程未启动'
Assert-True (@($partialMissing.inputs.failures).Count -eq 1 -and $partialMissing.inputs.failures[0].path -eq $absentInput -and $partialMissing.inputs.failures[0].stage -eq 'before') '缺少输入可定位到具体路径和采样阶段'
$missingProgram = Invoke-Tool '缺少程序' (Parameters 'missing-program' (Join-Path $working 'absent.exe') @() @($inputFile)) 2
Assert-True ($null -eq $missingProgram.childExitCode -and -not $missingProgram.complete) '程序缺失不冒充执行'
$existing = Join-Path $outputs 'occupied'
[void][IO.Directory]::CreateDirectory($existing)
$marker = Join-Path $existing 'marker.txt'
[IO.File]::WriteAllText($marker, 'preserve', $utf8)
$collisionParameters = Parameters 'occupied' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
$collision = Invoke-Tool '输出目录已存在' $collisionParameters 2
Assert-True (-not $collision.complete -and [IO.File]::ReadAllText($marker, $utf8) -eq 'preserve' -and @(Get-ChildItem -LiteralPath $existing).Count -eq 1) '已有输出目录完全保留'
foreach ($badPath in @('relative', '\\server\share\new-output', (Join-Path $outputs 'bad:stream'))) {
    $parameters = Parameters 'unused' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
    $parameters.OutputDirectory = $badPath
    $invalid = Invoke-Tool '拒绝不安全输出路径' $parameters 2
    Assert-True (-not $invalid.complete) '拒绝相对、UNC 和 ADS 输出路径'
}
$repository = Join-Path $runRoot 'repository'
[void][IO.Directory]::CreateDirectory($repository)
& git -C $repository init -q -b main
[IO.File]::WriteAllText((Join-Path $repository 'file.txt'), 'fixture', $utf8)
& git -C $repository -c core.hooksPath=$working add file.txt
& git -C $repository -c core.hooksPath=$working -c commit.gpgsign=false -c user.name=Fixture -c user.email=fixture@example.invalid commit -q -m fixture
$gitConfigHash = (Get-FileHash -LiteralPath (Join-Path $repository '.git/config') -Algorithm SHA256).Hash
$repositoryBefore = @(Get-ChildItem -LiteralPath $repository -Force -Recurse -File | Sort-Object FullName | ForEach-Object { "$($_.FullName) $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }) -join "`n"
$gitParameters = Parameters 'git-good' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
$gitParameters.Repository = $repository
$gitParameters.TargetRef = 'main'
$gitGood = Invoke-Tool '显式 Git 上下文' $gitParameters
Assert-True ($gitGood.git.known -and $gitGood.git.before.head -match '^[a-f0-9]{40}$' -and $gitGood.git.before.targetSha -eq $gitGood.git.before.head -and $gitGood.git.dirty -eq 'unknown') '读取 Git SHA 与目标，dirty 不猜测'
$pollutedParameters = Parameters 'git-polluted' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
$pollutedParameters.Repository = $repository
$polluted = Invoke-Tool '拒绝继承 Git 重定向' $pollutedParameters 2 $collector @{ GIT_DIR = (Join-Path $repository '.git'); GIT_WORK_TREE = $repository; GIT_INDEX_FILE = (Join-Path $working 'foreign-index') }
Assert-True (-not $polluted.git.known -and $polluted.childExitCode -eq 0 -and @($polluted.issues) -contains 'git-environment-redirect') 'Git 重定向明确未核验，不阻止独立命令、不修改环境信任'
$gitBadParameters = Parameters 'git-bad' $pwsh @('-NoProfile', '-File', $failScript) @($failScript)
$gitBadParameters.Repository = $working
$gitBad = Invoke-Tool 'Git 失败并存原生失败' $gitBadParameters 23
Assert-True (-not $gitBad.complete -and $gitBad.childExitCode -eq 23 -and $gitBad.collectorExitCode -eq 2) 'Git 取证失败不阻止独立命令，真实子进程非零优先'
$gitOnlyParameters = Parameters 'git-only-bad' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
$gitOnlyParameters.Repository = $working
$gitOnlyBad = Invoke-Tool 'Git 失败但命令成功' $gitOnlyParameters 2
Assert-True ($gitOnlyBad.childExitCode -eq 0 -and -not $gitOnlyBad.complete) '命令成功不能掩盖 Git 上下文缺失'
Assert-True ((Get-FileHash -LiteralPath (Join-Path $repository '.git/config') -Algorithm SHA256).Hash -eq $gitConfigHash) 'Git 配置保持原字节'
# 已审查：只在本次夹具内创建目录连接，拒绝经该连接创建输出；不删除。
$link = Join-Path $runRoot 'linked'
try { New-Item -ItemType Junction -Path $link -Target $outputs | Out-Null } catch { $skipped.Add('junction：当前环境不能创建合成目录连接') }
if (Test-Path -LiteralPath $link) {
    $linkedParameters = Parameters 'unused' $pwsh @('-NoProfile', '-Command', 'exit 0') @($inputFile)
    $linkedParameters.OutputDirectory = Join-Path $link 'escaped'
    $linked = Invoke-Tool '输出父目录是连接' $linkedParameters 2
    Assert-True (-not $linked.complete -and -not (Test-Path -LiteralPath (Join-Path $outputs 'escaped'))) '拒绝跟随连接父目录写入'
}
$inputLink = Join-Path $runRoot 'linked-input'
try { New-Item -ItemType Junction -Path $inputLink -Target $working | Out-Null } catch { $skipped.Add('input-junction：当前环境不能创建合成目录连接') }
if (Test-Path -LiteralPath $inputLink) {
    $linkedInputParameters = Parameters 'linked-input' $pwsh @('-NoProfile', '-Command', 'exit 0') @((Join-Path $inputLink 'input.txt'))
    $linkedInput = Invoke-Tool '输入父目录是连接' $linkedInputParameters 2
    Assert-True ($null -eq $linkedInput.childExitCode -and -not $linkedInput.complete) '输入经过连接时不开始验证'
}
$probeGood = Invoke-Tool '实际身份环境探针' @{ WorkingDirectory = $working; OutputDirectory = (Join-Path $outputs 'probe-good'); Repository = $repository; TargetRef = 'main' } 0 $probe
Assert-True ($probeGood.complete -and $probeGood.nativeExit23 -and $probeGood.fileReadWrite -and $probeGood.git.known) '环境探针验证真实退出 23、临时读写和 Git'
$probeBad = Invoke-Tool '环境探针保留 Git 失败' @{ WorkingDirectory = $working; OutputDirectory = (Join-Path $outputs 'probe-bad'); Repository = $working } 2 $probe
Assert-True (-not $probeBad.complete -and $probeBad.nativeExit23 -and $probeBad.fileReadWrite -and -not $probeBad.git.known) 'Git 失败保留，独立环境探针继续'
Assert-True ((Get-FileHash -LiteralPath (Join-Path $repository '.git/config') -Algorithm SHA256).Hash -eq $gitConfigHash) '环境探针没有修改 Git 配置'
$repositoryAfter = @(Get-ChildItem -LiteralPath $repository -Force -Recurse -File | Sort-Object FullName | ForEach-Object { "$($_.FullName) $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }) -join "`n"
Assert-True ($repositoryAfter -ceq $repositoryBefore) 'Git 取证与环境探针保持合成仓所有文件字节不变'
[ordered]@{
    passed = $assertions.Count; assertions = $assertions.ToArray(); runs = $runs.ToArray(); skipped = $skipped.ToArray()
    fixtureRoot = $runRoot; powershell = $PSVersionTable.PSVersion.ToString()
    scriptHashes = @($collector, $probe | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
    note = '合成测试不代表调用者已声明完整依赖，也不替其他工具或沙箱背书；夹具和原始输出保留。'
} | ConvertTo-Json -Depth 10
