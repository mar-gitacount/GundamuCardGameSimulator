using System.Collections.Generic;
using UnityEngine;

/// <summary>トラッシュ／除外ゾーンのオンライン同期。</summary>
public partial class BattleGameMain
{
    private bool _applyingRemoteZoneMutation;
    private int _zoneSyncSuppressDepth;
    private bool _onlineZoneSyncObserversRegistered;

    private bool ShouldEmitZoneSync()
    {
        return IsOnlineBattle()
            && !_applyingRemoteBattleAction
            && !_applyingRemoteZoneMutation
            && _zoneSyncSuppressDepth == 0;
    }

    private void WithZoneSyncSuppressed(System.Action action)
    {
        PushZoneSyncSuppress();
        try
        {
            action?.Invoke();
        }
        finally
        {
            PopZoneSyncSuppress();
        }
    }

    /// <summary>コルーチン全体など、yield を挟む区間で ZoneSync を抑止する。</summary>
    private void PushZoneSyncSuppress()
    {
        _zoneSyncSuppressDepth++;
    }

    private void PopZoneSyncSuppress()
    {
        _zoneSyncSuppressDepth--;
        if (_zoneSyncSuppressDepth < 0)
        {
            _zoneSyncSuppressDepth = 0;
        }
    }

    private void ResetOnlineZoneSyncState()
    {
        _applyingRemoteZoneMutation = false;
        _zoneSyncSuppressDepth = 0;
        UnregisterOnlineZoneSyncObservers();
    }

    private void RegisterOnlineZoneSyncObservers()
    {
        if (_onlineZoneSyncObserversRegistered || !IsOnlineBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.OnCardAddedToTrash += OnLocalPlayerZoneTrashAdded;
            cardGameRule.OnCardAddedToExile += OnLocalPlayerZoneExileAdded;
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.OnCardAddedToTrash += OnLocalEnemyZoneTrashAdded;
            enemyCardGameRule.OnCardAddedToExile += OnLocalEnemyZoneExileAdded;
        }

        _onlineZoneSyncObserversRegistered = true;
    }

    private void UnregisterOnlineZoneSyncObservers()
    {
        if (!_onlineZoneSyncObserversRegistered)
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.OnCardAddedToTrash -= OnLocalPlayerZoneTrashAdded;
            cardGameRule.OnCardAddedToExile -= OnLocalPlayerZoneExileAdded;
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.OnCardAddedToTrash -= OnLocalEnemyZoneTrashAdded;
            enemyCardGameRule.OnCardAddedToExile -= OnLocalEnemyZoneExileAdded;
        }

        _onlineZoneSyncObserversRegistered = false;
    }

    private void OnLocalPlayerZoneTrashAdded(int cardId)
    {
        NotifyLocalZoneAddTrash(PlayerType.Player, cardId);
    }

    private void OnLocalEnemyZoneTrashAdded(int cardId)
    {
        NotifyLocalZoneAddTrash(PlayerType.Enemy, cardId);
    }

    private void OnLocalPlayerZoneExileAdded(int cardId)
    {
        // 山札→除外・トラッシュ→除外はバッチ送信するため、単体の AddExile は送らない。
    }

    private void OnLocalEnemyZoneExileAdded(int cardId)
    {
    }

    private void SendOnlineZoneSync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateZoneSync(json));
    }

    private void NotifyLocalZoneAddTrash(PlayerType zoneOwner, int cardId)
    {
        if (!ShouldEmitZoneSync() || cardId < 0)
        {
            return;
        }

        string json = OnlineBattleZoneSyncPayload.ToJsonSingleCard(
            OnlineBattleZoneSyncPayload.AddTrash,
            (int)zoneOwner,
            cardId);
        SendOnlineZoneSync(json);
        Debug.Log($"[OnlineBattle] Zone sync sent AddTrash owner={zoneOwner} cardId={cardId}");
    }

    private void NotifyLocalZoneDeckToTrash(PlayerType zoneOwner, IReadOnlyList<int> cardIds, int deckRemain)
    {
        if (!ShouldEmitZoneSync() || cardIds == null || cardIds.Count == 0)
        {
            return;
        }

        int[] ids = new int[cardIds.Count];
        for (int i = 0; i < cardIds.Count; i++)
        {
            ids[i] = cardIds[i];
        }

        string json = OnlineBattleZoneSyncPayload.ToJson(
            OnlineBattleZoneSyncPayload.DeckToTrash,
            (int)zoneOwner,
            ids,
            deckRemain);
        SendOnlineZoneSync(json);
        Debug.Log(
            $"[OnlineBattle] Zone sync sent DeckToTrash owner={zoneOwner} count={ids.Length} deck={deckRemain}");
    }

    private void NotifyLocalZoneDeckToExile(PlayerType zoneOwner, IReadOnlyList<int> cardIds, int deckRemain)
    {
        if (!ShouldEmitZoneSync() || cardIds == null || cardIds.Count == 0)
        {
            return;
        }

        int[] ids = new int[cardIds.Count];
        for (int i = 0; i < cardIds.Count; i++)
        {
            ids[i] = cardIds[i];
        }

        string json = OnlineBattleZoneSyncPayload.ToJson(
            OnlineBattleZoneSyncPayload.DeckToExile,
            (int)zoneOwner,
            ids,
            deckRemain);
        SendOnlineZoneSync(json);
        Debug.Log(
            $"[OnlineBattle] Zone sync sent DeckToExile owner={zoneOwner} count={ids.Length} deck={deckRemain}");
    }

    private void NotifyLocalZoneTrashToExile(PlayerType zoneOwner, int cardId)
    {
        if (!ShouldEmitZoneSync() || cardId < 0)
        {
            return;
        }

        string json = OnlineBattleZoneSyncPayload.ToJsonSingleCard(
            OnlineBattleZoneSyncPayload.TrashToExile,
            (int)zoneOwner,
            cardId);
        SendOnlineZoneSync(json);
        Debug.Log($"[OnlineBattle] Zone sync sent TrashToExile owner={zoneOwner} cardId={cardId}");
    }

    private void HandleRemoteZoneSync(string payload)
    {
        if (!IsOnlineBattle())
        {
            return;
        }

        if (!OnlineBattleZoneSyncPayload.TryParse(payload, out OnlineBattleZoneSyncPayload sync))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid ZoneSync payload: {payload}");
            return;
        }

        PlayerType senderZoneOwner = (PlayerType)sync.zoneOwnerSide;
        CardGameRule rule = ResolveRemoteMirrorZoneRule(senderZoneOwner);
        PlayerType mirroredOwner = ResolveRemoteMirrorZoneOwner(senderZoneOwner);
        if (rule == null)
        {
            return;
        }

        _applyingRemoteZoneMutation = true;
        try
        {
            switch (sync.mutation)
            {
                case OnlineBattleZoneSyncPayload.DeckToTrash:
                    ApplyRemoteDeckToTrash(rule, mirroredOwner, sync.cardIds, sync.deckRemainCount);
                    break;
                case OnlineBattleZoneSyncPayload.DeckToExile:
                    ApplyRemoteDeckToExile(rule, mirroredOwner, sync.cardIds, sync.deckRemainCount);
                    break;
                case OnlineBattleZoneSyncPayload.TrashToExile:
                    ApplyRemoteTrashToExile(rule, sync.cardIds);
                    break;
                case OnlineBattleZoneSyncPayload.AddTrash:
                    ApplyRemoteAddTrash(rule, sync.cardIds);
                    break;
                default:
                    Debug.LogWarning($"[OnlineBattle] Unknown ZoneSync mutation: {sync.mutation}");
                    break;
            }
        }
        finally
        {
            _applyingRemoteZoneMutation = false;
        }
    }

    /// <summary>送信側 Player ゾーン → 受信側 Enemy ゾーン（相手視点の反転）。</summary>
    private static CardGameRule ResolveRemoteMirrorZoneRule(PlayerType senderZoneOwner, BattleGameMain host)
    {
        if (host == null)
        {
            return null;
        }

        return senderZoneOwner == PlayerType.Player ? host.enemyCardGameRule : host.cardGameRule;
    }

    private CardGameRule ResolveRemoteMirrorZoneRule(PlayerType senderZoneOwner)
    {
        return ResolveRemoteMirrorZoneRule(senderZoneOwner, this);
    }

    private static PlayerType ResolveRemoteMirrorZoneOwner(PlayerType senderZoneOwner)
    {
        return senderZoneOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
    }

    private void ApplyRemoteDeckToTrash(
        CardGameRule rule,
        PlayerType zoneOwner,
        int[] cardIds,
        int deckRemain)
    {
        if (rule == null || cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Length; i++)
        {
            int expectedId = cardIds[i];
            if (expectedId < 0)
            {
                continue;
            }

            if (!TryTakeExpectedCardFromDeckTop(rule, expectedId))
            {
                Debug.LogWarning(
                    $"[OnlineBattle] DeckToTrash mismatch at index {i}. expected={expectedId}");
            }

            rule.AddCardToTrash(expectedId);
        }

        FinalizeRemoteDeckRemain(rule, zoneOwner, deckRemain);
        Debug.Log($"[OnlineBattle] Remote DeckToTrash applied. count={cardIds.Length} deck={deckRemain}");
    }

    private void ApplyRemoteDeckToExile(
        CardGameRule rule,
        PlayerType zoneOwner,
        int[] cardIds,
        int deckRemain)
    {
        if (rule == null || cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Length; i++)
        {
            int expectedId = cardIds[i];
            if (expectedId < 0)
            {
                continue;
            }

            if (!TryTakeExpectedCardFromDeckTop(rule, expectedId))
            {
                Debug.LogWarning(
                    $"[OnlineBattle] DeckToExile mismatch at index {i}. expected={expectedId}");
            }

            rule.AddCardToExile(expectedId);
        }

        FinalizeRemoteDeckRemain(rule, zoneOwner, deckRemain);
        Debug.Log($"[OnlineBattle] Remote DeckToExile applied. count={cardIds.Length} deck={deckRemain}");
    }

    private static bool TryTakeExpectedCardFromDeckTop(CardGameRule rule, int expectedId)
    {
        if (rule == null || expectedId < 0)
        {
            return false;
        }

        System.Collections.Generic.List<int> top = rule.PeekTopCardIds(1);
        if (top.Count > 0 && top[0] == expectedId)
        {
            return rule.TryTakeCardAtDeckIndex(0, out _);
        }

        return rule.TryTakeCardById(expectedId, out _);
    }

    private void ApplyRemoteTrashToExile(CardGameRule rule, int[] cardIds)
    {
        if (rule == null || cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Length; i++)
        {
            int cardId = cardIds[i];
            if (cardId < 0)
            {
                continue;
            }

            if (!rule.TryRemoveCardFromTrash(cardId, out int removedId))
            {
                Debug.LogWarning($"[OnlineBattle] TrashToExile: card not in trash id={cardId}");
                removedId = cardId;
            }

            rule.AddCardToExile(removedId);
        }

        Debug.Log($"[OnlineBattle] Remote TrashToExile applied. count={cardIds.Length}");
    }

    private void ApplyRemoteAddTrash(CardGameRule rule, int[] cardIds)
    {
        if (rule == null || cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Length; i++)
        {
            int cardId = cardIds[i];
            if (cardId >= 0)
            {
                rule.AddCardToTrash(cardId);
            }
        }

        Debug.Log($"[OnlineBattle] Remote AddTrash applied. count={cardIds.Length}");
    }

    private void FinalizeRemoteDeckRemain(CardGameRule rule, PlayerType zoneOwner, int deckRemain)
    {
        if (rule == null || deckRemain < 0)
        {
            return;
        }

        rule.SetDeckRemainCount(deckRemain);
        SyncGundamRuleDeckCount(zoneOwner, deckRemain);
    }
}
