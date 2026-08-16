# 1) git から Cards を全復元
# 2) imageAddress だけ安全に追加（効果 YAML は触らない）
$ErrorActionPreference = 'Stop'
Set-Location 'd:\game\My project'

Write-Output '=== git restore Cards ==='
git checkout HEAD -- 'Assets/Resources/Data/Cards/'
if ($LASTEXITCODE -ne 0) { throw "git checkout failed: $LASTEXITCODE" }

$utf8 = New-Object System.Text.UTF8Encoding $false
$cardsDir = 'Assets\Resources\Data\Cards'
$imgDirs = @(
    'Assets\Resources_moved\Data\Images',
    'Assets\AddressableData\Images',
    'Assets\Resources\Data\Images'
)

# guid -> leaf, and id -> preferred address
$guidToLeaf = @{}
$idToLeaf = @{}
$allLeaves = New-Object 'System.Collections.Generic.List[string]'

foreach ($imgDir in $imgDirs) {
    if (-not (Test-Path $imgDir)) { continue }
    foreach ($meta in [IO.Directory]::GetFiles((Resolve-Path $imgDir), '*.meta')) {
        $imgPath = $meta.Substring(0, $meta.Length - 5)
        if (-not [IO.File]::Exists($imgPath)) { continue }
        $ext = [IO.Path]::GetExtension($imgPath).ToLowerInvariant()
        if (@('.png','.jpg','.jpeg','.webp','.tga') -notcontains $ext) { continue }
        $leaf = [IO.Path]::GetFileNameWithoutExtension($imgPath)
        $metaText = [IO.File]::ReadAllText($meta, $utf8)
        $gm = [regex]::Match($metaText, '(?m)^guid:\s*([a-f0-9]+)\s*$')
        if ($gm.Success) { $guidToLeaf[$gm.Groups[1].Value] = $leaf }
        if (-not $allLeaves.Contains($leaf)) { [void]$allLeaves.Add($leaf) }
        $im = [regex]::Match($leaf, '^(\d+)[_\s]')
        if ($im.Success) {
            $id = [int]$im.Groups[1].Value
            if (-not $idToLeaf.ContainsKey($id)) { $idToLeaf[$id] = $leaf }
        }
    }
}

# Addressables group addresses (prefer these leaves)
$aaPath = 'Assets\AddressableAssetsData\AssetGroups\Default Local Group.asset'
if (Test-Path $aaPath) {
    $aa = [IO.File]::ReadAllText((Resolve-Path $aaPath), $utf8)
    foreach ($m in [regex]::Matches($aa, 'm_Address:\s*"?(Data/Images/[^"\r\n]+)"?')) {
        $addr = $m.Groups[1].Value.Trim().Trim('"')
        if (-not $addr.StartsWith('Data/Images/')) { continue }
        $leaf = $addr.Substring('Data/Images/'.Length)
        # unescape \uXXXX in address leaves for matching
        $leafDecoded = [regex]::Replace($leaf, '\\u([0-9A-Fa-f]{4})', {
            param($mm) [string][char][Convert]::ToInt32($mm.Groups[1].Value, 16)
        })
        $im = [regex]::Match($leafDecoded, '^(\d+)[_\s]')
        if ($im.Success) {
            $id = [int]$im.Groups[1].Value
            $idToLeaf[$id] = $leafDecoded
        }
    }
}

function Escape-UnityYamlString([string]$s) {
    # Unity-safe: use \u for non-ascii and for apostrophe/space-heavy names keep double quotes with \u apostrophe
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $s.ToCharArray()) {
        $code = [int][char]$ch
        if ($ch -eq '`') { [void]$sb.Append('\`') } # noop
        if ($ch -eq '\') { [void]$sb.Append('\\'); continue }
        if ($ch -eq '"') { [void]$sb.Append('\"'); continue }
        if ($code -lt 32 -or $code -gt 126 -or $ch -eq "'") {
            [void]$sb.AppendFormat('\u{0:X4}', $code)
            continue
        }
        [void]$sb.Append($ch)
    }
    return $sb.ToString()
}

function Find-LeafForCard([string]$text, [int]$id, [string]$cardName) {
    if ($id -gt 0 -and $idToLeaf.ContainsKey($id)) { return $idToLeaf[$id] }

    # sprite guid from imageName
    $sm = [regex]::Match($text, 'imageName:\s*\{fileID:\s*[^}]*guid:\s*([a-f0-9]+)')
    if ($sm.Success -and $guidToLeaf.ContainsKey($sm.Groups[1].Value)) {
        return $guidToLeaf[$sm.Groups[1].Value]
    }

    if (-not [string]::IsNullOrWhiteSpace($cardName)) {
        foreach ($leaf in $allLeaves) {
            if ($leaf -eq $cardName) { return $leaf }
        }
        foreach ($leaf in $allLeaves) {
            if ($leaf.EndsWith('_' + $cardName) -or $leaf.EndsWith($cardName)) { return $leaf }
        }
        foreach ($leaf in $allLeaves) {
            if ($leaf.IndexOf($cardName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $leaf }
        }
    }
    return $null
}

$added = 0
$skipped = 0
$noLeaf = 0
foreach ($path in [IO.Directory]::GetFiles((Resolve-Path $cardsDir), '*.asset')) {
    $text = [IO.File]::ReadAllText($path, $utf8)
    $original = $text

    # already has imageAddress -> leave effects alone, but ensure no blank line before it
    if ($text -match '(?m)^\s*imageAddress:') {
        $text2 = [regex]::Replace($text, '(?m)^([ \t]*image:\s*\{fileID:[^\r\n]+\})(?:\r?\n[ \t]*)+\r?\n([ \t]*imageAddress:)', '${1}'+"`r`n"+'${2}')
        if ($text2 -ne $text) {
            [IO.File]::WriteAllText($path, ($text2 -replace "`r`n","`n" -replace "`n","`r`n"), $utf8)
            $added++
        } else { $skipped++ }
        continue
    }

    $id = 0
    $idm = [regex]::Match($text, '(?m)^\s*id:\s*(\d+)\s*$')
    if ($idm.Success) { $id = [int]$idm.Groups[1].Value }

    $cardName = ''
    $cn = [regex]::Match($text, '(?m)^\s*cardName:\s*(.+?)\s*$')
    if ($cn.Success) {
        $cardName = $cn.Groups[1].Value.Trim().Trim('"').Trim("'")
        $cardName = [regex]::Replace($cardName, '\\u([0-9A-Fa-f]{4})', {
            param($mm) [string][char][Convert]::ToInt32($mm.Groups[1].Value, 16)
        })
    }

    $leaf = Find-LeafForCard $text $id $cardName
    if ([string]::IsNullOrEmpty($leaf)) {
        $noLeaf++
        Write-Output ("NO_LEAF " + [IO.Path]::GetFileName($path) + " id=$id name=$cardName")
        continue
    }

    $escaped = Escape-UnityYamlString $leaf
    $addrLine = '  imageAddress: "Data/Images/' + $escaped + '"'

    # Insert after image: line; clear imageName sprite ref
    $text = [regex]::Replace($text, '(?m)^([ \t]*imageName:\s*)\{fileID:[^\r\n]+\}', '${1}{fileID: 0}')
    if ($text -match '(?m)^[ \t]*image:\s*\{fileID:') {
        $text = [regex]::Replace($text, '(?m)^([ \t]*image:\s*\{fileID:[^\r\n]+\})\r?\n', '${1}'+"`r`n"+$addrLine+"`r`n", 1)
    } else {
        Write-Output ("NO_IMAGE_LINE " + [IO.Path]::GetFileName($path))
        continue
    }

    # remove accidental blank lines before imageAddress
    $text = [regex]::Replace($text, '(?m)^([ \t]*image:\s*\{fileID:[^\r\n]+\})(?:\r?\n[ \t]*)+\r?\n([ \t]*imageAddress:)', '${1}'+"`r`n"+'${2}')
    $text = $text -replace "`r`n","`n" -replace "`n","`r`n"

    if ($text -ne $original) {
        [IO.File]::WriteAllText($path, $text, $utf8)
        $added++
    }
}

Write-Output "guidMap=$($guidToLeaf.Count) idMap=$($idToLeaf.Count) leaves=$($allLeaves.Count)"
Write-Output "updated=$added skippedHasAddr=$skipped noLeaf=$noLeaf"

# verify justice
$j = Get-Content 'Assets\Resources\Data\Cards\17justisGundam.asset' -Raw
Write-Output ("justice timedEffects=" + ($j -match 'timedEffects:\r?\n\s+- timing:'))
Write-Output ("justice type22=" + ($j -match 'type: 22'))
Write-Output ("justice imageAddress=" + ([regex]::Match($j, 'imageAddress:.*').Value))
