[CmdletBinding()]
param(
    [string] $ScriptPath = (Join-Path $PSScriptRoot '../scripts/Guard-Command.ps1')
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# 只向待测脚本的标准输入传入合成载荷；不执行载荷中的任何命令、不写文件、不联网。
$ScriptPath = [IO.Path]::GetFullPath($ScriptPath)
if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw '待测钩子脚本不存在。' }
# 夹具根目录由运行时盘符推出；用例中的 {ROOT}/{SLASHROOT}/{BASHROOT} 在调用前替换，载荷不含本机固定路径。
$drive = [IO.Path]::GetPathRoot($ScriptPath).Substring(0, 1)
$root = "${drive}:\FixtureWorkspace"
$sub = "$root\repo-a"
$placeholders = @{ '{ROOT}' = $root; '{SLASHROOT}' = $root.Replace('\', '/'); '{BASHROOT}' = '/' + $drive.ToLowerInvariant() + '/FixtureWorkspace' }
$assertions = [Collections.Generic.List[string]]::new()

function Invoke-Guard([string] $Tool, [string] $Command, [string] $Cwd = $sub, [switch] $NoRoot) {
    foreach ($key in $placeholders.Keys) { $Command = $Command.Replace($key, $placeholders[$key]) }
    $payload = [ordered]@{ session_id = 'fixture'; hook_event_name = 'PreToolUse'; tool_name = $Tool; tool_input = @{ command = $Command }; cwd = $Cwd } | ConvertTo-Json -Compress -Depth 4
    $PSNativeCommandUseErrorActionPreference = $false
    $rootArgs = if ($NoRoot) { @() } else { @('-WorkspaceRoot', $root) }
    $output = $payload | pwsh -NoProfile -NonInteractive -File $ScriptPath @rootArgs 2>&1
    return [pscustomobject]@{ exit = $LASTEXITCODE; text = ($output -join "`n") }
}

function Assert-Case([string] $Tool, [string] $Command, [bool] $ExpectDeny, [string] $Cwd = $sub, [string] $ReasonLike = '*', [switch] $NoRoot) {
    $result = Invoke-Guard $Tool $Command $Cwd -NoRoot:$NoRoot
    $denied = $false
    if ($result.text) {
        $decision = $result.text | ConvertFrom-Json
        $denied = $decision.hookSpecificOutput.permissionDecision -eq 'deny' -and $decision.hookSpecificOutput.permissionDecisionReason -like $ReasonLike
    }
    $name = "$Tool $(if ($ExpectDeny) { '拦截' } else { '放行' })：$Command（cwd=$Cwd）"
    if ($result.exit -ne 0 -or $denied -ne $ExpectDeny) { throw "断言失败：$name；退出码 $($result.exit)，输出 $($result.text)" }
    $assertions.Add($name)
}

# 删除（A2）
Assert-Case 'PowerShell' 'Remove-Item -LiteralPath .\a.txt' $true $sub '*A2*'
Assert-Case 'PowerShell' 'Get-ChildItem; rm .\a.txt' $true $sub '*A2*'
Assert-Case 'PowerShell' 'Microsoft.PowerShell.Management\Remove-Item x' $true $sub '*A2*'
Assert-Case 'PowerShell' 'Get-ChildItem *.tmp | ForEach-Object { del $_ }' $true $sub '*A2*'
Assert-Case 'PowerShell' '[IO.File]::Delete("a.txt")' $true $sub '*A2*'
Assert-Case 'PowerShell' 'pwsh -NoProfile -Command "Remove-Item x"' $true $sub '*A2*'
Assert-Case 'PowerShell' 'git -C {ROOT}\repo-a clean -fd' $true $sub '*git clean*'
Assert-Case 'Bash' 'ls && rm -rf build' $true $sub '*A2*'
Assert-Case 'Bash' 'FOO=1 sudo rm x' $true $sub '*A2*'
Assert-Case 'Bash' 'find . -name "*.log" -delete' $true $sub '*find*'
Assert-Case 'Bash' 'find . -exec rm {} \;' $true $sub '*A2*'
Assert-Case 'Bash' 'echo $(rm x)' $true $sub '*A2*'
Assert-Case 'Bash' 'git ls-files | xargs rm' $true $sub '*A2*'
Assert-Case 'Bash' 'bash -c "rm x"' $true $sub '*A2*'

# 工作区根目录禁止 Git
Assert-Case 'PowerShell' 'git status' $true $root '*根目录*'
Assert-Case 'PowerShell' 'git -C {ROOT} status' $true $sub '*根目录*'
Assert-Case 'PowerShell' 'Set-Location {ROOT}; git log -1' $true $sub '*根目录*'
Assert-Case 'PowerShell' 'git -C .. status' $true $sub '*根目录*'
Assert-Case 'Bash' 'cd {BASHROOT} && git status' $true $sub '*根目录*'
Assert-Case 'Bash' 'git -C "{SLASHROOT}/" status' $true $sub '*根目录*'

# 旧 shell
Assert-Case 'PowerShell' 'cmd /c dir' $true $sub '*pwsh*'
Assert-Case 'PowerShell' 'powershell.exe -NoProfile -Command Get-Date' $true $sub '*pwsh*'
Assert-Case 'Bash' 'cmd.exe /c dir' $true $sub '*pwsh*'

# 无法审查时失败关闭
Assert-Case 'PowerShell' '& $tool -x' $true $sub '*动态*'
Assert-Case 'PowerShell' 'pwsh -EncodedCommand AAAA' $true $sub '*EncodedCommand*'
Assert-Case 'PowerShell' 'Get-ChildItem (' $true $sub '*无法解析*'

# 放行：正常操作不受影响
Assert-Case 'PowerShell' 'git -C {ROOT}\repo-a status --short' $false $root
Assert-Case 'PowerShell' 'git status' $false $sub
Assert-Case 'PowerShell' 'Get-ChildItem -Recurse .ai | Measure-Object' $false $root
Assert-Case 'PowerShell' 'Move-Item a.txt b.txt; Write-Output "rm is a word"' $false $sub
Assert-Case 'PowerShell' 'Set-Location {ROOT}\repo-a; git log -1' $false $root
Assert-Case 'PowerShell' 'pwsh -NoProfile -File {ROOT}\scripts\Get-ReviewSnapshot.ps1 -Repository {ROOT}\repo-a -TargetRef main' $false $root
Assert-Case 'Bash' 'git -C {BASHROOT}/repo-a log --oneline | head -5' $false $root
Assert-Case 'Bash' 'echo "rm -rf is dangerous"' $false $sub
Assert-Case 'Bash' 'grep -rn "Remove-Item" .ai' $false $root

# 第二轮审查发现的绕过写法
Assert-Case 'PowerShell' "Invoke-Expression 'Remove-Item x'" $true $sub '*无法审查*'
Assert-Case 'PowerShell' "'Remove-Item x' | iex" $true $sub '*无法审查*'
Assert-Case 'PowerShell' "[scriptblock]::Create('Remove-Item x').Invoke()" $true $sub '*脚本块*'
Assert-Case 'PowerShell' "`$ExecutionContext.InvokeCommand.InvokeScript('Remove-Item x')" $true $sub '*脚本块*'
Assert-Case 'PowerShell' 'New-Alias zz Remove-Item; zz x' $true $sub '*无法审查*'
Assert-Case 'PowerShell' "Start-Process cmd -ArgumentList '/c del x'" $true $sub '*Start-Process*'
Assert-Case 'PowerShell' "Start-Process pwsh -ArgumentList '-c','Remove-Item x'" $true $sub '*Start-Process*'
Assert-Case 'PowerShell' '(Get-Item d).Delete($true)' $true $sub '*A2*'
Assert-Case 'PowerShell' 'Get-ChildItem *.tmp | ForEach-Object Delete' $true $sub '*A2*'
Assert-Case 'PowerShell' "`$t=[IO.File]; `$t::Delete('x')" $true $sub '*A2*'
Assert-Case 'PowerShell' "pwsh -Comm 'Remove-Item x'" $true $sub '*A2*'
Assert-Case 'PowerShell' 'pwsh -encod AAAA' $true $sub '*EncodedCommand*'
Assert-Case 'PowerShell' 'Invoke-Command -ScriptBlock $sb' $true $sub '*Invoke-Command*'
Assert-Case 'PowerShell' 'Invoke-Command -ScriptBlock { Remove-Item x }' $true $sub '*A2*'
Assert-Case 'PowerShell' 'git -c alias.zz=clean zz -fdx' $true $sub '*alias*'
Assert-Case 'PowerShell' 'git rm -r docs' $true $sub '*git rm*'
Assert-Case 'Bash' 'if true; then rm -rf build; fi' $true $sub '*A2*'
Assert-Case 'Bash' 'for f in a b; do rm $f; done' $true $sub '*A2*'
Assert-Case 'Bash' '{ rm -rf build; }' $true $sub '*A2*'
Assert-Case 'Bash' '! rm -rf build' $true $sub '*A2*'
Assert-Case 'Bash' 'r=rm; $r -rf build' $true $sub '*变量*'
Assert-Case 'Bash' 'eval "rm x"' $true $sub '*无法审查*'
Assert-Case 'Bash' 'busybox rm x' $true $sub '*A2*'
Assert-Case 'Bash' 'sudo -u me rm x' $true $sub '*A2*'
Assert-Case 'Bash' 'ls | xargs -I {} rm {}' $true $sub '*A2*'
Assert-Case 'Bash' 'find . -exec /bin/rm {} +' $true $sub '*A2*'
Assert-Case 'Bash' "find . -exec sh -c 'rm x' \;" $true $sub '*A2*'
Assert-Case 'Bash' "bash -lc 'rm x'" $true $sub '*A2*'
Assert-Case 'Bash' 'wsl rm -rf /tmp/x' $true $sub '*A2*'
Assert-Case 'Bash' 'python -c "import shutil; shutil.rmtree(''x'')"' $true $sub '*A2*'
Assert-Case 'Bash' "node -e `"require('fs').rmSync('x')`"" $true $sub '*A2*'
Assert-Case 'PowerShell' 'Set-Location $env:X; git status' $true $sub '*无法静态确定*'
Assert-Case 'PowerShell' 'Push-Location {ROOT}\repo-a; Pop-Location; git status' $true $root '*根目录*'

# 第二轮审查发现的误拦：必须放行
Assert-Case 'Bash' "git commit -m `"`$(cat <<'EOF'`nfix: don't crash`nEOF`n)`"" $false $sub
Assert-Case 'Bash' "echo hi # it's fine" $false $sub
Assert-Case 'PowerShell' "git commit -m 'fix .Delete() handling'" $false $sub
Assert-Case 'PowerShell' "Select-String -Pattern '[IO.File]::Delete' -Path x.ps1" $false $sub
Assert-Case 'PowerShell' 'git rm --cached secret.txt' $false $sub
Assert-Case 'PowerShell' 'Start-Process notepad.exe' $false $sub
Assert-Case 'Bash' "cat <<EOF > note.txt`nrm is just text here`nEOF" $false $sub

# 未指定 -WorkspaceRoot：跳过根目录 Git 检查，其余拦截照常
Assert-Case 'PowerShell' 'git status' $false $root '*' -NoRoot
Assert-Case 'PowerShell' 'git -C {ROOT} commit -m x' $false $sub '*' -NoRoot
Assert-Case 'PowerShell' 'Remove-Item x' $true $root '*A2*' -NoRoot
Assert-Case 'PowerShell' 'git clean -fdx' $true $root '*git clean*' -NoRoot

# 非 Shell 工具与异常载荷
$other = '{"tool_name":"Read","tool_input":{"file_path":"x"}}' | pwsh -NoProfile -File $ScriptPath -WorkspaceRoot $root
if ($LASTEXITCODE -ne 0 -or $other) { throw '断言失败：非 Shell 工具应静默放行' }
$assertions.Add('非 Shell 工具静默放行')
$PSNativeCommandUseErrorActionPreference = $false
$null = 'not json' | pwsh -NoProfile -File $ScriptPath -WorkspaceRoot $root 2>$null
if ($LASTEXITCODE -ne 2) { throw "断言失败：异常载荷应以退出码 2 失败关闭（实际 $LASTEXITCODE）" }
$assertions.Add('异常载荷以退出码 2（PreToolUse 阻断错误）失败关闭')

[ordered]@{
    passed = $assertions.Count
    testedScript = $ScriptPath
    scriptSHA256 = (Get-FileHash -LiteralPath $ScriptPath -Algorithm SHA256).Hash
    note = '仅传入合成载荷，未执行载荷命令。'
} | ConvertTo-Json
