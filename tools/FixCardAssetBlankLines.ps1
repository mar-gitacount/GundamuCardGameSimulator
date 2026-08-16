$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$cardsDir = 'd:\game\My project\Assets\Resources\Data\Cards'
$fixed = 0

foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    $original = $text

    # image: の直後〜 imageAddress までの空行を除去
    $text = [regex]::Replace(
        $text,
        '(?m)^([ \t]*image:\s*\{fileID:[^\r\n]+\})(?:\r?\n[ \t]*)+\r?\n([ \t]*imageAddress:)',
        '${1}' + "`r`n" + '${2}')

    # imageAddress は必ずダブルクォート
    $text = [regex]::Replace($text, '(?m)^([ \t]*)imageAddress:\s*(.+?)\s*$', {
        param($m)
        $indent = $m.Groups[1].Value
        $val = $m.Groups[2].Value.Trim().Trim('"').Trim("'")
        return ($indent + 'imageAddress: "' + $val + '"')
    })

    $text = $text -replace "`r`n", "`n" -replace "`n", "`r`n"

    if ($text -ne $original) {
        [IO.File]::WriteAllText($path, $text, $utf8)
        $fixed++
    }
}

Write-Output ("fixedBlankOrQuote=$fixed")

# verify 77
$p77 = Join-Path $cardsDir '77StrikeFreedomgandom.asset'
$t = [IO.File]::ReadAllText($p77, $utf8)
$lines = $t -split "`r`n"
for ($i = 18; $i -le 26 -and $i -lt $lines.Length; $i++) {
    Write-Output (('{0}:{1}' -f ($i+1), $lines[$i]))
}
