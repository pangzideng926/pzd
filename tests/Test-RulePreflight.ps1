[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TempRoot,
    [string] $ScriptPath = (Join-Path $PSScriptRoot '../scripts/Test-RuleSync.ps1')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# 所有写入均为显式 TempRoot 下的新夹具；不清理文件、不访问网络。
if (-not [IO.Path]::IsPathFullyQualified($TempRoot)) { throw 'TempRoot 必须是绝对路径。' }
$ScriptPath = [IO.Path]::GetFullPath($ScriptPath)
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw '待测预检脚本不存在。' }
$testedScriptSHA256 = (Get-FileHash -LiteralPath $ScriptPath -Algorithm SHA256).Hash
$repository = Split-Path (Split-Path $ScriptPath -Parent) -Parent
$rulePath = Join-Path $repository '.ai/规则/规则同步.md'
$ruleText = Get-Content -LiteralPath $rulePath -Raw -Encoding UTF8
$allowlistBlock = [regex]::Match($ruleText, '(?s)<!-- source-allowlist:start -->(.*?)<!-- source-allowlist:end -->')
if (-not $allowlistBlock.Success) { throw '现行同步规则缺少可解析的白名单标记。' }
$paths = @([regex]::Matches($allowlistBlock.Groups[1].Value, '(?m)^\|\s*`([^`]+)`\s*\|') | ForEach-Object { $_.Groups[1].Value })
if ($paths.Count -lt 8) { throw '测试需要至少八条现行白名单路径。' }

$runRoot = Join-Path ([IO.Path]::GetFullPath($TempRoot)) ('rule-preflight-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $runRoot 'source'
$target = Join-Path $runRoot 'repository'
$hooks = Join-Path $runRoot 'empty-hooks'
[void][IO.Directory]::CreateDirectory($source)
[void][IO.Directory]::CreateDirectory($target)
[void][IO.Directory]::CreateDirectory($hooks)
$assertions = [Collections.Generic.List[string]]::new()
$skipped = [Collections.Generic.List[string]]::new()
$runs = [Collections.Generic.List[object]]::new()

function Write-FixtureFile([string] $Root, [string] $Relative, [string] $Text) {
    $file = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path $file -Parent))
    [IO.File]::WriteAllText($file, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-FixtureGit([string[]] $GitArguments, [string] $GitRoot = $target) {
    # hooksPath 只作用于本次命令，防止夹具触发机器上的全局钩子。
    $output = & git --no-optional-locks -C $GitRoot -c "core.hooksPath=$hooks" -c 'core.fsmonitor=false' -c 'core.excludesFile=' -c 'commit.gpgsign=false' -c 'core.autocrlf=false' -c 'user.name=Rule preflight fixture' -c 'user.email=fixture@example.invalid' @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "Git 夹具命令失败，退出码 $LASTEXITCODE。" }
    return ($output -join "`n")
}

function Assert-True([bool] $Condition, [string] $Name) {
    if (-not $Condition) { throw "断言失败：$Name。夹具保留于 $runRoot" }
    $assertions.Add($Name)
}

function Get-TreeFingerprint([string] $Root) {
    $entries = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            $relative = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/')
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                $entries.Add("LINK`t$relative`t$($item.LinkTarget)")
            } elseif ($item.PSIsContainer) {
                $entries.Add("DIR`t$relative")
                $pending.Push($item.FullName)
            } else {
                $entries.Add("FILE`t$relative`t$((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash)")
            }
        }
    }
    return (($entries | Sort-Object) -join "`n")
}

function Get-GitObservedState {
    # status/diff 工作树内容可能运行 clean filter；测试快照也只观察 index、ref 和文件字节。
    return ((Invoke-FixtureGit @('ls-files', '--stage', '-z')) + "`n" + (Invoke-FixtureGit @('rev-parse', 'HEAD')) + "`n" + (Invoke-FixtureGit @('symbolic-ref', '--quiet', 'HEAD')))
}

function Invoke-Check([string] $Name, [int] $ExpectedExit = 0, [hashtable] $InheritedGitEnvironment = @{}, [switch] $SkipGitState) {
    $gitBefore = if ($SkipGitState) { $null } else { Get-GitObservedState }
    $sourceBefore = Get-TreeFingerprint $source
    $targetBefore = Get-TreeFingerprint $target
    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = (Get-Command pwsh).Source
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $processInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $ScriptPath, '-SourceRoot', $source, '-RepositoryRoot', $target)) {
        [void]$processInfo.ArgumentList.Add($argument)
    }
    foreach ($key in $InheritedGitEnvironment.Keys) { $processInfo.Environment[$key] = $InheritedGitEnvironment[$key] }
    $process = [Diagnostics.Process]::Start($processInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()
    Assert-True ($exitCode -eq $ExpectedExit) "$Name：退出码 $ExpectedExit"
    try { $result = $stdout | ConvertFrom-Json -Depth 64 } catch { throw "$Name 未输出单一有效 JSON；原始内容不回显以免泄漏夹具敏感值。" }
    Assert-True ($result.readOnly -eq $true) "$Name：声明只读"
    Assert-True ((Get-TreeFingerprint $source) -ceq $sourceBefore) "$Name：源树内容与链接不变"
    Assert-True ((Get-TreeFingerprint $target) -ceq $targetBefore) "$Name：目标树含 Git 文件内容不变"
    if (-not $SkipGitState) { Assert-True ((Get-GitObservedState) -ceq $gitBefore) "$Name：Git 索引与引用不变" }
    Assert-True ($null -ne $result.checked -and $null -ne $result.notChecked) "$Name：区分已检查与未检查"
    $runs.Add([pscustomobject]@{ name = $Name; exitCode = $exitCode; findings = @($result.findings).Count; stderrEmpty = [string]::IsNullOrWhiteSpace($stderr); gitSnapshotUsed = -not $SkipGitState })
    return [pscustomobject]@{ report = $result; stdout = $stdout; stderr = $stderr }
}

# 基础夹具：以现行规则文件作为唯一白名单来源，其余文件为无项目内容的合成材料。
Write-FixtureFile $target '.ai/规则/规则同步.md' $ruleText
foreach ($path in $paths) {
    Write-FixtureFile $source $path "# Fixture`n`n固定测试正文。`n"
    Write-FixtureFile $target $path "# Fixture`n`n固定测试正文。`n"
}
Write-FixtureFile $source $paths[1] "# Fixture`n`n本地候选改变。`n"
$archive = Join-Path $runRoot 'retained-fixture-files'
[void][IO.Directory]::CreateDirectory($archive)
Move-Item -LiteralPath (Join-Path $source $paths[2]) -Destination (Join-Path $archive 'source-missing.md')
Move-Item -LiteralPath (Join-Path $target $paths[3]) -Destination (Join-Path $archive 'target-missing.md')
Move-Item -LiteralPath (Join-Path $source $paths[4]) -Destination (Join-Path $archive 'both-source.md')
Move-Item -LiteralPath (Join-Path $target $paths[4]) -Destination (Join-Path $archive 'both-target.md')
Write-FixtureFile $source $paths[5] "# 链接测试`n`n[缺失](definitely-missing.md)`n`n[坏锚点](#definitely-missing-heading)`n"
$syntheticValue = 'TEST_ONLY_SYNTHETIC_VALUE_' + [Guid]::NewGuid().ToString('N')
$missingRelative = [IO.Path]::GetRelativePath((Split-Path (Join-Path $target $paths[5]) -Parent), (Join-Path $target $paths[3])).Replace('\', '/')
Write-FixtureFile $target $paths[5] ("# 格式测试`n`npassword = `"" + $syntheticValue + "`"`n`n[缺失]($missingRelative)`n`n[坏锚点](#definitely-missing-heading)`n`n[越界](../../../outside-allowlist-secret.txt)`n")
[void](Invoke-FixtureGit @('init', '-b', 'main'))
[void](Invoke-FixtureGit @('add', '--', '.'))
[void](Invoke-FixtureGit @('commit', '-m', 'fixture baseline'))
$baseline = Invoke-FixtureGit @('rev-parse', 'HEAD')
[void](Invoke-FixtureGit @('remote', 'add', 'origin', 'https://example.invalid/fixture.git'))
[void](Invoke-FixtureGit @('update-ref', 'refs/remotes/origin/main', $baseline))
[void](Invoke-FixtureGit @('config', 'branch.main.remote', 'origin'))
[void](Invoke-FixtureGit @('config', 'branch.main.merge', 'refs/heads/main'))
$reviewAssets = @('scripts/Test-Lessons.ps1', 'tests/Test-Lessons.ps1', 'tests/ReviewBehavior.md', '.ai/规则/验证工具.md', 'scripts/Invoke-Verification.ps1', 'scripts/Test-ExecutionEnvironment.ps1', 'tests/Test-VerificationTools.ps1')
foreach ($asset in $reviewAssets) { Write-FixtureFile $target $asset "# 合成复盘资产`n" }
[void](Invoke-FixtureGit (@('add', '--') + $reviewAssets))

$basic = Invoke-Check '基本比较与内容提示'
foreach ($asset in $reviewAssets) {
    Assert-True ($asset -in $basic.report.git.stagedPaths) "复盘资产确实进入暂存检查：$asset"
    Assert-True (@($basic.report.findings | Where-Object { $_.kind -eq 'outside-allowlist' -and $_.path -eq $asset }).Count -eq 0) "复盘资产未被误判越界：$asset"
}
$expectedStates = @('identical', 'different', 'source-missing', 'target-missing', 'both-missing')
for ($i = 0; $i -lt $expectedStates.Count; $i++) {
    $comparison = @($basic.report.comparisons | Where-Object path -CEQ $paths[$i])
    Assert-True ($comparison.Count -eq 1 -and $comparison[0].state -eq $expectedStates[$i]) "比较状态：$($expectedStates[$i])"
}
Assert-True (@($basic.report.findings | Where-Object kind -EQ 'broken-link').Count -gt 0) '发现坏文件链接'
Assert-True (@($basic.report.findings | Where-Object kind -EQ 'broken-anchor').Count -gt 0) '发现坏标题锚点'
Assert-True (@($basic.report.findings | Where-Object kind -EQ 'excluded-link').Count -gt 0) '拒绝解析仓库外的相对链接'
Assert-True (@($basic.report.findings | Where-Object kind -EQ 'sensitive-pattern').Count -gt 0) '提示敏感格式'
Assert-True (-not ($basic.stdout.Contains($syntheticValue) -or $basic.stderr.Contains($syntheticValue))) '敏感值不进入标准输出或错误输出'

# 用两次合成提交证明当前工作树已移除的内容仍留在待发布历史中。
$historyValue = 'TEST_ONLY_HISTORY_VALUE_' + [Guid]::NewGuid().ToString('N')
Write-FixtureFile $target $paths[6] ("# 历史格式测试`n`npassword = `"" + $historyValue + "`"`n")
$historicalOutside = 'private-fixture/should-never-publish.md'
Write-FixtureFile $target $historicalOutside "仅作排除路径测试，不含项目资料。`n"
[void](Invoke-FixtureGit @('add', '--', $paths[6], $historicalOutside))
[void](Invoke-FixtureGit @('commit', '-m', 'fixture pending addition'))
$historicalCommit = Invoke-FixtureGit @('rev-parse', 'HEAD')
Write-FixtureFile $target $paths[6] "# Fixture`n`n固定测试正文。`n"
Move-Item -LiteralPath (Join-Path $target $historicalOutside) -Destination (Join-Path $archive 'historical-outside.md')
[void](Invoke-FixtureGit @('add', '-u', '--', $paths[6], $historicalOutside))
[void](Invoke-FixtureGit @('commit', '-m', 'fixture pending removal'))
$stagedOutside = '.ai/项目/fixture-only.md'
Write-FixtureFile $target $stagedOutside "排除路径的合成测试。`n"
[void](Invoke-FixtureGit @('add', '--', $stagedOutside))
Write-FixtureFile $target $paths[7] "# 未暂存测试`n"

$history = Invoke-Check '暂存区与待发布历史'
Assert-True (@($history.report.git.stagedPaths) -contains $stagedOutside) '记录非白名单暂存路径'
Assert-True (@($history.report.git.workingPaths) -contains $paths[7]) '记录未暂存变化'
Assert-True (@($history.report.git.pendingCommits).Count -eq 2) '按本地跟踪引用识别两个待发布夹具提交'
Assert-True (@($history.report.git.pendingCommits | Where-Object sha -EQ $historicalCommit).Count -eq 1) '保留完整历史提交 SHA'
Assert-True (@($history.report.findings | Where-Object { $_.kind -eq 'outside-allowlist' -and $_.scope -eq 'staged' -and $_.path -eq $stagedOutside }).Count -gt 0) '非白名单暂存项告警'
Assert-True (@($history.report.findings | Where-Object { $_.kind -eq 'outside-allowlist' -and $_.scope -eq 'commit' -and $_.path -eq $historicalOutside }).Count -gt 0) '已移出工作树的非白名单历史仍告警'
Assert-True (@($history.report.findings | Where-Object { $_.kind -eq 'sensitive-pattern' -and $_.scope -eq 'commit' -and $_.path -eq $paths[6] }).Count -gt 0) '已改干净文件的待发布历史敏感格式仍告警'
Assert-True (-not ($history.stdout.Contains($historyValue) -or $history.stderr.Contains($historyValue))) '历史敏感值不进入输出'

# 只创建指向夹具内部专用文件的链接；无创建权限时明确跳过，不提权。
$linkDestination = Join-Path $runRoot 'outside-allowlist-secret.txt'
[IO.File]::WriteAllText($linkDestination, ('password = "' + $syntheticValue + '"'), [Text.UTF8Encoding]::new($false))
$linkPath = Join-Path $source $paths[4]
[void][IO.Directory]::CreateDirectory((Split-Path $linkPath -Parent))
$linkCreated = $false
try {
    [void](New-Item -ItemType SymbolicLink -Path $linkPath -Target $linkDestination)
    $linkCreated = $true
} catch {
    $skipped.Add('当前环境不能创建符号链接；未提权，链接拒读动态测试未运行。')
}
if ($linkCreated) {
    $linked = Invoke-Check '符号链接边界'
    $comparison = @($linked.report.comparisons | Where-Object path -CEQ $paths[4])
    Assert-True ($comparison.Count -eq 1 -and $comparison[0].state -eq 'excluded-link') '白名单路径的符号链接不读取目标'
    Assert-True (-not ($linked.stdout.Contains($syntheticValue) -or $linked.stderr.Contains($syntheticValue))) '链接目标内容不泄漏'
}

# 将重定向变量仅注入被测子进程；宿主测试工具始终操作自己的明确夹具目录。
$external = Join-Path $runRoot 'foreign-repository'
[void][IO.Directory]::CreateDirectory($external)
[void](Invoke-FixtureGit -GitArguments @('init', '-b', 'foreign-fixture') -GitRoot $external)
Copy-Item -LiteralPath (Join-Path $target '.git/index') -Destination (Join-Path $external '.git/foreign-index')
Write-FixtureFile $external 'outside-sentinel.txt' "外部夹具应保持不变。`n"
$externalBefore = Get-TreeFingerprint $external
$expectedHead = Invoke-FixtureGit @('rev-parse', 'HEAD')
$inherited = @{
    GIT_DIR = (Join-Path $external '.git')
    GIT_WORK_TREE = $external
    GIT_INDEX_FILE = (Join-Path $external '.git/foreign-index')
    GIT_OBJECT_DIRECTORY = (Join-Path $external '.git/objects')
    GIT_CONFIG_COUNT = '1'
    GIT_CONFIG_KEY_0 = 'core.worktree'
    GIT_CONFIG_VALUE_0 = $external
}
$isolated = Invoke-Check '继承 Git 环境隔离' 0 $inherited
Assert-True ($isolated.report.git.head -ceq $expectedHead) '继承 GIT_DIR 等变量后仍检查明确目标 HEAD'
Assert-True ((Get-TreeFingerprint $external) -ceq $externalBefore) '外部 Git 目录与索引未改变'

# 故意破坏 Git 路由的夹具不先调用 Git；用全树字节指纹验证只读边界。
Write-FixtureFile $target '.git/commondir' ((Join-Path $external '.git') + "`n")
$commonBlocked = Invoke-Check '禁止 Git commondir 重定向' 2 -SkipGitState
Assert-True (@($commonBlocked.report.findings | Where-Object kind -EQ 'check-incomplete').Count -gt 0) 'commondir 外部重定向明确失败关闭'
Assert-True ((Get-TreeFingerprint $external) -ceq $externalBefore) 'commondir 指向的外部 Git 树不变'
Move-Item -LiteralPath (Join-Path $target '.git/commondir') -Destination (Join-Path $archive 'commondir-marker')

$originalHead = Get-Content -LiteralPath (Join-Path $target '.git/HEAD') -Raw -Encoding UTF8
$outsideValue = 'OUTSIDE_FIXTURE_ONLY_' + [Guid]::NewGuid().ToString('N')
Write-FixtureFile $target 'outside.md' $outsideValue
Write-FixtureFile $target '.git/HEAD' "ref: refs/heads/../../../outside.md`n"
$headBlocked = Invoke-Check '禁止 Git HEAD 路径越界' 2 -SkipGitState
Assert-True (@($headBlocked.report.findings | Where-Object kind -EQ 'check-incomplete').Count -gt 0) '非法 HEAD 引用明确失败关闭'
Assert-True (-not ($headBlocked.stdout.Contains($outsideValue) -or $headBlocked.stderr.Contains($outsideValue))) '非法 HEAD 不回显越界目标内容'
Write-FixtureFile $target '.git/HEAD' $originalHead

# 命令只有在错误执行 clean filter 时才会写标记；正确行为是配置检查阶段直接失败关闭。
$sentinel = Join-Path $runRoot 'filter-was-executed.txt'
$fakeBody = '[IO.File]::WriteAllText(''' + $sentinel.Replace("'", "''") + ''', ''unexpected execution'', [Text.UTF8Encoding]::new($false))'
$fakeCommand = 'pwsh -NoLogo -NoProfile -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($fakeBody))
$originalConfig = Get-Content -LiteralPath (Join-Path $target '.git/config') -Raw -Encoding UTF8
Write-FixtureFile $target '.gitattributes' "*.md filter=fixture`n"
[void](Invoke-FixtureGit @('config', 'filter.fixture.clean', $fakeCommand))
[void](Invoke-FixtureGit @('config', 'filter.fixture.required', 'true'))
Assert-True (-not (Test-Path -LiteralPath $sentinel)) 'filter 标记在测试前不存在'
$filterBlocked = Invoke-Check '禁止 Git 外部过滤器' 2
Assert-True (@($filterBlocked.report.findings | Where-Object kind -EQ 'check-incomplete').Count -gt 0) 'filter 配置明确标为检查未完成'
Assert-True (-not (Test-Path -LiteralPath $sentinel)) '预检及测试快照均未执行 fake filter'
Write-FixtureFile $target '.git/config' $originalConfig

# 浅历史不满足完整待发布历史检查条件；不把不足的对象证据误报为文件已经删除。
Write-FixtureFile $target '.git/shallow' ($baseline + "`n")
$shallowBlocked = Invoke-Check '不完整 Git 历史失败关闭' 2
Assert-True (@($shallowBlocked.report.findings | Where-Object kind -EQ 'check-incomplete').Count -gt 0) '浅历史明确标为检查未完成'
Move-Item -LiteralPath (Join-Path $target '.git/shallow') -Destination (Join-Path $archive 'shallow-marker')

Move-Item -LiteralPath (Join-Path $target '.ai/规则/规则同步.md') -Destination (Join-Path $archive 'missing-config.md')
$incomplete = Invoke-Check '配置缺失失败关闭' 2
Assert-True (@($incomplete.report.findings | Where-Object kind -EQ 'check-incomplete').Count -gt 0) '无法确定白名单时明确检查未完成'
Assert-True ((Get-FileHash -LiteralPath $ScriptPath -Algorithm SHA256).Hash -ceq $testedScriptSHA256) '所有用例对应同一预检脚本指纹'

[pscustomobject]@{
    status = 'passed'
    assertions = $assertions.Count
    checks = $assertions.ToArray()
    skipped = $skipped.ToArray()
    runs = $runs.ToArray()
    fixtureRoot = $runRoot
    testedScript = $ScriptPath
    scriptSHA256 = $testedScriptSHA256
    note = '仅本地隔离夹具有合成 Git 提交；真实规则仓库未提交，未执行网络访问，夹具原位保留。'
} | ConvertTo-Json -Depth 12
