$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$cardsDir = 'd:\game\My project\Assets\Resources\Data\Cards'
$imgDir = 'd:\game\My project\Assets\Resources_moved\Data\Images'
$out = 'd:\game\My project\Tools\_fix_report.txt'
$report = New-Object 'System.Collections.Generic.List[string]'

# Build id -> preferred leaf (exact id_ prefix, prefer id_ then contains)
$leaves = New-Object 'System.Collections.Generic.List[string]'
foreach ($f in [IO.Directory]::GetFiles($imgDir, '*.png')) {
    [void]$leaves.Add([IO.Path]::GetFileNameWithoutExtension($f))
}
foreach ($ext in @('*.jpg','*.jpeg','*.webp','*.tga')) {
    foreach ($f in [IO.Directory]::GetFiles($imgDir, $ext)) {
        $leaf = [IO.Path]::GetFileNameWithoutExtension($f)
        if (-not $leaves.Contains($leaf)) { [void]$leaves.Add($leaf) }
    }
}

function Unescape-UnityString([string]$s) {
    $s = $s.Trim()
    if (($s.StartsWith('"') -and $s.EndsWith('"')) -or ($s.StartsWith("'") -and $s.EndsWith("'"))) {
        $s = $s.Substring(1, $s.Length - 2)
    }
    return [regex]::Replace($s, '\\u([0-9A-Fa-f]{4})', {
        param($mm) [string][char][Convert]::ToInt32($mm.Groups[1].Value, 16)
    })
}

function Pick-Leaf([string]$text) {
    $idMatch = [regex]::Match($text, '(?m)^\s*id:\s*(\d+)\s*$')
    $id = if ($idMatch.Success) { $idMatch.Groups[1].Value } else { '' }

    $hits = New-Object 'System.Collections.Generic.List[string]'
    if ($id -ne '') {
        foreach ($leaf in $leaves) {
            if ($leaf -eq $id -or $leaf.StartsWith($id + '_') -or $leaf.StartsWith($id + ' ')) {
                # avoid 14 matching 140: require boundary
                if ($leaf -eq $id) { [void]$hits.Add($leaf); continue }
                $rest = $leaf.Substring($id.Length, 1)
                if ($rest -eq '_' -or $rest -eq ' ') { [void]$hits.Add($leaf) }
            }
        }
        if ($hits.Count -eq 1) { return $hits[0] }
        if ($hits.Count -gt 1) {
            foreach ($h in $hits) { if ($h.StartsWith($id + '_')) { return $h } }
            return $hits[0]
        }
    }

    $cnMatch = [regex]::Match($text, '(?m)^\s*cardName:\s*(.+?)\s*$')
    if ($cnMatch.Success) {
        $cardName = Unescape-UnityString $cnMatch.Groups[1].Value
        foreach ($leaf in $leaves) { if ($leaf -eq $cardName) { return $leaf } }
        foreach ($leaf in $leaves) {
            if ($leaf.EndsWith($cardName) -or ($cardName.Length -ge 3 -and $leaf.Contains($cardName))) {
                return $leaf
            }
        }
    }

    # keep existing if present in leaves
    $addrMatch = [regex]::Match($text, '(?m)^\s*imageAddress:\s*(.+?)\s*$')
    if ($addrMatch.Success) {
        $raw = Unescape-UnityString $addrMatch.Groups[1].Value
        if ($raw.StartsWith('Data/Images/')) { $raw = $raw.Substring('Data/Images/'.Length) }
        foreach ($leaf in $leaves) { if ($leaf -eq $raw) { return $leaf } }
    }
    return $null
}

$fixed = 0
$ok = 0
$fail = 0
foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    $leaf = Pick-Leaf $text
    if ([string]::IsNullOrEmpty($leaf)) {
        $fail++
        [void]$report.Add('FAIL ' + [IO.Path]::GetFileName($path))
        continue
    }

    $newLine = '  imageAddress: "Data/Images/' + ($leaf -replace '\\','\\\\' -replace '"','\"') + '"'
    if ($text -match '(?m)^\s*imageAddress:\s*') {
        $newText = [regex]::Replace($text, '(?m)^\s*imageAddress:\s*.+$', $newLine)
    } else {
        $newText = [regex]::Replace($text, '(?m)^(\s*)image:\s*\{fileID:[^\n]+', '${0}' + "`r`n" + $newLine)
    }
    $newText = [regex]::Replace($newText, '(?m)(\r?\n)(?:[ \t]*\r?\n)+([ \t]*imageAddress:)', '${1}${2}')
    $newText = $newText -replace "`r`n", "`n" -replace "`n", "`r`n"

    if ($newText -ne $text) {
        [IO.File]::WriteAllText($path, $newText, $utf8)
        $fixed++
    } else {
        $ok++
    }
}

[void]$report.Insert(0, "leaves=$($leaves.Count) fixed=$fixed unchanged=$ok fail=$fail")
[IO.File]::WriteAllLines($out, $report, $utf8)

# verify no unquoted imageAddress
$unquoted = 0
foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    foreach ($m in [regex]::Matches($text, '(?m)^\s*imageAddress:\s*(.+)\s*$')) {
        $v = $m.Groups[1].Value.Trim()
        if (-not (($v.StartsWith('"') -and $v.EndsWith('"')) -or ($v.StartsWith("'") -and $v.EndsWith("'")))) {
            $unquoted++
            [void]$report.Add('UNQUOTED ' + [IO.Path]::GetFileName($path) + ' => ' + $v)
        }
    }
}
[void]$report.Add("unquoted=$unquoted")
[IO.File]::WriteAllLines($out, $report, $utf8)
