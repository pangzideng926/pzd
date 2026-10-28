#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Repository,
    [string]$TargetRef,
    [ValidateRange(1, 300)][int]$TimeoutSeconds = 15,
    [string]$TimeZone = 'Asia/Shanghai'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# 探针运行于调用它的真实身份，复用单命令采集器保存每一项原始证据。
# 不改 safe.directory/ACL/全局配置，不安装程序、不联网、不删除探针文件。
$utf8 = [Text.UTF8Encoding]::new($false)
$issues = [Collections.Generic.List[string]]::new()
$started = [DateTimeOffset]::UtcNow
$outputReady = $false
$zone = $null
$result = [ordered]@{
    tool = 'Test-ExecutionEnvironment'; complete = $false; identity = [Environment]::UserName
    startedAt = $null; endedAt = $null; timeZone = $TimeZone; workingDirectory = $WorkingDirectory; outputDirectory = $OutputDirectory
    fileReadWrite = $false; nativeExit23 = $false
    pwsh = [ordered]@{ path = $null; version = $null; known = $false }
    git = [ordered]@{ path = $null; version = $null; known = $null; before = $null; after = $null; dirty = 'unknown' }
    checks = @(); issues = @()
    limitations = @('仅证明本次调用身份和环境；不替其他工具、沙箱、工作目录或未来会话背书。', '临时读写只覆盖本次新输出目录；Git dirty 和依赖完备性未检查。')
}
$checks = [Collections.Generic.List[object]]::new()
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
function Run-Check([string]$Name, [string]$Program, [string[]]$Arguments, [int]$ExpectedExit, [switch]$WithGit) {
    $parameters = @{
        Executable = $Program; ArgumentList = $Arguments; WorkingDirectory = $working
        OutputDirectory = (Join-Path $output $Name); InputFile = @($Program)
        TimeoutSeconds = $TimeoutSeconds; TimeZone = $TimeZone
    }
    if ($WithGit) {
        if ($Repository) { $parameters.Repository = $Repository }
        if ($TargetRef) { $parameters.TargetRef = $TargetRef }
    }
    $raw = & $collector @parameters
    $code = $LASTEXITCODE
    $report = ($raw -join "`n") | ConvertFrom-Json -Depth 20
    $checks.Add([ordered]@{ name = $Name; expectedExitCode = $ExpectedExit; exitCode = $code; result = (Join-Path $parameters.OutputDirectory 'result.json') })
    if ($code -ne $ExpectedExit -or -not $report.complete) { $issues.Add("$Name-failed") }
    return $report
}
try {
    if ($IsWindows) { $result.identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name }
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZone)
    $working = Get-SafePath $WorkingDirectory
    if (-not (Test-Path -LiteralPath $working -PathType Container)) { throw 'working-directory-unavailable' }
    $output = Get-SafePath $OutputDirectory
    if (Test-Path -LiteralPath $output) { throw 'output-already-exists' }
    if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($output)) -PathType Container)) { throw 'output-parent-unavailable' }
    [void](New-Item -ItemType Directory -Path $output -ErrorAction Stop)
    $outputReady = $true
    $result.workingDirectory = $working
    $result.outputDirectory = $output
    $collector = Join-Path $PSScriptRoot 'Invoke-Verification.ps1'
    $pwshPath = Get-SafePath ([Environment]::ProcessPath)
    $result.pwsh.path = $pwshPath
    try {
        $path = Join-Path $output 'file-probe.txt'
        $content = 'verification-probe 中文 ' + [Guid]::NewGuid().ToString('N')
        $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $bytes = $utf8.GetBytes($content); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        $result.fileReadWrite = ([IO.File]::ReadAllText($path, $utf8) -ceq $content)
        if (-not $result.fileReadWrite) { $issues.Add('file-roundtrip-failed') }
    } catch { $issues.Add('file-read-write-failed') }
    try {
        $version = Run-Check 'pwsh-version' $pwshPath @('-NoProfile', '-NonInteractive', '-Command', '$PSVersionTable.PSVersion.ToString()') 0
        $result.pwsh.known = ($version.complete -and $version.childExitCode -eq 0)
        if ($result.pwsh.known) { $result.pwsh.version = [IO.File]::ReadAllText($version.stdout.path, $utf8).Trim() }
    } catch { $issues.Add('pwsh-version-unavailable') }
    try {
        $native = Run-Check 'native-exit-23' $pwshPath @('-NoProfile', '-NonInteractive', '-Command', 'exit 23') 23
        $result.nativeExit23 = ($native.complete -and $native.childExitCode -eq 23 -and $native.exitCode -eq 23)
        if (-not $result.nativeExit23) { $issues.Add('native-exit-code-not-preserved') }
    } catch { $issues.Add('native-exit-probe-unavailable') }
    try {
        $gitPath = Get-SafePath ((Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source)
        $result.git.path = $gitPath
        $gitVersion = Run-Check 'git-version' $gitPath @('--version') 0
        if ($gitVersion.complete -and $gitVersion.childExitCode -eq 0) { $result.git.version = [IO.File]::ReadAllText($gitVersion.stdout.path, $utf8).Trim() }
    } catch { $issues.Add('git-version-unavailable') }
    if ($Repository -or $TargetRef) {
        try {
            $context = Run-Check 'git-context' $pwshPath @('-NoProfile', '-NonInteractive', '-Command', 'exit 0') 0 -WithGit
            $result.git.known = $context.git.known
            $result.git.before = $context.git.before
            $result.git.after = $context.git.after
            if (-not $context.git.known) { $issues.Add('git-context-unavailable') }
        } catch { $result.git.known = $false; $issues.Add('git-context-probe-failed') }
    }
} catch {
    $known = @('absolute-path-required', 'unsupported-path', 'linked-path', 'working-directory-unavailable', 'output-already-exists', 'output-parent-unavailable')
    $issues.Add($(if ($_.Exception.Message -in $known) { $_.Exception.Message } else { 'environment-probe-failed' }))
}
$result.startedAt = if ($null -ne $zone) { [TimeZoneInfo]::ConvertTime($started, $zone).ToString('o') } else { $started.ToString('o') }
$ended = [DateTimeOffset]::UtcNow
$result.endedAt = if ($null -ne $zone) { [TimeZoneInfo]::ConvertTime($ended, $zone).ToString('o') } else { $ended.ToString('o') }
$result.complete = ($issues.Count -eq 0 -and $result.fileReadWrite -and $result.nativeExit23 -and $result.pwsh.known)
$result.checks = $checks.ToArray()
$result.issues = $issues.ToArray()
if ($outputReady) {
    try {
        $path = Get-SafePath (Join-Path $output 'result.json')
        $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        try { $bytes = $utf8.GetBytes(($result | ConvertTo-Json -Depth 12)); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    } catch { $result.complete = $false; $result.issues += 'result-save-failed' }
}
$result | ConvertTo-Json -Depth 12
if ($result.complete) { exit 0 }
exit 2
