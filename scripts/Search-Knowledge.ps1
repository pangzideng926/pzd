#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string[]]$Keyword,
    [ValidateRange(1, 200)][int]$Top = 10
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

# Read-only: lists candidate knowledge entries with metadata; never writes files or echoes entry bodies.
# UseAdvice/ReviewReasons come from metadata only and do not verify sources or the current environment.
$fieldPattern = '^\s*(?:-\s*)?(关键词|适用范围|状态|证据|最后核验|复核条件|有效至|替代条目|场景)\s*[：:]\s*(.*)$'
$maxBytes = 1048576

function New-Entry([string]$File, [int]$Line, [string]$Heading) {
    $separator = $Heading.IndexOfAny([char[]]'：:')
    $id = if ($separator -gt 0) { $Heading.Substring(0, $separator).Trim() } else { $null }
    $title = if ($separator -gt 0) { $Heading.Substring($separator + 1).Trim() } else { $Heading.Trim() }
    return [ordered]@{ file = $File; line = $Line; id = $id; title = $title; fields = @{}; body = [Text.StringBuilder]::new(); seen = $null }
}

$code = 0
$result = [ordered]@{ tool = 'Search-Knowledge'; readOnly = $true; complete = $false }
try {
    if (-not [IO.Path]::IsPathFullyQualified($Root)) { throw 'absolute-root-required' }
    $rootPath = [IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) { throw 'root-not-found' }
    $terms = @($Keyword | ForEach-Object { $_ -split '[,，]' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
    if ($terms.Count -eq 0) { throw 'keyword-required' }
    $today = [DateOnly]::FromDateTime([DateTime]::Today)

    $entries = [Collections.Generic.List[object]]::new()
    $skipped = [Collections.Generic.List[string]]::new()
    $files = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter '*.md' -Attributes !ReparsePoint)
    foreach ($fileItem in $files) {
        $relative = [IO.Path]::GetRelativePath($rootPath, $fileItem.FullName).Replace('\', '/')
        if ($fileItem.Length -gt $maxBytes) { $skipped.Add("$relative（超过 1 MiB）"); continue }
        $lines = [IO.File]::ReadAllLines($fileItem.FullName, [Text.UTF8Encoding]::new($false))
        $fence = $null
        $current = $null
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $text = $lines[$i].TrimStart([char]0xFEFF)
            # A fence closes only with the same marker character and at least the opening length (CommonMark).
            if ($text -match '^\s{0,3}(`{3,}|~{3,})') {
                $marker = $Matches[1]
                if (-not $fence) { $fence = $marker; continue }
                if ($marker[0] -eq $fence[0] -and $marker.Length -ge $fence.Length -and $text.Trim() -eq $marker) { $fence = $null; continue }
            }
            if ($fence) { continue }
            if ($text -match '^##\s+(.+)$') {
                if ($current) { $entries.Add($current) }
                $current = New-Entry $relative ($i + 1) $Matches[1]
                continue
            }
            if (-not $current) { continue }
            if ($text -match $fieldPattern -and -not $current.fields.ContainsKey($Matches[1])) { $current.fields[$Matches[1]] = $Matches[2].Trim() }
            if ($text -match 'Seen\s+(\d+)\s*×') { $current.seen = [int]$Matches[1] }
            [void]$current.body.AppendLine($text)
        }
        if ($current) { $entries.Add($current) }
    }

    $hits = foreach ($entry in $entries) {
        $head = "$($entry.id) $($entry.title) $($entry.fields['关键词'])"
        $body = $entry.body.ToString()
        $matched = 0; $score = 0
        foreach ($term in $terms) {
            $inHead = $head.Contains($term, [StringComparison]::OrdinalIgnoreCase)
            $inBody = $body.Contains($term, [StringComparison]::OrdinalIgnoreCase)
            if ($inHead -or $inBody) { $matched++ }
            if ($inHead) { $score += 3 } elseif ($inBody) { $score += 1 }
        }
        if ($matched -eq 0) { continue }

        $status = $entry.fields['状态']
        $reasons = [Collections.Generic.List[string]]::new()
        if (-not $status) { $reasons.Add('状态未标注') }
        elseif ($status -match '已替代|已停用') { $reasons.Add('已替代或停用，仅作历史与反例') }
        elseif ($status -match '待核验') { $reasons.Add('待核验') }
        $verified = $entry.fields['最后核验']
        if (-not $verified -or $verified -match '未记录|未复验') { $reasons.Add('未记录核验') }
        $expires = $entry.fields['有效至']
        if ($expires) {
            $parsed = [DateOnly]::MinValue
            if ([DateOnly]::TryParseExact($expires, 'yyyy-MM-dd', [ref]$parsed)) { if ($parsed -lt $today) { $reasons.Add("已过有效期 $expires") } }
            else { $reasons.Add('有效至格式无法识别') }
        }
        $advice = if ($status -match '已替代|已停用') { 'history-only' } elseif ($reasons.Count -gt 0) { 'verify-first' } else { 'candidate' }

        [ordered]@{
            id = $entry.id; title = $entry.title; file = $entry.file; line = $entry.line
            status = if ($status) { $status } else { '未标注' }
            evidence = $entry.fields['证据']; lastVerified = $verified; scene = $entry.fields['场景']; seen = $entry.seen
            matchedKeywords = $matched; score = $score
            ReviewReasons = $reasons.ToArray(); UseAdvice = $advice
        }
    }
    $ranked = @($hits | Sort-Object -Property @{ Expression = { $_.matchedKeywords }; Descending = $true }, @{ Expression = { $_.score }; Descending = $true }, @{ Expression = { $_.file } }, @{ Expression = { $_.line } })

    $result.complete = $true
    $result.root = $rootPath
    $result.keywords = $terms
    $result.scannedFiles = $files.Count
    $result.entryCount = $entries.Count
    $result.matchCount = $ranked.Count
    $result.results = @($ranked | Select-Object -First $Top)
    $result.skipped = $skipped.ToArray()
    $result.note = '命中只是候选：引用前读取条目全文与原证据，核对项目、版本、环境和反例。'
} catch {
    $code = 2
    $result.error = $_.Exception.Message
}
$result | ConvertTo-Json -Depth 6
exit $code
