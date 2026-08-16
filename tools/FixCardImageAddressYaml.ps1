$ErrorActionPreference = 'Stop'
$dir = 'd:\game\My project\Assets\Resources\Data\Cards'
$fixed = 0
$noAddr = New-Object System.Collections.Generic.List[string]

Get-ChildItem (Join-Path $dir '*.asset') | ForEach-Object {
    $path = $_.FullName
    $text = [System.IO.File]::ReadAllText($path)
    $original = $text

    if ($text -notmatch '(?m)^\s*imageAddress:') {
        [void]$noAddr.Add($_.Name)
        return
    }

    # Quote imageAddress values (Unity YAML requires quotes when spaces/special chars exist)
    $text = [regex]::Replace($text, '(?m)^(\s*)imageAddress:\s*(.+?)\s*$', {
        param($m)
        $indent = $m.Groups[1].Value
        $val = $m.Groups[2].Value.Trim()
        if ($val.Length -ge 2 -and (
                ($val.StartsWith('"') -and $val.EndsWith('"')) -or
                ($val.StartsWith("'") -and $val.EndsWith("'")))) {
            # normalize to double quotes
            if ($val.StartsWith("'")) {
                $inner = $val.Substring(1, $val.Length - 2)
                return ($indent + 'imageAddress: "' + $inner.Replace('"', '\"') + '"')
            }
            return ($indent + 'imageAddress: ' + $val)
        }
        return ($indent + 'imageAddress: "' + $val.Replace('"', '\"') + '"')
    })

    # Remove blank line(s) immediately before imageAddress
    $text = [regex]::Replace($text, '(?m)(\r?\n)(?:[ \t]*\r?\n)+([ \t]*imageAddress:)', '$1$2')

    # Ensure LF/CRLF consistency: keep CRLF for Unity on Windows
    $text = $text -replace "`r`n", "`n" -replace "`n", "`r`n"

    if ($text -ne $original) {
        [System.IO.File]::WriteAllText($path, $text)
        $fixed++
    }
}

Write-Output ("fixed=" + $fixed)
Write-Output ("noAddr=" + $noAddr.Count)
if ($noAddr.Count -gt 0) {
    Write-Output '--- missing imageAddress ---'
    $noAddr | Select-Object -First 40 | ForEach-Object { Write-Output $_ }
}

# verify samples
@(
    '27zawort.asset',
    '156Cagalli Yula Athha.asset',
    '70Mikazuki Augus.asset',
    '153Calamity Gundam & Raider Gundam.asset',
    '14シンアスカ.asset'
) | ForEach-Object {
    $p = Join-Path $dir $_
    if (Test-Path $p) {
        Write-Output ('==== ' + $_)
        $lines = [System.IO.File]::ReadAllLines($p)
        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match 'imageName:|image:|imageAddress:|version:') {
                Write-Output $lines[$i]
            }
        }
    }
}
