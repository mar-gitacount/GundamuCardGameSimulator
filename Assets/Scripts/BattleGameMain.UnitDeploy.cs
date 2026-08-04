using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>効果によるユニット配備（トークン／手札／トラッシュ）と OnAttack 非戦闘効果チェーン。</summary>
public partial class BattleGameMain
{
    private CardController _pendingOnAttackPreCombatResolvedAttacker;

    /// <summary>
    /// OnAttack の DiscardFromHand が Skip／枚数不足のとき、同攻撃内の ReturnUnitToDeckBottom を抑止する。
    /// </summary>
    private bool _suppressOnAttackReturnToDeckBottomAfterFailedDiscard;
    /// <summary>同一攻撃宣言内で OnAttack 非戦闘効果（GrantAttackFlag 等）を解決済みか。</summary>
    private CardController _onAttackPreCombatCompletedAttacker;

    private PlayerType ResolveDeployRecipientPlayerType(PlayerType sourceOwner, EffectData effect)
    {
        if (effect != null && effect.target == TargetType.EnemyPlayer)
        {
            return sourceOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return sourceOwner;
    }

    private CardGameRule ResolveDeployRecipientRule(PlayerType recipient)
    {
        return recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;
    }

    private List<CardController> CollectHandDeployCandidates(PlayerType owner, EffectData effect)
    {
        List<CardController> result = new List<CardController>();
        CardGameRule rule = owner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule?.HandScrollContent == null || effect == null)
        {
            return result;
        }

        for (int i = 0; i < rule.HandScrollContent.childCount; i++)
        {
            CardController cc = rule.HandScrollContent.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || !effect.MatchesDeployCandidateFilter(cc.Data))
            {
                continue;
            }

            // LV 等の実効ステータス条件（手札コントローラ基準）
            if (effect.HasTargetUnitFilter() && !effect.MatchesTargetUnitFilter(cc, null))
            {
                continue;
            }

            result.Add(cc);
        }

        return result;
    }

    private static string FormatDeployUnitFromHandSelectionTitle(EffectData effect)
    {
        if (effect == null)
        {
            return "手札から配備するユニットを選択";
        }

        string detail = effect.FormatTargetUnitFilterDescription();
        if (!string.IsNullOrEmpty(detail))
        {
            return $"手札から配備（{detail}）";
        }

        return "手札から配備するユニットを選択";
    }

    private List<TrashExileCandidate> CollectTrashDeployCandidates(CardGameRule trashRule, EffectData effect)
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
            if (!effect.MatchesDeployCandidateFilter(data))
            {
                continue;
            }

            result.Add(new TrashExileCandidate(i, cardId, data));
        }

        return result;
    }

    private CardController InstantiateBattleUnit(CardData data, Transform parent)
    {
        if (data == null || CardImagePrefab == null || parent == null)
        {
            return null;
        }

        GameObject go = Instantiate(CardImagePrefab, parent);
        CardController cc = go.GetComponent<CardController>();
        if (cc != null)
        {
            cc.SetUp(data, OnCardClicked);
        }

        return cc;
    }

    /// <summary>バトルゾーンへ配備（手札／トラッシュ／トークン共通のフィールド反映）。</summary>
    private bool DeployUnitToBattleZone(
        CardController unit,
        PlayerType recipient,
        CardGameRule rule,
        bool triggerOnPlayed,
        bool fromHand)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || rule == null)
        {
            return false;
        }

        unit.transform.SetParent(rule.PlayerDeployPanel, false);

        if (recipient == PlayerType.Player)
        {
            if (fromHand)
            {
                playerHandCards.Remove(unit.Data);
            }

            if (!playerBattleZoneCards.Contains(unit))
            {
                playerBattleZoneCards.Add(unit);
            }
        }
        else
        {
            if (fromHand)
            {
                enemyHandCards.Remove(unit.Data);
            }

            if (!enemyBattleZoneCards.Contains(unit))
            {
                enemyBattleZoneCards.Add(unit);
            }
        }

        unit.SetEligibleForShieldZoneDeploy(false);
        unit.ResetRuntimeStatsFromData();
        ApplyUnitDeployFieldAttackState(unit);
        AssignBattleInstanceIdIfNeeded(unit);
        ApplyPilotMountFieldAurasToDeployedUnit(unit, recipient);
        // トークン等の効果配置直後にも、ガンダム等の自ターン盤面バフをかけ直す
        RefreshAllFieldOwnerTurnPassives();

        if (triggerOnPlayed)
        {
            TriggerOnPlayedEffects(unit, recipient, RefreshAllHandsConditionalOnHandAuto);
        }

        if (IsOnlineBattle() && currentPlayerType == PlayerType.Player && !_applyingRemoteBattleAction)
        {
            NotifyLocalPlayCardDeployed(unit, recipient);
        }

        Debug.Log(
            $"[DeployUnit] {unit.Data.cardName}(id:{unit.Data.id}) → {recipient} battle zone "
            + $"(triggerOnPlayed:{triggerOnPlayed})");
        return true;
    }

    private bool TryDeployTokenUnit(
        EffectData effect,
        int resolvedMagnitude,
        PlayerType sourceOwner,
        CardController sourceCard)
    {
        if (effect == null || effect.deployUnitSource != DeployUnitSource.Token)
        {
            return false;
        }

        int cardId = effect.deployCardId;
        if (cardId <= 0 && sourceCard?.Data != null && sourceCard.Data.type == Type.UnitToken)
        {
            cardId = sourceCard.Data.id;
        }

        if (cardId <= 0)
        {
            Debug.LogWarning($"[DeployUnit] Token deploy skipped: deployCardId unset (source:{sourceCard?.Data?.id})");
            return false;
        }

        CardData tokenData = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (tokenData == null || !tokenData.IsUnitLike())
        {
            Debug.LogWarning($"[DeployUnit] Unknown or non-unit token id:{cardId}");
            return false;
        }

        PlayerType recipient = ResolveDeployRecipientPlayerType(sourceOwner, effect);
        CardGameRule rule = ResolveDeployRecipientRule(recipient);
        if (rule?.PlayerDeployPanel == null)
        {
            return false;
        }

        int deployCount = effect.GetDeployUnitCount(resolvedMagnitude);
        int applied = 0;
        BeginOnlineEffectSyncBatch(sourceOwner);
        for (int i = 0; i < deployCount; i++)
        {
            CardController spawned = InstantiateBattleUnit(tokenData, rule.PlayerDeployPanel);
            if (spawned == null)
            {
                break;
            }

            if (DeployUnitToBattleZone(spawned, recipient, rule, effect.deployUnitTriggerOnPlayed, fromHand: false))
            {
                applied++;
            }
        }

        FlushOnlineEffectSyncBatch();
        Debug.Log(
            $"[Effect] DeployUnit Token x{applied}/{deployCount} id:{cardId} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id}");
        return applied > 0;
    }

    private bool TryDeployUnitFromTrashIndex(
        CardGameRule trashRule,
        PlayerType trashOwner,
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
        if (resolved == null || !resolved.IsUnitLike())
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        CardGameRule deployRule = ResolveDeployRecipientRule(recipient);
        CardController spawned = InstantiateBattleUnit(resolved, deployRule.PlayerDeployPanel);
        if (spawned == null)
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        return DeployUnitToBattleZone(spawned, recipient, deployRule, triggerOnPlayed, fromHand: false);
    }

    private void ApplyDeployUnitFromTrashAuto(
        CardGameRule trashRule,
        PlayerType trashOwner,
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
            if (TryDeployUnitFromTrashIndex(
                trashRule,
                trashOwner,
                recipient,
                candidate.TrashIndex,
                candidate.Data,
                triggerOnPlayed))
            {
                deployed++;
            }
        }
    }

    private void ApplyDeployUnitEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        if (effect == null || effect.type != EffectType.DeployUnit)
        {
            onComplete?.Invoke();
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        int deployCount = effect.GetDeployUnitCount(magnitude);
        if (deployCount <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        switch (effect.deployUnitSource)
        {
            case DeployUnitSource.Token:
                BeginOnlineEffectSyncBatch(ownerType);
                TryDeployTokenUnit(effect, magnitude, ownerType, sourceCard);
                FlushOnlineEffectSyncBatch();
                SyncAllResourceViewsFromRule();
                onComplete?.Invoke();
                return;

            case DeployUnitSource.Hand:
                ApplyDeployUnitFromHandEffect(sourceCard, ownerType, effect, deployCount, onComplete);
                return;

            case DeployUnitSource.Trash:
                ApplyDeployUnitFromTrashEffect(sourceCard, ownerType, effect, deployCount, onComplete);
                return;

            default:
                onComplete?.Invoke();
                return;
        }
    }

    private void ApplyDeployUnitFromHandEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int deployCount,
        Action onComplete)
    {
        PlayerType recipient = ResolveDeployRecipientPlayerType(ownerType, effect);
        List<CardController> candidates = CollectHandDeployCandidates(recipient, effect);
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            int deployed = 0;
            CardGameRule rule = ResolveDeployRecipientRule(recipient);
            for (int i = 0; i < candidates.Count && deployed < deployCount; i++)
            {
                CardController pick = candidates[i];
                if (DeployUnitToBattleZone(pick, recipient, rule, effect.deployUnitTriggerOnPlayed, fromHand: true))
                {
                    deployed++;
                }
            }

            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        if (!effect.RequiresDeployUnitZoneSelection() && candidates.Count == 1)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            DeployUnitToBattleZone(
                candidates[0],
                recipient,
                ResolveDeployRecipientRule(recipient),
                effect.deployUnitTriggerOnPlayed,
                fromHand: true);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowDeployUnitFromHandSelectionCoroutine(
            sourceCard,
            ownerType,
            recipient,
            effect,
            candidates,
            deployCount,
            onComplete));
    }

    private void ApplyDeployUnitFromTrashEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int deployCount,
        Action onComplete)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        if (trashRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType trashOwner = effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        PlayerType recipient = ResolveDeployRecipientPlayerType(ownerType, effect);
        List<TrashExileCandidate> candidates = CollectTrashDeployCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Min(deployCount, candidates.Count);
        if (ownerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            ApplyDeployUnitFromTrashAuto(
                trashRule,
                trashOwner,
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
            TryDeployUnitFromTrashIndex(
                trashRule,
                trashOwner,
                recipient,
                candidates[0].TrashIndex,
                candidates[0].Data,
                effect.deployUnitTriggerOnPlayed);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowDeployUnitFromTrashSelectionCoroutine(
            sourceCard,
            trashRule,
            ownerType,
            trashOwner,
            recipient,
            effect,
            candidates,
            pickCount,
            onComplete));
    }

    private IEnumerator ShowDeployUnitFromHandSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType recipient,
        EffectData effect,
        List<CardController> candidates,
        int deployCount,
        Action onComplete)
    {
        if (candidates == null || candidates.Count == 0 || CardImagePrefab == null)
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

        bool resolved = false;
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "DeployUnitFromHandSelect",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("DeployHandTitle", UIAnchor.TopCenter, 760, 48);
        title.text = FormatDeployUnitFromHandSelectionTitle(effect);
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
            for (int i = 0; i < candidates.Count; i++)
            {
                CardController candidate = candidates[i];
                if (candidate?.Data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                CardController display = go.GetComponent<CardController>();
                if (display != null)
                {
                    display.SetUp(candidate.Data, _ => { });
                    go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                }

                Button pickBtn = go.GetComponent<Button>();
                if (pickBtn == null)
                {
                    pickBtn = go.AddComponent<Button>();
                }

                CardController pickedRef = candidate;
                pickBtn.onClick.AddListener(() =>
                {
                    if (resolved)
                    {
                        return;
                    }

                    resolved = true;
                    BeginOnlineEffectSyncBatch(ownerType);
                    DeployUnitToBattleZone(
                        pickedRef,
                        recipient,
                        ResolveDeployRecipientRule(recipient),
                        effect.deployUnitTriggerOnPlayed,
                        fromHand: true);
                    FlushOnlineEffectSyncBatch();
                    SyncAllResourceViewsFromRule();
                    Destroy(root);
                    activeOnActionPopupRoot = null;
                    isOnActionPopupOpen = false;
                    onComplete?.Invoke();
                });
            }
        }

        Button cancel = root.CreateChildButton("キャンセル");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 44f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        cancel.onClick.AddListener(() =>
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onComplete?.Invoke();
        });

        yield return new WaitUntil(() => resolved || root == null);
        if (!resolved)
        {
            onComplete?.Invoke();
        }
    }

    private IEnumerator ShowDeployUnitFromTrashSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        PlayerType ownerType,
        PlayerType trashOwner,
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
                TryDeployUnitFromTrashIndex(
                    trashRule,
                    trashOwner,
                    recipient,
                    only.TrashIndex,
                    only.Data,
                    effect.deployUnitTriggerOnPlayed);
                FlushOnlineEffectSyncBatch();
                SyncAllResourceViewsFromRule();
                usedTrashIndices.Add(only.TrashIndex);
                remaining--;
                continue;
            }

            bool pickedThisRound = false;
            DestroyActiveOnActionPopupIfAny();
            GameObject root = new GameObject(
                "DeployUnitFromTrashSelect",
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

            TextMeshProUGUI title = root.CreateChildTextCustom("DeployTrashTitle", UIAnchor.TopCenter, 760, 48);
            title.text = $"トラッシュから配備 ({remaining}枚)";
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
                    pickBtn.onClick.AddListener(() =>
                    {
                        if (pickedThisRound)
                        {
                            return;
                        }

                        pickedThisRound = true;
                        BeginOnlineEffectSyncBatch(ownerType);
                        TryDeployUnitFromTrashIndex(
                            trashRule,
                            trashOwner,
                            recipient,
                            trashIndex,
                            data,
                            effect.deployUnitTriggerOnPlayed);
                        FlushOnlineEffectSyncBatch();
                        SyncAllResourceViewsFromRule();
                        usedTrashIndices.Add(trashIndex);
                        remaining--;
                        Destroy(root);
                        activeOnActionPopupRoot = null;
                        isOnActionPopupOpen = false;
                    });
                }
            }

            Button skip = root.CreateChildButton("スキップ");
            RectTransform skipRt = skip.GetComponent<RectTransform>();
            skipRt.sizeDelta = new Vector2(160f, 44f);
            skipRt.anchorMin = new Vector2(0.5f, 0f);
            skipRt.anchorMax = new Vector2(0.5f, 0f);
            skipRt.pivot = new Vector2(0.5f, 0f);
            skipRt.anchoredPosition = new Vector2(0f, 36f);
            skip.onClick.AddListener(() =>
            {
                if (pickedThisRound)
                {
                    return;
                }

                pickedThisRound = true;
                remaining = 0;
                Destroy(root);
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
            });

            yield return new WaitUntil(() => pickedThisRound || root == null);
        }

        onComplete?.Invoke();
    }

    private static bool IsOnAttackNonCombatEffect(EffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (effect.type == EffectType.DeployUnit
            || effect.type == EffectType.ExileFromTrash
            || effect.type == EffectType.Draw
            || effect.type == EffectType.MillTopToTrash
            || effect.type == EffectType.ExileFromDeck
            || effect.type == EffectType.Look
            || effect.type == EffectType.ActivateMountedCardOnMain
            || effect.type == EffectType.ActivateResource
            || ((effect.type == EffectType.Damage)
                && (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)))
        {
            return true;
        }

        // GrantAttackFlag は TryOpenOnAttackAllyGrantAttackFlagSelection で攻撃前に解決。
        if (effect.type == EffectType.GrantAttackFlag)
        {
            return false;
        }

        return !effect.target.IsOpponentUnitTarget() && !effect.type.UsesTargetCountValue();
    }

    private static bool TimedBlockNeedsOnAttackPreCombatResolution(TimedEffectData timed)
    {
        if (timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            if (IsOnAttackNonCombatEffect(effects[i]))
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct OnAttackEffectSource
    {
        public OnAttackEffectSource(CardController source, PlayerType owner)
        {
            Source = source;
            Owner = owner;
        }

        public CardController Source { get; }
        public PlayerType Owner { get; }
    }

    private List<OnAttackEffectSource> BuildOnAttackEffectSources(CardController attacker, PlayerType attackerOwner)
    {
        List<OnAttackEffectSource> list = new List<OnAttackEffectSource>();
        if (attacker != null && attacker.Data != null)
        {
            list.Add(new OnAttackEffectSource(attacker, attackerOwner));
        }

        if (attacker?.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            list.Add(new OnAttackEffectSource(attacker.MountedPilot, attackerOwner));
        }

        return list;
    }

    private List<TimedEffectData> CollectOnAttackPreCombatBlocksForSource(
        CardController source,
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (source?.Data?.timedEffects == null || attacker == null)
        {
            return blocks;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = source.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!TimedBlockNeedsOnAttackPreCombatResolution(timed))
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(attackerOwner, source, i))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            blocks.Add(timed);
        }

        return blocks;
    }

    private List<TimedEffectData> CollectOnAttackPreCombatBlocks(
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<OnAttackEffectSource> sources = BuildOnAttackEffectSources(attacker, attackerOwner);
        for (int si = 0; si < sources.Count; si++)
        {
            blocks.AddRange(CollectOnAttackPreCombatBlocksForSource(
                sources[si].Source,
                attacker,
                attackerOwner));
        }

        return blocks;
    }

    /// <summary>攻撃宣言時に DeployUnit 等の非戦闘 OnAttack 効果を解決する。解決が必要なら true。</summary>
    private bool TryBeginOnAttackPreCombatEffectChain(
        CardController attacker,
        PlayerType attackerOwner,
        Action onResolved)
    {
        if (attacker == null)
        {
            return false;
        }

        _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = false;
        List<TimedEffectData> unitBlocks = CollectOnAttackPreCombatBlocksForSource(
            attacker,
            attacker,
            attackerOwner);
        CardController pilot = attacker.MountedPilot;
        List<TimedEffectData> pilotBlocks = CollectOnAttackPreCombatBlocksForSource(
            pilot,
            attacker,
            attackerOwner);
        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            return false;
        }

        Debug.Log(
            $"[OnAttackPreCombat] Start unitBlocks:{unitBlocks.Count} pilotBlocks:{pilotBlocks.Count} "
            + $"attacker:{attacker.Data?.cardName}(id:{attacker.Data?.id}) "
            + $"pilot:{pilot?.Data?.cardName ?? "none"}");

        _pendingOnAttackPreCombatResolvedAttacker = attacker;
        BeginEffectChainObservationScope();

        void FinishPreCombat()
        {
            EndEffectChainObservationScope();
            MarkOnAttackPreCombatEffectsApplied(attacker);
            _onAttackPreCombatCompletedAttacker = attacker;
            onResolved?.Invoke();
        }

        ResolveUnitPilotEffectOrder(
            attackerOwner,
            attacker,
            pilot,
            unitBlocks,
            pilotBlocks,
            attacker.Data,
            ordered =>
            {
                if (ordered == null || ordered.Count == 0)
                {
                    FinishPreCombat();
                    return;
                }

                RunOrderedOnAttackPreCombatEntries(
                    attacker,
                    attackerOwner,
                    ordered,
                    0,
                    FinishPreCombat);
            });
        return true;
    }

    private void RunOrderedOnAttackPreCombatEntries(
        CardController attacker,
        PlayerType attackerOwner,
        List<UnitPilotEffectOrderEntry> ordered,
        int index,
        Action onComplete)
    {
        if (ordered == null || index >= ordered.Count)
        {
            onComplete?.Invoke();
            return;
        }

        UnitPilotEffectOrderEntry entry = ordered[index];
        if (entry == null || entry.Source == null || entry.Blocks == null || entry.Blocks.Count == 0)
        {
            RunOrderedOnAttackPreCombatEntries(attacker, attackerOwner, ordered, index + 1, onComplete);
            return;
        }

        RunOnAttackPreCombatTimedBlocks(
            attacker,
            attackerOwner,
            entry.Blocks,
            0,
            () => RunOrderedOnAttackPreCombatEntries(
                attacker,
                attackerOwner,
                ordered,
                index + 1,
                onComplete));
    }

    private void RunOnAttackPreCombatTimedBlocks(
        CardController attacker,
        PlayerType attackerOwner,
        List<TimedEffectData> blocks,
        int blockIndex,
        Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        CardController source = ResolveOnAttackBlockSource(attacker, attackerOwner, block);
        if (source == null)
        {
            RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete);
            return;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        if (!CanRunTimedBlockAtChainTime(block, ctx, "OnAttack"))
        {
            RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete);
            return;
        }

        if (block.oncePerTurn)
        {
            int timedIndex = IndexOfTimedEffectOnCard(source, block);
            if (timedIndex >= 0)
            {
                MarkPaidActivationUsedThisTurn(attackerOwner, source, timedIndex);
            }
        }

        TryExecuteOnAttackPreCombatEffectChain(
            source,
            attackerOwner,
            block.GetResolvedEffects(),
            0,
            () => RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete));
    }

    private static int IndexOfTimedEffectOnCard(CardController source, TimedEffectData block)
    {
        if (source?.Data?.timedEffects == null || block == null)
        {
            return -1;
        }

        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            if (ReferenceEquals(source.Data.timedEffects[i], block))
            {
                return i;
            }
        }

        return -1;
    }

    private CardController ResolveOnAttackBlockSource(
        CardController attacker,
        PlayerType attackerOwner,
        TimedEffectData block)
    {
        if (attacker?.Data?.timedEffects != null && attacker.Data.timedEffects.Contains(block))
        {
            return attacker;
        }

        if (attacker?.MountedPilot?.Data?.timedEffects != null
            && attacker.MountedPilot.Data.timedEffects.Contains(block))
        {
            return attacker.MountedPilot;
        }

        return attacker;
    }

    private void TryExecuteOnAttackPreCombatEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int index,
        Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (!IsOnAttackNonCombatEffect(effect))
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        EffectActivationContext activationContext = BuildOnAttackActivationContext(
            ownerType,
            _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnAttackPreCombat"))
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            ApplyDeployUnitEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (effect.type == EffectType.ActivateMountedCardOnMain)
        {
            ApplyActivateMountedCardOnMain(
                sourceCard,
                ownerType,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            PlayerType handOwner = ResolveHandDiscardOwner(ownerType, effect);
            List<CardController> handCandidates = CollectSelectableHandCards(handOwner);
            if (handCandidates.Count == 0)
            {
                Debug.Log("[OnAttackPreCombat] 捨てる手札がありません (DiscardFromHand)。山札下送りを抑止。");
                _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = true;
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            TryExecuteManualHandSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                success =>
                {
                    if (!success)
                    {
                        _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = true;
                        Debug.Log(
                            "[OnAttackPreCombat] DiscardFromHand 未完了（Skip または枚数不足）。"
                            + " ReturnUnitToDeckBottom を抑止。");
                    }

                    TryExecuteOnAttackPreCombatEffectChain(
                        sourceCard,
                        ownerType,
                        effects,
                        index + 1,
                        onDone);
                });
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            CardController attackHost = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                attackHost,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
    }

    private void ClearOnAttackPreCombatResolvedState()
    {
        _pendingOnAttackPreCombatResolvedAttacker = null;
    }

    private void ClearOnAttackPreCombatCompletedForNewAttack()
    {
        _onAttackPreCombatCompletedAttacker = null;
    }

    private EntityId _onAttackPreCombatEffectsAppliedAttackerId;

    private void ResetOnAttackPreCombatEffectsAppliedGuard()
    {
        _onAttackPreCombatEffectsAppliedAttackerId = default;
    }

    private void MarkOnAttackPreCombatEffectsApplied(CardController attacker)
    {
        if (attacker != null)
        {
            _onAttackPreCombatEffectsAppliedAttackerId = attacker.GetEntityId();
        }
    }

    private bool HasOnAttackPreCombatEffectsBeenApplied(CardController attacker)
    {
        return attacker != null
            && _onAttackPreCombatEffectsAppliedAttackerId == attacker.GetEntityId();
    }

    /// <summary>
    /// Draw / DeployUnit 等の非戦闘 OnAttack 効果を同期的に適用（敵 AI や pre-combat チェーン未経由時のフォールバック）。
    /// </summary>
    private void ApplyOnAttackPreCombatEffectsImmediately(CardController attacker, PlayerType attackerOwner)
    {
        if (attacker == null || HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            return;
        }

        List<TimedEffectData> blocks = CollectOnAttackPreCombatBlocks(attacker, attackerOwner);
        if (blocks.Count == 0)
        {
            return;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        BeginEffectChainObservationScope();
        try
        {
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                TimedEffectData block = blocks[bi];
                CardController source = ResolveOnAttackBlockSource(attacker, attackerOwner, block);
                if (source == null || !CanRunTimedBlockAtChainTime(block, ctx, "OnAttackPreCombatSync"))
                {
                    continue;
                }

                IReadOnlyList<EffectData> effects = block.GetResolvedEffects();
                for (int ei = 0; ei < effects.Count; ei++)
                {
                    EffectData effect = effects[ei];
                    if (effect == null || !IsOnAttackNonCombatEffect(effect))
                    {
                        continue;
                    }

                    if (!ShouldApplyChainedEffect(effect, ctx, "OnAttackPreCombatSync"))
                    {
                        continue;
                    }

                    if (EffectRequiresManualUnitSelection(effect))
                    {
                        continue;
                    }

                    ApplyEffect(source, attackerOwner, effect);
                }
            }
        }
        finally
        {
            EndEffectChainObservationScope();
        }

        MarkOnAttackPreCombatEffectsApplied(attacker);
        Debug.Log(
            $"[OnAttackPreCombat] Sync applied blocks:{blocks.Count} attacker:{attacker.Data?.cardName}(id:{attacker.Data?.id})");
    }

    /// <summary>
    /// キラデバフと同様、TryUnitVsUnitAttack の前に OnAttack 効果 UI を解決する。
    /// 1) Draw 等の非戦闘 OnAttack → 2) GrantAttackFlag → 3) 敵ユニット向け OnAttack 効果。
    /// </summary>
    /// <returns>非同期 UI 表示中なら true（onResolved は UI 完了後に呼ばれる）。</returns>
    private bool TryOpenOnAttackEffectSelectionBeforeCombat(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        Action onResolved)
    {
        if (attacker == null)
        {
            onResolved?.Invoke();
            return false;
        }

        void AfterAllyGrantAttackFlag()
        {
            _onAttackPreCombatCompletedAttacker = attacker;
            if (TryOpenOnAttackEnemySelectionPanel(attacker, attackerOwner, attackedTarget, onResolved))
            {
                return;
            }

            onResolved?.Invoke();
        }

        void AfterPreCombatOnAttackChain()
        {
            if (TryOpenOnAttackAllyGrantAttackFlagSelection(attacker, attackerOwner, AfterAllyGrantAttackFlag))
            {
                return;
            }

            AfterAllyGrantAttackFlag();
        }

        if (_onAttackPreCombatCompletedAttacker == attacker)
        {
            AfterAllyGrantAttackFlag();
            return isOnActionPopupOpen;
        }

        if (HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            AfterPreCombatOnAttackChain();
            return isOnActionPopupOpen;
        }

        if (TryBeginOnAttackPreCombatEffectChain(attacker, attackerOwner, AfterPreCombatOnAttackChain))
        {
            return true;
        }

        AfterPreCombatOnAttackChain();
        return isOnActionPopupOpen;
    }

    /// <summary>攻撃対象確定後：OnAttack 効果 UI → ユニット戦へ。</summary>
    private void BeginUnitAttackAfterTargetDeclared(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner)
    {
        void ProceedUnitAttack()
        {
            // OnAttack 完了後はブロック→アクションの順で続行する
            ContinueUnitAttackAfterOnAttackEffects(attacker, attackerOwner, defender, skipOnActionPause: false);
        }

        if (TryOpenOnAttackEffectSelectionBeforeCombat(attacker, attackerOwner, defender, ProceedUnitAttack))
        {
            return;
        }

        ProceedUnitAttack();
    }

    private void ResumeUnitVsUnitAttackAfterOnAttackPreCombat(
        CardController attacker,
        PlayerType attackerOwner,
        CardController defender,
        bool skipOnActionPause,
        bool skipAttackedSidePanelPause)
    {
        if (attacker == null)
        {
            return;
        }

        if (pendingOnAttackEffectResolvedAttacker != attacker)
        {
            if (TryOpenOnAttackEnemySelectionPanel(
                attacker,
                attackerOwner,
                defender,
                () => ContinueUnitAttackAfterOnAttackEffects(attacker, attackerOwner, defender, skipOnActionPause)))
            {
                return;
            }

            pendingOnAttackEffectResolvedAttacker = attacker;
        }

        ContinueUnitAttackAfterOnAttackEffects(attacker, attackerOwner, defender, skipOnActionPause);
    }

    /// <summary>
    /// OnAttack（Sazabi 等）完了後の続行。
    /// ブロッカーがいればブロック → アクションの順（アクションを先に始めない）。
    /// </summary>
    private void ContinueUnitAttackAfterOnAttackEffects(
        CardController attacker,
        PlayerType attackerOwner,
        CardController defender,
        bool skipOnActionPause)
    {
        if (attacker == null)
        {
            return;
        }

        pendingOnAttackEffectResolvedAttacker = attacker;
        _onAttackPreCombatCompletedAttacker = attacker;

        if (attackFlowBlockSelectionResolved)
        {
            if (attackFlowPostBlockPassOnActionDone)
            {
                CardController resumeAttacker = attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker;
                CardController resumeDefender = attackFlowDeclaredDefenderUnit != null
                    ? attackFlowDeclaredDefenderUnit
                    : defender;
                if (!IsUnitAliveOnAnyDeployField(resumeDefender))
                {
                    CancelPendingUnitAttackFlow();
                    return;
                }

                ExecuteUnitVsUnitDeclaredCombat(
                    resumeAttacker,
                    resumeDefender,
                    attackFlowAttackerOwner,
                    ResolveCardOwner(resumeDefender.transform));
                return;
            }

            RunOnActionStepsImmediatelyAfterBlockPass(
                attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker,
                attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender,
                attackFlowAttackerOwner,
                ResolveCardOwner((attackFlowDeclaredDefenderUnit != null
                    ? attackFlowDeclaredDefenderUnit
                    : defender).transform),
                AttackFlowStrikeKind.UnitVsUnit);
            return;
        }

        PlayerType defenderOwner = defender != null
            ? ResolveCardOwner(defender.transform)
            : (attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player);

        Debug.Log(
            $"[AttackFlow] OnAttack 完了 → ブロック→アクションへ "
            + $"attacker:{attacker.Data?.cardName}");
        // skipAttackedSidePanelPause=false: ブロック UI／オンラインブロック待ちをスキップしない
        TryUnitVsUnitAttack(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            skipOnActionPause,
            skipAttackedSidePanelPause: false);
    }
}
