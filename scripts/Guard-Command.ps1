#requires -Version 7.3
[CmdletBinding()]
param(
    [string]$WorkspaceRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# Claude Code PreToolUse hook for Bash/PowerShell: denies deletions (A2, including git rm/clean), Git at the
# workspace root, cmd.exe / Windows PowerShell 5, and constructs it cannot review statically (string evaluation,
# aliases, dynamic names, encoded commands). Without -WorkspaceRoot the root-Git check is skipped.
# Read-only; prints a deny decision or nothing. Internal errors exit 2 (a blocking error for PreToolUse).
# This is a safety net against accidental violations, not a sandbox: script files and other tools are not inspected.
$deleteCommands = @('remove-item', 'rm', 'del', 'erase', 'rd', 'rmdir', 'ri', 'clear-recyclebin', 'remove-itemproperty', 'unlink', 'shred', 'trash')
$legacyShells = @('cmd', 'powershell', 'powershell_ise')
$nestedShells = @('pwsh', 'bash', 'sh', 'zsh', 'dash', 'ksh', 'wsl', 'busybox')
$interpreters = @('python', 'python3', 'py', 'node', 'perl', 'ruby', 'deno', 'bun', 'php')
$unreviewable = @('invoke-expression', 'iex', 'new-alias', 'set-alias', 'nal', 'sal', 'eval', 'add-type')
$pushCommands = @('push-location', 'pushd')
$popCommands = @('pop-location', 'popd')
$locationCommands = @('set-location', 'cd', 'sl', 'chdir') + $pushCommands
$deleteApiPattern = '\b(os\.(remove|unlink|rmdir|removedirs)|shutil\.rmtree|rmtree|unlinkSync|rmSync|rmdirSync|fs\.(rm|unlink|rmdir)|Remove-Item|FileUtils\.rm|File\.delete|\.unlink\(|unlink\s*\(|rmdir\s*\()'
$script:maxDepth = 4

function Get-NormalPath([string]$Path) {
    if (-not $Path) { return $null }
    $value = $Path.Trim()
    if ($value -match '^/([a-zA-Z])(/|$)') { $value = $Matches[1] + ':\' + $value.Substring([Math]::Min(3, $value.Length)) }
    $value = $value.Replace('/', '\')
    return [IO.Path]::GetFullPath($value).TrimEnd('\')
}
function Join-Location([string]$Base, [string]$Target) {
    # $null means "cannot be resolved statically" (variables, subexpressions, home shortcuts, unknown base).
    if (-not $Target -or $Target -match '[\$`~]' -or -not $Base) { return $null }
    $converted = if ($Target -match '^/([a-zA-Z])(/|$)') { $Matches[1] + ':\' + $Target.Substring([Math]::Min(3, $Target.Length)) } else { $Target }
    try {
        if ([IO.Path]::IsPathFullyQualified($converted)) { return Get-NormalPath $converted }
        return Get-NormalPath (Join-Path $Base $converted)
    } catch { return $null }
}
function Get-CommandKey([string]$Name) {
    $leaf = [IO.Path]::GetFileName($Name.Trim('"', "'").Replace('/', '\'))
    return ($leaf -replace '\.(exe|cmd|bat|com|ps1)$', '').ToLowerInvariant()
}
function Test-WorkspaceRoot([string]$Directory) {
    return $null -ne $script:root -and $null -ne $Directory -and [string]::Equals($Directory, $script:root, [StringComparison]::OrdinalIgnoreCase)
}
function Test-OptionPrefix([string]$Word, [string]$Full, [int]$MinLength) {
    # PowerShell accepts any unambiguous prefix of a parameter name, e.g. -Comm for -Command.
    $w = $Word.ToLowerInvariant()
    return $w.Length -ge $MinLength -and $Full.StartsWith($w)
}

# Decision for one simple command: key = normalized name, words = remaining arguments as plain strings.
function Get-CommandDenial([string]$Key, [string[]]$Words, [string]$Directory, [int]$Depth) {
    if ($Key.StartsWith('$') -or $Key.Contains('$(') -or $Key.Contains('`')) { return '命令名来自变量或替换，钩子无法审查；请直接写出命令名。' }
    if ($deleteCommands -contains $Key) { return "A2 硬闸：禁止删除命令 $Key；删除一律改为移动到回收站。" }
    if ($legacyShells -contains $Key) { return "命令必须通过 PowerShell 7（pwsh）执行，禁止 $Key。" }
    if ($unreviewable -contains $Key) { return "$Key 会执行或改写字符串形式的命令，钩子无法审查；请直接写出命令。" }

    if ($Key -eq 'find') {
        for ($i = 0; $i -lt $Words.Count; $i++) {
            $w = $Words[$i].ToLowerInvariant()
            if ($w -eq '-delete') { return 'A2 硬闸：禁止 find -delete。' }
            if ($w -in @('-exec', '-execdir', '-ok', '-okdir') -and $i + 1 -lt $Words.Count) {
                $inner = @($Words | Select-Object -Skip ($i + 1) | ForEach-Object { $_ } | Where-Object { $_ -notin @(';', '+', '\;') })
                $denial = Get-CommandDenial (Get-CommandKey $inner[0]) @($inner | Select-Object -Skip 1) $Directory ($Depth + 1)
                if ($denial) { return $denial }
            }
        }
        return $null
    }
    if ($Key -eq 'xargs') {
        $i = 0
        while ($i -lt $Words.Count -and $Words[$i].StartsWith('-')) {
            if ($Words[$i] -in @('-I', '-i', '-L', '-n', '-P', '-s', '-d', '-E', '-a', '--max-args', '--max-procs', '--delimiter', '--arg-file')) { $i++ }
            $i++
        }
        if ($i -lt $Words.Count) { return Get-CommandDenial (Get-CommandKey $Words[$i]) @($Words | Select-Object -Skip ($i + 1)) $Directory ($Depth + 1) }
        return $null
    }
    if ($Key -in @('wsl', 'busybox', 'doas', 'sudo', 'nice', 'timeout', 'env', 'nohup', 'command', 'exec', 'builtin', 'time')) {
        $i = 0
        while ($i -lt $Words.Count -and ($Words[$i].StartsWith('-') -or ($Key -eq 'timeout' -and $Words[$i] -match '^\d') -or ($Key -eq 'env' -and $Words[$i] -match '^[A-Za-z_][A-Za-z0-9_]*='))) {
            if ($Words[$i] -in @('-u', '-g', '-U', '-C', '-h', '-p', '-d', '-n', '--user', '--group', '--distribution', '--cd')) { $i++ }
            $i++
        }
        if ($i -lt $Words.Count) { return Get-CommandDenial (Get-CommandKey $Words[$i]) @($Words | Select-Object -Skip ($i + 1)) $Directory ($Depth + 1) }
        return $null
    }
    if ($Key -eq 'pwsh') {
        for ($i = 0; $i -lt $Words.Count; $i++) {
            $flag = $Words[$i]
            if (-not $flag.StartsWith('-')) { continue }
            if ((Test-OptionPrefix $flag '-encodedcommand' 3) -or $flag.ToLowerInvariant() -in @('-e', '-ec', '-enc')) { return '无法审查 pwsh -EncodedCommand，请改用明文命令。' }
            if ((Test-OptionPrefix $flag '-command' 3) -or $flag.ToLowerInvariant() -eq '-c') {
                return Get-TextDenial (($Words | Select-Object -Skip ($i + 1)) -join ' ') $Directory 'powershell' ($Depth + 1)
            }
            if ((Test-OptionPrefix $flag '-file' 2) -or $flag.ToLowerInvariant() -eq '-f') { return $null }
        }
        return $null
    }
    if ($Key -in @('bash', 'sh', 'zsh', 'dash', 'ksh')) {
        for ($i = 0; $i -lt $Words.Count; $i++) {
            $flag = $Words[$i]
            if ($flag -match '^-[A-Za-z]*c[A-Za-z]*$' -and $i + 1 -lt $Words.Count) { return Get-TextDenial $Words[$i + 1] $Directory 'bash' ($Depth + 1) }
            if (-not $flag.StartsWith('-')) { break }
        }
        return $null
    }
    if ($interpreters -contains $Key) {
        for ($i = 0; $i -lt $Words.Count; $i++) {
            if ($Words[$i] -in @('-c', '-e', '-E', '--eval', '-r', '-p', '--print') -and $i + 1 -lt $Words.Count) {
                if ($Words[$i + 1] -match $deleteApiPattern) { return "A2 硬闸：$Key 内联代码含删除操作。" }
            }
        }
        return $null
    }
    if ($Key -eq 'git') {
        $effective = $Directory
        $sub = $null; $subIndex = -1
        for ($i = 0; $i -lt $Words.Count; $i++) {
            $word = $Words[$i]
            # Case-sensitive: git -C <dir> and -c <config> differ only by case.
            if ($word -ceq '-C' -and $i + 1 -lt $Words.Count) { $effective = Join-Location $effective $Words[$i + 1]; $i++; continue }
            if ($word -ceq '-c' -and $i + 1 -lt $Words.Count) {
                if ($Words[$i + 1] -match '^alias\.') { return 'git -c alias.* 会改写子命令，钩子无法审查。' }
                $i++; continue
            }
            if ($word -in @('--git-dir', '--work-tree', '--namespace', '--exec-path', '--config-env') -and $i + 1 -lt $Words.Count) { $i++; continue }
            if ($word -like '--config-env=alias.*' -or $word -like '-calias.*') { return 'git alias 配置会改写子命令，钩子无法审查。' }
            if ($word -like '-*') { continue }
            $sub = $word.ToLowerInvariant(); $subIndex = $i
            break
        }
        if ($sub -eq 'clean') { return 'A2 硬闸：git clean 会删除未跟踪文件，禁止执行。' }
        if ($sub -eq 'rm' -and @($Words | Select-Object -Skip ($subIndex + 1)) -notcontains '--cached') { return 'A2 硬闸：git rm 会删除工作区文件；只取消跟踪请用 git rm --cached，删除文件改为移动到回收站。' }
        if ($script:root) {
            # [string] parameters turn $null into '', so test for empty rather than $null.
            if ([string]::IsNullOrEmpty($effective)) { return '已配置工作区根目录，但无法静态确定 Git 的执行目录；请用 git -C <子仓库绝对路径>。' }
            if (Test-WorkspaceRoot $effective) { return "禁止在工作区根目录 $script:root 执行 Git；请用 git -C <子仓库> 指向目标子仓库。" }
        }
    }
    return $null
}

function Get-PowerShellDenial([string]$Text, [string]$Directory, [int]$Depth) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Text, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { return "PowerShell 命令无法解析，钩子无法审查：$($errors[0].Message)" }

    # .NET deletion and string-to-code APIs are checked on the AST, so string literals never match.
    foreach ($call in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true)) {
        if ($call.Member -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { return '成员名是动态表达式，钩子无法审查。' }
        $member = $call.Member.Value
        if ($member -match '^Delete') { return "A2 硬闸：禁止通过 .NET 方法 $member 删除文件或目录。" }
        if ($member -in @('InvokeScript', 'NewScriptBlock') -or ($member -eq 'Create' -and $call.Expression.Extent.Text -match 'scriptblock')) { return '把字符串编译为脚本块执行，钩子无法审查。' }
    }

    $commands = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true) | Sort-Object { $_.Extent.StartOffset })
    $current = $Directory
    $stack = [Collections.Generic.Stack[object]]::new()
    foreach ($command in $commands) {
        $name = $command.GetCommandName()
        if (-not $name) { return '命令名是动态表达式，钩子无法审查；请直接写出命令名。' }
        $key = Get-CommandKey $name
        $elements = @($command.CommandElements | Select-Object -Skip 1)
        $words = @($elements | ForEach-Object {
            if ($_ -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $_.Value }
            elseif ($_ -is [System.Management.Automation.Language.CommandParameterAst]) { '-' + $_.ParameterName }
            elseif ($_ -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) { $_.Value }
            else { $_.Extent.Text.Trim('"', "'") }
        })
        if ($locationCommands -contains $key) {
            if ($pushCommands -contains $key) { $stack.Push($current) }
            $target = @($words | Where-Object { $_ -notlike '-*' } | Select-Object -First 1)
            $current = if ($target.Count -gt 0) { Join-Location $current $target[0] } else { $null }
            continue
        }
        if ($popCommands -contains $key) { $current = if ($stack.Count -gt 0) { $stack.Pop() } else { $null }; continue }
        if ($key -in @('foreach-object', '%') -and @($words | Where-Object { $_ -match '^Delete' }).Count -gt 0) { return 'A2 硬闸：禁止通过 ForEach-Object 调用 Delete 方法。' }
        if ($key -in @('invoke-command', 'icm')) {
            if (-not @($elements | Where-Object { $_ -is [System.Management.Automation.Language.ScriptBlockExpressionAst] })) { return 'Invoke-Command 的脚本块不是字面量，钩子无法审查。' }
            continue
        }
        if ($key -in @('start-process', 'saps', 'start')) {
            $target = @($words | Where-Object { $_ -notlike '-*' } | Select-Object -First 1)
            $targetKey = if ($target.Count -gt 0) { Get-CommandKey $target[0] } else { '' }
            if (-not $targetKey -or $targetKey.StartsWith('$') -or $targetKey -in ($nestedShells + $legacyShells + $interpreters + $deleteCommands + @('git'))) {
                return 'Start-Process 启动的程序或参数钩子无法审查；请直接运行命令。'
            }
            continue
        }
        $denial = Get-CommandDenial $key $words $current $Depth
        if ($denial) { return $denial }
    }
    return $null
}

function Split-BashSegments([string]$Text) {
    # Minimal shell lexer: quotes, backslash escapes, # comments, heredoc bodies (skipped as data);
    # splits on ; & | ( ) { } and newlines.
    $segments = [Collections.Generic.List[object]]::new()
    $words = [Collections.Generic.List[string]]::new()
    $word = [Text.StringBuilder]::new()
    $quote = [char]0; $hasWord = $false
    $heredocs = [Collections.Generic.List[object]]::new()
    $flushWord = { if ($hasWord) { $words.Add($word.ToString()); [void]$word.Clear(); Set-Variable -Name hasWord -Value $false -Scope 1 } }
    $i = 0
    while ($i -lt $Text.Length) {
        $c = $Text[$i]
        if ($quote -ne [char]0) {
            if ($c -eq $quote) { $quote = [char]0 }
            elseif ($c -eq '\' -and $quote -eq '"' -and $i + 1 -lt $Text.Length) { $i++; [void]$word.Append($Text[$i]) }
            else { [void]$word.Append($c) }
            $i++; continue
        }
        if ($c -eq "'" -or $c -eq '"') { $quote = $c; $hasWord = $true; $i++; continue }
        if ($c -eq '\' -and $i + 1 -lt $Text.Length) { [void]$word.Append($Text[$i + 1]); $hasWord = $true; $i += 2; continue }
        if ($c -eq '#' -and -not $hasWord) { while ($i -lt $Text.Length -and $Text[$i] -ne "`n") { $i++ }; continue }
        if ($c -eq '<' -and $i + 1 -lt $Text.Length -and $Text[$i + 1] -eq '<') {
            if ($i + 2 -lt $Text.Length -and $Text[$i + 2] -eq '<') { $i += 3; continue }
            $j = $i + 2; $dash = $false
            if ($j -lt $Text.Length -and $Text[$j] -eq '-') { $dash = $true; $j++ }
            while ($j -lt $Text.Length -and $Text[$j] -in @(' ', "`t")) { $j++ }
            $m = [regex]::Match($Text.Substring($j), '^(?:''([^'']*)''|"([^"]*)"|([A-Za-z0-9_\-\.]+))')
            if ($m.Success) {
                $delimiter = @($m.Groups[1], $m.Groups[2], $m.Groups[3] | Where-Object Success | Select-Object -First 1)[0].Value
                $heredocs.Add([pscustomobject]@{ delimiter = $delimiter; dash = $dash })
                $i = $j + $m.Length; continue
            }
        }
        if ($c -in @(' ', "`t")) { & $flushWord; $i++; continue }
        if ($c -in @(';', '&', '|', "`n", "`r", '(', ')', '{', '}')) {
            & $flushWord
            if ($words.Count -gt 0) { $segments.Add($words.ToArray()); $words.Clear() }
            if ($c -eq "`n" -and $heredocs.Count -gt 0) {
                $i++
                foreach ($doc in $heredocs) {
                    while ($i -lt $Text.Length) {
                        $end = $Text.IndexOf("`n", $i); if ($end -lt 0) { $end = $Text.Length }
                        $line = $Text.Substring($i, $end - $i).TrimEnd("`r")
                        $i = [Math]::Min($end + 1, $Text.Length)
                        if ((& { if ($doc.dash) { $line.TrimStart("`t") } else { $line } }) -eq $doc.delimiter) { break }
                    }
                }
                $heredocs.Clear(); continue
            }
            $i++; continue
        }
        [void]$word.Append($c); $hasWord = $true; $i++
    }
    if ($quote -ne [char]0) { throw 'unterminated-quote' }
    & $flushWord
    if ($words.Count -gt 0) { $segments.Add($words.ToArray()) }
    # Comma keeps a single segment from being unrolled into its words by the pipeline.
    return , $segments
}

function Get-BashDenial([string]$Text, [string]$Directory, [int]$Depth) {
    foreach ($inner in [regex]::Matches($Text, '\$\(([^()]*)\)|`([^`]*)`|<\(([^()]*)\)')) {
        $body = @($inner.Groups[1], $inner.Groups[2], $inner.Groups[3] | Where-Object Success | Select-Object -First 1)[0].Value
        $denial = Get-TextDenial $body $Directory 'bash' ($Depth + 1)
        if ($denial) { return $denial }
    }
    $segments = try { Split-BashSegments $Text } catch { return "Bash 命令无法解析，钩子无法审查：$($_.Exception.Message)" }
    $current = $Directory
    $stack = [Collections.Generic.Stack[object]]::new()
    $keywords = @('then', 'do', 'else', 'elif', 'if', 'while', 'until', '!', 'time', 'coproc')
    foreach ($segment in $segments) {
        $words = @($segment)
        while ($words.Count -gt 0 -and ($words[0] -match '^[A-Za-z_][A-Za-z0-9_]*=' -or $keywords -contains $words[0])) { $words = @($words | Select-Object -Skip 1) }
        if ($words.Count -eq 0 -or $words[0] -in @('fi', 'done', 'esac', 'in')) { continue }
        if ($words[0] -eq 'for' -or $words[0] -eq 'case' -or $words[0] -eq 'select') { continue }
        $key = Get-CommandKey $words[0]
        $rest = @($words | Select-Object -Skip 1)
        if ($key -in @('cd', 'pushd')) {
            if ($key -eq 'pushd') { $stack.Push($current) }
            $current = if ($rest.Count -gt 0) { Join-Location $current $rest[0] } else { $null }
            continue
        }
        if ($key -eq 'popd') { $current = if ($stack.Count -gt 0) { $stack.Pop() } else { $null }; continue }
        if ($key -in @('source', '.')) { continue }
        $denial = Get-CommandDenial $key $rest $current $Depth
        if ($denial) { return $denial }
    }
    return $null
}

function Get-TextDenial([string]$Text, [string]$Directory, [string]$Language, [int]$Depth) {
    if ($Depth -gt $script:maxDepth) { return '嵌套命令层级过深，钩子无法审查。' }
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    if ($Language -eq 'powershell') { return Get-PowerShellDenial $Text $Directory $Depth }
    return Get-BashDenial $Text $Directory $Depth
}

try {
    $script:root = $null
    if ($WorkspaceRoot) {
        if (-not [IO.Path]::IsPathFullyQualified($WorkspaceRoot)) { throw 'WorkspaceRoot 必须是绝对路径' }
        $script:root = Get-NormalPath $WorkspaceRoot
    }
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json -Depth 20
    $tool = [string]$payload.tool_name
    if ($tool -notin @('Bash', 'PowerShell')) { exit 0 }
    $commandText = [string]$payload.tool_input.command
    $cwd = $null
    if ($payload.PSObject.Properties.Name -contains 'cwd' -and $payload.cwd) { try { $cwd = Get-NormalPath ([string]$payload.cwd) } catch { $cwd = $null } }
    $language = if ($tool -eq 'PowerShell') { 'powershell' } else { 'bash' }
    $reason = Get-TextDenial $commandText $cwd $language 0
    if ($reason) {
        [ordered]@{
            hookSpecificOutput = [ordered]@{
                hookEventName = 'PreToolUse'
                permissionDecision = 'deny'
                permissionDecisionReason = "工作区钩子拦截：$reason"
            }
        } | ConvertTo-Json -Depth 4 -Compress
    }
    exit 0
} catch {
    # Exit code 2 is a blocking error for PreToolUse, so a broken hook fails closed even without onFailure.
    [Console]::Error.WriteLine("Guard-Command 失败（按阻断处理）：$($_.Exception.Message)")
    exit 2
}
