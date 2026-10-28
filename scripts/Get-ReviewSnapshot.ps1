#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$TargetRef,
    [string]$BaselineSha,
    [string]$Since,
    [string]$TimeZone = 'Asia/Shanghai'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

# Read-only daily-review snapshot: no filesystem writes, no Git mutations, no fetch, no file contents in output.
# Exit 0 = snapshot complete (limitations may still apply); 2 = incomplete; 1 = invalid parameters (pwsh binding).
foreach ($name in 'GIT_DIR','GIT_WORK_TREE','GIT_INDEX_FILE','GIT_OBJECT_DIRECTORY','GIT_ALTERNATE_OBJECT_DIRECTORIES','GIT_COMMON_DIR','GIT_NAMESPACE','GIT_REPLACE_REF_BASE','GIT_CONFIG_PARAMETERS','GIT_CONFIG_COUNT','GIT_EXTERNAL_DIFF','GIT_ATTR_SOURCE') {
    # [NullString] unsets the variable; a plain $null would be marshalled as an empty string.
    [Environment]::SetEnvironmentVariable($name, [NullString]::Value)
}
$env:GIT_NO_REPLACE_OBJECTS = '1'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GIT_ATTR_NOSYSTEM = '1'

$limitations = [Collections.Generic.List[string]]::new()
$notChecked = @('远端状态（不 fetch）', '提交内容审查', '验证证据与需求对照')
$repo = $null
# Attributes come from the empty tree, so .gitattributes cannot enable textconv, diff drivers or clean/smudge filters.
$emptyTree = '4b825dc642cb6eb9a060e54bf8d69288fbee4904'
$gitSafety = @('--no-optional-locks', "--attr-source=$emptyTree", '-c', 'core.fsmonitor=false', '-c', 'core.attributesFile=', '-c', 'diff.external=', '-c', 'i18n.logOutputEncoding=UTF-8', '-c', 'core.quotePath=false')

function Invoke-Git([string[]]$GitArguments) {
    $output = & git -C $repo @gitSafety @GitArguments
    return @($output | Where-Object { $_ -ne '' })
}
function Test-GitSuccess([string[]]$GitArguments) {
    # For checks whose exit code 1/128 is an expected answer rather than a failure.
    try { & git -C $repo @gitSafety @GitArguments *> $null; return $true }
    catch [System.Management.Automation.NativeCommandExitException] {
        if ($_.Exception.ExitCode -in 1, 128) { return $false }
        throw
    }
}
function Get-PatchId([string]$Sha) {
    # Plumbing diff without textconv/external drivers; native-to-native pipe keeps bytes in PowerShell 7.4+.
    $line = & git -C $repo @gitSafety diff-tree -p --no-color --no-textconv --no-ext-diff --no-commit-id --root $Sha | git patch-id --stable
    if (-not $line) { return $null }
    return (($line | Select-Object -First 1) -split ' ')[0]
}
function Get-CommitInfo([string]$Sha) {
    $parts = @(Invoke-Git @('show', '-s', '--no-textconv', '--format=%H%x1f%P%x1f%cI%x1f%aI%x1f%s', $Sha))[0] -split [char]0x1f, 5
    $parents = @($parts[1] -split ' ' | Where-Object { $_ })
    return [ordered]@{ sha = $parts[0]; parents = $parents; committedAt = $parts[2]; authoredAt = $parts[3]; subject = $parts[4] }
}
function ConvertTo-ZonedTime([string]$Value, [TimeZoneInfo]$Zone) {
    # Inputs without an explicit offset are read as wall-clock time in -TimeZone, not the machine's zone.
    $culture = [Globalization.CultureInfo]::InvariantCulture
    if ($Value -match '(Z|[+-]\d{2}:?\d{2})$') { return [DateTimeOffset]::Parse($Value, $culture) }
    $local = [DateTime]::SpecifyKind([DateTime]::Parse($Value, $culture), [DateTimeKind]::Unspecified)
    return [DateTimeOffset]::new($local, $Zone.GetUtcOffset($local))
}
function Get-ReflogEntryTimes([string]$Ref, [string[]]$Shas, [DateTimeOffset]$Since) {
    # Map first-parent commits to the reflog time at which the ref first contained them (local observation only).
    $entered = @{}
    if (-not $Ref) { return $entered }
    $rows = @(try { Invoke-Git @('log', '-g', '--date=iso-strict', '--format=%H%x1f%gd', $Ref) } catch [System.Management.Automation.NativeCommandExitException] { @() })
    if ($rows.Count -eq 0) { return $entered }
    [array]::Reverse($rows)
    $previous = $null
    $wanted = [Collections.Generic.HashSet[string]]::new([string[]]$Shas)
    # Only reflog moves from one day before the window are expanded, so long reflogs stay cheap;
    # the latest move wins, so a commit reset away and re-entered today counts as entering today.
    $horizon = $Since.AddDays(-1)
    foreach ($row in $rows) {
        $tip, $selector = $row -split [char]0x1f, 2
        $time = if ($selector -match '@\{(.+)\}$') { $Matches[1] } else { $null }
        $recent = $time -and [DateTimeOffset]::Parse($time, [Globalization.CultureInfo]::InvariantCulture) -ge $horizon
        if ($previous -and $recent -and $tip -ne $previous) {
            $moved = @(try { Invoke-Git @('rev-list', '--first-parent', "$previous..$tip") } catch [System.Management.Automation.NativeCommandExitException] { @() })
            foreach ($sha in $moved) { if ($wanted.Contains($sha)) { $entered[$sha] = $time } }
        }
        $previous = $tip
    }
    return $entered
}
function Test-InfoAttributesFilter {
    # .git/info/attributes is not covered by --attr-source; a filter there could run during dirty detection.
    $path = @(Invoke-Git @('rev-parse', '--git-path', 'info/attributes'))[0]
    $full = if ([IO.Path]::IsPathFullyQualified($path)) { $path } else { Join-Path $repo $path }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $false }
    return [bool](Select-String -LiteralPath $full -Pattern '\bfilter=' -Quiet -Encoding utf8)
}

$code = 0
$result = [ordered]@{ tool = 'Get-ReviewSnapshot'; readOnly = $true; complete = $false }
try {
    if (-not [IO.Path]::IsPathFullyQualified($Repository)) { throw 'absolute-repository-required' }
    $repo = [IO.Path]::GetFullPath($Repository).TrimEnd('\', '/')
    $tz = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZone)
    $capturedAt = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $tz)
    if ($Since) { $sinceAt = ConvertTo-ZonedTime $Since $tz }
    else {
        $midnight = $capturedAt.Date
        $sinceAt = [DateTimeOffset]::new($midnight, $tz.GetUtcOffset($midnight))
    }

    $versionText = @(& git --version)[0]
    if ($versionText -match '(\d+)\.(\d+)' -and ([int]$Matches[1] -lt 2 -or ([int]$Matches[1] -eq 2 -and [int]$Matches[2] -lt 40))) {
        throw "git-too-old：需要 Git 2.40+（--attr-source），当前 $versionText"
    }
    $toplevel = @(Invoke-Git @('rev-parse', '--show-toplevel'))[0]
    # An empty prefix means the path is the worktree root, regardless of short names, subst drives or junctions.
    $prefix = @(Invoke-Git @('rev-parse', '--show-prefix'))
    if ($prefix.Count -gt 0) { throw "repository-not-toplevel：$repo 不是仓库根目录（Git 解析到 $toplevel）" }
    if ($TargetRef.StartsWith('-')) { throw 'invalid-target-ref' }
    $cutoff = @(Invoke-Git @('rev-parse', '--verify', '--end-of-options', "$TargetRef^{commit}"))[0]
    # --symbolic-full-name would echo --end-of-options back, so the ref is validated above instead.
    $fullRef = @(Invoke-Git @('rev-parse', '--symbolic-full-name', $TargetRef) | Where-Object { $_ -like 'refs/*' })
    $targetFullRef = if ($fullRef.Count -gt 0) { $fullRef[0] } else { $null }
    if (-not $targetFullRef) { $limitations.Add('target-not-ref：目标不是引用（如裸 SHA），无法读取 reflog 进入时间') }
    $shallow = @(Invoke-Git @('rev-parse', '--is-shallow-repository'))[0] -eq 'true'
    if ($shallow) { $limitations.Add('shallow-repository：历史不完整，覆盖可能缺失') }

    # Index-vs-HEAD plus stat checks; filters are disabled by the empty attribute source.
    $staged = @(Invoke-Git @('diff', '--cached', '--name-only', '--no-ext-diff', '--no-textconv'))
    $dirtyKnown = $true
    if (Test-InfoAttributesFilter) {
        # Skip the content check that could run that filter; only untracked files are listed.
        $dirtyKnown = $false
        $working = @(Invoke-Git @('ls-files', '--others', '--exclude-standard'))
        $limitations.Add('dirty-unknown：.git/info/attributes 配置了 filter，为避免执行它未检测已跟踪文件的修改')
    } else {
        $working = @(Invoke-Git @('ls-files', '--modified', '--deleted', '--others', '--exclude-standard'))
    }
    $dirtyPaths = @(@($staged) + @($working) | Sort-Object -Unique)
    if ($working.Count -gt 0) { $limitations.Add('dirty-stat-only：工作树改动按 stat 元数据检测，可能多报') }

    $mode = 'date-window'
    $base = $null
    if ($BaselineSha) {
        if (Test-GitSuccess @('rev-parse', '--verify', '--quiet', '--end-of-options', "$BaselineSha^{commit}")) {
            $base = @(Invoke-Git @('rev-parse', '--verify', '--end-of-options', "$BaselineSha^{commit}"))[0]
            if (Test-GitSuccess @('merge-base', '--is-ancestor', $base, $cutoff)) { $mode = 'baseline-range' }
            else { $limitations.Add('baseline-not-ancestor：上次基线不是截止提交的祖先，可能发生历史改写；已改按时间窗口列候选') }
        } else {
            $limitations.Add('baseline-missing：上次基线对象不存在（可能强推后已 gc）；已改按时间窗口列候选')
        }
    } else {
        $limitations.Add('no-baseline：无上次基线，进入事件按第一父链提交时间筛选，只是候选，不能证明当日进入主线')
    }
    $eventShas = @(if ($mode -eq 'baseline-range') {
        Invoke-Git @('rev-list', '--first-parent', '--reverse', "$base..$cutoff")
    } else {
        Invoke-Git @('rev-list', '--first-parent', '--reverse', "--since=$($sinceAt.ToString('o'))", $cutoff)
    })
    $enteredAt = Get-ReflogEntryTimes $targetFullRef $eventShas $sinceAt
    if ($eventShas.Count -gt 0 -and $enteredAt.Count -lt $eventShas.Count) {
        $limitations.Add("reflog-partial：$($eventShas.Count - $enteredAt.Count) 个事件没有本地 reflog 进入时间，只能按提交时间判断")
    }

    $events = [Collections.Generic.List[object]]::new()
    $commits = [ordered]@{}
    $innerMerges = [Collections.Generic.List[string]]::new()
    $patchIds = @{}
    foreach ($sha in $eventShas) {
        $info = Get-CommitInfo $sha
        $committedInWindow = [DateTimeOffset]::Parse($info.committedAt, [Globalization.CultureInfo]::InvariantCulture) -ge $sinceAt
        $entered = if ($enteredAt.ContainsKey($sha)) { $enteredAt[$sha] } else { $null }
        $enteredInWindow = if ($entered) { [DateTimeOffset]::Parse($entered, [Globalization.CultureInfo]::InvariantCulture) -ge $sinceAt } else { $null }
        $event = [ordered]@{ sha = $info.sha; kind = 'commit'; committedAt = $info.committedAt; authoredAt = $info.authoredAt; subject = $info.subject; committedInWindow = $committedInWindow; reflogEnteredAt = $entered; enteredInWindow = $enteredInWindow; parents = $info.parents; files = @(); patchId = $null; broughtIn = @() }
        if ($info.parents.Count -gt 1) {
            $event.kind = 'merge'
            $event.files = @(Invoke-Git @('diff', '--name-only', '--no-ext-diff', '--no-textconv', $info.parents[0], $info.sha))
            $brought = [Collections.Generic.List[object]]::new()
            $range = @('rev-list', '--reverse', $info.sha, "^$($info.parents[0])")
            if ($base) { $range += "^$base" }
            foreach ($inner in (Invoke-Git $range)) {
                if ($inner -eq $info.sha) { continue }
                $innerInfo = Get-CommitInfo $inner
                $isMerge = $innerInfo.parents.Count -gt 1
                $innerPatch = if ($isMerge) { $null } else { Get-PatchId $inner }
                $brought.Add([ordered]@{ sha = $inner; isMerge = $isMerge; committedAt = $innerInfo.committedAt; authoredAt = $innerInfo.authoredAt; subject = $innerInfo.subject; patchId = $innerPatch })
                if ($isMerge) { if (-not $innerMerges.Contains($inner)) { $innerMerges.Add($inner) } }
                elseif (-not $commits.Contains($inner)) { $commits[$inner] = $innerPatch }
            }
            $event.broughtIn = $brought.ToArray()
        } else {
            if ($info.parents.Count -eq 0) {
                $event.kind = 'root'
                $event.files = @(Invoke-Git @('diff-tree', '--root', '--no-commit-id', '--name-only', '-r', $info.sha))
            } else {
                $event.files = @(Invoke-Git @('diff', '--name-only', '--no-ext-diff', '--no-textconv', $info.parents[0], $info.sha))
            }
            $event.patchId = Get-PatchId $info.sha
            if (-not $commits.Contains($info.sha)) { $commits[$info.sha] = $event.patchId }
        }
        $events.Add($event)
    }
    foreach ($entry in $commits.GetEnumerator()) {
        if (-not $entry.Value) { continue }
        if (-not $patchIds.ContainsKey($entry.Value)) { $patchIds[$entry.Value] = [Collections.Generic.List[string]]::new() }
        $patchIds[$entry.Value].Add($entry.Key)
    }
    $equivalent = @($patchIds.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Name | ForEach-Object { [ordered]@{ patchId = $_.Name; shas = $_.Value.ToArray() } })
    if ($events.Count -eq 0) { $limitations.Add('zero-events：本快照未发现进入事件；零候选不等于远端零合入') }

    $result.complete = $true
    $result.repository = $repo
    $result.toplevel = $toplevel
    $result.targetRef = $TargetRef
    $result.targetFullRef = $targetFullRef
    $result.cutoffSha = $cutoff
    $result.mode = $mode
    $result.baselineSha = $base
    $result.timeZone = $TimeZone
    $result.since = $sinceAt.ToString('yyyy-MM-ddTHH:mm:sszzz')
    $result.capturedAt = $capturedAt.ToString('yyyy-MM-ddTHH:mm:sszzz')
    $result.snapshotKind = 'local-snapshot'
    $result.shallow = $shallow
    $result.dirty = [ordered]@{ known = $dirtyKnown; isDirty = if ($dirtyKnown) { $dirtyPaths.Count -gt 0 } else { $null }; entryCount = $dirtyPaths.Count; paths = @($dirtyPaths | Select-Object -First 200); truncated = $dirtyPaths.Count -gt 200 }
    $result.events = $events.ToArray()
    $result.coverage = [ordered]@{
        eventCount = $events.Count
        mergeCount = @($events | Where-Object { $_.kind -eq 'merge' }).Count
        committedOutOfWindowCount = @($events | Where-Object { -not $_.committedInWindow }).Count
        enteredOutOfWindowCount = @($events | Where-Object { $_.enteredInWindow -eq $false }).Count
        uniqueCommitCount = $commits.Count
        toReview = @($commits.Keys)
        innerMerges = $innerMerges.ToArray()
        equivalentPatches = $equivalent
        equivalentScope = '仅本快照内的提交'
    }
} catch [System.Management.Automation.NativeCommandExitException] {
    $code = 2
    $limitations.Add("git-failed：Git 命令退出码 $($_.Exception.ExitCode)")
} catch {
    $code = 2
    $limitations.Add("snapshot-failed：$($_.Exception.Message)")
}
$result.limitations = $limitations.ToArray()
$result.notChecked = $notChecked
$result | ConvertTo-Json -Depth 8
exit $code
