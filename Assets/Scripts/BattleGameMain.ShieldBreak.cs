using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>シールド破壊時の公開 UI とバースト（OnBurst）解決。</summary>
public partial class BattleGameMain
{
    private const float ShieldBreakCardSpacing = 24f;
    private const string ShieldBreakSelectOutlineName = "ShieldBreakSelectOutline";
    private const string ShieldBreakOrderBadgeName = "ShieldBreakOrderBadge";

    private struct PendingShieldBreakBatch
    {
        public Gundam2024RuleScript.PlayerSide Side;
        public int Count;
        public bool SimultaneousReveal;
    }

    /// <summary>制圧で破壊される先頭 N 枚の内訳。</summary>
    private sealed class SuppressBreakingLayout
    {
        public readonly List<int> BreakingZoneIndices = new List<int>();
        public readonly List<int> BaseDeployBurstZoneIndices = new List<int>();
        public readonly List<int> OrderedBurstZoneIndices = new List<int>();
    }

    /// <summary>制圧 UI でプレイヤーが決めた順序・ベース配備対象。</summary>
    private sealed class SuppressBreakPlayerChoice
    {
        /// <summary>破壊するシールド（ゾーン先頭からのインデックス）。</summary>
        public readonly List<int> BreakingZoneIndices = new List<int>();

        /// <summary>DeployBase 以外のバーストを解決する順序。</summary>
        public readonly List<int> NonBaseBurstOrderZoneIndices = new List<int>();

        /// <summary>ベースゾーンへ配備するベースのゾーンインデックス。-1 は未使用。</summary>
        public int BaseDeployZoneIndex = -1;
    }

    private static bool ShouldResolveShieldBurst(CardData data)
    {
        return data != null && TimedEffectResolver.HasEffectTiming(data, EffectTiming.OnBurst);
    }

    private static bool HasDeployBaseOnBurst(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        List<EffectData> effects = TimedEffectResolver.CollectEffectsByTiming(data, EffectTiming.OnBurst);
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null && effects[i].type == EffectType.DeployBase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAddSelfToHandOnBurst(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        List<EffectData> effects = TimedEffectResolver.CollectEffectsByTiming(data, EffectTiming.OnBurst);
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null && effects[i].type == EffectType.AddSelfToHand)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDeploySelfToShieldOnBurst(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        List<EffectData> effects = TimedEffectResolver.CollectEffectsByTiming(data, EffectTiming.OnBurst);
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null && effects[i].type == EffectType.DeploySelfToShield)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBaseCardWithDeployBaseBurst(CardData data)
    {
        return data != null && data.type == Type.Base && HasDeployBaseOnBurst(data);
    }

    private static SuppressBreakingLayout BuildSuppressBreakingLayout(CardGameRule rule, int breakCount)
    {
        SuppressBreakingLayout layout = new SuppressBreakingLayout();
        if (rule == null || breakCount <= 0)
        {
            return layout;
        }

        int picks = Mathf.Min(breakCount, rule.GetShieldZoneCardCount());
        for (int i = 0; i < picks; i++)
        {
            layout.BreakingZoneIndices.Add(i);
            if (!rule.TryGetShieldZoneCardAt(i, out ShieldBreakTaken taken) || taken.Data == null)
            {
                continue;
            }

            if (IsBaseCardWithDeployBaseBurst(taken.Data))
            {
                layout.BaseDeployBurstZoneIndices.Add(i);
            }
            else if (ShouldResolveShieldBurst(taken.Data))
            {
                layout.OrderedBurstZoneIndices.Add(i);
            }
        }

        return layout;
    }

    private static bool IsZoneIndexInList(List<int> list, int zoneIndex)
    {
        return list != null && list.Contains(zoneIndex);
    }

    /// <summary>バースト後に場に残すのは手札・ベース枠・シールドゾーンへの再登録。</summary>
    private static bool IsBurstCardRetained(CardController card, CardGameRule rule)
    {
        if (card == null || rule == null)
        {
            return false;
        }

        if (rule.HandScrollContent != null && card.transform.IsChildOf(rule.HandScrollContent))
        {
            return true;
        }

        if (rule.BaseSlotContent != null && card.transform.IsChildOf(rule.BaseSlotContent))
        {
            return true;
        }

        // 破壊時はリストから外しても親がシールドゾーンのまま残るため、登録済みのみ残存扱い。
        return rule.IsRegisteredInShieldZone(card);
    }

    private IEnumerator WaitForShieldBreakFlowCompleteCoroutine()
    {
        yield return null;
        yield return new WaitUntil(() =>
            !shieldBreakQueueRunning
            && pendingShieldBreakBatches.Count == 0
            && !isShieldBreakFlowOpen);
    }

    private IEnumerator RunShieldBreakQueueCoroutine()
    {
        shieldBreakQueueRunning = true;
        try
        {
            while (pendingShieldBreakBatches.Count > 0)
            {
                PendingShieldBreakBatch batch = pendingShieldBreakBatches.Dequeue();
                yield return ProcessShieldBreakBatchCoroutine(batch.Side, batch.Count, batch.SimultaneousReveal);
            }
        }
        finally
        {
            shieldBreakQueueRunning = false;
            if (pendingShieldBreakBatches.Count == 0)
            {
                isShieldBreakFlowOpen = false;
            }
        }
    }

    private IEnumerator ProcessShieldBreakBatchCoroutine(
        Gundam2024RuleScript.PlayerSide side,
        int brokenCount,
        bool simultaneousReveal)
    {
        if (brokenCount <= 0 || isMatchFinished)
        {
            yield break;
        }

        CardGameRule rule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        PlayerType shieldOwner = side == Gundam2024RuleScript.PlayerSide.Player ? PlayerType.Player : PlayerType.Enemy;
        if (rule == null)
        {
            yield break;
        }

        isShieldBreakFlowOpen = true;

        List<ShieldBreakTaken> takenCards = new List<ShieldBreakTaken>(brokenCount);
        SuppressBreakPlayerChoice playerChoice = null;
        bool isSuppress = simultaneousReveal && brokenCount > 1;

        try
        {
        if (isSuppress)
        {
            SuppressBreakingLayout layout = BuildSuppressBreakingLayout(rule, brokenCount);
            if (layout.BreakingZoneIndices.Count == 0)
            {
                yield break;
            }

            if (shieldOwner == PlayerType.Player)
            {
                bool confirmed = false;
                yield return RunSuppressPlayerSelectionCoroutine(
                    rule,
                    shieldOwner,
                    layout,
                    c =>
                    {
                        playerChoice = c;
                        confirmed = true;
                    });

                if (!confirmed || playerChoice == null || !IsSuppressPlayerChoiceReady(playerChoice, layout))
                {
                    yield break;
                }
            }
            else
            {
                playerChoice = BuildEnemySuppressChoice(layout);
            }

            takenCards = DetachShieldCardsBySuppressChoice(rule, playerChoice);
        }
        else
        {
            for (int i = 0; i < brokenCount; i++)
            {
                if (isMatchFinished)
                {
                    yield break;
                }

                if (!rule.TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken))
                {
                    Debug.LogWarning(
                        $"[ShieldBreak] No shield card UI for break {i + 1}/{brokenCount} side:{side} (took {takenCards.Count})");
                    break;
                }

                takenCards.Add(taken);
            }

            if (takenCards.Count == 0)
            {
                yield break;
            }

            yield return ShowShieldBreakRevealCoroutine(takenCards, shieldOwner, simultaneousReveal);

            yield return ResolveBurstEffectsForTakenCardsCoroutine(takenCards, shieldOwner);
            for (int i = 0; i < takenCards.Count; i++)
            {
                CommitShieldBreakTakenAfterBurst(takenCards[i], rule, shieldOwner);
            }

            yield break;
        }

        if (takenCards.Count == 0)
        {
            yield break;
        }

        yield return ResolveShieldBreakTakenCardsCoroutine(takenCards, shieldOwner, rule, playerChoice);
        }
        finally
        {
            ReconcileShieldStateWithZone(side, force: true);
            SyncAllResourceViewsFromRule();
        }
    }

    private static bool IsSuppressPlayerChoiceReady(
        SuppressBreakPlayerChoice choice,
        SuppressBreakingLayout layout)
    {
        if (choice == null || layout == null)
        {
            return false;
        }

        if (layout.BaseDeployBurstZoneIndices.Count >= 2 && choice.BaseDeployZoneIndex < 0)
        {
            return false;
        }

        if (layout.OrderedBurstZoneIndices.Count > 0
            && choice.NonBaseBurstOrderZoneIndices.Count != layout.OrderedBurstZoneIndices.Count)
        {
            return false;
        }

        return true;
    }

    private IEnumerator ResolveShieldBreakTakenCardsCoroutine(
        List<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner,
        CardGameRule rule,
        SuppressBreakPlayerChoice choice)
    {
        if (choice == null || takenCards == null)
        {
            yield break;
        }

        Dictionary<int, ShieldBreakTaken> byZone = new Dictionary<int, ShieldBreakTaken>();
        for (int i = 0; i < takenCards.Count && i < choice.BreakingZoneIndices.Count; i++)
        {
            byZone[choice.BreakingZoneIndices[i]] = takenCards[i];
        }

        HashSet<int> processed = new HashSet<int>();

        for (int b = 0; b < choice.BreakingZoneIndices.Count; b++)
        {
            int zoneIndex = choice.BreakingZoneIndices[b];
            if (!byZone.TryGetValue(zoneIndex, out ShieldBreakTaken taken))
            {
                continue;
            }

            if (!IsBaseCardWithDeployBaseBurst(taken.Data))
            {
                continue;
            }

            if (choice.BaseDeployZoneIndex >= 0 && zoneIndex == choice.BaseDeployZoneIndex)
            {
                continue;
            }

            rule.CommitShieldCardToTrash(taken);
            processed.Add(zoneIndex);
            Debug.Log($"[Suppress] Base discarded (not deployed) {taken.Data?.cardName}(id:{taken.CardId})");
        }

        List<ShieldBreakTaken> orderedBurstCards = new List<ShieldBreakTaken>(choice.NonBaseBurstOrderZoneIndices.Count);
        for (int o = 0; o < choice.NonBaseBurstOrderZoneIndices.Count; o++)
        {
            int zoneIndex = choice.NonBaseBurstOrderZoneIndices[o];
            if (byZone.TryGetValue(zoneIndex, out ShieldBreakTaken taken))
            {
                orderedBurstCards.Add(taken);
            }
        }

        if (orderedBurstCards.Count > 0)
        {
            yield return ResolveBurstEffectsForTakenCardsCoroutine(orderedBurstCards, shieldOwner);
            for (int o = 0; o < choice.NonBaseBurstOrderZoneIndices.Count; o++)
            {
                int zoneIndex = choice.NonBaseBurstOrderZoneIndices[o];
                if (byZone.TryGetValue(zoneIndex, out ShieldBreakTaken taken))
                {
                    CommitShieldBreakTakenAfterBurst(taken, rule, shieldOwner);
                    processed.Add(zoneIndex);
                }
            }
        }

        if (choice.BaseDeployZoneIndex >= 0
            && byZone.TryGetValue(choice.BaseDeployZoneIndex, out ShieldBreakTaken baseTaken))
        {
            yield return ResolveBurstEffectsForTakenCardsCoroutine(
                new List<ShieldBreakTaken> { baseTaken },
                shieldOwner);
            CommitShieldBreakTakenAfterBurst(baseTaken, rule, shieldOwner);
            processed.Add(choice.BaseDeployZoneIndex);
        }

        for (int i = 0; i < choice.BreakingZoneIndices.Count; i++)
        {
            int zoneIndex = choice.BreakingZoneIndices[i];
            if (processed.Contains(zoneIndex) || !byZone.TryGetValue(zoneIndex, out ShieldBreakTaken taken))
            {
                continue;
            }

            rule.CommitShieldCardToTrash(taken);
        }
    }

    private static SuppressBreakPlayerChoice BuildEnemySuppressChoice(SuppressBreakingLayout layout)
    {
        SuppressBreakPlayerChoice choice = new SuppressBreakPlayerChoice();
        if (layout == null)
        {
            return choice;
        }

        choice.BreakingZoneIndices.AddRange(layout.BreakingZoneIndices);
        choice.NonBaseBurstOrderZoneIndices.AddRange(layout.OrderedBurstZoneIndices);
        if (layout.BaseDeployBurstZoneIndices.Count > 0)
        {
            choice.BaseDeployZoneIndex = layout.BaseDeployBurstZoneIndices[0];
        }

        return choice;
    }

    private List<ShieldBreakTaken> DetachShieldCardsBySuppressChoice(
        CardGameRule rule,
        SuppressBreakPlayerChoice choice)
    {
        List<ShieldBreakTaken> result = new List<ShieldBreakTaken>();
        if (choice == null || choice.BreakingZoneIndices.Count == 0)
        {
            return result;
        }

        List<int> descending = new List<int>(choice.BreakingZoneIndices);
        descending.Sort((a, b) => b.CompareTo(a));
        Dictionary<int, ShieldBreakTaken> detached = new Dictionary<int, ShieldBreakTaken>();
        for (int i = 0; i < descending.Count; i++)
        {
            int zoneIndex = descending[i];
            if (rule.TryDetachShieldCardAtZoneIndex(zoneIndex, out ShieldBreakTaken taken, revealFace: true))
            {
                detached[zoneIndex] = taken;
            }
            else
            {
                Debug.LogWarning($"[ShieldBreak] Suppress detach failed zoneIndex:{zoneIndex}");
            }
        }

        for (int i = 0; i < choice.BreakingZoneIndices.Count; i++)
        {
            int zoneIndex = choice.BreakingZoneIndices[i];
            if (detached.TryGetValue(zoneIndex, out ShieldBreakTaken taken))
            {
                result.Add(taken);
            }
        }

        return result;
    }

    private IEnumerator ShowShieldBreakRevealCoroutine(
        List<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner,
        bool simultaneousReveal)
    {
        GameObject root = BuildShieldBreakRevealPanel(
            takenCards,
            shieldOwner,
            simultaneousReveal,
            suppressSelectionMode: false,
            layout: null,
            liveChoice: null,
            zoneIndices: null);
        if (root == null)
        {
            yield break;
        }

        bool dismissed = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 42f);
        okBtn.onClick.AddListener(() =>
        {
            dismissed = true;
            CloseShieldBreakRevealPanel(root);
        });

        yield return new WaitUntil(() => dismissed);
    }

    private IEnumerator RunSuppressPlayerSelectionCoroutine(
        CardGameRule rule,
        PlayerType shieldOwner,
        SuppressBreakingLayout layout,
        System.Action<SuppressBreakPlayerChoice> onConfirmed)
    {
        if (layout == null || layout.BreakingZoneIndices.Count == 0)
        {
            yield break;
        }

        SuppressBreakPlayerChoice choice = new SuppressBreakPlayerChoice();

        List<int> zoneIndices = new List<int>(layout.BreakingZoneIndices);
        List<ShieldBreakTaken> displayCards = new List<ShieldBreakTaken>(zoneIndices.Count);
        for (int i = 0; i < zoneIndices.Count; i++)
        {
            int zoneIndex = zoneIndices[i];
            if (rule.TryGetShieldZoneCardAt(zoneIndex, out ShieldBreakTaken taken))
            {
                displayCards.Add(taken);
                if (taken.Controller != null)
                {
                    taken.Controller.RevealShieldFace();
                }
            }
        }

        bool dismissed = false;
        Button okBtn = null;
        TextMeshProUGUI okLabel = null;
        System.Action refreshOk = null;
        refreshOk = () =>
        {
            if (okBtn == null)
            {
                return;
            }

            bool ready = IsSuppressPlayerChoiceReady(choice, layout);
            okBtn.interactable = ready;
            if (okLabel != null)
            {
                okLabel.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        };

        GameObject root = BuildShieldBreakRevealPanel(
            displayCards,
            shieldOwner,
            simultaneousReveal: true,
            suppressSelectionMode: true,
            layout,
            choice,
            zoneIndices,
            refreshOk);
        if (root == null)
        {
            yield break;
        }

        okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 42f);

        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        okBtn.interactable = false;
        refreshOk();

        okBtn.onClick.AddListener(() =>
        {
            if (!okBtn.interactable)
            {
                return;
            }

            dismissed = true;
            CloseShieldBreakRevealPanel(root);
        });

        yield return new WaitUntil(() => dismissed);

        choice.BreakingZoneIndices.Clear();
        choice.BreakingZoneIndices.AddRange(layout.BreakingZoneIndices);

        if (layout.BaseDeployBurstZoneIndices.Count == 1)
        {
            choice.BaseDeployZoneIndex = layout.BaseDeployBurstZoneIndices[0];
        }

        if (layout.OrderedBurstZoneIndices.Count == 1 && choice.NonBaseBurstOrderZoneIndices.Count == 0)
        {
            choice.NonBaseBurstOrderZoneIndices.Add(layout.OrderedBurstZoneIndices[0]);
        }

        onConfirmed?.Invoke(choice);
    }

    private void CloseShieldBreakRevealPanel(GameObject root)
    {
        if (root != null)
        {
            Destroy(root);
        }

        if (activeOnActionPopupRoot == root)
        {
            activeOnActionPopupRoot = null;
        }
    }

    private GameObject BuildShieldBreakRevealPanel(
        List<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner,
        bool simultaneousReveal,
        bool suppressSelectionMode,
        SuppressBreakingLayout layout,
        SuppressBreakPlayerChoice liveChoice,
        List<int> zoneIndices,
        System.Action onSelectionChanged = null)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || takenCards == null || takenCards.Count == 0 || CardImagePrefab == null)
        {
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("ShieldBreakReveal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        int count = takenCards.Count;
        string ownerLabel = shieldOwner == PlayerType.Player ? "プレイヤー" : "エネミー";
        bool anyBurst = false;
        for (int i = 0; i < count; i++)
        {
            if (ShouldResolveShieldBurst(takenCards[i].Data))
            {
                anyBurst = true;
                break;
            }
        }

        bool hasBasePick = layout != null && layout.BaseDeployBurstZoneIndices.Count > 0;
        bool hasOrderBurst = layout != null && layout.OrderedBurstZoneIndices.Count > 0;

        TextMeshProUGUI title = root.CreateChildTextCustom("ShieldBreakTitle", UIAnchor.TopCenter, 760, 44);
        title.text = suppressSelectionMode
            ? "シールド破壊（制圧）"
            : simultaneousReveal && count > 1
                ? "シールド破壊（制圧）"
                : count > 1
                    ? "シールド破壊（同時）"
                    : "シールド破壊";
        title.fontSize = 26;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("ShieldBreakSub", UIAnchor.TopCenter, 760, 44);
        if (suppressSelectionMode)
        {
            if (hasBasePick && hasOrderBurst)
            {
                sub.text = $"{ownerLabel}のシールド{count}枚：ベースは1枚だけ配備（他は破棄）、その他バーストは順番を指定";
            }
            else if (hasBasePick)
            {
                sub.text = layout.BaseDeployBurstZoneIndices.Count >= 2
                    ? $"{ownerLabel}のベース{count}枚：配備する1枚を選んでください（もう1枚は破棄）"
                    : $"{ownerLabel}のベースが制圧で破壊されます";
            }
            else
            {
                sub.text = $"{ownerLabel}のシールド{count}枚：バーストを解決する順にタップ（①→②）";
            }
        }
        else
        {
            sub.text = simultaneousReveal && count > 1
                ? $"{ownerLabel}のシールド{count}枚が制圧で破壊されます"
                : count > 1
                    ? $"{ownerLabel}のシールド{count}枚が破壊されました"
                    : $"{ownerLabel}のシールド1枚が破壊されました";
        }

        sub.fontSize = 17;
        sub.color = new Color(0.88f, 0.92f, 1f, 1f);
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -52f);

        if (anyBurst && !suppressSelectionMode)
        {
            TextMeshProUGUI burstBanner = root.CreateChildTextCustom("ShieldBurstBanner", UIAnchor.TopCenter, 640, 28);
            burstBanner.text = "【バースト】あり（OK 後に解決）";
            burstBanner.fontSize = 18;
            burstBanner.color = new Color(1f, 0.85f, 0.45f, 1f);
            burstBanner.alignment = TextAlignmentOptions.Center;
            burstBanner.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -82f);
        }

        float step = BattleCardPreviewWidth + ShieldBreakCardSpacing;
        float startX = -(count - 1) * step * 0.5f;
        const float cardRowY = -118f;
        List<GameObject> cardRoots = new List<GameObject>(count);

        for (int i = 0; i < count; i++)
        {
            ShieldBreakTaken taken = takenCards[i];
            if (taken.Data == null)
            {
                continue;
            }

            float cardX = startX + i * step;
            int zoneIndex = zoneIndices != null && i < zoneIndices.Count ? zoneIndices[i] : i;
            bool isBasePick = layout != null && IsZoneIndexInList(layout.BaseDeployBurstZoneIndices, zoneIndex);
            bool isOrderBurst = layout != null && IsZoneIndexInList(layout.OrderedBurstZoneIndices, zoneIndex);
            bool hasBurst = ShouldResolveShieldBurst(taken.Data);
            string roleSuffix = isBasePick
                ? "\n【ベース】"
                : isOrderBurst
                    ? "\n【バースト順】"
                    : hasBurst
                        ? "\n【バースト】"
                        : string.Empty;
            string caption = taken.Data.cardName + roleSuffix;
            Color captionColor = isBasePick
                ? new Color(0.55f, 0.9f, 1f, 1f)
                : hasBurst
                    ? new Color(1f, 0.85f, 0.45f, 1f)
                    : new Color(0.92f, 0.92f, 0.92f, 1f);

            if (taken.Controller != null)
            {
                int capturedZoneIndex = zoneIndex;
                GameObject cardRoot = AppendSelectableShieldBreakCardPreview(
                    root,
                    taken.Controller,
                    caption,
                    new Vector2(cardX, cardRowY),
                    captionColor,
                    suppressSelectionMode,
                    () => OnSuppressShieldCardTapped(
                        liveChoice,
                        layout,
                        capturedZoneIndex,
                        cardRoots,
                        zoneIndices,
                        onSelectionChanged),
                    () => GetSuppressOrderForZone(liveChoice, capturedZoneIndex),
                    () => liveChoice != null && liveChoice.BaseDeployZoneIndex == capturedZoneIndex);
                if (cardRoot != null)
                {
                    cardRoots.Add(cardRoot);
                }
            }
        }

        if (suppressSelectionMode && liveChoice != null && layout != null)
        {
            RefreshSuppressSelectionVisuals(cardRoots, liveChoice, zoneIndices, layout);
        }

        TextMeshProUGUI hint = root.CreateChildTextCustom("ShieldBreakHint", UIAnchor.TopCenter, 760, 44);
        hint.text = suppressSelectionMode
            ? hasBasePick && hasOrderBurst
                ? "ベースを1枚タップで配備指定、バーストカードは順番タップ → OK"
                : hasBasePick
                    ? "配備するベースを1枚選び OK（選ばないベースは破棄）"
                    : "バーストするカードを順にタップ（再タップで取り消し）→ OK → カードごとに敵ユニットを選択"
            : "破壊されるカードを確認し、OK で続行（バーストは OK 後、カードごとに敵ユニットを選択）";
        hint.fontSize = 15;
        hint.color = new Color(0.8f, 0.85f, 0.9f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        float hintY = count > 1 ? -340f : -320f;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, hintY);

        return root;
    }

    private static int GetSuppressOrderForZone(SuppressBreakPlayerChoice choice, int zoneIndex)
    {
        if (choice?.NonBaseBurstOrderZoneIndices == null)
        {
            return -1;
        }

        return choice.NonBaseBurstOrderZoneIndices.IndexOf(zoneIndex);
    }

    private void OnSuppressShieldCardTapped(
        SuppressBreakPlayerChoice choice,
        SuppressBreakingLayout layout,
        int zoneIndex,
        List<GameObject> cardRoots,
        List<int> zoneIndices,
        System.Action onSelectionChanged)
    {
        if (choice == null || layout == null)
        {
            return;
        }

        if (IsZoneIndexInList(layout.BaseDeployBurstZoneIndices, zoneIndex))
        {
            choice.BaseDeployZoneIndex = zoneIndex;
        }
        else if (IsZoneIndexInList(layout.OrderedBurstZoneIndices, zoneIndex))
        {
            int existing = choice.NonBaseBurstOrderZoneIndices.IndexOf(zoneIndex);
            if (existing >= 0)
            {
                choice.NonBaseBurstOrderZoneIndices.RemoveAt(existing);
            }
            else if (choice.NonBaseBurstOrderZoneIndices.Count < layout.OrderedBurstZoneIndices.Count)
            {
                choice.NonBaseBurstOrderZoneIndices.Add(zoneIndex);
            }
        }

        RefreshSuppressSelectionVisuals(cardRoots, choice, zoneIndices, layout);
        onSelectionChanged?.Invoke();
    }

    private static void RefreshSuppressSelectionVisuals(
        List<GameObject> cardRoots,
        SuppressBreakPlayerChoice choice,
        List<int> zoneIndices,
        SuppressBreakingLayout layout)
    {
        if (cardRoots == null || choice == null || zoneIndices == null || layout == null)
        {
            return;
        }

        for (int i = 0; i < cardRoots.Count && i < zoneIndices.Count; i++)
        {
            int zoneIndex = zoneIndices[i];
            GameObject cardRoot = cardRoots[i];
            if (cardRoot == null)
            {
                continue;
            }

            bool isBasePick = IsZoneIndexInList(layout.BaseDeployBurstZoneIndices, zoneIndex);
            bool isOrderBurst = IsZoneIndexInList(layout.OrderedBurstZoneIndices, zoneIndex);
            bool selected = isBasePick
                ? choice.BaseDeployZoneIndex == zoneIndex
                : isOrderBurst && choice.NonBaseBurstOrderZoneIndices.Contains(zoneIndex);
            int order = GetSuppressOrderForZone(choice, zoneIndex);
            bool showBaseDeployLabel = isBasePick && choice.BaseDeployZoneIndex == zoneIndex;

            Transform outline = cardRoot.transform.Find(ShieldBreakSelectOutlineName);
            if (selected)
            {
                if (outline == null)
                {
                    GameObject outlineGo = new GameObject(
                        ShieldBreakSelectOutlineName,
                        typeof(RectTransform),
                        typeof(Image));
                    outlineGo.transform.SetParent(cardRoot.transform, false);
                    outlineGo.transform.SetAsFirstSibling();
                    RectTransform outlineRt = outlineGo.GetComponent<RectTransform>();
                    outlineRt.anchorMin = Vector2.zero;
                    outlineRt.anchorMax = Vector2.one;
                    outlineRt.offsetMin = new Vector2(-4f, -4f);
                    outlineRt.offsetMax = new Vector2(4f, 4f);
                    Image outlineImg = outlineGo.GetComponent<Image>();
                    outlineImg.color = isBasePick
                        ? new Color(0.45f, 0.85f, 1f, 0.95f)
                        : new Color(1f, 0.85f, 0.35f, 0.95f);
                    outlineImg.raycastTarget = false;
                }

                Transform badge = cardRoot.transform.Find(ShieldBreakOrderBadgeName);
                if (isOrderBurst && order >= 0)
                {
                    TextMeshProUGUI badgeText;
                    if (badge == null)
                    {
                        GameObject badgeGo = new GameObject(
                            ShieldBreakOrderBadgeName,
                            typeof(RectTransform),
                            typeof(TextMeshProUGUI));
                        badgeGo.transform.SetParent(cardRoot.transform, false);
                        badgeGo.transform.SetAsLastSibling();
                        RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
                        badgeRt.anchorMin = new Vector2(1f, 1f);
                        badgeRt.anchorMax = new Vector2(1f, 1f);
                        badgeRt.pivot = new Vector2(1f, 1f);
                        badgeRt.sizeDelta = new Vector2(28f, 28f);
                        badgeRt.anchoredPosition = new Vector2(6f, 6f);
                        badgeText = badgeGo.GetComponent<TextMeshProUGUI>();
                        badgeText.fontSize = 18;
                        badgeText.fontStyle = FontStyles.Bold;
                        badgeText.color = Color.black;
                        badgeText.alignment = TextAlignmentOptions.Center;
                    }
                    else
                    {
                        badgeText = badge.GetComponent<TextMeshProUGUI>();
                    }

                    if (badgeText != null)
                    {
                        badgeText.text = (order + 1).ToString();
                    }
                }
                else if (badge != null)
                {
                    Object.Destroy(badge.gameObject);
                }
                else if (showBaseDeployLabel)
                {
                    Transform deployLabel = cardRoot.transform.Find("BaseDeployLabel");
                    if (deployLabel == null)
                    {
                        TextMeshProUGUI label = cardRoot.CreateChildTextCustom(
                            "BaseDeployLabel",
                            UIAnchor.TopCenter,
                            120,
                            22);
                        label.text = "配備";
                        label.fontSize = 14;
                        label.color = new Color(0.45f, 0.95f, 1f, 1f);
                        label.alignment = TextAlignmentOptions.Center;
                        label.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 8f);
                    }
                }
            }
            else
            {
                if (outline != null)
                {
                    Object.Destroy(outline.gameObject);
                }

                Transform badge = cardRoot.transform.Find(ShieldBreakOrderBadgeName);
                if (badge != null)
                {
                    Object.Destroy(badge.gameObject);
                }

                Transform deployLabel = cardRoot.transform.Find("BaseDeployLabel");
                if (deployLabel != null)
                {
                    Object.Destroy(deployLabel.gameObject);
                }
            }
        }
    }

    private GameObject AppendSelectableShieldBreakCardPreview(
        GameObject parent,
        CardController liveCard,
        string caption,
        Vector2 anchoredPosition,
        Color captionColor,
        bool selectable,
        System.Action onSelected,
        System.Func<int> getOrder,
        System.Func<bool> isBaseDeployPick)
    {
        GameObject cardRoot = new GameObject(
            $"ShieldBreakCard_{liveCard.Data.id}_{anchoredPosition.x}",
            typeof(RectTransform));
        cardRoot.transform.SetParent(parent.transform, false);
        RectTransform cardRootRt = cardRoot.GetComponent<RectTransform>();
        cardRootRt.anchorMin = new Vector2(0.5f, 1f);
        cardRootRt.anchorMax = new Vector2(0.5f, 1f);
        cardRootRt.pivot = new Vector2(0.5f, 1f);
        cardRootRt.sizeDelta = new Vector2(BattleCardPreviewWidth + 12f, BattleCardPreviewHeight + 36f);
        cardRootRt.anchoredPosition = anchoredPosition;

        GameObject preview = AppendNonInteractiveCardPreview(
            cardRoot,
            liveCard,
            caption,
            Vector2.zero,
            captionColor);
        if (preview == null)
        {
            Destroy(cardRoot);
            return null;
        }

        RectTransform previewRt = preview.GetComponent<RectTransform>();
        if (previewRt != null)
        {
            previewRt.anchoredPosition = new Vector2(0f, -8f);
        }

        if (selectable)
        {
            Image hit = cardRoot.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.01f);
            hit.raycastTarget = true;
            Button pick = cardRoot.AddComponent<Button>();
            pick.targetGraphic = hit;
            pick.onClick.AddListener(() => onSelected?.Invoke());
        }

        return cardRoot;
    }
}
