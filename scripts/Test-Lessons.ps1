#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string[]]$File
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# 只读显式 File 正文；Root 内引用只查元数据，锚点仅在目标也入选时检查。
# 不调用 Git、不联网、不写文件，不输出标题、正文、链接目标或事故 ID。
$maxBytes = 1048576
$findings = [Collections.Generic.List[object]]::new()
$notChecked = [Collections.Generic.List[object]]::new()
$files = [Collections.Generic.List[object]]::new()
$entries = [Collections.Generic.List[object]]::new()
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
$documents = [Collections.Generic.Dictionary[string,object]]::new($pathComparer)
$result = [ordered]@{ tool = 'Test-Lessons'; readOnly = $true; complete = $false; files = @(); entries = @(); findings = @(); notChecked = @() }
$exitCode = 0

function Add-Notice($List, [string]$Kind, [string]$Path = '', [int]$Line = 0, [string]$Id = '') {
    $List.Add([ordered]@{ kind = $Kind; file = $Path; line = $Line; id = $Id })
}
function Test-LinkItem($Item) {
    if ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $true }
    $linkType = $Item.PSObject.Properties['LinkType']
    return ($null -ne $linkType -and $linkType.Value -eq 'HardLink')
}
function Get-RootPath([string]$Value) {
    if (-not [IO.Path]::IsPathFullyQualified($Value)) { throw 'absolute-root-required' }
    if ($Value -match '^[/\\]{2}') { throw 'network-or-device-path' }
    $full = [IO.Path]::GetFullPath($Value)
    $item = Get-Item -LiteralPath $full -Force
    if (-not $item.PSIsContainer) { throw 'directory-required' }
    $cursor = $item
    while ($null -ne $cursor) {
        if (Test-LinkItem $cursor) { throw 'linked-root' }
        $cursor = $cursor.Parent
    }
    if ($full -eq [IO.Path]::GetPathRoot($full)) { return $full }
    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}
function Resolve-SelectedPath([string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '[:*?]') { throw 'invalid-selected-path' }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($rootPath, $Relative))
    if (-not $full.StartsWith($rootPrefix, $pathComparison)) { throw 'outside-root' }
    if ([IO.Path]::GetExtension($full) -ine '.md') { throw 'markdown-required' }
    return $full
}
function Get-PathState([string]$Full) {
    # 调用方先做词法边界判断，再逐级 stat，绝不沿目录连接读取下一层。
    $cursor = $rootPath
    foreach ($part in ([IO.Path]::GetRelativePath($rootPath, $Full) -split '[/\\]')) {
        $cursor = [IO.Path]::Combine($cursor, $part)
        try { $item = Get-Item -LiteralPath $cursor -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { return [pscustomobject]@{ state = 'missing'; item = $null } }
        if (Test-LinkItem $item) { return [pscustomobject]@{ state = 'linked-path'; item = $null } }
        if ($cursor -ne $Full -and -not $item.PSIsContainer) { return [pscustomobject]@{ state = 'missing'; item = $null } }
    }
    return [pscustomobject]@{ state = $(if ($item.PSIsContainer) { 'directory' } else { 'file' }); item = $item }
}
function Read-SelectedDocument([string]$Full) {
    $state = Get-PathState $Full
    if ($state.state -ne 'file') { throw $state.state }
    if ($state.item.Length -gt $maxBytes) { throw 'file-too-large' }
    $stream = [IO.File]::Open($Full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        if ($stream.Length -gt $maxBytes) { throw 'file-too-large' }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -eq 0) { throw 'file-changed' }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1) { throw 'file-changed' }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        return [pscustomobject]@{ text = $text; bytes = $bytes.Length; sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) }
    } finally { $stream.Dispose() }
}
function Get-VisibleLines([string]$Text) {
    $lines = [Collections.Generic.List[object]]::new()
    $fence = $null
    $comment = $false
    $number = 0
    foreach ($raw in ($Text -split "`n")) {
        $number++
        $line = $raw.TrimEnd("`r").TrimStart([char]0xFEFF)
        if ($null -ne $fence) {
            if ($line -match '^\s{0,3}(`{3,}|~{3,})') {
                $marker = $Matches[1]
                if ($marker[0] -eq $fence[0] -and $marker.Length -ge $fence.Length -and $line.Trim() -eq $marker) { $fence = $null }
            }
            continue
        }
        # 注释内的条目、链接和围栏都不可见；逐行处理以保留原始定位。
        $visible = [Text.StringBuilder]::new()
        $commentScan = [regex]::Replace($line, '(`+).*?\1', { param($match) ' ' * $match.Length })
        $cursor = 0
        while ($cursor -lt $line.Length) {
            if ($comment) {
                $end = $line.IndexOf('-->', $cursor, [StringComparison]::Ordinal)
                if ($end -lt 0) { break }
                $comment = $false
                $cursor = $end + 3
            } else {
                $start = $commentScan.IndexOf('<!--', $cursor, [StringComparison]::Ordinal)
                if ($start -lt 0) { [void]$visible.Append($line.Substring($cursor)); break }
                [void]$visible.Append($line.Substring($cursor, $start - $cursor))
                $comment = $true
                $cursor = $start + 4
            }
        }
        $line = $visible.ToString()
        if ($line -match '^\s{0,3}(`{3,}|~{3,})') { $fence = $Matches[1]; continue }
        $lines.Add([pscustomobject]@{ line = $number; text = $line })
    }
    return $lines.ToArray()
}
function Get-HeadingIds($Lines) {
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seen = @{}
    foreach ($line in $Lines) {
        $withoutCode = [regex]::Replace($line.text, '(`+).*?\1', '')
        foreach ($tag in [regex]::Matches($withoutCode, '<([A-Za-z][A-Za-z0-9:-]*)\b([^>]*)>')) {
            foreach ($attribute in [regex]::Matches($tag.Groups[2].Value, '\b(id|name)\s*=\s*(["''])(.*?)\2', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                if ($attribute.Groups[1].Value -ieq 'id' -or $tag.Groups[1].Value -ieq 'a') {
                    [void]$ids.Add([Net.WebUtility]::HtmlDecode($attribute.Groups[3].Value))
                }
            }
        }
        if ($line.text -match '^#{1,6}\s+(.+?)\s*#*\s*$') {
            $base = $Matches[1].ToLowerInvariant() -replace '[^\p{L}\p{N}\p{M}_\-\s]', '' -replace '\s', '-'
            $id = $base
            if ($seen.ContainsKey($base)) { $seen[$base]++; $id = "$base-$($seen[$base])" } else { $seen[$base] = 0 }
            [void]$ids.Add($id)
        }
    }
    return ,$ids
}
function Get-InlineLinks([string]$Text) {
    # 支持行内相对引用和 <含空格目标>；不把代码示例当作实际引用。
    $withoutCode = [regex]::Replace($Text, '(`+).*?\1', '')
    foreach ($match in [regex]::Matches($withoutCode, '\]\(\s*(?:<([^<>\r\n]+)>|([^\s()]+))(?:\s+["''][^"'']*["''])?\s*\)')) {
        if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
    }
}
function Test-Meaningful([string]$Value) {
    $valueText = $Value.Trim().Trim('`')
    return ($valueText -and $valueText -notmatch '^<.*>$|^(?:未记录|未知|无|待补充|待核验|待填写|未提供|未验证|不适用|none|unknown)(?:[。.!！]?)$')
}
function New-Entry([string]$Path, [int]$Line, [string]$Id) {
    return [ordered]@{ file = $Path; line = $Line; id = $Id; evidence = $false; legacyEvidence = $false; traceableLink = $false; seenValues = [Collections.Generic.List[long]]::new(); incidentIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); completeValues = [Collections.Generic.List[string]]::new(); invalidIncident = $false }
}
function Add-EntryResult($Entry) {
    $evidenceStatus = if ($Entry.evidence -and $Entry.traceableLink) { 'present' } elseif ($Entry.evidence -or $Entry.traceableLink -or $Entry.legacyEvidence) { 'unknown' } else { 'missing' }
    if ($evidenceStatus -eq 'missing') { Add-Notice $findings 'missing-evidence' $Entry.file $Entry.line $Entry.id }
    elseif ($evidenceStatus -eq 'unknown') { Add-Notice $notChecked 'evidence-unknown' $Entry.file $Entry.line $Entry.id }
    $reported = if ($Entry.seenValues.Count -eq 1) { $Entry.seenValues[0] } else { $null }
    $expected = $null
    $status = 'unknown'
    if ($Entry.completeValues.Count -eq 1 -and $Entry.completeValues[0] -eq '是' -and -not $Entry.invalidIncident -and $Entry.incidentIds.Count -gt 0 -and $null -ne $reported) {
        $expected = $Entry.incidentIds.Count
        $status = if ($reported -eq $expected) { 'matched' } else { 'mismatch' }
        if ($status -eq 'mismatch') { Add-Notice $findings 'seen-mismatch' $Entry.file $Entry.line $Entry.id }
    } else { Add-Notice $notChecked 'seen-unknown' $Entry.file $Entry.line $Entry.id }
    $entries.Add([ordered]@{ file = $Entry.file; line = $Entry.line; id = $Entry.id; evidenceStatus = $evidenceStatus; seen = [ordered]@{ reported = $reported; expected = $expected; status = $status } })
}
function Check-Link([string]$Href, [string]$Full, [string]$Relative, [int]$Line, [string]$Id) {
    if ($Href -match '^[A-Za-z][A-Za-z0-9+.-]*:' -and $Href -notmatch '^[A-Za-z]:[/\\]') {
        Add-Notice $notChecked 'external-link' $Relative $Line $Id
        return
    }
    try {
        $parts = $Href -split '#', 2
        $target = [Uri]::UnescapeDataString($parts[0])
        if ($target -match '^[/\\]{2}|[*?]' -or ($target -match ':' -and $target -notmatch '^[A-Za-z]:[/\\][^:]*$')) {
            Add-Notice $notChecked 'unsupported-link' $Relative $Line $Id
            return
        }
        $resolved = if ($target) { [IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetDirectoryName($Full), $target)) } else { $Full }
        if (-not $resolved.StartsWith($rootPrefix, $pathComparison)) {
            Add-Notice $notChecked 'link-outside-root' $Relative $Line $Id
            return
        }
        $state = Get-PathState $resolved
        if ($state.state -eq 'missing') { Add-Notice $findings 'broken-link' $Relative $Line $Id; return }
        if ($state.state -eq 'linked-path') { Add-Notice $notChecked 'linked-reference' $Relative $Line $Id; return }
        if ($parts.Count -eq 2 -and $parts[1]) {
            if (-not $documents.ContainsKey($resolved)) { Add-Notice $notChecked 'anchor-not-selected' $Relative $Line $Id; return }
            $anchor = [Uri]::UnescapeDataString($parts[1])
            if (-not $documents[$resolved].anchors.Contains($anchor)) { Add-Notice $findings 'broken-anchor' $Relative $Line $Id }
        }
    } catch { Add-Notice $notChecked 'reference-unavailable' $Relative $Line $Id }
}

try {
    $rootPath = Get-RootPath $Root
    $rootPrefix = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $selected = [Collections.Generic.HashSet[string]]::new($pathComparer)
    foreach ($relativeInput in $File) {
        $relative = ''
        try {
            $full = Resolve-SelectedPath $relativeInput
            $relative = [IO.Path]::GetRelativePath($rootPath, $full).Replace('\', '/')
            if (-not $selected.Add($full)) { continue }
            $document = Read-SelectedDocument $full
            $visible = @(Get-VisibleLines $document.text)
            $documents[$full] = [pscustomobject]@{ relative = $relative; lines = $visible; anchors = (Get-HeadingIds $visible) }
            $files.Add([ordered]@{ file = $relative; state = 'checked'; bytes = $document.bytes; sha256 = $document.sha256 })
        } catch {
            $kind = if ($_.Exception.Message -in @('invalid-selected-path', 'outside-root', 'markdown-required', 'missing', 'directory', 'linked-path', 'file-too-large', 'file-changed')) { $_.Exception.Message } else { 'file-unavailable' }
            Add-Notice $notChecked $kind $relative
            $exitCode = 2
        }
    }
    foreach ($full in @($documents.Keys | Sort-Object)) {
        $document = $documents[$full]
        $current = $null
        foreach ($line in $document.lines) {
            if ($line.text -match '^##\s+(AI-\d+)\s*[：:]') {
                if ($null -ne $current) { Add-EntryResult $current }
                $current = New-Entry $document.relative $line.line $Matches[1]
            } elseif ($line.text -match '^##\s+AI-[^：:\r\n]+[：:]') {
                if ($null -ne $current) { Add-EntryResult $current; $current = $null }
                Add-Notice $notChecked 'unsupported-entry-id' $document.relative $line.line
            } elseif ($line.text -match '^#{1,2}\s') {
                if ($null -ne $current) { Add-EntryResult $current; $current = $null }
            }
            $id = if ($null -ne $current) { $current.id } else { '' }
            $withoutCode = [regex]::Replace($line.text, '(`+).*?\1', '')
            if ($withoutCode -match '\[[^\]\r\n]+\]\[[^\]\r\n]*\]|^\s{0,3}\[[^\]\r\n]+\]:') {
                Add-Notice $notChecked 'unsupported-reference-link' $document.relative $line.line $id
            }
            $links = @(Get-InlineLinks $line.text)
            foreach ($href in $links) { Check-Link $href $full $document.relative $line.line $id }
            if ($null -eq $current) { continue }
            if ($links.Count -gt 0) { $current.traceableLink = $true }
            if ($line.text -match '^\s*(?:-\s*)?证据\s*[：:]\s*(.*)$') {
                if (Test-Meaningful $Matches[1]) { $current.evidence = $true }
            }
            if ($line.text -match '^\s*(?:-\s*)?(?:事故|来源|验证)\s*[：:]\s*(.*)$') {
                # 兼容旧格式：这些字段仅证明已有待核验线索，不推断其真实性。
                if (Test-Meaningful $Matches[1]) { $current.legacyEvidence = $true }
            }
            if ($line.text -match '^\s*(?:-\s*)?事故\s*ID\s*完整\s*[：:]\s*(.*)$') {
                $current.completeValues.Add($Matches[1].Trim())
            } elseif ($line.text -match '^\s*(?:-\s*)?事故\s*ID\s*[：:]\s*(.*)$') {
                $value = $Matches[1].Trim().Trim('`')
                if (Test-Meaningful $value) { [void]$current.incidentIds.Add($value) } else { $current.invalidIncident = $true }
            }
            if ($line.text -match '^\s*(?:-\s*)?Seen\s+(\d+)\s*×') {
                $number = 0L
                if ([long]::TryParse($Matches[1], [ref]$number)) { $current.seenValues.Add($number) }
            }
        }
        if ($null -ne $current) { Add-EntryResult $current }
    }
    foreach ($group in @($entries | Group-Object -Property { $_['id'] } | Where-Object Count -GT 1)) {
        foreach ($entry in $group.Group) { Add-Notice $findings 'duplicate-id' $entry.file $entry.line $entry.id }
    }
    $result.complete = ($exitCode -eq 0)
} catch {
    $kind = if ($_.Exception.Message -in @('absolute-root-required', 'network-or-device-path', 'directory-required', 'linked-root', 'volume-root-not-supported')) { $_.Exception.Message } else { 'check-unavailable' }
    Add-Notice $notChecked $kind
    $exitCode = 2
}
$result.files = $files.ToArray()
$result.entries = $entries.ToArray()
$result.findings = $findings.ToArray()
$result.notChecked = $notChecked.ToArray()
$result | ConvertTo-Json -Depth 8
exit $exitCode
