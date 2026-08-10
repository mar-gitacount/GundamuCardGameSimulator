using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ベースカードの配備（シールドゾーン EX 枠）とシールド攻撃時のベース HP 処理。</summary>
public partial class BattleGameMain
{
    /// <summary>オンライン同期用。直近の配備ベース HP 変化（-1=なし、0=破壊、1+=現在HP）。</summary>
    private int _pendingDefenderDeployedBaseHpForOnlineSync = -1;

    /// <summary>
    /// true のとき効果ダメージ／突破の防御領域 Notify を抑止する。
    /// エフェクトバトル撃破時は UnitAttack にスナップショットを同梱するため二重送信を避ける。
    /// </summary>
    private bool _suppressOnlineDefenderAreaStateNotify;

    private void ClearPendingDefenderDeployedBaseHpForOnlineSync()
    {
        _pendingDefenderDeployedBaseHpForOnlineSync = -1;
    }

    private void MarkPendingDefenderDeployedBaseHpForOnlineSync(int hpAfter)
    {
        _pendingDefenderDeployedBaseHpForOnlineSync = Mathf.Max(0, hpAfter);
    }

    private int ConsumePendingDefenderDeployedBaseHpForOnlineSync()
    {
        int value = _pendingDefenderDeployedBaseHpForOnlineSync;
        _pendingDefenderDeployedBaseHpForOnlineSync = -1;
        return value;
    }

    private int ResolveOnlineSyncDeployedBaseHp(Gundam2024RuleScript.PlayerSide defenderSide)
    {
        CardController baseCard = GetDeployedBaseForRuleSide(defenderSide);
        if (baseCard != null && baseCard.Data != null)
        {
            return Mathf.Max(0, baseCard.CurrentHp);
        }

        return -1;
    }

    private void RegisterBaseProtectionCallbacks()
    {
        if (gundamRule == null)
        {
            return;
        }

        gundamRule.HasActiveDeployedBase = HasActiveDeployedBaseForRuleSide;
    }

    private CardGameRule GetCardRuleForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        return side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
    }

    private CardController GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule rule = GetCardRuleForRuleSide(side);
        if (rule == null)
        {
            return null;
        }

        if (rule.DeployedBase != null)
        {
            return rule.DeployedBase;
        }

        if (rule.BaseSlotContent == null)
        {
            return null;
        }

        for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
        {
            CardController occupant = rule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            if (occupant != null && occupant.Data != null && occupant.Data.type == Type.Base && occupant.CurrentHp > 0)
            {
                return occupant;
            }
        }

        return null;
    }

    private bool HasActiveDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        CardController baseCard = GetDeployedBaseForRuleSide(side);
        return baseCard != null && baseCard.Data != null && baseCard.CurrentHp > 0;
    }

    private bool IsCardInBaseSlot(CardController card)
    {
        if (card == null)
        {
            return false;
        }

        return (cardGameRule.BaseSlotContent != null && card.transform.IsChildOf(cardGameRule.BaseSlotContent))
            || (enemyCardGameRule.BaseSlotContent != null && card.transform.IsChildOf(enemyCardGameRule.BaseSlotContent));
    }

    private const string DeployedBaseHpOverlayName = "BaseHpOverlay";

    private void RefreshDeployedBaseHpOverlay(CardController baseCard)
    {
        if (baseCard == null || baseCard.Data == null)
        {
            return;
        }

        Transform existing = baseCard.transform.Find(DeployedBaseHpOverlayName);
        TextMeshProUGUI hpText;
        if (existing == null)
        {
            GameObject overlayRoot = new GameObject(DeployedBaseHpOverlayName, typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(baseCard.transform, false);
            overlayRoot.transform.SetAsLastSibling();
            RectTransform overlayRt = overlayRoot.GetComponent<RectTransform>();
            overlayRt.anchorMin = new Vector2(0f, 1f);
            overlayRt.anchorMax = new Vector2(1f, 1f);
            overlayRt.pivot = new Vector2(0.5f, 1f);
            overlayRt.sizeDelta = new Vector2(0f, 20f);
            overlayRt.anchoredPosition = Vector2.zero;
            Image bg = overlayRoot.GetComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            bg.raycastTarget = false;

            hpText = overlayRoot.CreateChildTextCustom("BaseHpText", UIAnchor.FullSize, 58, 20);
            hpText.fontSize = 14;
            hpText.fontStyle = FontStyles.Bold;
            hpText.color = Color.white;
            hpText.alignment = TextAlignmentOptions.Center;
            hpText.enableWordWrapping = false;
        }
        else
        {
            hpText = existing.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (hpText != null)
        {
            hpText.text = $"HP {baseCard.CurrentHp}";
        }
    }

    private void SyncBaseZoneHeaderDisplay(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule rule = GetCardRuleForRuleSide(side);
        if (rule == null)
        {
            return;
        }

        CardController baseCard = rule.DeployedBase;
        if (baseCard != null && baseCard.Data != null)
        {
            RefreshDeployedBaseHpOverlay(baseCard);
            return;
        }

        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        rule.SetExBaseDisplay(state.exBase);
    }

    /// <summary>手札からの配備用。CanPlayCard とリソース消費をまとめて行う（バースト配備は使わない）。</summary>
    private bool TryPayHandDeployCost(Gundam2024RuleScript.PlayerSide side, CardController card, int exToUse = 0)
    {
        if (card == null || card.Data == null || gundamRule == null)
        {
            return false;
        }

        int requiredLevel = card.CurrentLevel;
        int cost = card.CurrentCost;
        if (!gundamRule.CanPlayCard(side, requiredLevel, cost, exToUse))
        {
            Gundam2024RuleScript.PlayerState state = GetRuleState(side);
            Debug.Log(
                $"[DeployPay] Cannot play from hand card:{card.Data.cardName}(id:{card.Data.id}) "
                + $"lvReq:{requiredLevel} cost:{cost} exUse:{exToUse} side:{side} "
                + $"totalLv:{state.TotalLevel} resource:{state.resource} exRes:{state.exResource}");
            return false;
        }

        if (!gundamRule.TryConsumeResource(side, cost, exToUse, card.Data.id, requiredLevel))
        {
            return false;
        }

        AfterLocalResourceChanged(side);
        return true;
    }

    private void BeginDeployBaseFromHand(CardController cardController, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (cardController == null || cardController.Data == null || cardController.Data.type != Type.Base)
        {
            return;
        }

        DeployBaseFromHand(cardController, ownerType, ownerRule);
    }

    private void DeployBaseFromHand(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (DeployCardToBaseZone(cardController, ownerType, ownerRule, triggerOnPlayed: true))
        {
            Debug.Log(
                $"[BaseDeploy] {cardController.Data.cardName}(id:{cardController.Data.id}) side:{ownerType} HP:{cardController.CurrentHp} (shields unchanged)");
        }
    }

    private bool HadActiveBaseLayerBeforeDeploy(PlayerType ownerType, CardGameRule ownerRule)
    {
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        if (state.exBase > 0)
        {
            return true;
        }

        CardController deployed = ownerRule != null ? ownerRule.DeployedBase : null;
        if (deployed != null && deployed.Data != null && deployed.CurrentHp > 0)
        {
            return true;
        }

        return ownerRule != null && ownerRule.HasOccupantInBaseSlot();
    }

    /// <summary>EX ベース（数値）とベース枠のカードを破壊してから新ベースを置く。</summary>
    private void DestroyExistingBaseLayerBeforeDeploy(
        PlayerType ownerType,
        CardGameRule ownerRule,
        CardController incomingCard)
    {
        if (ownerRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        gundamRule.SetExBasePoints(ruleSide, 0);

        CardController registered = ownerRule.DeployedBase;
        if (registered != null && registered != incomingCard)
        {
            SendDeployedBaseToTrash(registered, ownerType, ownerRule);
        }

        if (ownerRule.BaseSlotContent == null)
        {
            return;
        }

        for (int i = ownerRule.BaseSlotContent.childCount - 1; i >= 0; i--)
        {
            CardController occupant = ownerRule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            if (occupant != null && occupant != incomingCard)
            {
                SendDeployedBaseToTrash(occupant, ownerType, ownerRule);
            }
        }
    }

    /// <summary>ベース配備前にシールドゾーン登録を外し、ゾーンからの昇格ならシールド枚数を1減らす。</summary>
    private void PrepareCardForBaseZoneDeploy(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ruleSide,
        ref bool wasInHand)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        cardController.SetEligibleForShieldZoneDeploy(false);

        bool inShieldZone = ownerRule.ShieldCardsContent != null
            && cardController.transform.IsChildOf(ownerRule.ShieldCardsContent);
        bool wasTrackedInShieldZone = ownerRule.TryUnregisterShieldZoneCard(cardController);

        // シールド破壊バースト経由は DamageShield で既に枚数減算済み。再減算すると OnBaseDeployed の
        // AddShieldToHand が盾0扱いになり失敗する。
        if (burstDeployBasePreferSourceCard)
        {
            return;
        }

        if (!wasInHand && (wasTrackedInShieldZone || inShieldZone))
        {
            if (!gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1))
            {
                Debug.LogWarning(
                    $"[BaseDeploy] Shield count could not be reduced when promoting {cardController.Data?.cardName} to base.");
            }
        }
    }

    /// <summary>ベースカードを EX ベース枠へ配備する共通処理。</summary>
    private bool DeployCardToBaseZone(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        bool triggerOnPlayed)
    {
        if (cardController == null || cardController.Data == null || cardController.Data.type != Type.Base || ownerRule == null)
        {
            return false;
        }

        // 既に正式登録済みのときだけスキップ（枠の子になっているだけでは未配備扱い）
        if (ownerRule.DeployedBase == cardController && IsCardInBaseSlot(cardController))
        {
            return true;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        bool replacingBaseLayer = HadActiveBaseLayerBeforeDeploy(ownerType, ownerRule);
        DestroyExistingBaseLayerBeforeDeploy(ownerType, ownerRule, cardController);

        bool wasInHand = ownerRule.HandScrollContent != null
            && cardController.transform.IsChildOf(ownerRule.HandScrollContent);
        PrepareCardForBaseZoneDeploy(cardController, ownerType, ownerRule, ruleSide, ref wasInHand);
        if (ownerType == PlayerType.Player && wasInHand)
        {
            RecordEnemyAiObservedPlayerCardPlay(cardController, "DeployBase");
        }

        RemoveCardFromHandLists(cardController, ownerType);
        cardController.RevealShieldFace();
        ownerRule.AttachDeployedBaseCard(cardController);
        cardController.ResetRuntimeStatsFromData();
        // Axis: 配備前に自分効果で自ユニットを破壊していれば、ここでアームしてメイン起動可能にする
        TryArmCardFromOwnEffectDestroyHistory(cardController, ownerType);
        RefreshDeployedBaseHpOverlay(cardController);

        if (triggerOnPlayed)
        {
            TriggerOnPlayedEffects(cardController, ownerType, RefreshAllHandsConditionalOnHandAuto);
        }

        int shieldZoneBeforeDeployEffects = ownerRule.GetShieldZoneCardCount();
        TriggerBaseDeployedEffects(cardController, ownerType, replacingBaseLayer);

        // バースト配備→【配備時】シールド手札：OnBaseDeployed が何らかの理由で取れなかった場合のフォールバック
        if (burstDeployBasePreferSourceCard
            && shieldZoneBeforeDeployEffects > 0
            && ownerRule.GetShieldZoneCardCount() == shieldZoneBeforeDeployEffects)
        {
            Debug.LogWarning(
                $"[BaseDeploy] OnBaseDeployed AddShield が未適用のため再試行 "
                + $"(card:{cardController.Data.cardName} zone:{shieldZoneBeforeDeployEffects})");
            TryMoveShieldFromZoneToHand(ownerRule, ownerType, ruleSide);
        }

        SyncResourceViewsFromRule(ruleSide);
        SyncBaseZoneHeaderDisplay(ruleSide);

        if (replacingBaseLayer)
        {
            Debug.Log(
                $"[BaseDeploy] Replaced base layer with {cardController.Data.cardName}(id:{cardController.Data.id}) side:{ownerType}");
        }

        NotifyLocalDeployBaseSynced(cardController, ownerType);
        return true;
    }

    private List<TrashExileCandidate> CollectTrashDeployBaseCandidates(CardGameRule trashRule, EffectData effect)
    {
        List<TrashExileCandidate> result = new List<TrashExileCandidate>();
        if (trashRule == null || effect == null)
        {
            return result;
        }

        IReadOnlyList<int> trashIds = trashRule.GetTrashCardIds();
        for (int i = 0; i < trashIds.Count; i++)
        {
            int cardId = trashIds[i];
            CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
            if (!effect.MatchesDeployBaseCandidateFilter(data))
            {
                continue;
            }

            result.Add(new TrashExileCandidate(i, cardId, data));
        }

        return result;
    }

    private bool TryDeployBaseFromTrashIndex(
        CardGameRule trashRule,
        PlayerType recipient,
        int trashIndex,
        CardData data,
        bool triggerOnPlayed)
    {
        if (trashRule == null)
        {
            return false;
        }

        int removedId = -1;
        WithZoneSyncSuppressed(() =>
        {
            if (!trashRule.TryRemoveCardFromTrashAt(trashIndex, out removedId))
            {
                removedId = -1;
            }
        });

        if (removedId < 0)
        {
            return false;
        }

        CardData resolved = data ?? DeckSettinObject.Instance.GetCardDataById(removedId);
        if (resolved == null || resolved.type != Type.Base)
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        CardGameRule deployRule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (deployRule == null || CardImagePrefab == null)
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        // ベース枠の子として生成すると DeployCardToBaseZone が「既に枠内」と誤判定するため、親なしで生成する
        GameObject go = Instantiate(CardImagePrefab);
        CardController spawned = go.GetComponent<CardController>();
        if (spawned == null)
        {
            Destroy(go);
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        spawned.SetUp(resolved, OnCardClicked);
        if (!DeployCardToBaseZone(spawned, recipient, deployRule, triggerOnPlayed))
        {
            Destroy(go);
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        // 手札へ誤配置されていないことを保証する
        if (deployRule.HandScrollContent != null
            && spawned.transform.IsChildOf(deployRule.HandScrollContent))
        {
            Debug.LogWarning(
                $"[DeployBase] trash deploy landed in hand — re-attaching to base zone. card:{resolved.cardName}");
            RemoveCardFromHandLists(spawned, recipient);
            if (!DeployCardToBaseZone(spawned, recipient, deployRule, triggerOnPlayed: false))
            {
                trashRule.AddCardToTrash(removedId);
                Destroy(go);
                return false;
            }
        }

        Debug.Log(
            $"[DeployBase] from trash {resolved.cardName}(id:{resolved.id}) → {recipient} base zone "
            + $"(registered:{deployRule.DeployedBase == spawned})");
        return true;
    }

    private void ApplyDeployBaseFromTrashAuto(
        CardGameRule trashRule,
        PlayerType recipient,
        List<TrashExileCandidate> candidates,
        int pickCount,
        bool triggerOnPlayed)
    {
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        int deployed = 0;
        for (int i = 0; i < ordered.Count && deployed < pickCount; i++)
        {
            TrashExileCandidate candidate = ordered[i];
            if (TryDeployBaseFromTrashIndex(
                trashRule,
                recipient,
                candidate.TrashIndex,
                candidate.Data,
                triggerOnPlayed))
            {
                deployed++;
            }
        }
    }

    private void ApplyDeployBaseFromTrashEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int magnitude,
        Action onComplete)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        if (trashRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType recipient = ResolveEffectOwnerPlayerType(ownerType, effect.target);
        List<TrashExileCandidate> candidates = CollectTrashDeployBaseCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[DeployBase] trash candidates empty (Neo Zeon Base etc.) by cardId:{sourceCard?.Data?.id ?? -1}");
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Max(1, magnitude);
        pickCount = Mathf.Min(pickCount, candidates.Count);

        if (ownerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            ApplyDeployBaseFromTrashAuto(
                trashRule,
                recipient,
                candidates,
                pickCount,
                effect.deployUnitTriggerOnPlayed);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        if (candidates.Count == 1 && pickCount == 1)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            TryDeployBaseFromTrashIndex(
                trashRule,
                recipient,
                candidates[0].TrashIndex,
                candidates[0].Data,
                effect.deployUnitTriggerOnPlayed);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowDeployBaseFromTrashSelectionCoroutine(
            trashRule,
            ownerType,
            recipient,
            effect,
            candidates,
            pickCount,
            onComplete));
    }

    private IEnumerator ShowDeployBaseFromTrashSelectionCoroutine(
        CardGameRule trashRule,
        PlayerType ownerType,
        PlayerType recipient,
        EffectData effect,
        List<TrashExileCandidate> candidates,
        int pickCount,
        Action onComplete)
    {
        if (trashRule == null || candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        int remaining = pickCount;
        HashSet<int> usedTrashIndices = new HashSet<int>();

        while (remaining > 0)
        {
            List<TrashExileCandidate> available = new List<TrashExileCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                TrashExileCandidate c = candidates[i];
                if (!usedTrashIndices.Contains(c.TrashIndex))
                {
                    available.Add(c);
                }
            }

            if (available.Count == 0)
            {
                break;
            }

            if (available.Count == 1)
            {
                TrashExileCandidate only = available[0];
                BeginOnlineEffectSyncBatch(ownerType);
                TryDeployBaseFromTrashIndex(
                    trashRule,
                    recipient,
                    only.TrashIndex,
                    only.Data,
                    effect != null && effect.deployUnitTriggerOnPlayed);
                FlushOnlineEffectSyncBatch();
                SyncAllResourceViewsFromRule();
                usedTrashIndices.Add(only.TrashIndex);
                remaining--;
                continue;
            }

            bool pickedThisRound = false;
            DestroyActiveOnActionPopupIfAny();
            GameObject root = new GameObject(
                "DeployBaseFromTrashSelect",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            activeOnActionPopupRoot = root;
            isOnActionPopupOpen = true;
            root.transform.SetParent(canvas.transform, false);
            root.transform.SetAsLastSibling();
            root.SetFullSize();
            Image dim = root.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);
            dim.raycastTarget = true;

            TextMeshProUGUI title = root.CreateChildTextCustom("DeployBaseTrashTitle", UIAnchor.TopCenter, 760, 48);
            title.text = remaining > 1
                ? $"トラッシュからベースを配備 ({remaining}枚)"
                : "トラッシュからベースを配備";
            title.fontSize = 26;
            title.fontStyle = FontStyles.Bold;
            title.color = Color.white;
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

            GameObject scrollGo = root.CreateGridScrollView(560, 360, UIAnchor.TopCenter);
            RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchoredPosition = new Vector2(0f, -72f);
            scrollGo.ConfigureGridCellFromViewportHeight(0.75f, 56f);
            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            RectTransform content = sr != null ? sr.content : null;

            if (content != null)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    TrashExileCandidate candidate = available[i];
                    CardData data = candidate.Data ?? DeckSettinObject.Instance.GetCardDataById(candidate.CardId);
                    if (data == null)
                    {
                        continue;
                    }

                    GameObject go = Instantiate(CardImagePrefab, content);
                    CardController display = go.GetComponent<CardController>();
                    if (display != null)
                    {
                        display.SetUp(data, _ => { });
                        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                    }

                    Button pickBtn = go.GetComponent<Button>();
                    if (pickBtn == null)
                    {
                        pickBtn = go.AddComponent<Button>();
                    }

                    int trashIndex = candidate.TrashIndex;
                    CardData pickData = data;
                    pickBtn.onClick.AddListener(() =>
                    {
                        if (pickedThisRound)
                        {
                            return;
                        }

                        pickedThisRound = true;
                        BeginOnlineEffectSyncBatch(ownerType);
                        TryDeployBaseFromTrashIndex(
                            trashRule,
                            recipient,
                            trashIndex,
                            pickData,
                            effect != null && effect.deployUnitTriggerOnPlayed);
                        FlushOnlineEffectSyncBatch();
                        SyncAllResourceViewsFromRule();
                        usedTrashIndices.Add(trashIndex);
                        remaining--;
                        DestroyActiveOnActionPopupIfAny();
                    });
                }
            }

            while (!pickedThisRound && isOnActionPopupOpen && root != null)
            {
                yield return null;
            }

            if (!pickedThisRound)
            {
                break;
            }
        }

        onComplete?.Invoke();
    }

    private void ApplyDeployBaseEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude,
        bool allowBurstSource = false,
        Action onComplete = null)
    {
        if (effect != null && effect.deployUnitSource == DeployUnitSource.Trash)
        {
            ApplyDeployBaseFromTrashEffect(sourceCard, sourceOwner, effect, magnitude, onComplete);
            return;
        }

        // バースト配備は常に破壊されたカードのオーナー側へ（旧 JSON の target=EnemyAllUnits でも自陣）
        PlayerType recipient = allowBurstSource
            ? sourceOwner
            : ResolveEffectOwnerPlayerType(sourceOwner, effect != null ? effect.target : TargetType.SelfPlayer);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        int applied = 0;
        int want = Mathf.Max(1, magnitude);
        for (int i = 0; i < want; i++)
        {
            if (allowBurstSource
                && sourceCard != null
                && recipient == sourceOwner
                && applied == 0
                && DeployCardToBaseZone(sourceCard, recipient, rule, triggerOnPlayed: false))
            {
                Debug.Log(
                    $"[DeployBase] burst source {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) → {recipient} base zone");
                applied++;
                continue;
            }

            if (applied == 0)
            {
                Debug.LogWarning($"[DeployBase] No deployable base for burst side:{recipient}");
            }

            break;
        }

        Debug.Log(
            $"[Effect] DeployBase x{applied}/{want} target:{(effect != null ? effect.target.ToString() : "?")} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
        onComplete?.Invoke();
    }

    private void RemoveCardFromHandLists(CardController cardController, PlayerType ownerType)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            playerHandCards.Remove(cardController.Data);
        }
        else
        {
            enemyHandCards.Remove(cardController.Data);
        }
    }

    private void SendDeployedBaseToTrash(CardController baseCard, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (baseCard == null)
        {
            return;
        }

        ownerRule.ClearDeployedBaseCard();
        SendCardToTrash(baseCard, ownerType);
    }

    private bool TryApplyEffectDamageToDeployedBase(
        Gundam2024RuleScript.PlayerSide targetSide,
        int baseMagnitude,
        out string logMessage,
        out bool destroyed)
    {
        logMessage = null;
        destroyed = false;
        if (baseMagnitude <= 0)
        {
            return false;
        }

        CardController defenderBase = GetDeployedBaseForRuleSide(targetSide);
        if (defenderBase == null || defenderBase.Data == null || defenderBase.CurrentHp <= 0)
        {
            return false;
        }

        // 先頭の配備ベースが効果ダメージ無効なら、ここでダメージ全体を消費する（EX/シールドへ抜けない）。
        // 戦闘ダメージ（シールド攻撃）は TryApplyShieldAttackDamageToDeployedBase 側で別途受ける。
        if (DoesCardIgnoreEffectDamage(defenderBase))
        {
            logMessage =
                $"[EffectDamage] Ignored by Base {defenderBase.Data.cardName} (EffectDamageImmunity) — no damage to EX/shield.";
            return true;
        }

        int amount = ResolveEffectDamageAmount(baseMagnitude, defenderBase);
        if (amount <= 0)
        {
            return false;
        }

        int hpBefore = defenderBase.CurrentHp;
        defenderBase.ApplyDamage(amount);
        MarkPendingDefenderDeployedBaseHpForOnlineSync(defenderBase.CurrentHp);
        PlayerType defenderOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardGameRule defenderRule = GetCardRuleForRuleSide(targetSide);

        logMessage =
            $"[EffectDamage] Dealt {amount} to Base {defenderBase.Data.cardName} HP:{hpBefore}→{defenderBase.CurrentHp}";

        SyncBaseZoneHeaderDisplay(targetSide);

        if (defenderBase.CurrentHp <= 0)
        {
            MarkPendingDefenderDeployedBaseHpForOnlineSync(0);
            SendDeployedBaseToTrash(defenderBase, defenderOwner, defenderRule);
            SyncResourceViewsFromRule(targetSide);
            logMessage += " (destroyed)";
            destroyed = true;
        }

        return true;
    }

    /// <summary>
    /// ランタイム付与、またはカード定義（自身への EffectDamageImmunity Buff）による効果ダメージ無効。
    /// </summary>
    private static bool DoesCardIgnoreEffectDamage(CardController card)
    {
        if (card == null)
        {
            return false;
        }

        if (card.HasEffectDamageImmunity)
        {
            return true;
        }

        return CardDataDeclaresSelfEffectDamageImmunity(card.Data);
    }

    /// <summary>
    /// Argama 等：OnBaseDeployed の自身 EffectDamageImmunity をカード定義から判定する。
    /// オンラインミラー配備で Buff 未付与でも同じ挙動にする。
    /// </summary>
    private static bool CardDataDeclaresSelfEffectDamageImmunity(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null)
                {
                    continue;
                }

                if (effect.type == EffectType.Buff
                    && effect.statTarget == EffectStatTarget.EffectDamageImmunity
                    && effect.target == TargetType.Self)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 効果ダメージによるプレイヤー領域へのダメージ。
    /// 配備ベース → EXベース（いずれも value 分）→ シールド1枚のみの順。戦闘交換ダメージとは別経路。
    /// 先頭の配備ベースが効果ダメージ無効ならダメージをそこで消費し、後ろへは抜けない。
    /// baseMagnitude は生の効果量。配備ベースは自身の修飾のみ適用、EX/シールドは修飾なし。
    /// sourceUnit があるとき、シールドエリアのカード破壊で OnOpponentShieldAreaCardDestroyed を発火する。
    /// </summary>
    private void ApplyEffectDamageToPlayerArea(
        Gundam2024RuleScript.PlayerSide targetSide,
        int baseMagnitude,
        CardController sourceUnit = null)
    {
        if (baseMagnitude <= 0 || gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState target = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int shieldBefore = target != null ? target.shield : 0;
        int exBaseBefore = target != null ? target.exBase : 0;
        bool destroyedShieldAreaCard = false;

        if (TryApplyEffectDamageToDeployedBase(targetSide, baseMagnitude, out string baseLog, out bool baseDestroyed))
        {
            Debug.Log(baseLog);
            destroyedShieldAreaCard = baseDestroyed;
            SyncResourceViewsFromRule(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
            TryNotifyOpponentShieldAreaCardDestroyedFromEffectDamage(sourceUnit, destroyedShieldAreaCard);
            return;
        }

        int exDamage = ResolveEffectDamageAmount(baseMagnitude);
        if (target != null && target.exBase > 0 && exDamage > 0)
        {
            gundamRule.DamageExBaseOnly(targetSide, exDamage);
            Debug.Log($"[EffectDamage] Dealt {exDamage} to EX Base (now {target.exBase}).");
            destroyedShieldAreaCard = exBaseBefore > 0 && target.exBase <= 0;
            SyncResourceViewsFromRule(targetSide);
            SyncBaseZoneHeaderDisplay(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
            TryNotifyOpponentShieldAreaCardDestroyedFromEffectDamage(sourceUnit, destroyedShieldAreaCard);
            return;
        }

        // シールド攻撃フロー中はベース層（配備ベース/EX）消化後も実シールドを割らない。
        if (blockShieldFlowDuringShieldAttack && targetSide == blockedShieldFlowSide)
        {
            Debug.Log(
                $"[EffectDamage] Shield-attack flow — skipped shield break (side:{targetSide}, amount:{baseMagnitude}).");
            return;
        }

        if (target != null && target.shield > 0)
        {
            gundamRule.DamageShield(targetSide, 1, simultaneousReveal: false);
            Debug.Log($"[EffectDamage] Broke 1 shield (effect value:{baseMagnitude} does not multiply shield breaks).");
            destroyedShieldAreaCard = true;
            SyncResourceViewsFromRule(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
            TryNotifyOpponentShieldAreaCardDestroyedFromEffectDamage(sourceUnit, destroyedShieldAreaCard);
        }
    }

    private void TryNotifyOpponentShieldAreaCardDestroyedFromEffectDamage(
        CardController sourceUnit,
        bool destroyedShieldAreaCard)
    {
        if (!destroyedShieldAreaCard || sourceUnit == null || sourceUnit.Data == null || !sourceUnit.Data.IsUnitLike())
        {
            return;
        }

        PlayerType ownerType = ResolveCardOwner(sourceUnit.transform);
        StartCoroutine(WaitOnOpponentShieldAreaCardDestroyedCoroutine(sourceUnit, ownerType));
    }

    private static CardController ResolveUnitSourceForShieldAreaDamage(CardController sourceCard)
    {
        if (sourceCard == null || sourceCard.Data == null)
        {
            return null;
        }

        if (sourceCard.Data.IsUnitLike())
        {
            return sourceCard;
        }

        if (sourceCard.MountedUnit != null && sourceCard.MountedUnit.Data != null
            && sourceCard.MountedUnit.Data.IsUnitLike())
        {
            return sourceCard.MountedUnit;
        }

        return null;
    }

    private void TryNotifyOnlineDefenderAreaStateAfterEffectDamage(
        Gundam2024RuleScript.PlayerSide targetSide,
        int shieldBefore,
        int exBaseBefore)
    {
        if (_suppressOnlineDefenderAreaStateNotify)
        {
            return;
        }

        if (!IsOnlineBattle()
            || currentPlayerType != PlayerType.Player
            || _applyingRemoteBattleAction
            || targetSide != Gundam2024RuleScript.PlayerSide.Enemy)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState defender = gundamRule.Enemy;
        if (defender.shield == shieldBefore
            && defender.exBase == exBaseBefore
            && _pendingDefenderDeployedBaseHpForOnlineSync < 0)
        {
            return;
        }

        NotifyLocalDefenderAreaStateSync();
    }

    private bool TryApplyShieldAttackDamageToDeployedBase(
        CardController attacker,
        Gundam2024RuleScript.PlayerSide targetSide,
        int strikeAp,
        out string logMessage,
        out bool destroyedDeployedBase)
    {
        logMessage = null;
        destroyedDeployedBase = false;
        CardController defenderBase = GetDeployedBaseForRuleSide(targetSide);
        if (defenderBase == null || defenderBase.Data == null || defenderBase.CurrentHp <= 0)
        {
            return false;
        }

        int power = Mathf.Max(0, strikeAp);
        if (power <= 0)
        {
            return false;
        }

        int hpBefore = defenderBase.CurrentHp;
        defenderBase.ApplyDamage(power);
        MarkPendingDefenderDeployedBaseHpForOnlineSync(defenderBase.CurrentHp);
        PlayerType defenderOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardGameRule defenderRule = GetCardRuleForRuleSide(targetSide);

        logMessage =
            $"[Attack] Shield attack dealt {power} to Base {defenderBase.Data.cardName} HP:{hpBefore}→{defenderBase.CurrentHp}";

        SyncBaseZoneHeaderDisplay(targetSide);

        if (defenderBase.CurrentHp <= 0)
        {
            MarkPendingDefenderDeployedBaseHpForOnlineSync(0);
            SendDeployedBaseToTrash(defenderBase, defenderOwner, defenderRule);
            SyncResourceViewsFromRule(targetSide);
            logMessage += " (destroyed)";
            destroyedDeployedBase = true;
        }

        return true;
    }
}
