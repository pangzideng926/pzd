#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [AllowEmptyCollection()][string[]]$ArgumentList = @(),
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [AllowEmptyCollection()][string[]]$InputFile = @(),
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 300,
    [string]$Repository,
    [string]$TargetRef,
    [string]$TimeZone = 'Asia/Shanghai'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# 单次显式执行；不拼接 shell、不发现依赖、不调整 Git 信任/权限或全局配置。
# 子命令的行为由调用者授权。参数和原始输出原样保存，调用者不得传入凭据。
$utf8 = [Text.UTF8Encoding]::new($false)
$issues = [Collections.Generic.List[string]]::new()
$startedUtc = [DateTimeOffset]::UtcNow
$zone = $null
$outputReady = $false
$result = [ordered]@{
    tool = 'Invoke-Verification'; complete = $false; outcome = 'not-started'
    startedAt = $null; endedAt = $null; timeZone = $TimeZone
    identity = [Environment]::UserName
    host = [ordered]@{ powershell = $PSVersionTable.PSVersion.ToString(); os = [Environment]::OSVersion.VersionString; processId = $PID }
    executable = [ordered]@{ requested = $Executable; path = $null; fileVersion = $null; sha256 = $null }
    argumentList = @($ArgumentList); workingDirectory = $WorkingDirectory; outputDirectory = $OutputDirectory
    timeoutSeconds = $TimeoutSeconds; timedOut = $false; childExitCode = $null; collectorExitCode = 0; exitCode = 2
    inputs = [ordered]@{ declaredCount = @($InputFile).Count; requested = @($InputFile); unchanged = $null; before = @(); after = @(); failures = @() }
    git = [ordered]@{ requested = [bool]$Repository; known = $null; dirty = 'unknown'; before = $null; after = $null; unchanged = $null }
    stdout = $null; stderr = $null; issues = @()
    limitations = @('输入只覆盖显式清单，未证明依赖完整；前后快照不能排除中途变化后恢复。', 'Git dirty 未采集，不由输入清单推算。', '仅记录本次身份、程序与环境，不自动授予 L1/L2 或证明部署及其他宿主。')
}

function Get-SafePath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'absolute-path-required' }
    if ($Path -match '^[/\\]{2}|[*?]' -or $Path.Substring([IO.Path]::GetPathRoot($Path).Length).Contains(':')) { throw 'unsupported-path' }
    $full = [IO.Path]::GetFullPath($Path)
    $cursor = [IO.Path]::GetPathRoot($full)
    foreach ($part in ($full.Substring($cursor.Length) -split '[/\\]')) {
        if (-not $part) { continue }
        $cursor = [IO.Path]::Combine($cursor, $part)
        try { $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'linked-path' }
    }
    return $full
}
function Get-FileEvidence([string]$Path) {
    $full = Get-SafePath $Path
    $item = Get-Item -LiteralPath $full -Force
    if ($item.PSIsContainer) { throw 'input-file-required' }
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    return [ordered]@{ path = $full; bytes = $item.Length; sha256 = $hash }
}
function Get-InputSnapshot([string[]]$Paths, [string]$Stage) {
    $seen = [Collections.Generic.HashSet[string]]::new($(if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }))
    foreach ($path in $Paths) {
        try {
            $full = Get-SafePath $path
            if ($seen.Add($full)) { Get-FileEvidence $full }
        } catch {
            # 只保留调用者显式路径及采样阶段，不回显异常正文或文件内容。
            $result.inputs.failures += [ordered]@{ path = $path; stage = $Stage; kind = 'input-unavailable' }
        }
    }
}
function Resolve-Executable([string]$Value) {
    if ([IO.Path]::IsPathFullyQualified($Value)) { $path = Get-SafePath $Value }
    else {
        if ($Value -match '[/\\:]') { throw 'executable-name-or-absolute-required' }
        $path = (Get-Command -Name $Value -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $path = Get-SafePath $path
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'executable-unavailable' }
    return $path
}
function Invoke-GitRead([string[]]$Arguments, [int[]]$AllowedCodes = @(0)) {
    $psi = [Diagnostics.ProcessStartInfo]::new($gitExecutable)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $utf8
    $psi.StandardErrorEncoding = $utf8
    $psi.WorkingDirectory = $working
    # 保留调用者的信任配置以真实暴露所有权失败；禁止可选写锁和懒取远端对象。
    $psi.Environment['GIT_OPTIONAL_LOCKS'] = '0'
    $psi.Environment['GIT_NO_LAZY_FETCH'] = '1'
    $psi.Environment['GIT_NO_REPLACE_OBJECTS'] = '1'
    $psi.Environment['GIT_TERMINAL_PROMPT'] = '0'
    foreach ($arg in @('--no-optional-locks', '-C', $repositoryPath, '-c', 'core.fsmonitor=false') + $Arguments) { $psi.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw 'git-timeout' }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        # 诊断留在独立原始记录内，不把 Git 的失败伪装成通过。
        $gitLog.Add([ordered]@{ arguments = $Arguments; exitCode = $process.ExitCode; stdout = $text; stderr = $errorText })
        if ($process.ExitCode -notin $AllowedCodes) { throw 'git-read-failed' }
        return $text.Trim()
    } finally { $process.Dispose() }
}
function Get-GitContext {
    foreach ($name in @('GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_COMMON_DIR', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_NAMESPACE', 'GIT_SHALLOW_FILE', 'GIT_GRAFT_FILE', 'GIT_REPLACE_REF_BASE')) {
        if (-not [string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($name, 'Process'))) { throw 'git-environment-redirect' }
    }
    $top = Invoke-GitRead @('rev-parse', '--show-toplevel')
    if ([IO.Path]::GetFullPath($top).TrimEnd('\', '/') -ine $repositoryPath.TrimEnd('\', '/')) { throw 'git-root-mismatch' }
    $head = Invoke-GitRead @('rev-parse', '--verify', 'HEAD^{commit}')
    $branch = Invoke-GitRead @('symbolic-ref', '--quiet', '--short', 'HEAD') @(0, 1)
    $targetSha = if ($TargetRef) { Invoke-GitRead @('rev-parse', '--verify', '--end-of-options', "$TargetRef^{commit}") } else { $null }
    return [ordered]@{ repository = $repositoryPath; head = $head; branch = $branch; targetRef = $TargetRef; targetSha = $targetSha }
}
function Save-Json([string]$Path, $Value) {
    $Path = Get-SafePath $Path
    $text = $Value | ConvertTo-Json -Depth 14
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = $utf8.GetBytes($text)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}

$gitLog = [Collections.Generic.List[object]]::new()
try {
    if ($IsWindows) { $result.identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name }
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZone)
    $working = Get-SafePath $WorkingDirectory
    if (-not (Test-Path -LiteralPath $working -PathType Container)) { throw 'working-directory-unavailable' }
    $output = Get-SafePath $OutputDirectory
    if (Test-Path -LiteralPath $output) { throw 'output-already-exists' }
    if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($output)) -PathType Container)) { throw 'output-parent-unavailable' }
    # New-Item 不使用 Force；并发创建同名目录时失败，禁止覆盖旧记录。
    [void](New-Item -ItemType Directory -Path $output -ErrorAction Stop)
    $outputReady = $true
    $result.workingDirectory = $working
    $result.outputDirectory = $output
    $stdoutPath = Join-Path $output 'stdout.log'
    $stderrPath = Join-Path $output 'stderr.log'
    $program = Resolve-Executable $Executable
    $programEvidence = Get-FileEvidence $program
    $result.executable.path = $program
    $result.executable.sha256 = $programEvidence.sha256
    $result.executable.fileVersion = (Get-Item -LiteralPath $program).VersionInfo.FileVersion
    $result.inputs.before = @(Get-InputSnapshot $InputFile 'before')
    if ($result.inputs.failures.Count -gt 0) { throw 'input-evidence-unavailable-before' }
    $inputPaths = @($result.inputs.before | ForEach-Object { $_.path })
    if ($TargetRef -and -not $Repository) { $issues.Add('target-ref-without-repository') }
    if ($Repository) {
        try {
            $repositoryPath = Get-SafePath $Repository
            $gitExecutable = Resolve-Executable 'git'
            $result.git.before = Get-GitContext
            $result.git.known = $true
        } catch {
            $result.git.known = $false
            if ($_.Exception.Message -eq 'git-environment-redirect') { $issues.Add('git-environment-redirect') }
            $issues.Add('git-context-unavailable-before')
        }
    }
    $psi = [Diagnostics.ProcessStartInfo]::new($program)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $working
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($arg in $ArgumentList) { $psi.ArgumentList.Add($arg) }
    $stdoutFile = [IO.File]::Open($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $stderrFile = [IO.File]::Open($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $cancelOutput = [Threading.CancellationTokenSource]::new()
    try {
        [void]$process.Start()
        $stdoutCopy = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile, $cancelOutput.Token)
        $stderrCopy = $process.StandardError.BaseStream.CopyToAsync($stderrFile, $cancelOutput.Token)
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $result.timedOut = $true
            $issues.Add('command-timeout')
            try { $process.Kill($true) } catch { if (-not $process.HasExited) { $issues.Add('process-stop-failed') } }
            if (-not $process.WaitForExit(5000)) { $issues.Add('process-still-running') }
        }
        if ($process.HasExited) { $result.childExitCode = $process.ExitCode }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutCopy, $stderrCopy), 5000)) {
            $cancelOutput.Cancel()
            $issues.Add('output-incomplete')
        }
    } finally {
        $cancelOutput.Cancel()
        $process.Dispose()
        $stdoutFile.Dispose()
        $stderrFile.Dispose()
        $cancelOutput.Dispose()
    }
    try {
        $result.inputs.after = @(Get-InputSnapshot $inputPaths 'after')
        if ($result.inputs.failures.Count -gt 0) { throw 'input-evidence-unavailable-after' }
        if ($inputPaths.Count -gt 0) {
            $result.inputs.unchanged = (($result.inputs.before | ConvertTo-Json -Depth 4 -Compress) -ceq ($result.inputs.after | ConvertTo-Json -Depth 4 -Compress))
            if (-not $result.inputs.unchanged) { $issues.Add('input-changed') }
        }
        if ((Get-FileEvidence $program).sha256 -cne $result.executable.sha256) { $issues.Add('executable-changed') }
    } catch { $issues.Add('input-evidence-unavailable-after') }
    if ($Repository) {
        try {
            $result.git.after = Get-GitContext
            if ($null -ne $result.git.before) {
                $result.git.unchanged = (($result.git.before | ConvertTo-Json -Compress) -ceq ($result.git.after | ConvertTo-Json -Compress))
                if (-not $result.git.unchanged) { $issues.Add('git-context-changed') }
            }
        } catch {
            $result.git.known = $false
            if ($_.Exception.Message -eq 'git-environment-redirect' -and -not $issues.Contains('git-environment-redirect')) { $issues.Add('git-environment-redirect') }
            $issues.Add('git-context-unavailable-after')
        }
    }
} catch {
    $known = @('absolute-path-required', 'unsupported-path', 'linked-path', 'working-directory-unavailable', 'output-already-exists', 'output-parent-unavailable', 'input-file-required', 'executable-name-or-absolute-required', 'executable-unavailable', 'input-evidence-unavailable-before')
    $issues.Add($(if ($_.Exception.Message -in $known) { $_.Exception.Message } else { 'collection-failed' }))
}
if ($outputReady) {
    foreach ($streamName in @('stdout', 'stderr')) {
        $path = Join-Path $result.outputDirectory "$streamName.log"
        try {
            if (-not (Test-Path -LiteralPath $path)) {
                $path = Get-SafePath $path
                $empty = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
                $empty.Dispose()
            }
            $result[$streamName] = Get-FileEvidence $path
        } catch { $issues.Add('output-evidence-unavailable') }
    }
    if ($gitLog.Count -gt 0) {
        try { Save-Json (Join-Path $result.outputDirectory 'git-context.json') $gitLog.ToArray() }
        catch { $issues.Add('git-log-save-failed') }
    }
}
$result.startedAt = if ($null -ne $zone) { [TimeZoneInfo]::ConvertTime($startedUtc, $zone).ToString('o') } else { $startedUtc.ToString('o') }
$endedUtc = [DateTimeOffset]::UtcNow
$result.endedAt = if ($null -ne $zone) { [TimeZoneInfo]::ConvertTime($endedUtc, $zone).ToString('o') } else { $endedUtc.ToString('o') }
$result.collectorExitCode = if ($issues.Count -gt 0) { 2 } else { 0 }
$result.complete = ($issues.Count -eq 0 -and $null -ne $result.childExitCode)
# 优先级：超时 124 > 真实子进程非零 > 采集失败 2 > 0。JSON 始终分别保留两者。
$result.exitCode = if ($result.timedOut) { 124 } elseif ($null -ne $result.childExitCode -and $result.childExitCode -ne 0) { $result.childExitCode } else { $result.collectorExitCode }
$result.outcome = if ($result.timedOut) { 'timed-out' } elseif ($null -eq $result.childExitCode) { 'not-started' } elseif ($result.childExitCode -ne 0) { 'failed' } elseif (-not $result.complete -or $null -eq $result.inputs.unchanged) { 'unverified' } else { 'succeeded' }
$result.issues = $issues.ToArray()
if ($outputReady) {
    try { Save-Json (Join-Path $result.outputDirectory 'result.json') $result }
    catch {
        $result.complete = $false
        $result.collectorExitCode = 2
        if ($result.exitCode -eq 0) { $result.exitCode = 2 }
        $result.issues += 'result-save-failed'
        if ($result.outcome -eq 'succeeded') { $result.outcome = 'unverified' }
    }
}
$result | ConvertTo-Json -Depth 14
exit $result.exitCode
