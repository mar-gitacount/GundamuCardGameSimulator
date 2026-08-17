# EOS P2P 1170B 制限に対するオンライン対戦メッセージサイズ調査
# Unity JsonUtility と同形式の JSON を手組みして UTF-8 バイト数を計測する。

$Limit = 1170
$ChunkLimit = 1100

function Get-Utf8Bytes([string]$s) {
    return [System.Text.Encoding]::UTF8.GetByteCount($s)
}

function Wrap-LeanEnvelope([string]$type, [string]$payloadJson) {
    # JsonUtility: payload 内の " は \" にエスケープ
    $escaped = $payloadJson.Replace('\', '\\').Replace('"', '\"')
    return "{`"type`":`"$type`",`"payload`":`"$escaped`"}"
}

function Make-RestChange([int]$instanceId = 42, [int]$zone = 0, [int]$cardId = 17, [int]$zoneIndex = 0) {
    return @{
        targetInstanceId = $instanceId
        targetZoneOwnerSide = $zone
        targetCardId = $cardId
        targetZoneIndex = $zoneIndex
        changeKind = "Rest"
        hpAfter = 0
        signedStatValue = 0
        statTarget = 0
        duration = 0
        statModifierSourceKey = ""
        grantSourceInstanceId = 0
        grantSourceZoneOwnerSide = 0
        onDestroyedRequestId = 0
        destroyerInstanceId = 0
        nonEffectDestroy = 0
    }
}

function Make-EffectSyncPayload([array]$changes) {
    $parts = @()
    foreach ($c in $changes) {
        $parts += (@"
{"targetInstanceId":$($c.targetInstanceId),"targetZoneOwnerSide":$($c.targetZoneOwnerSide),"targetCardId":$($c.targetCardId),"targetZoneIndex":$($c.targetZoneIndex),"changeKind":"$($c.changeKind)","hpAfter":$($c.hpAfter),"signedStatValue":$($c.signedStatValue),"statTarget":$($c.statTarget),"duration":$($c.duration),"statModifierSourceKey":"$($c.statModifierSourceKey)","grantSourceInstanceId":$($c.grantSourceInstanceId),"grantSourceZoneOwnerSide":$($c.grantSourceZoneOwnerSide),"onDestroyedRequestId":$($c.onDestroyedRequestId),"destroyerInstanceId":$($c.destroyerInstanceId),"nonEffectDestroy":$($c.nonEffectDestroy)}
"@).Trim()
    }
    return "{`"unitChanges`":[$($parts -join ',')]}"
}

function Report([string]$name, [string]$json) {
    $bytes = Get-Utf8Bytes $json
    $ok = if ($bytes -le $Limit) { "OK" } else { "OVER" }
    $color = if ($bytes -le $Limit) { "Green" } else { "Red" }
    Write-Host ("{0,-40} {1,5} B  [{2}] (limit {3})" -f $name, $bytes, $ok, $Limit) -ForegroundColor $color
    if ($bytes -gt $Limit) { Write-Host "  preview: $($json.Substring(0, [Math]::Min(200, $json.Length)))..." }
    return $bytes
}

Write-Host "=== EOS P2P message size survey (turn end / rest scenario) ===" -ForegroundColor Cyan
Write-Host ""

# EndTurn
Report "EndTurn" '{"type":"EndTurn"}'

# OnActionBegin
$onActionBeginInner = '{"action":"OnActionBegin","requestId":12,"actingZoneSide":1,"onActionContext":"turn end:enemy-action","attackerInstanceId":0,"actionStepSessionId":3}'
Report "OnActionBegin (turn end)" (Wrap-LeanEnvelope "OnActionBegin" $onActionBeginInner)

# OnActionEnd
$onActionEndInner = '{"action":"OnActionEnd","requestId":12,"actingZoneSide":1,"actionStepPassKind":1,"sessionPlayerActionEnded":0,"sessionEnemyActionEnded":1,"resourceAfter":3,"exResourceAfter":0,"levelAfter":4}'
Report "OnActionEnd (ActionEnd)" (Wrap-LeanEnvelope "OnActionEnd" $onActionEndInner)

# ResourceState
$resInner = '{"action":"ResourceState","resourceAfter":3,"exResourceAfter":0,"levelAfter":4,"actingZoneSide":0}'
Report "ResourceState" (Wrap-LeanEnvelope "ResourceState" $resInner)

Write-Host ""
Write-Host "--- EffectSync (Rest) ---" -ForegroundColor Yellow

$rest1 = Make-EffectSyncPayload @( (Make-RestChange) )
Report "EffectSync 1x Rest" (Wrap-LeanEnvelope "EffectSync" $rest1)

# 複数 REST を1パケットに詰めた場合（攻撃後複数ユニット等）
$changes = @()
1..10 | ForEach-Object { $changes += Make-RestChange -instanceId $_ -zoneIndex ($_ - 1) }
$rest10 = Make-EffectSyncPayload $changes
Report "EffectSync 10x Rest (same packet)" (Wrap-LeanEnvelope "EffectSync" $rest10)

# RefreshOwnerTurnFieldPassives（ターン終了時）
$refreshInner = Make-EffectSyncPayload @(@{ targetInstanceId=0; targetZoneOwnerSide=0; targetCardId=-1; targetZoneIndex=-1; changeKind="RefreshOwnerTurnFieldPassives"; hpAfter=0; signedStatValue=0; statTarget=0; duration=0; statModifierSourceKey=""; grantSourceInstanceId=0; grantSourceZoneOwnerSide=0; onDestroyedRequestId=0; destroyerInstanceId=0; nonEffectDestroy=0 })
Report "EffectSync RefreshOwnerTurnFieldPassives" (Wrap-LeanEnvelope "EffectSync" $refreshInner)

Write-Host ""
Write-Host "--- EffectSync chunk simulation (code limit=$ChunkLimit B) ---" -ForegroundColor Yellow

$allChanges = @()
1..20 | ForEach-Object { $allChanges += Make-RestChange -instanceId (1000 + $_) -zoneIndex ($_ - 1) }

$cursor = 0
$chunkIndex = 0
while ($cursor -lt $allChanges.Count) {
    $chunkCount = 0
    $messageJson = $null
    while (($cursor + $chunkCount) -lt $allChanges.Count) {
        $nextCount = $chunkCount + 1
        $slice = $allChanges[$cursor..($cursor + $nextCount - 1)]
        $effectJson = Make-EffectSyncPayload $slice
        $candidate = Wrap-LeanEnvelope "EffectSync" $effectJson
        $utf8 = Get-Utf8Bytes $candidate
        if ($chunkCount -gt 0 -and $utf8 -gt $ChunkLimit) { break }
        $messageJson = $candidate
        $chunkCount = $nextCount
        if ($utf8 -gt $ChunkLimit) { break }
    }
    if ($chunkCount -le 0) { Write-Host "ChunkFail at cursor=$cursor" -ForegroundColor Red; break }
    $bytes = Get-Utf8Bytes $messageJson
    Report "EffectSync chunk #$chunkIndex ($chunkCount changes)" $messageJson
    $cursor += $chunkCount
    $chunkIndex++
}

Write-Host ""
Write-Host "--- Worst case: Stat modifier with long sourceKey ---" -ForegroundColor Yellow
$longKey = "owner_turn_field_" + ("x" * 80)
$statInner = Make-EffectSyncPayload @(@{
    targetInstanceId = 42; targetZoneOwnerSide = 0; targetCardId = 17; targetZoneIndex = 0
    changeKind = "Stat"; hpAfter = 5; signedStatValue = 1; statTarget = 1; duration = 2
    statModifierSourceKey = $longKey
    grantSourceInstanceId = 0; grantSourceZoneOwnerSide = 0; onDestroyedRequestId = 0
    destroyerInstanceId = 0; nonEffectDestroy = 0
})
Report "EffectSync 1x Stat (long sourceKey)" (Wrap-LeanEnvelope "EffectSync" $statInner)

Write-Host ""
Write-Host "Note: 1174 KB = ~1.2MB would be catastrophic; EOS limit is ~1170 BYTES per UDP packet." -ForegroundColor Gray
Write-Host "Check game logs for: [EosP2P] SendPacket failed | [OnlineBattle] P2P send failed | [EffectSync][ChunkOversized]" -ForegroundColor Gray
