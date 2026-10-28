#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$RepositoryRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

# No filesystem writes, Git mutations, network calls, or original text in output.
$findings = [Collections.Generic.List[object]]::new()
$comparisons = [Collections.Generic.List[object]]::new()
$checked = [Collections.Generic.List[string]]::new()
$notChecked = [Collections.Generic.List[string]]::new()
$maxBytes = 1048576
$gitState = [ordered]@{ head=$null; branch=$null; stagedPaths=@(); workingPaths=@(); pendingCommits=@(); trackingRef='refs/remotes/origin/main'; trackingSha=$null; status='not-checked'; workingMode='stat-only-may-overreport'; indexHash=$null }

function Add-Finding([string]$Kind,[string]$Path,[string]$Scope,[int]$Line=0,[string]$Sha='') {
    $findings.Add([ordered]@{kind=$Kind; path=$Path; scope=$Scope; line=$Line; sha=$Sha})
}
function Resolve-Root([string]$Value) {
    if (-not [IO.Path]::IsPathFullyQualified($Value)) { throw 'absolute-root-required' }
    $full = [IO.Path]::GetFullPath($Value).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if($full -match '^[A-Za-z]:$' -or -not $full) { throw 'volume-root-not-supported' }
    $item = Get-Item -LiteralPath $full -Force
    if (-not $item.PSIsContainer) { throw 'directory-required' }
    $cursor = $item
    while ($null -ne $cursor) {
        if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'linked-root' }
        $cursor = $cursor.Parent
    }
    return $full
}
function Get-SafePath([string]$Root,[string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw 'outside-root' }
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    $prefix = $Root + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'outside-root' }
    $parts = [IO.Path]::GetRelativePath($Root,$full) -split '[/\\]'
    $cursor = $Root
    foreach ($part in $parts) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'linked-path' }
        }
    }
    return $full
}
function Read-SafeText([string]$Root,[string]$Relative) {
    $p = Get-SafePath $Root $Relative
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return $null }
    if ((Get-Item -LiteralPath $p).Length -gt $maxBytes) { throw 'file-too-large' }
    return [IO.File]::ReadAllText($p,[Text.UTF8Encoding]::new($false,$true))
}
function Get-TextHash([string]$Value) {
    # Ignore line-ending differences only; don't rewrite or infer semantic equivalence.
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value.Replace("`r`n","`n"))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}
function Invoke-GitRead([string[]]$GitArgs,[int[]]$AllowedCodes=@(0)) {
    $psi = [Diagnostics.ProcessStartInfo]::new('git')
    $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    $psi.StandardOutputEncoding=[Text.Encoding]::UTF8; $psi.StandardErrorEncoding=[Text.Encoding]::UTF8
    foreach($key in @($psi.Environment.Keys | Where-Object { $_ -like 'GIT_*' })) { [void]$psi.Environment.Remove($key) }
    $psi.Environment['GIT_CONFIG_NOSYSTEM']='1'
    $psi.Environment['GIT_CONFIG_GLOBAL']=if($IsWindows){'NUL'}else{'/dev/null'}
    $psi.Environment['GIT_ATTR_NOSYSTEM']='1'
    $psi.Environment['GIT_NO_REPLACE_OBJECTS']='1'
    $psi.Environment['GIT_OPTIONAL_LOCKS']='0'
    $psi.Environment['GIT_TERMINAL_PROMPT']='0'
    $psi.Environment['GIT_NO_LAZY_FETCH']='1'
    $emptyConfig=if($IsWindows){'NUL'}else{'/dev/null'}
    foreach ($arg in @('--no-optional-locks','-c',"safe.directory=$repo",'-c',"core.excludesFile=$emptyConfig",'-c','core.fsmonitor=false','-c','core.untrackedCache=false','-c','core.quotePath=false','-C',$repo)+$GitArgs) { $psi.ArgumentList.Add($arg) }
    $p=[Diagnostics.Process]::new(); $p.StartInfo=$psi
    try {
        [void]$p.Start()
        $stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
        if (-not $p.WaitForExit(15000)) { $p.Kill(); throw 'git-timeout' }
        $result=$stdout.GetAwaiter().GetResult(); $err=$stderr.GetAwaiter().GetResult()
        if ($p.ExitCode -notin $AllowedCodes) { throw 'git-read-failed' }
        if ($err) { Add-Finding 'git-diagnostic' 'git' 'repository' }
        return [pscustomobject]@{code=$p.ExitCode; text=$result}
    } finally { $p.Dispose() }
}
function Scan-Text([string]$Text,[string]$Path,[string]$Scope,[string]$Sha='') {
    $patterns = @{
        'sensitive-pattern'='(?i)(?:password|passwd|pwd|token|api[_-]?key|secret)\s*[:=]\s*["''][^"''\r\n]{6,}["'']|(?:ghp_|github_pat_|sk-proj-)[A-Za-z0-9_-]{8,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|https?://[^\s/@]+:[^\s/@]+@'
        'local-path'='(?i)(?<![\w<])[A-Z]:[/\\][^\s`"<>]+|\\\\[A-Za-z0-9_.-]+\\|/(?:Users|home)/[^\s/]+/'
        'private-address'='(?<!\d)(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})(?!\d)'
    }
    $number=0
    foreach ($line in ($Text -split "`n")) {
        $number++
        foreach ($kind in $patterns.Keys) { if ($line -match $patterns[$kind]) { Add-Finding $kind $Path $Scope $number $Sha } }
    }
}
function Get-HeadingIds([string]$Text) {
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seen=@{}; $fenced=$false
    foreach($line in ($Text -split "`n")) {
        if($line -match '^\s*(```|~~~)') { $fenced=-not $fenced; continue }
        if (-not $fenced -and $line -match '^#{1,6}\s+(.+?)\s*#*\s*$') {
            $id=$Matches[1].ToLowerInvariant() -replace '[^\p{L}\p{N}\p{M}_\-\s]','' -replace '\s','-'
            $base=$id
            if($seen.ContainsKey($base)){ $seen[$base]++; $id="$base-$($seen[$base])" }else{ $seen[$base]=0 }
            [void]$ids.Add($id)
        }
    }
    return ,$ids
}
function Check-Links([string]$Text,[string]$Path) {
    if(-not $Path.EndsWith('.md')) { return }
    foreach($m in [regex]::Matches($Text,'\]\(([^\s)]+)\)')) {
        $href=$m.Groups[1].Value
        if($href -match '^(?:https?://|mailto:)' -or $href -match '[<>]') { continue }
        $line=1+([regex]::Matches($Text.Substring(0,$m.Index),"`n")).Count
        $parts=$href -split '#',2
        try {
            $directory=[IO.Path]::GetDirectoryName($Path)
            if(-not $directory) { $directory='.' }
            $rel=if($parts[0]) { [IO.Path]::GetRelativePath($repo,(Get-SafePath $repo (Join-Path $directory ([Uri]::UnescapeDataString($parts[0]))))).Replace('\','/') } else { $Path }
            if(-not (Test-Path -LiteralPath (Get-SafePath $repo $rel) -PathType Leaf)) { Add-Finding 'broken-link' $Path 'target' $line; continue }
            # Do not inspect project files through links, even when they exist.
            if($rel -notin $publishPaths) { Add-Finding 'link-outside-scope' $Path 'target' $line; continue }
            $linked=Read-SafeText $repo $rel
            if($null -eq $linked) { Add-Finding 'broken-link' $Path 'target' $line; continue }
            if($parts.Count -eq 2 -and $parts[1]) {
                $anchor=[Uri]::UnescapeDataString($parts[1])
                if(-not (Get-HeadingIds $linked).Contains($anchor)) { Add-Finding 'broken-anchor' $Path 'target' $line }
            }
        } catch { Add-Finding 'excluded-link' $Path 'target' $line }
    }
}
function Scan-GitVersion([string]$Spec,[string]$Path,[string]$Scope,[string]$Sha='') {
    if($Path -notin $publishPaths) { Add-Finding 'outside-allowlist' $Path $Scope 0 $Sha; return }
    # An absent tree/index entry is a deletion; a missing blob is not.
    $entry=if($Scope -eq 'staged') { (Invoke-GitRead @('ls-files','--stage','-z','--',$Path)).text } else { (Invoke-GitRead @('ls-tree','-z',$Sha,'--',$Path)).text }
    if(-not $entry) { return }
    if($entry -match '^(120000|160000) ') { Add-Finding 'excluded-link' $Path $Scope 0 $Sha; return }
    $size=Invoke-GitRead @('cat-file','-s',$Spec) @(0,128)
    if($size.code -ne 0) { Add-Finding 'content-unchecked' $Path $Scope 0 $Sha; throw 'blob-unavailable' }
    if([long]$size.text.Trim() -gt $maxBytes) { Add-Finding 'file-too-large' $Path $Scope 0 $Sha; return }
    $blob=Invoke-GitRead @('cat-file','blob',$Spec) @(0,128)
    if($blob.code -ne 0) { Add-Finding 'content-unchecked' $Path $Scope 0 $Sha; throw 'blob-unavailable' }
    Scan-Text $blob.text $Path $Scope $Sha
}

$code=0; $allowlist=@()
try {
    $source=Resolve-Root $SourceRoot; $repo=Resolve-Root $RepositoryRoot
    if($source -eq $repo) { throw 'source-and-target-must-differ' }
    $policy=Read-SafeText $repo '.ai/规则/规则同步.md'
    $block=[regex]::Match($policy,'(?s)<!-- source-allowlist:start -->(.*?)<!-- source-allowlist:end -->')
    if(-not $block.Success) { throw 'missing-allowlist' }
    $allowlist=@([regex]::Matches($block.Groups[1].Value,'(?m)^\|\s*`([^`]+)`\s*\|') | ForEach-Object { $_.Groups[1].Value })
    if($allowlist.Count -eq 0 -or @($allowlist | Sort-Object -Unique).Count -ne $allowlist.Count) { throw 'invalid-allowlist' }
    foreach($path in $allowlist) {
        if($path -ne 'AGENTS.md' -and $path -notmatch '^\.ai/规则/[^./\\:]+\.md$') { throw 'invalid-source-path' }
    }
    $publishPaths=@($allowlist)+@('README.md','CLAUDE.md','.gitignore','.ai/规则/规则同步.md','.ai/规则/验证工具.md','.ai/记忆.md','.ai/工作区导航.md','.ai/工具接入.md','.ai/子仓库接入检查.md','.ai/知识库/索引.md','.ai/知识库/AI错题本.md','.ai/模板/子仓库/AGENTS.md','.ai/模板/子仓库/CLAUDE.md','scripts/Test-RuleSync.ps1','scripts/Get-ReviewSnapshot.ps1','scripts/Search-Knowledge.ps1','scripts/Test-Lessons.ps1','scripts/Guard-Command.ps1','scripts/Invoke-Verification.ps1','scripts/Test-ExecutionEnvironment.ps1','tests/Test-RulePreflight.ps1','tests/Test-WorkflowScripts.ps1','tests/Test-Lessons.ps1','tests/ReviewBehavior.md','tests/Test-GuardCommand.ps1','tests/Test-VerificationTools.ps1')
    $publishPaths=@($publishPaths | Sort-Object -Unique)
    foreach($path in $allowlist) {
        $left=$null; $right=$null; $state='excluded-link'
        try {
            $left=Read-SafeText $source $path; $right=Read-SafeText $repo $path
            $state=if($null -eq $left -and $null -eq $right){'both-missing'}elseif($null -eq $left){'source-missing'}elseif($null -eq $right){'target-missing'}elseif((Get-TextHash $left) -eq (Get-TextHash $right)){'identical'}else{'different'}
            if($null -ne $left) { Scan-Text $left $path 'source' }
        } catch { Add-Finding 'excluded-link' $path 'source' }
        $comparisons.Add([ordered]@{path=$path; state=$state; sourceHash=if($null -ne $left){Get-TextHash $left}else{$null}; targetHash=if($null -ne $right){Get-TextHash $right}else{$null}})
    }
    foreach($path in $publishPaths) {
        try {
            $content=Read-SafeText $repo $path
            if($null -ne $content) { Scan-Text $content $path 'target'; Check-Links $content $path }
        } catch { Add-Finding 'excluded-link' $path 'target' }
    }
    $checked.Add('白名单文件现状、有限敏感格式、允许目标文档的本地链接与ATX标题锚点')
    [void](Get-SafePath $repo '.git')
    # Require an ordinary checkout. Linked worktrees may point outside RepositoryRoot.
    if(-not (Test-Path -LiteralPath (Join-Path $repo '.git') -PathType Container)) { throw 'ordinary-checkout-required' }
    # Reject redirection before launching Git, without printing local config values.
    foreach($metadata in @('.git/config','.git/index','.git/HEAD','.git/packed-refs','.git/refs','.git/refs/heads','.git/refs/heads/main','.git/refs/remotes','.git/refs/remotes/origin','.git/refs/remotes/origin/main','.git/info/exclude','.git/objects','.git/objects/info','.git/objects/pack')) { [void](Get-SafePath $repo $metadata) }
    $config=Read-SafeText $repo '.git/config'
    if($config -match '(?im)^\s*\[\s*(?:include(?:If)?|filter)\b|^\s*(?:worktree|attributesFile|worktreeConfig|excludesFile)\s*=') { throw 'unsupported-git-config' }
    foreach($externalStore in @('.git/commondir','.git/objects/info/alternates','.git/objects/info/http-alternates','.git/info/grafts','.git/shallow')) {
        if(Test-Path -LiteralPath (Get-SafePath $repo $externalStore)) { throw 'unsupported-git-history-store' }
    }
    $headText=(Read-SafeText $repo '.git/HEAD').Trim()
    $refPaths=@('.git/refs/remotes/origin/main')
    if($headText -match '^ref: (refs/heads/[^\s]+)$') {
        $headRef=$Matches[1]
        if($headRef -match '\.\.|[\\:]|//|/$|/\.(?:/|$)') { throw 'invalid-head-ref' }
        $refPaths+='.git/'+$headRef
    }
    elseif($headText -notmatch '^[0-9a-fA-F]{40}(?:[0-9a-fA-F]{24})?$') { throw 'unsupported-head' }
    foreach($refPath in $refPaths) {
        $refText=Read-SafeText $repo $refPath
        if($null -ne $refText -and $refText.Trim() -notmatch '^[0-9a-fA-F]{40}(?:[0-9a-fA-F]{24})?$') { throw 'unsupported-symbolic-ref-chain' }
    }
    $top=(Invoke-GitRead @('rev-parse','--show-toplevel')).text.Trim()
    if([IO.Path]::GetFullPath($top).TrimEnd([IO.Path]::DirectorySeparatorChar) -ne $repo) { throw 'wrong-git-root' }
    $gitState.head=(Invoke-GitRead @('rev-parse','HEAD')).text.Trim()
    $gitState.branch=(Invoke-GitRead @('symbolic-ref','--quiet','--short','HEAD') @(0,1)).text.Trim()
    $index=Get-SafePath $repo '.git/index'
    if(Test-Path -LiteralPath $index){$gitState.indexHash=(Get-FileHash -LiteralPath $index -Algorithm SHA256).Hash}
    $staged=(Invoke-GitRead @('diff','--cached','--name-only','-z','--no-renames','--no-ext-diff','--no-textconv',$gitState.head,'--')).text
    # ls-files --modified uses stat metadata; do not hash/read non-allowlisted worktree contents.
    $modified=(Invoke-GitRead @('ls-files','--modified','--deleted','-z')).text
    $untracked=(Invoke-GitRead @('ls-files','--others','--exclude-standard','-z')).text
    $gitState.stagedPaths=@($staged -split "`0" | Where-Object { $_ } | Sort-Object -Unique)
    $gitState.workingPaths=@(($staged+$modified+$untracked) -split "`0" | Where-Object { $_ } | Sort-Object -Unique)
    foreach($path in $gitState.workingPaths) { if($path -notin $publishPaths) { Add-Finding 'outside-allowlist' $path 'target' } }
    foreach($path in $gitState.stagedPaths) { Scan-GitVersion ":$path" $path 'staged' }
    $ref=Invoke-GitRead @('rev-parse','--verify','--quiet',$gitState.trackingRef) @(0,1)
    if($ref.code -eq 0) {
        $gitState.status='local-tracking-only'
        $gitState.trackingSha=$ref.text.Trim()
        $pending=[Collections.Generic.List[object]]::new()
        $commits=(Invoke-GitRead @('rev-list',"$($gitState.trackingSha)..$($gitState.head)")).text -split '\r?\n' | Where-Object { $_ }
        foreach($sha in $commits) {
            # -m covers merge parents; check every commit, not only the net final diff.
            $changed=(Invoke-GitRead @('diff-tree','--root','-m','--no-commit-id','--name-only','-r','-z','--no-renames',$sha)).text
            $paths=@($changed -split "`0" | Where-Object { $_ } | Sort-Object -Unique)
            foreach($path in $paths) { Scan-GitVersion "${sha}:$path" $path 'commit' $sha }
            $pending.Add([ordered]@{sha=$sha; paths=$paths})
        }
        $gitState.pendingCommits=$pending.ToArray()
    } else { $gitState.status='tracking-missing'; Add-Finding 'tracking-missing' 'origin/main' 'repository' }
    if((Invoke-GitRead @('rev-parse','HEAD')).text.Trim() -ne $gitState.head) { throw 'head-changed' }
    $currentRef=(Invoke-GitRead @('rev-parse','--verify','--quiet',$gitState.trackingRef) @(0,1)).text.Trim()
    if($currentRef -ne [string]$gitState.trackingSha) { throw 'tracking-changed' }
    $currentIndex=if(Test-Path -LiteralPath $index){(Get-FileHash -LiteralPath $index -Algorithm SHA256).Hash}else{$null}
    if($currentIndex -ne $gitState.indexHash) { throw 'index-changed' }
    $checked.Add('Git工作区、暂存区、相对本地origin/main的每个待推提交路径和允许文件内容')
} catch {
    $code=2
    # Never echo native errors or source text: they can contain credentials/paths.
    Add-Finding 'check-incomplete' 'preflight' 'tool'
}
$notChecked.Add('未联网、未fetch；本地origin/main可能过时，不代表远端最新状态')
$notChecked.Add('无上次同步对应基线；different仅表示两侧不同，不推定本地新增或覆盖方向')
$notChecked.Add('不读取非白名单项目内容；不判断全部客户名称、语义隐私、业务正确性或是否可发布')
$notChecked.Add('链接仅检查允许目标的行内相对链接和ATX标题；不联网、不完整模拟Markdown渲染器')
$notChecked.Add('工作区变化由stat元数据判断，可能包含仅时间戳改变的文件；读取不是原子快照，仍需写入前核对指纹。拒绝外部worktree、include/filter配置、浅历史和对象库重定向')
[ordered]@{
    schemaVersion=1; readOnly=$true; status=if($code -eq 0){'completed'}else{'incomplete'}
    allowlist=$allowlist; comparisons=$comparisons.ToArray(); findings=$findings.ToArray()
    git=$gitState; checked=$checked.ToArray(); notChecked=$notChecked.ToArray()
} | ConvertTo-Json -Depth 12
exit $code
