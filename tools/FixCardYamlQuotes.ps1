$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$cardsDir = 'd:\game\My project\Assets\Resources\Data\Cards'
$fixed = 0
$report = New-Object 'System.Collections.Generic.List[string]'

function Quote-IfNeeded([string]$indent, [string]$key, [string]$rawValue) {
    $val = $rawValue.Trim()
    if (($val.StartsWith('"') -and $val.EndsWith('"')) -or ($val.StartsWith("'") -and $val.EndsWith("'"))) {
        # normalize to double quotes, unescape simple
        $inner = $val.Substring(1, $val.Length - 2)
        return ($indent + $key + ': "' + ($inner -replace '\\','\\' -replace '"','\"') + '"')
    }

    # Unity YAML: スペース・アポストロフィ・コロン・# などを含むなら必須でクォート
    $needsQuote = $val -match '[\s:\#''\"]' -or $val.Length -eq 0
    if ($needsQuote) {
        return ($indent + $key + ': "' + ($val -replace '\\','\\' -replace '"','\"') + '"')
    }
    return ($indent + $key + ': ' + $val)
}

foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    $original = $text

    # image と imageAddress の間の空行を除去
    $text = [regex]::Replace(
        $text,
        '(?m)^([ \t]*image:\s*\{fileID:[^\r\n]+\})(?:\r?\n[ \t]*)+\r?\n([ \t]*imageAddress:)',
        '${1}' + "`r`n" + '${2}')

    # 危険な文字列フィールドをクォート
    foreach ($key in @('cardName', 'imageAddress', 'gcgOfficialId', 'm_Name')) {
        $text = [regex]::Replace($text, '(?m)^([ \t]*)' + [regex]::Escape($key) + ':\s*(.*?)\s*$', {
            param($m)
            return (Quote-IfNeeded $m.Groups[1].Value $key $m.Groups[2].Value)
        })
    }

    $text = $text -replace "`r`n", "`n" -replace "`n", "`r`n"

    if ($text -ne $original) {
        [IO.File]::WriteAllText($path, $text, $utf8)
        $fixed++
        # force reimport hint by touching .meta
        $meta = $path + '.meta'
        if ([IO.File]::Exists($meta)) {
            [IO.File]::SetLastWriteTimeUtc($meta, [DateTime]::UtcNow)
        }
    }
}

# verify Hobby Hizack
$p = Join-Path $cardsDir '100Hobby Hizack.asset'
$lines = [IO.File]::ReadAllLines($p, $utf8)
[void]$report.Add("fixed=$fixed")
for ($i = 14; $i -le 26 -and $i -lt $lines.Length; $i++) {
    [void]$report.Add(('{0}:{1}' -f ($i+1), $lines[$i]))
}

# find any remaining unquoted imageAddress with spaces
$bad = 0
foreach ($path in [IO.Directory]::GetFiles($cardsDir, '*.asset')) {
    $t = [IO.File]::ReadAllText($path, $utf8)
    foreach ($m in [regex]::Matches($t, '(?m)^[ \t]*imageAddress:\s*(.+)$')) {
        $v = $m.Groups[1].Value.Trim()
        if ($v.Contains(' ') -and -not (($v.StartsWith('"') -and $v.EndsWith('"')))) {
            $bad++
            [void]$report.Add('BAD ' + [IO.Path]::GetFileName($path) + ' => ' + $v)
        }
    }
}
[void]$report.Add("unquotedImageAddressWithSpace=$bad")
[IO.File]::WriteAllLines('d:\game\My project\Tools\_yaml_fix_report.txt', $report, $utf8)
$report | ForEach-Object { Write-Output $_ }
