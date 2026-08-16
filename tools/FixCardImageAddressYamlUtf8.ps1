$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$cardsDir = 'd:\game\My project\Assets\Resources\Data\Cards'
$imgDirs = @(
    'd:\game\My project\Assets\Resources_moved\Data\Images',
    'd:\game\My project\Assets\Resources\Data\Images',
    'd:\game\My project\Assets\AddressableData\Images'
)

$guidToLeaf = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$leaves = New-Object 'System.Collections.Generic.List[string]'
foreach ($imgDir in $imgDirs) {
    if (-not [IO.Directory]::Exists($imgDir)) { continue }
    foreach ($meta in [IO.Directory]::GetFiles($imgDir, '*.meta')) {
        $imgPath = $meta.Substring(0, $meta.Length - 5)
        if (-not [IO.File]::Exists($imgPath)) { continue }
        $ext = [IO.Path]::GetExtension($imgPath).ToLowerInvariant()
        if (@('.png','.jpg','.jpeg','.webp','.tga') -notcontains $ext) { continue }
        $leaf = [IO.Path]::GetFileNameWithoutExtension($imgPath)
        $metaText = [IO.File]::ReadAllText($meta, $utf8)
        $m = [regex]::Match($metaText, '(?m)^guid:\s*([a-f0-9]+)\s*$')
        if ($m.Success) { $guidToLeaf[$m.Groups[1].Value] = $leaf }
        if (-not $leaves.Contains($leaf)) { [void]$leaves.Add($leaf) }
    }
}
Write-Output ("guidMap=" + $guidToLeaf.Count + " leaves=" + $leaves.Count)

function Unescape-UnityString([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s.Trim()
    if (($s.StartsWith('"') -and $s.EndsWith('"')) -or ($s.StartsWith("'") -and $s.EndsWith("'"))) {
        $s = $s.Substring(1, $s.Length - 2)
    }
    return [regex]::Replace($s, '\\u([0-9A-Fa-f]{4})', {
        param($mm) [string][char][Convert]::ToInt32($mm.Groups[1].Value, 16)
    })
}

function Find-Leaf([string]$text, [string]$currentLeaf) {
    if (-not [string]::IsNullOrEmpty($currentLeaf)) {
        foreach ($leaf in $leaves) {
            if ($leaf -eq $currentLeaf) { return $leaf }
        }
    }

    $idMatch = [regex]::Match($text, '(?m)^\s*id:\s*(\d+)\s*$')
    if ($idMatch.Success) {
        $id = $idMatch.Groups[1].Value
        $exact = $null
        $prefixHits = New-Object 'System.Collections.Generic.List[string]'
        foreach ($leaf in $leaves) {
            if ($leaf -eq $id -or $leaf.StartsWith($id + '_') -or $leaf.StartsWith($id + ' ')) {
                [void]$prefixHits.Add($leaf)
            }
        }
        if ($prefixHits.Count -eq 1) { return $prefixHits[0] }
        if ($prefixHits.Count -gt 1) {
            # Prefer currentLeaf if among hits; else shortest / first with id_
            foreach ($h in $prefixHits) { if ($h -eq $currentLeaf) { return $h } }
            foreach ($h in $prefixHits) { if ($h.StartsWith($id + '_')) { return $h } }
            return $prefixHits[0]
        }
    }

    $cnMatch = [regex]::Match($text, '(?m)^\s*cardName:\s*(.+?)\s*$')
    if ($cnMatch.Success) {
        $cardName = Unescape-UnityString $cnMatch.Groups[1].Value
        foreach ($leaf in $leaves) {
            if ($leaf -eq $cardName) { return $leaf }
        }
        foreach ($leaf in $leaves) {
            if ($leaf.EndsWith($cardName) -or $leaf.Contains($cardName)) { return $leaf }
        }
    }

    return $null
}

$fixed = 0
$unresolved = 0
foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    $original = $text
    $name = [IO.Path]::GetFileName($path)

    $addrMatch = [regex]::Match($text, '(?m)^\s*imageAddress:\s*(.+?)\s*$')
    $currentLeaf = ''
    if ($addrMatch.Success) {
        $raw = Unescape-UnityString $addrMatch.Groups[1].Value
        if ($raw.StartsWith('Data/Images/')) {
            $currentLeaf = $raw.Substring('Data/Images/'.Length)
        } else {
            $currentLeaf = $raw
        }
    }

    # Detect corruption (replacement char)
    $isCorrupt = $currentLeaf.Contains([char]0xFFFD)

    $leaf = Find-Leaf $text $currentLeaf
    if ([string]::IsNullOrEmpty($leaf)) {
        if (-not $isCorrupt -and -not [string]::IsNullOrEmpty($currentLeaf)) {
            $leaf = $currentLeaf
        } else {
            $unresolved++
            Write-Output ("UNRESOLVED " + $name + " current=[" + $currentLeaf + "]")
            continue
        }
    }

    $line = 'imageAddress: "Data/Images/' + ($leaf -replace '\\','\\\\' -replace '"','\"') + '"'
    if ($addrMatch.Success) {
        $text = [regex]::Replace($text, '(?m)^(\s*)imageAddress:\s*.+$', '${1}' + $line)
    } else {
        # insert after image:
        $text = [regex]::Replace($text, '(?m)^(\s*)image:\s*\{fileID:[^\n]+', '${0}' + "`r`n" + '${1}' + $line)
    }

    $text = [regex]::Replace($text, '(?m)(\r?\n)(?:[ \t]*\r?\n)+([ \t]*imageAddress:)', '${1}${2}')
    $text = $text -replace "`r`n", "`n" -replace "`n", "`r`n"

    if ($text -ne $original -or $isCorrupt) {
        [IO.File]::WriteAllText($path, $text, $utf8)
        $fixed++
    }
}

Write-Output ("fixed=" + $fixed + " unresolved=" + $unresolved)

# dump id 14 and any with FF FD
foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    if ($text -match '(?m)^\s*id:\s*14\s*$' -or $text.Contains([char]0xFFFD) -or $text -match '(?m)^\s*id:\s*156\s*$') {
        Write-Output ('==== ' + [IO.Path]::GetFileName($path))
        foreach ($mm in [regex]::Matches($text, '(?m)^\s*(id|cardName|imageAddress):.*$')) {
            Write-Output $mm.Value
        }
    }
}
