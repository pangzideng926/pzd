[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TempRoot,
    [string] $ScriptRoot = (Join-Path $PSScriptRoot '../scripts')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# 所有写入均为显式 TempRoot 下的新夹具；不清理文件、不访问网络。
if (-not [IO.Path]::IsPathFullyQualified($TempRoot)) { throw 'TempRoot 必须是绝对路径。' }
$snapshotScript = [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Get-ReviewSnapshot.ps1'))
$searchScript = [IO.Path]::GetFullPath((Join-Path $ScriptRoot 'Search-Knowledge.ps1'))
foreach ($path in $snapshotScript, $searchScript) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "待测脚本不存在：$path" } }

$runRoot = Join-Path ([IO.Path]::GetFullPath($TempRoot)) ('workflow-scripts-' + [Guid]::NewGuid().ToString('N'))
$repo = Join-Path $runRoot 'repository'
$hooks = Join-Path $runRoot 'empty-hooks'
$knowledge = Join-Path $runRoot 'knowledge'
foreach ($dir in $repo, $hooks, $knowledge) { [void][IO.Directory]::CreateDirectory($dir) }
$assertions = [Collections.Generic.List[string]]::new()

function Write-FixtureFile([string] $Root, [string] $Relative, [string] $Text) {
    $file = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path $file -Parent))
    [IO.File]::WriteAllText($file, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-FixtureGit([string[]] $GitArguments, [string] $Date = '2026-10-10T09:00:00+08:00') {
    # 日期只作用于本次命令；hooksPath 防止夹具触发机器上的全局钩子。
    $env:GIT_AUTHOR_DATE = $Date
    $env:GIT_COMMITTER_DATE = $Date
    try {
        $output = & git --no-optional-locks -C $repo -c "core.hooksPath=$hooks" -c 'core.fsmonitor=false' -c 'commit.gpgsign=false' -c 'core.autocrlf=false' -c 'user.name=Workflow fixture' -c 'user.email=fixture@example.invalid' @GitArguments
    } finally {
        $env:GIT_AUTHOR_DATE = $null
        $env:GIT_COMMITTER_DATE = $null
    }
    return ($output -join "`n")
}

function Assert-True([bool] $Condition, [string] $Name) {
    if (-not $Condition) { throw "断言失败：$Name。夹具保留于 $runRoot" }
    $assertions.Add($Name)
}

function Get-RepoState {
    $files = Get-ChildItem -LiteralPath $repo -Recurse -Force -File | Sort-Object FullName | ForEach-Object { "$([IO.Path]::GetRelativePath($repo, $_.FullName))`t$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
    return ($files -join "`n")
}

function Invoke-Script([string] $Path, [string[]] $Arguments, [int] $ExpectedExit = 0) {
    # 预期失败用例也要读取 JSON，因此本函数内由退出码断言代替原生失败中止。
    $PSNativeCommandUseErrorActionPreference = $false
    $output = & pwsh -NoProfile -File $Path @Arguments
    $exit = $LASTEXITCODE
    Assert-True ($exit -eq $ExpectedExit) "$(Split-Path $Path -Leaf) 退出码 $ExpectedExit（实际 $exit）"
    return (($output -join "`n") | ConvertFrom-Json -Depth 10 -DateKind String)
}

# ---- 复盘快照夹具：基线、普通提交、带入旧日期提交的合并、cherry-pick 等价补丁 ----
Invoke-FixtureGit @('init', '-q', '-b', 'main') | Out-Null
Write-FixtureFile $repo 'app.txt' "base`n"
Invoke-FixtureGit @('add', 'app.txt') | Out-Null
Invoke-FixtureGit @('commit', '-q', '-m', 'base') '2026-10-01T09:00:00+08:00' | Out-Null
$baseline = Invoke-FixtureGit @('rev-parse', 'HEAD')

Invoke-FixtureGit @('checkout', '-q', '-b', 'feature') | Out-Null
Write-FixtureFile $repo 'feature.txt' "old feature`n"
Invoke-FixtureGit @('add', 'feature.txt') | Out-Null
Invoke-FixtureGit @('commit', '-q', '-m', 'old-dated feature') '2026-10-02T09:00:00+08:00' | Out-Null
$oldFeature = Invoke-FixtureGit @('rev-parse', 'HEAD')

Invoke-FixtureGit @('checkout', '-q', 'main') | Out-Null
Write-FixtureFile $repo 'app.txt' "base`nmain change`n"
Invoke-FixtureGit @('commit', '-q', '-am', 'main change') '2026-10-17T10:00:00+08:00' | Out-Null
$mainChange = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('merge', '-q', '--no-ff', '-m', 'merge feature', 'feature') '2026-10-17T11:00:00+08:00' | Out-Null
$merge = Invoke-FixtureGit @('rev-parse', 'HEAD')

Invoke-FixtureGit @('checkout', '-q', '-b', 'hotfix', $baseline) | Out-Null
Write-FixtureFile $repo 'fix.txt' "fix`n"
Invoke-FixtureGit @('add', 'fix.txt') | Out-Null
Invoke-FixtureGit @('commit', '-q', '-m', 'hotfix') '2026-10-17T12:00:00+08:00' | Out-Null
$hotfix = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('checkout', '-q', 'main') | Out-Null
Invoke-FixtureGit @('cherry-pick', $hotfix) '2026-10-17T13:00:00+08:00' | Out-Null
$picked = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('merge', '-q', '--no-ff', '-m', 'merge hotfix', 'hotfix') '2026-10-17T14:00:00+08:00' | Out-Null
$mergeHotfix = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('checkout', '-q', '-b', 'unmerged', $baseline) | Out-Null
Write-FixtureFile $repo 'side.txt' "side`n"
Invoke-FixtureGit @('add', 'side.txt') | Out-Null
Invoke-FixtureGit @('commit', '-q', '-m', 'unmerged side') '2026-10-17T15:00:00+08:00' | Out-Null
$unmerged = Invoke-FixtureGit @('rev-parse', 'HEAD')
Invoke-FixtureGit @('checkout', '-q', 'main') | Out-Null

$before = Get-RepoState
$withBase = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', $baseline, '-Since', '2026-10-17T00:00:00+08:00')
Assert-True ($withBase.complete -and $withBase.mode -eq 'baseline-range') '有基线时按基线区间计算'
Assert-True ($withBase.cutoffSha -eq $mergeHotfix) '固定截止 SHA'
Assert-True ((@($withBase.events.sha) -join ',') -eq "$mainChange,$merge,$picked,$mergeHotfix") '按第一父链顺序列出进入事件'
$mergeEvent = @($withBase.events | Where-Object sha -EQ $merge)[0]
Assert-True ($mergeEvent.kind -eq 'merge' -and @($mergeEvent.broughtIn.sha) -contains $oldFeature) '合并展开旧日期带入提交'
Assert-True (@($mergeEvent.files) -contains 'feature.txt') '合并记录相对第一父的集成差异文件'
Assert-True (@($withBase.coverage.toReview) -contains $oldFeature -and @($withBase.coverage.toReview) -notcontains $merge) '待审清单含带入提交、不含合并提交本身'
Assert-True ($withBase.coverage.uniqueCommitCount -eq 4) '按完整 SHA 去重计数'
$equivalent = @($withBase.coverage.equivalentPatches)
Assert-True ($equivalent.Count -eq 1 -and @($equivalent[0].shas) -contains $picked -and @($equivalent[0].shas) -contains $hotfix) 'patch-id 识别 cherry-pick 等价补丁'
Assert-True (-not $withBase.dirty.isDirty) '干净工作树报告非 dirty'
Assert-True ((Get-RepoState) -ceq $before) '快照不修改仓库文件与 Git 元数据'

$noBase = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-Since', '2026-10-17T00:00:00+08:00')
Assert-True ($noBase.mode -eq 'date-window' -and @($noBase.limitations | Where-Object { $_ -like 'no-baseline*' }).Count -eq 1) '无基线时标注候选限制'
Assert-True (@($noBase.events.sha) -notcontains $baseline) '时间窗口排除窗口前的第一父提交'

$window = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', $baseline, '-Since', '2026-10-17T12:30:00+08:00')
Assert-True ($window.coverage.committedOutOfWindowCount -eq 2) '基线区间内按提交时间区分窗口外事件'
Assert-True ((@($window.events | Where-Object sha -EQ $merge)[0].reflogEnteredAt) -like '2026-10-17T11:00:00*' -and $window.coverage.enteredOutOfWindowCount -eq 2) '从本地 reflog 读取进入主线时间'

$noOffset = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', $baseline, '-Since', '2026-10-17T12:30:00')
Assert-True ($noOffset.since -eq '2026-10-17T12:30:00+08:00') '无偏移的 -Since 按 -TimeZone 解释'
$missingBase = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', ('1234' * 10), '-Since', '2026-10-17T00:00:00+08:00')
Assert-True ($missingBase.complete -and $missingBase.mode -eq 'date-window' -and @($missingBase.limitations | Where-Object { $_ -like 'baseline-missing*' }).Count -eq 1) '基线对象不存在时降级为时间窗口'
Assert-True (@($noBase.events).Count -gt 0) '时间窗口确实列出当天事件'
$subdir = Join-Path $repo 'nested'
[void][IO.Directory]::CreateDirectory($subdir)
$nested = Invoke-Script $snapshotScript @('-Repository', $subdir, '-TargetRef', 'main') 2
Assert-True (-not $nested.complete -and (@($nested.limitations) -join ';') -like '*repository-not-toplevel*') '子目录不能冒充仓库根目录'

$rewritten = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', $unmerged, '-Since', '2026-10-17T00:00:00+08:00')
Assert-True ($rewritten.mode -eq 'date-window' -and @($rewritten.limitations | Where-Object { $_ -like 'baseline-not-ancestor*' }).Count -eq 1) '基线非祖先时降级并标注'

Write-FixtureFile $repo 'untracked.txt' "x`n"
$dirty = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'main', '-BaselineSha', $baseline)
Assert-True ($dirty.dirty.isDirty -and @($dirty.dirty.paths) -contains 'untracked.txt') '报告未跟踪文件为 dirty'
$missing = Invoke-Script $snapshotScript @('-Repository', $repo, '-TargetRef', 'no-such-branch') 2
Assert-True (-not $missing.complete -and $missing.limitations.Count -gt 0) '无效引用明确未完成'

# ---- 只读性：仓库配置的 textconv 与 clean filter 不得被执行 ----
$probe = Join-Path $runRoot 'probe-repo'
$markers = Join-Path $runRoot 'markers'
[void][IO.Directory]::CreateDirectory($probe)
[void][IO.Directory]::CreateDirectory($markers)
$textconvMarker = (Join-Path $markers 'textconv-ran.txt').Replace('\', '/')
$cleanMarker = (Join-Path $markers 'clean-ran.txt').Replace('\', '/')
$saved = $repo; $repo = $probe
Invoke-FixtureGit @('init', '-q', '-b', 'main') | Out-Null
Write-FixtureFile $probe '.gitattributes' "*.dat diff=probe filter=probe`n"
Invoke-FixtureGit @('config', 'diff.probe.textconv', "sh -c 'echo ran > $textconvMarker; cat `"`$0`"'") | Out-Null
Invoke-FixtureGit @('config', 'filter.probe.clean', "sh -c 'echo ran > $cleanMarker; cat'") | Out-Null
Invoke-FixtureGit @('config', 'filter.probe.smudge', 'cat') | Out-Null
$env:GIT_ATTR_SOURCE = $null
Write-FixtureFile $probe 'a.dat' "one`n"
& git -C $probe -c "core.hooksPath=$hooks" -c 'filter.probe.clean=cat' add .gitattributes a.dat
Invoke-FixtureGit @('-c', 'filter.probe.clean=cat', 'commit', '-q', '-m', 'probe base') '2026-10-17T09:00:00+08:00' | Out-Null
$probeBase = Invoke-FixtureGit @('rev-parse', 'HEAD')
Write-FixtureFile $probe 'a.dat' "two`n"
& git -C $probe -c "core.hooksPath=$hooks" -c 'filter.probe.clean=cat' add a.dat
Invoke-FixtureGit @('-c', 'filter.probe.clean=cat', 'commit', '-q', '-m', 'probe change') '2026-10-17T10:00:00+08:00' | Out-Null
Write-FixtureFile $probe 'a.dat' "three`n"
$repo = $saved
$probeSnap = Invoke-Script $snapshotScript @('-Repository', $probe, '-TargetRef', 'main', '-BaselineSha', $probeBase)
Assert-True ($probeSnap.complete -and $probeSnap.coverage.uniqueCommitCount -eq 1) '探针仓库快照完成'
Assert-True (-not (Test-Path -LiteralPath $textconvMarker)) '快照不执行仓库配置的 textconv'
Assert-True (-not (Test-Path -LiteralPath $cleanMarker)) '快照不执行仓库配置的 clean filter'

# reflog：同一提交被移出后当天重新进入，以最近一次进入时间为准
$probeChange = $probeSnap.cutoffSha
$saved = $repo; $repo = $probe
Invoke-FixtureGit @('update-ref', '-m', 'move back', 'refs/heads/main', $probeBase) '2026-10-17T11:00:00+08:00' | Out-Null
Invoke-FixtureGit @('update-ref', '-m', 're-enter', 'refs/heads/main', $probeChange) '2026-10-17T20:00:00+08:00' | Out-Null
$repo = $saved
$reentered = Invoke-Script $snapshotScript @('-Repository', $probe, '-TargetRef', 'main', '-BaselineSha', $probeBase, '-Since', '2026-10-17T15:00:00+08:00')
$reEvent = @($reentered.events | Where-Object sha -EQ $probeChange)[0]
Assert-True ($reEvent.reflogEnteredAt -like '2026-10-17T20:00:00*' -and $reEvent.enteredInWindow -eq $true) '重新进入主线的提交按最近进入时间判断'

# .git/info/attributes 中的 filter 不受 --attr-source 约束：跳过已跟踪文件的内容比对
Write-FixtureFile $probe '.git/info/attributes' "*.dat filter=probe`n"
$infoAttr = Invoke-Script $snapshotScript @('-Repository', $probe, '-TargetRef', 'main', '-BaselineSha', $probeBase)
Assert-True ($infoAttr.complete -and $infoAttr.dirty.known -eq $false -and $null -eq $infoAttr.dirty.isDirty) 'info/attributes 有 filter 时 dirty 标为未知'
Assert-True (@($infoAttr.limitations | Where-Object { $_ -like 'dirty-unknown*' }).Count -eq 1) 'info/attributes 有 filter 时写入限制'
Assert-True (-not (Test-Path -LiteralPath $cleanMarker)) 'info/attributes 中的 clean filter 未被执行'

# ---- 知识检索夹具 ----
Write-FixtureFile $knowledge '主题/库存.md' @"
# 库存经验

``````markdown
## <稳定ID>：模板标题 库存
状态：<现行>
``````

## K-001：撤销出库需回写库存
关键词：库存, 撤销, 回写
状态：现行
证据：实际验证
最后核验：2026-10-01 订单仓

结论：撤销时按原单数量回写。

## K-002：旧的库存锁方案
关键词：库存锁
状态：已替代
证据：历史助手报告
最后核验：未记录
替代条目：K-001

## K-003：金额四舍五入
关键词：金额
状态：现行
证据：用户确认
最后核验：2026-01-01
有效至：2026-02-01
"@
Write-FixtureFile $knowledge '主题/混合格式.md' @"
~~~~text
``````
## X-999：围栏内的假标题 库存
~~~~

## K-010：半角冒号字段 库存锁
状态: 待核验
最后核验: 2026-10-01
"@
Write-FixtureFile $knowledge 'AI错题本.md' @"
## AI-001：库存扣减必须先写失败测试
- 场景：返工
- 事故：abc — 撤销未回写
- Seen 2× (2026-10-01, 2026-10-05).
"@
$knowledgeBefore = (Get-ChildItem -LiteralPath $knowledge -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object Hash) -join ','

$search = Invoke-Script $searchScript @('-Root', $knowledge, '-Keyword', '库存,撤销')
Assert-True ($search.complete -and $search.results[0].id -eq 'K-001') '多关键词命中排在最前'
Assert-True (@($search.results.id) -notcontains '<稳定ID>') '跳过代码块中的模板标题'
$replaced = @($search.results | Where-Object id -EQ 'K-002')[0]
Assert-True ($replaced.UseAdvice -eq 'history-only') '已替代条目仅作历史'
$lesson = @($search.results | Where-Object id -EQ 'AI-001')[0]
Assert-True ($lesson.status -eq '未标注' -and $lesson.UseAdvice -eq 'verify-first' -and $lesson.seen -eq 2 -and $lesson.scene -eq '返工') '错题本条目读取场景与 Seen，未标注状态需先核验'
$current = @($search.results | Where-Object id -EQ 'K-001')[0]
Assert-True ($current.UseAdvice -eq 'candidate' -and $current.ReviewReasons.Count -eq 0) '现行且有核验的条目为候选'

$mixed = Invoke-Script $searchScript @('-Root', $knowledge, '-Keyword', '库存锁')
Assert-True (@($mixed.results.id) -notcontains 'X-999') '混用 ~~~ 与 ``` 的围栏内标题被跳过'
$halfWidth = @($mixed.results | Where-Object id -EQ 'K-010')[0]
Assert-True ($halfWidth.status -eq '待核验' -and $halfWidth.UseAdvice -eq 'verify-first') '识别半角冒号字段与待核验状态'

$expired = Invoke-Script $searchScript @('-Root', $knowledge, '-Keyword', '金额')
Assert-True ($expired.results.Count -eq 1 -and (@($expired.results[0].ReviewReasons) -join ';') -like '*已过有效期*') '过期条目提示复核'
$none = Invoke-Script $searchScript @('-Root', $knowledge, '-Keyword', '不存在的词')
Assert-True ($none.complete -and $none.matchCount -eq 0) '无命中返回空结果'
Assert-True (((Get-ChildItem -LiteralPath $knowledge -Recurse -File | Get-FileHash -Algorithm SHA256 | ForEach-Object Hash) -join ',') -ceq $knowledgeBefore) '检索不修改知识库文件'
$relative = Invoke-Script $searchScript @('-Root', 'relative', '-Keyword', 'x') 2
Assert-True (-not $relative.complete) '相对路径根目录被拒绝'

[ordered]@{
    passed = $assertions.Count
    assertions = $assertions.ToArray()
    fixtureRoot = $runRoot
    note = '仅在显式 TempRoot 下创建合成 Git 仓库与知识夹具；未访问网络，夹具原位保留。'
} | ConvertTo-Json -Depth 4
