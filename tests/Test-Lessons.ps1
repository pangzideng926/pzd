#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TempRoot,
    [string]$ScriptPath = (Join-Path $PSScriptRoot '../scripts/Test-Lessons.ps1')
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# 只在显式 TempRoot 下创建合成夹具；不读取真实知识库、不联网、不删除夹具。
if (-not [IO.Path]::IsPathFullyQualified($TempRoot)) { throw 'TempRoot 必须为绝对路径。' }
$ScriptPath = [IO.Path]::GetFullPath($ScriptPath)
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw '待测错题检查脚本不存在。' }
$runRoot = Join-Path $TempRoot ('lessons-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $runRoot 'source'
$outside = Join-Path $runRoot 'outside'
foreach ($directory in $source, $outside) { [void][IO.Directory]::CreateDirectory($directory) }
$assertions = [Collections.Generic.List[string]]::new()
$runs = [Collections.Generic.List[object]]::new()
$skipped = [Collections.Generic.List[string]]::new()

function Assert-True([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "断言失败：$Name。合成夹具保留于 $runRoot" }
    $assertions.Add($Name)
}
function Write-Fixture([string]$Relative, [string]$Text) {
    $path = Join-Path $source $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path $path -Parent))
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
}
function Get-TreeFingerprint {
    $rows = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($runRoot)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            $relative = [IO.Path]::GetRelativePath($runRoot, $item.FullName)
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                $rows.Add("LINK`t$relative`t$($item.LinkTarget)")
            } elseif ($item.PSIsContainer) {
                $rows.Add("DIR`t$relative")
                $pending.Push($item.FullName)
            } else {
                $rows.Add("FILE`t$relative`t$((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash)")
            }
        }
    }
    return (($rows | Sort-Object) -join "`n")
}
function Invoke-Check([string]$Name, [string[]]$Files, [int]$ExpectedExit = 0, [string]$Root = $source, [string]$LockFile = '') {
    $before = Get-TreeFingerprint
    $payload = @{ script = $ScriptPath; parameters = @{ Root = $Root; File = $Files } } | ConvertTo-Json -Compress
    $base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
    # JSON 作为数据解码，参数不拼入可执行语句；支持 -File 的真实数组和包含空格的路径。
    $command = "`$a = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$base64')) | ConvertFrom-Json -AsHashtable; `$parameters = `$a.parameters; & `$a.script @parameters; exit `$LASTEXITCODE"
    $psi = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command)))) { $psi.ArgumentList.Add($argument) }
    $heldFile = if ($LockFile) { [IO.File]::Open($LockFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None) } else { $null }
    $process = [Diagnostics.Process]::Start($psi)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw '合成检查超时。' }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($null -ne $heldFile) { $heldFile.Dispose(); $heldFile = $null }
        Assert-True ($process.ExitCode -eq $ExpectedExit) "$Name：退出码 $ExpectedExit"
        try { $report = $stdout | ConvertFrom-Json -Depth 20 } catch { throw "$Name：输出不是单一 JSON，原文不回显。" }
        Assert-True ($report.readOnly -eq $true) "$Name：只读声明"
        Assert-True ([string]::IsNullOrWhiteSpace($stderr)) "$Name：没有未脱敏错误输出"
        Assert-True ((Get-TreeFingerprint) -ceq $before) "$Name：源与边界外合成树指纹不变"
        Assert-True ($stdout -notmatch 'SYNTHETIC_PRIVATE_BODY|SYNTHETIC_PRIVATE_INCIDENT') "$Name：正文及事故 ID 不回显"
        $runs.Add([ordered]@{ name = $Name; exitCode = $process.ExitCode; findings = @($report.findings).Count; notChecked = @($report.notChecked).Count })
        return $report
    } finally {
        $process.Dispose()
        if ($null -ne $heldFile) { $heldFile.Dispose() }
    }
}

Write-Fixture 'book.md' @'
# 合成错题本
````markdown
## AI-900：围栏模板
- Seen 999×.
~~~
```
````
## AI-001：有完整证据的教训
- 证据：实际验证 [验证](notes/proof.md#通过)
- 事故 ID：SYNTHETIC_PRIVATE_INCIDENT
- 事故 ID：SYNTHETIC_PRIVATE_INCIDENT
- 事故 ID 完整：是
- Seen 1× (2026-10-20).
- Done right：SYNTHETIC_PRIVATE_BODY
## AI-002：旧格式
- 事故：历史事故 [旧证据](notes/proof.md)
- Seen 3× (2026-10-18, 2026-10-20).
## AI-003：缺少证据
- 证据：<待补充>
- Seen 1×.
## AI-004：账面计次不一致
- 证据：实际验证 [验证](notes/proof.md)
- 事故 ID：case-1
- 事故 ID：case-2
- 事故 ID 完整：是
- Seen 3×.
## AI-005：只有证据标签
- 证据：实际验证
- 事故 ID：case-3
- 事故 ID 完整：是
- Seen 1×.
'@
Write-Fixture 'notes/proof.md' "# 验证`n## 通过`n正文不应出现在输出中。`n## 通过`n"
Write-Fixture 'unselected.md' "## AI-001：不应扫描的重复编号`n- 证据：未记录`n"
$main = Invoke-Check '显式数组与旧条目兼容' @('book.md', 'notes/proof.md')
Assert-True ($main.complete -and @($main.entries).Count -eq 5) '只解析显式文件中围栏外的 AI 条目'
Assert-True (@($main.findings | Where-Object kind -EQ 'duplicate-id').Count -eq 0) '不扫描未选定文件'
$entry = @($main.entries | Where-Object id -EQ 'AI-001')[0]
Assert-True ($entry.seen.status -eq 'matched' -and $entry.seen.expected -eq 1) '完整事故 ID 去重后核对 Seen'
Assert-True (@($main.entries | Where-Object id -EQ 'AI-002')[0].seen.status -eq 'unknown') '旧条目缺完整事故账本时 Seen 未知'
Assert-True (@($main.findings | Where-Object { $_.kind -eq 'missing-evidence' -and $_.id -eq 'AI-003' }).Count -eq 1) '占位证据不视为真实材料'
Assert-True (@($main.findings | Where-Object { $_.kind -eq 'missing-evidence' -and $_.id -eq 'AI-002' }).Count -eq 0) '旧格式可追溯链接不误报缺证据'
Assert-True (@($main.findings | Where-Object { $_.kind -eq 'seen-mismatch' -and $_.id -eq 'AI-004' }).Count -eq 1) '完整账面与 Seen 不一致报告问题'
Assert-True (@($main.entries | Where-Object id -EQ 'AI-005')[0].evidenceStatus -eq 'unknown') '仅标签不能声称证据已验证'
Assert-True (@($main.notChecked | Where-Object kind -EQ 'anchor-not-selected').Count -eq 0) '显式选择锚点目标时实际核验'

Write-Fixture 'legacy.md' @'
## AI-030：旧事故用完整提交定位
- 事故：fixture-repository commit 0123456789abcdef0123456789abcdef01234567 — 合成事故
- Seen 1×.
## AI-031：旧事故用任务与事件定位
- 事故：fixture-project / task-001 / event-02 — 合成事件
- Seen 1×.
## AI-032：旧验证字段
- 验证：2026-10-20 合成检查记录，退出码 0，未核验真实性。
- Seen 1×.
## AI-033：旧来源字段
- 来源：fixture-session，2026-10-20T12:00:00+08:00，消息 4。
- Seen 1×.
## AI-034：旧格式空占位
- 事故：未记录
- 来源：<待补充>
- 验证：未知
- Seen 1×.
'@
$legacy = Invoke-Check '旧事故来源验证字段兼容' @('legacy.md')
foreach ($id in @('AI-030', 'AI-031', 'AI-032', 'AI-033')) {
    Assert-True (@($legacy.entries | Where-Object id -EQ $id)[0].evidenceStatus -eq 'unknown') "$id：旧字段是待核验线索，不能误报缺失或已验证"
}
Assert-True (@($legacy.entries | Where-Object id -EQ 'AI-034')[0].evidenceStatus -eq 'missing') '旧字段全为占位仍缺证据'
Assert-True (@($legacy.findings | Where-Object kind -EQ 'missing-evidence').Count -eq 1) '仅缺少实质材料的旧条目报告缺证据'

Write-Fixture 'markdown.md' @'
# 合成 Markdown 边界
<!--
## AI-040：被注释隐藏的重复条目
- 证据：未知
~~~
-->
## AI-040：可见条目 <!-- 注释中的文字 -->
- 证据：实际验证 <!-- 隐藏内容 --> [显式锚点](#stable-proof)
<a id="stable-proof"></a>
- 来源：[缺文件][missing] 和 [折叠引用][]
[missing]: missing.md
[折叠引用]: unknown.md
<!-- 整行注释 -->可见同一行 [可见链接](notes/proof.md)
说明：`<!--` 是行内代码，不开启注释。
~~~
<!--
## AI-999：围栏内注释不影响后续状态
~~~
## AI-041：围栏后可见条目
- 证据：实际验证 [命名锚点](#named-proof)
<a name='named-proof'></a>
'@
$markdown = Invoke-Check 'Markdown 注释引用和显式锚点' @('markdown.md')
Assert-True (@($markdown.entries).Count -eq 2 -and @($markdown.findings | Where-Object kind -EQ 'duplicate-id').Count -eq 0) 'HTML 注释条目不参与编号检查，围栏注释不污染可见状态'
Assert-True (@($markdown.entries | Where-Object id -EQ 'AI-040')[0].line -eq 7) '忽略注释后保留原始行号与同一行可见正文'
Assert-True (@($markdown.findings | Where-Object kind -EQ 'broken-anchor').Count -eq 0) '简单显式 HTML id 与命名锚点不误报坏锚点'
Assert-True (@($markdown.notChecked | Where-Object kind -EQ 'unsupported-reference-link').Count -gt 0) '引用式链接明确列为未检查'

Write-Fixture 'unsupported-id.md' @'
## AI-T001：历史编号保留
- 证据：未知
- 事故 ID：case-1
- 事故 ID 完整：是
- Seen 9×.
## AI-SYNTHETIC_PRIVATE_BODY：任意编号文字不能回显
- Seen 4×.
<!--
## AI-T002：注释内的编号不报告
-->
~~~
## AI-T003：围栏内的编号不报告
~~~
## 普通标题：不扩成条目检查
## AI-050：已支持的编号
- 证据：实际验证 [验证](notes/proof.md)
- Seen 1×.
'@
$unsupportedId = Invoke-Check '未知 AI 编号明确报告' @('unsupported-id.md')
$unsupportedNotices = @($unsupportedId.notChecked | Where-Object kind -EQ 'unsupported-entry-id')
Assert-True ($unsupportedNotices.Count -eq 2 -and @($unsupportedNotices | Where-Object id -NE '').Count -eq 0) '未支持编号只输出文件行号，不回显原编号'
Assert-True (@($unsupportedId.entries).Count -eq 1 -and $unsupportedId.entries[0].id -eq 'AI-050') '未知编号不解析 Seen，普通标题与隐藏示例不受影响'

$duplicate = Invoke-Check '所选文件之间重复编号' @('book.md', 'unselected.md')
Assert-True (@($duplicate.findings | Where-Object kind -EQ 'duplicate-id').Count -eq 2) '重复编号回指两个选中条目'
$deduplicated = Invoke-Check '同文件重复入参' @('book.md', 'book.md')
Assert-True (@($deduplicated.entries).Count -eq 5) '重复入参不制造重复事故与编号'

Write-Fixture 'links.md' @'
## AI-010：链接边界
- 证据：实际验证 [存在](notes/proof.md#通过)
- 来源：[坏锚点](notes/proof.md#不存在) [重复标题](notes/proof.md#通过-1)
- 来源：[缺文件](missing.md) [外部文件](../outside/evidence.md#private)
- 来源：[互联网](https://example.invalid/proof#value) [未知业务锚点](business.md#私有)
- 来源：[空格](<notes/with space.md#标题>)
'@
Write-Fixture 'notes/with space.md' "# 标题`n"
Write-Fixture 'business.md' "# 私有`nSYNTHETIC_PRIVATE_BODY`n"
[IO.File]::WriteAllText((Join-Path $outside 'evidence.md'), "# private`nSYNTHETIC_PRIVATE_BODY`n", [Text.UTF8Encoding]::new($false))
$links = Invoke-Check '引用与锚点边界' @('links.md', 'notes/proof.md', 'notes/with space.md')
Assert-True (@($links.findings | Where-Object kind -EQ 'broken-link').Count -eq 1) '报告确实不存在的本地引用'
Assert-True (@($links.findings | Where-Object kind -EQ 'broken-anchor').Count -eq 1) '报告入选 Markdown 中的坏锚点并支持重复标题'
Assert-True (@($links.notChecked | Where-Object kind -EQ 'link-outside-root').Count -eq 1) '越界引用不访问'
Assert-True (@($links.notChecked | Where-Object kind -EQ 'anchor-not-selected').Count -eq 1) '未选业务文件只检查存在性'
Assert-True (@($links.notChecked | Where-Object kind -EQ 'external-link').Count -eq 1) '远程链接不联网'
$locked = Invoke-Check '未选业务正文独占锁探针' @('links.md', 'notes/proof.md', 'notes/with space.md') 0 $source (Join-Path $source 'business.md')
Assert-True (@($locked.notChecked | Where-Object kind -EQ 'anchor-not-selected').Count -eq 1) '未选业务正文无法打开时仍只查路径并标记锚点未知'

Write-Fixture 'unknown.md' @'
## AI-020：完整声明没有事故
- 证据：未知
- 事故 ID 完整：是
- Seen 1×.
## AI-021：含占位事故不能核对
- 证据：未记录
- 事故 ID：<待补充>
- 事故 ID 完整：是
- Seen 1×.
## AI-022：Seen 自相矛盾
- 证据：实际验证
- 事故 ID：case-1
- 事故 ID 完整：是
- Seen 1×.
- Seen 2×.
'@
$unknown = Invoke-Check '不完整与矛盾计数' @('unknown.md')
Assert-True (@($unknown.entries | Where-Object { $_.seen.status -ne 'unknown' }).Count -eq 0) '缺事故、占位事故和矛盾 Seen 均不推算'
Write-Fixture 'blank.md' @'
# 空白模板
~~~markdown
## AI-001：模板示例
- 证据：<来源>
~~~
'@
$blank = Invoke-Check '空白围栏模板' @('blank.md')
Assert-True ($blank.complete -and @($blank.entries).Count -eq 0 -and @($blank.findings).Count -eq 0) '空白模板无需制造问题条目'

foreach ($bad in @('../outside/evidence.md', (Join-Path $outside 'evidence.md'), 'book.md:stream', '\\server\share\book.md')) {
    $invalid = Invoke-Check '拒绝危险选择路径' @($bad) 2
    Assert-True (-not $invalid.complete -and @($invalid.entries).Count -eq 0) '危险选择不读取正文'
}
$missing = Invoke-Check '缺少选定文件' @('absent.md') 2
Assert-True (-not $missing.complete) '选定文件缺失使检查未完成'
$relativeRoot = Invoke-Check '拒绝相对根目录' @('book.md') 2 'relative'
Assert-True (-not $relativeRoot.complete) '根目录必须绝对'
Write-Fixture 'large.md' ('a' * 1048577)
$large = Invoke-Check '拒绝超大文件' @('large.md') 2
Assert-True (-not $large.complete -and @($large.notChecked | Where-Object kind -EQ 'file-too-large').Count -eq 1) '超 1 MiB 文件不读'
Write-Fixture 'limit.md' ('a' * 1048576)
$limit = Invoke-Check '恰好 1 MiB' @('limit.md')
Assert-True ($limit.complete) '恰好 1 MiB 仍可完成'
[IO.File]::WriteAllBytes((Join-Path $source 'invalid-utf8.md'), [byte[]]@(0xC3, 0x28))
$encoding = Invoke-Check '非法 UTF8' @('invalid-utf8.md') 2
Assert-True (-not $encoding.complete) '非法编码报告未完成且不输出异常正文'

# 已审查：以下链接只连接本次夹具内部。仅创建，不修改目标、不删除。
$hardLink = Join-Path $source 'hard.md'
try { New-Item -ItemType HardLink -Path $hardLink -Target (Join-Path $source 'book.md') | Out-Null } catch { $skipped.Add('hard-link：环境不支持创建合成硬链接') }
if (Test-Path -LiteralPath $hardLink) {
    $hard = Invoke-Check '拒绝硬链接文件' @('hard.md') 2
    Assert-True (-not $hard.complete) '拒绝硬链接读取'
}
$junction = Join-Path $source 'linked'
try { New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null } catch { $skipped.Add('junction：环境不支持创建合成目录连接') }
if (Test-Path -LiteralPath $junction) {
    $linked = Invoke-Check '拒绝目录连接' @('linked/evidence.md') 2
    Assert-True (-not $linked.complete) '不通过目录连接逃离根目录'
    $linkedRoot = Invoke-Check '拒绝连接根目录' @('evidence.md') 2 $junction
    Assert-True (-not $linkedRoot.complete) '连接路径不能作为根目录'
}
$symbolic = Join-Path $source 'symbolic.md'
try { New-Item -ItemType SymbolicLink -Path $symbolic -Target (Join-Path $outside 'evidence.md') | Out-Null } catch { $skipped.Add('symbolic-link：环境不支持创建合成符号链接') }
if (Test-Path -LiteralPath $symbolic) {
    $symbol = Invoke-Check '拒绝符号链接' @('symbolic.md') 2
    Assert-True (-not $symbol.complete) '不跟随符号链接文件'
}

[ordered]@{
    passed = $assertions.Count
    assertions = $assertions.ToArray()
    runs = $runs.ToArray()
    skipped = $skipped.ToArray()
    fixtureRoot = $runRoot
    scriptSHA256 = (Get-FileHash -LiteralPath $ScriptPath -Algorithm SHA256).Hash
    note = '合成输入验证脚本契约与只读边界；不证明历史证据真实性或 AI 实际遵规效果。'
} | ConvertTo-Json -Depth 8
