$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$out = 'd:\game\My project\Tools\_leaf_dump.txt'
$lines = New-Object 'System.Collections.Generic.List[string]'

foreach ($f in [IO.Directory]::GetFiles('d:\game\My project\Assets\Resources_moved\Data\Images', '*.png')) {
    $fn = [IO.Path]::GetFileNameWithoutExtension($f)
    if ($fn.StartsWith('14')) {
        $bytes = $utf8.GetBytes($fn)
        $hex = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ' '
        [void]$lines.Add('IMG=' + $fn)
        [void]$lines.Add('HEX=' + $hex)
    }
}

foreach ($f in [IO.Directory]::GetFiles('d:\game\My project\Assets\Resources\Data\Cards', '*.asset')) {
    $t = [IO.File]::ReadAllText($f, $utf8)
    if ($t -match '(?m)^\s*id:\s*14\s*$') {
        [void]$lines.Add('CARDFILE=' + [IO.Path]::GetFileName($f))
        $m = [regex]::Match($t, '(?m)^\s*imageAddress:\s*(.+)\s*$')
        if ($m.Success) {
            $val = $m.Groups[1].Value
            [void]$lines.Add('ADDR=' + $val)
            $inner = $val.Trim().Trim('"')
            $bytes = $utf8.GetBytes($inner)
            $hex = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ' '
            [void]$lines.Add('ADDRHEX=' + $hex)
        }
        # force fix
        $correct = $null
        foreach ($img in [IO.Directory]::GetFiles('d:\game\My project\Assets\Resources_moved\Data\Images', '*.png')) {
            $leaf = [IO.Path]::GetFileNameWithoutExtension($img)
            if ($leaf.StartsWith('14_')) { $correct = $leaf; break }
        }
        if ($null -ne $correct) {
            $line = '  imageAddress: "' + $correct + '"'
            # wait need full path prefix
            $line = '  imageAddress: "Data/Images/' + $correct + '"'
            $newText = [regex]::Replace($t, '(?m)^\s*imageAddress:\s*.+$', $line)
            $newText = $newText -replace "`r`n", "`n" -replace "`n", "`r`n"
            [IO.File]::WriteAllText($f, $newText, $utf8)
            [void]$lines.Add('FIXED_TO=' + $correct)
            $bytes2 = $utf8.GetBytes($correct)
            [void]$lines.Add('FIXEDHEX=' + (($bytes2 | ForEach-Object { $_.ToString('x2') }) -join ' '))
        }
    }
}

[IO.File]::WriteAllLines($out, $lines, $utf8)
