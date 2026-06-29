using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>山札トップを見る（Look）効果と OnLook 誘発。</summary>
public partial class BattleGameMain
{
    private sealed class LookedDeckEntry
    {
        public int DeckIndex;
        public int CardId;
        public CardData Data;
    }

    private enum LookedRemainderDispositionChoice
    {
        ReturnToDeckTop,
        ShuffleToDeckBottom
    }

    private sealed class LookResolutionContext
    {
        public CardController SourceCard;
        public PlayerType OwnerType;
        public CardGameRule DeckRule;
        public PlayerType DeckOwnerType;
        public string DeckLabel;
        public int RequestedLookCount;
        public List<LookedDeckEntry> Entries = new List<LookedDeckEntry>();
        public HashSet<int> TakenCardIds = new HashSet<int>();
    }

    private CardGameRule ResolveDeckRuleForLook(PlayerType effectOwner, EffectData effect)
    {
        bool opponentDeck = effect != null && effect.target == TargetType.EnemyPlayer;
        PlayerType deckOwner = opponentDeck
            ? (effectOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : effectOwner;
        return deckOwner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
    }

    private static string FormatLookDeckOwnerLabel(PlayerType deckOwner)
    {
        return deckOwner == PlayerType.Player ? "自分" : "相手";
    }

    private static PlayerType ResolveHandOwnerForLookEffect(PlayerType effectOwner, TargetType target)
    {
        if (target == TargetType.EnemyPlayer)
        {
            return effectOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return effectOwner;
    }

    private List<CardData> ResolveCardDataListFromIds(List<int> cardIds)
    {
        List<CardData> cards = new List<CardData>();
        if (cardIds == null || cardIds.Count == 0)
        {
            return cards;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            CardData data = DeckSettinObject.Instance.GetCardDataById(cardIds[i]);
            if (data != null)
            {
                cards.Add(data);
            }
        }

        return cards;
    }

    private static List<LookedDeckEntry> BuildLookedDeckEntries(List<int> peekedIds)
    {
        List<LookedDeckEntry> entries = new List<LookedDeckEntry>();
        if (peekedIds == null)
        {
            return entries;
        }

        for (int i = 0; i < peekedIds.Count; i++)
        {
            CardData data = DeckSettinObject.Instance.GetCardDataById(peekedIds[i]);
            if (data == null)
            {
                continue;
            }

            entries.Add(new LookedDeckEntry
            {
                DeckIndex = i,
                CardId = peekedIds[i],
                Data = data
            });
        }

        return entries;
    }

    /// <summary>Look 効果を解決。OnLook があれば見た枚から手札追加 UI 等を続けて解決する。</summary>
    private void ApplyLookEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        System.Action onComplete)
    {
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        CardGameRule deckRule = ResolveDeckRuleForLook(ownerType, effect);
        if (deckRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<int> peekedIds = deckRule.PeekTopCardIds(magnitude);
        bool opponentDeck = effect != null && effect.target == TargetType.EnemyPlayer;
        PlayerType deckOwner = opponentDeck
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        string deckLabel = FormatLookDeckOwnerLabel(deckOwner);
        List<LookedDeckEntry> entries = BuildLookedDeckEntries(peekedIds);
        string cardNames = entries.Count > 0
            ? string.Join(", ", entries.ConvertAll(e => $"{e.Data.cardName}(id:{e.Data.id})"))
            : "(none)";

        Debug.Log(
            $"[Effect] Look x{magnitude} deck:{deckLabel} actual:{peekedIds.Count} "
            + $"by cardId:{sourceCard?.Data?.id} → {cardNames}");

        LookResolutionContext context = new LookResolutionContext
        {
            SourceCard = sourceCard,
            OwnerType = ownerType,
            DeckRule = deckRule,
            DeckOwnerType = deckOwner,
            DeckLabel = deckLabel,
            RequestedLookCount = magnitude,
            Entries = entries
        };

        if (entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        TriggerOnLookEffects(context, onComplete);
    }

    private void ApplyEffectRespectingLookAsync(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        System.Action onChainContinue)
    {
        if (effect != null && effect.type == EffectType.Look)
        {
            ApplyLookEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.MillTopToTrash)
        {
            ApplyMillTopToTrashEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ExileFromDeck)
        {
            ApplyExileFromDeckEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ExileFromTrash)
        {
            ApplyExileFromTrashEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.DeployUnit)
        {
            ApplyDeployUnitEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (ShouldRevealDrawnCards(effect, ownerType))
        {
            StartCoroutine(ApplyDrawEffectWithRevealCoroutine(sourceCard, ownerType, effect, onChainContinue));
            return;
        }

        ApplyEffect(sourceCard, ownerType, effect);
        onChainContinue?.Invoke();
    }

    private List<TimedEffectData> CollectOnLookBlocks(CardController sourceCard, PlayerType ownerType)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            return blocks;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || !timed.IsOnLookResolutionBlock())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            blocks.Add(timed);
        }

        return blocks;
    }

    private void TriggerOnLookEffects(LookResolutionContext context, System.Action onComplete)
    {
        if (context == null || context.SourceCard == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<TimedEffectData> blocks = CollectOnLookBlocks(context.SourceCard, context.OwnerType);
        if (blocks.Count == 0)
        {
            if (context.OwnerType == PlayerType.Player)
            {
                ShowLookDeckViewOnlyPopup(context, onComplete);
            }
            else
            {
                onComplete?.Invoke();
            }

            return;
        }

        Debug.Log(
            $"[OnLook] 開始: {context.SourceCard.Data?.cardName}(id:{context.SourceCard.Data?.id}) blocks:{blocks.Count}");

        void runChain()
        {
            RunOnLookTimedBlocks(context, blocks, 0, onComplete);
        }

        if (context.OwnerType == PlayerType.Player
            && !OnLookBlocksContainAddToHand(blocks)
            && OnLookBlocksContainRemainderDisposition(blocks))
        {
            ShowLookDeckViewOnlyPopup(context, runChain);
            return;
        }

        runChain();
    }

    private static bool OnLookBlocksContainAddToHand(List<TimedEffectData> blocks)
    {
        return OnLookBlocksContainEffectType(blocks, EffectType.AddToHandFromLooked);
    }

    private static bool OnLookBlocksContainRemainderDisposition(List<TimedEffectData> blocks)
    {
        return OnLookBlocksContainEffectType(blocks, EffectType.ReturnLookedRemainderToDeckTop)
            || OnLookBlocksContainEffectType(blocks, EffectType.ShuffleLookedRemainderToDeckBottom)
            || OnLookBlocksContainEffectType(blocks, EffectType.ChooseLookedRemainderDisposition);
    }

    private static bool OnLookBlocksContainEffectType(List<TimedEffectData> blocks, EffectType type)
    {
        if (blocks == null)
        {
            return false;
        }

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData block = blocks[bi];
            if (block == null)
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = block.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect != null && effect.type == type)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RunOnLookTimedBlocks(
        LookResolutionContext context,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        TryExecuteOnLookEffectChain(
            context,
            block.GetResolvedEffects(),
            0,
            () => RunOnLookTimedBlocks(context, blocks, blockIndex + 1, onComplete));
    }

    private void TryExecuteOnLookEffectChain(
        LookResolutionContext context,
        IReadOnlyList<EffectData> effects,
        int index,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnLookEffectChain(context, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.AddToHandFromLooked)
        {
            ApplyAddToHandFromLookedEffect(
                context,
                effect,
                () => TryExecuteOnLookEffectChain(context, effects, index + 1, onDone));
            return;
        }

        if (effect.type == EffectType.ReturnLookedRemainderToDeckTop)
        {
            ApplyReturnLookedRemainderToDeckTop(context);
            TryExecuteOnLookEffectChain(context, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.ShuffleLookedRemainderToDeckBottom)
        {
            ApplyShuffleLookedRemainderToDeckBottom(context);
            TryExecuteOnLookEffectChain(context, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.ChooseLookedRemainderDisposition)
        {
            ApplyChooseLookedRemainderDispositionEffect(
                context,
                () => TryExecuteOnLookEffectChain(context, effects, index + 1, onDone));
            return;
        }

        Debug.LogWarning($"[OnLook] 未対応の効果タイプ {effect.type} — スキップ");
        TryExecuteOnLookEffectChain(context, effects, index + 1, onDone);
    }

    private void ApplyAddToHandFromLookedEffect(
        LookResolutionContext context,
        EffectData effect,
        System.Action onComplete)
    {
        if (context == null || effect == null || context.Entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (!effect.HasTargetFeatureFilter())
        {
            Debug.LogWarning(
                $"[OnLook] AddToHandFromLooked には targetFeature / targetFeatureId の指定が必要です "
                + $"(cardId:{context.SourceCard?.Data?.id})");
            if (context.OwnerType == PlayerType.Player)
            {
                ShowLookDeckViewOnlyPopup(context, onComplete);
            }
            else
            {
                onComplete?.Invoke();
            }

            return;
        }

        int pickCount = Mathf.Max(1, ResolveEffectMagnitude(effect, context.OwnerType, context.SourceCard));
        PlayerType handOwner = ResolveHandOwnerForLookEffect(context.OwnerType, effect.target);
        CardGameRule handRule = handOwner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        List<LookedDeckEntry> selectable = FilterLookedEntriesForAddEffect(context.Entries, effect);
        string featureLabel = effect.FormatTargetFeaturesLabel();
        if (string.IsNullOrEmpty(featureLabel))
        {
            featureLabel = "未指定";
        }

        if (context.OwnerType == PlayerType.Enemy)
        {
            for (int i = 0; i < pickCount && selectable.Count > 0; i++)
            {
                LookedDeckEntry pick = selectable[0];
                TakeLookedEntryToHand(context, handRule, handOwner, pick, effect);
                selectable.RemoveAt(0);
            }

            onComplete?.Invoke();
            return;
        }

        if (selectable.Count == 0)
        {
            Debug.Log(
                $"[OnLook] 手札に加えられるカードなし（特性:{featureLabel}）— 閲覧のみ");
            ShowLookDeckViewOnlyPopup(
                context,
                onComplete,
                $"特性「{featureLabel}」に合うカードはありませんでした");
            return;
        }

        ShowLookDeckPickToHandPopup(context, effect, selectable, pickCount, handOwner, handRule, onComplete);
    }

    private static List<LookedDeckEntry> FilterLookedEntriesForAddEffect(
        List<LookedDeckEntry> entries,
        EffectData effect)
    {
        List<LookedDeckEntry> result = new List<LookedDeckEntry>();
        if (entries == null)
        {
            return result;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            LookedDeckEntry entry = entries[i];
            if (entry?.Data != null && effect.MatchesLookedCardDataFeatureFilter(entry.Data))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private void TakeLookedEntryToHand(
        LookResolutionContext context,
        CardGameRule handRule,
        PlayerType handOwner,
        LookedDeckEntry entry,
        EffectData effect)
    {
        if (context?.DeckRule == null || entry == null || handRule == null)
        {
            return;
        }

        if (!context.DeckRule.TryTakeCardById(entry.CardId, out _))
        {
            Debug.LogWarning($"[OnLook] 山札からの取得に失敗 id:{entry.CardId}");
            return;
        }

        context.TakenCardIds.Add(entry.CardId);
        AddCardIdToHand(handRule, handOwner, entry.CardId);
        Debug.Log(
            $"[Effect] AddToHandFromLooked {entry.Data.cardName}(id:{entry.CardId}) "
            + $"feature:{effect?.FormatTargetFeaturesLabel() ?? "any"} "
            + $"handOwner:{handOwner} by cardId:{context.SourceCard?.Data?.id}");
    }

    private void AddCardIdToHand(CardGameRule targetRule, PlayerType targetType, int cardId)
    {
        if (targetRule == null || cardId < 0)
        {
            return;
        }

        CardData cardData = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (cardData == null || CardImagePrefab == null || targetRule.HandScrollContent == null)
        {
            return;
        }

        GameObject cardImage = Instantiate(CardImagePrefab, targetRule.HandScrollContent);
        CardController drawnCard = cardImage.GetComponent<CardController>();
        drawnCard.SetUp(cardData, OnCardClicked);
        if (targetType == PlayerType.Player)
        {
            playerHandCards.Add(drawnCard.Data);
        }
        else
        {
            enemyHandCards.Add(drawnCard.Data);
        }

        TriggerOnHandAutoEffects(drawnCard, targetType, skipHandZoneCheck: true);
        targetRule.RefreshHandCountDisplay();
    }

    private static List<int> CollectUntakenLookedCardIdsStillInDeck(LookResolutionContext context)
    {
        List<int> result = new List<int>();
        if (context?.Entries == null || context.DeckRule == null)
        {
            return result;
        }

        for (int i = 0; i < context.Entries.Count; i++)
        {
            LookedDeckEntry entry = context.Entries[i];
            if (entry == null || entry.CardId < 0)
            {
                continue;
            }

            if (context.TakenCardIds.Contains(entry.CardId))
            {
                continue;
            }

            if (!context.DeckRule.ContainsCardId(entry.CardId))
            {
                continue;
            }

            result.Add(entry.CardId);
        }

        return result;
    }

    private static void RemoveCardIdsFromDeck(CardGameRule deckRule, List<int> cardIds)
    {
        if (deckRule == null || cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            deckRule.TryTakeCardById(cardIds[i], out _);
        }
    }

    private void ApplyReturnLookedRemainderToDeckTop(LookResolutionContext context)
    {
        if (context?.DeckRule == null)
        {
            return;
        }

        List<int> remainder = CollectUntakenLookedCardIdsStillInDeck(context);
        if (remainder.Count == 0)
        {
            return;
        }

        ApplyLookedRemainderDisposition(context, LookedRemainderDispositionChoice.ReturnToDeckTop);
    }

    private void ApplyShuffleLookedRemainderToDeckBottom(LookResolutionContext context)
    {
        ApplyLookedRemainderDisposition(context, LookedRemainderDispositionChoice.ShuffleToDeckBottom);
    }

    private void ApplyLookedRemainderDisposition(
        LookResolutionContext context,
        LookedRemainderDispositionChoice disposition)
    {
        if (context?.DeckRule == null)
        {
            return;
        }

        List<int> remainder = CollectUntakenLookedCardIdsStillInDeck(context);
        if (remainder.Count == 0)
        {
            return;
        }

        RemoveCardIdsFromDeck(context.DeckRule, remainder);
        if (disposition == LookedRemainderDispositionChoice.ReturnToDeckTop)
        {
            context.DeckRule.PrependCardsToTopInOrder(remainder);
            Debug.Log(
                $"[Effect] ReturnLookedRemainderToDeckTop count:{remainder.Count} deck:{context.DeckLabel} "
                + $"by cardId:{context.SourceCard?.Data?.id}");
            return;
        }

        for (int i = remainder.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int tmp = remainder[i];
            remainder[i] = remainder[j];
            remainder[j] = tmp;
        }

        context.DeckRule.AppendCardsToBottom(remainder);
        Debug.Log(
            $"[Effect] ShuffleLookedRemainderToDeckBottom count:{remainder.Count} deck:{context.DeckLabel} "
            + $"by cardId:{context.SourceCard?.Data?.id}");
    }

    private void ApplyChooseLookedRemainderDispositionEffect(
        LookResolutionContext context,
        System.Action onComplete)
    {
        if (context?.DeckRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<int> remainder = CollectUntakenLookedCardIdsStillInDeck(context);
        if (remainder.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (context.OwnerType == PlayerType.Enemy)
        {
            LookedRemainderDispositionChoice pick = UnityEngine.Random.value < 0.5f
                ? LookedRemainderDispositionChoice.ReturnToDeckTop
                : LookedRemainderDispositionChoice.ShuffleToDeckBottom;
            ApplyLookedRemainderDisposition(context, pick);
            onComplete?.Invoke();
            return;
        }

        ShowLookRemainderDispositionChoicePopup(context, remainder.Count, choice =>
        {
            ApplyLookedRemainderDisposition(context, choice);
            onComplete?.Invoke();
        });
    }

    private void ShowLookRemainderDispositionChoicePopup(
        LookResolutionContext context,
        int remainderCount,
        System.Action<LookedRemainderDispositionChoice> onChosen)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onChosen?.Invoke(LookedRemainderDispositionChoice.ShuffleToDeckBottom);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "LookRemainderDispositionChoice",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("DispositionTitle", UIAnchor.TopCenter, 720, 56);
        title.text = $"残り{remainderCount}枚の行き先を選んでください";
        title.fontSize = 24;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("DispositionSubtitle", UIAnchor.TopCenter, 700, 40);
        sub.text = $"対象山札: {context.DeckLabel}";
        sub.fontSize = 18;
        sub.color = new Color(0.85f, 0.9f, 1f, 1f);
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -130f);

        void CloseAndChoose(LookedRemainderDispositionChoice choice)
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            Debug.Log($"[OnLook] ChooseLookedRemainderDisposition → {choice}");
            onChosen?.Invoke(choice);
        }

        Button topBtn = root.CreateChildButton("ReturnToDeckTop");
        RectTransform topRt = topBtn.GetComponent<RectTransform>();
        topRt.sizeDelta = new Vector2(320f, 52f);
        topRt.anchorMin = new Vector2(0.5f, 0.5f);
        topRt.anchorMax = new Vector2(0.5f, 0.5f);
        topRt.pivot = new Vector2(0.5f, 0.5f);
        topRt.anchoredPosition = new Vector2(0f, 24f);
        TextMeshProUGUI topLabel = topBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (topLabel != null)
        {
            topLabel.text = "山札の上に戻す";
        }

        topBtn.onClick.AddListener(() => CloseAndChoose(LookedRemainderDispositionChoice.ReturnToDeckTop));

        Button bottomBtn = root.CreateChildButton("ShuffleToDeckBottom");
        RectTransform bottomRt = bottomBtn.GetComponent<RectTransform>();
        bottomRt.sizeDelta = new Vector2(320f, 52f);
        bottomRt.anchorMin = new Vector2(0.5f, 0.5f);
        bottomRt.anchorMax = new Vector2(0.5f, 0.5f);
        bottomRt.pivot = new Vector2(0.5f, 0.5f);
        bottomRt.anchoredPosition = new Vector2(0f, -44f);
        TextMeshProUGUI bottomLabel = bottomBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (bottomLabel != null)
        {
            bottomLabel.text = "山札の下にランダムで送る";
        }

        bottomBtn.onClick.AddListener(() => CloseAndChoose(LookedRemainderDispositionChoice.ShuffleToDeckBottom));
    }

    private void ShowLookDeckViewOnlyPopup(
        LookResolutionContext context,
        System.Action onClose,
        string subtitle = null)
    {
        ShowLookDeckPopupCore(
            context,
            selectableEntries: null,
            pickCount: 0,
            handOwner: context.OwnerType,
            handRule: null,
            addEffect: null,
            onClose,
            subtitle);
    }

    private void ShowLookDeckPickToHandPopup(
        LookResolutionContext context,
        EffectData addEffect,
        List<LookedDeckEntry> selectableEntries,
        int pickCount,
        PlayerType handOwner,
        CardGameRule handRule,
        System.Action onComplete)
    {
        string featureLabel = addEffect?.FormatTargetFeaturesLabel();
        string subtitle = string.IsNullOrEmpty(featureLabel)
            ? $"見たカードから{pickCount}枚選んで手札に加えられます"
            : $"特性「{featureLabel}」のカードを{pickCount}枚選んで手札に加えられます";
        ShowLookDeckPopupCore(
            context,
            selectableEntries,
            pickCount,
            handOwner,
            handRule,
            addEffect,
            onComplete,
            subtitle);
    }

    private void ShowLookDeckPopupCore(
        LookResolutionContext context,
        List<LookedDeckEntry> selectableEntries,
        int pickCount,
        PlayerType handOwner,
        CardGameRule handRule,
        EffectData addEffect,
        System.Action onClose,
        string subtitle = null)
    {
        if (context == null || context.Entries.Count == 0 || CardImagePrefab == null)
        {
            onClose?.Invoke();
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onClose?.Invoke();
            return;
        }

        bool selectionMode = selectableEntries != null && selectableEntries.Count > 0 && pickCount > 0;
        HashSet<int> selectableIds = new HashSet<int>();
        if (selectionMode)
        {
            for (int i = 0; i < selectableEntries.Count; i++)
            {
                selectableIds.Add(selectableEntries[i].CardId);
            }
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("LookDeckTopPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("LookDeckTitle", UIAnchor.TopCenter, 720, 52);
        title.text = selectionMode
            ? $"山札を見る — {context.DeckLabel}（上から{context.RequestedLookCount}枚）"
            : $"山札を見る（{context.DeckLabel}・上から{context.RequestedLookCount}枚中 {context.Entries.Count}枚）";
        title.fontSize = 24;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        if (!string.IsNullOrEmpty(subtitle))
        {
            TextMeshProUGUI sub = root.CreateChildTextCustom("LookDeckSubtitle", UIAnchor.TopCenter, 700, 36);
            sub.text = subtitle;
            sub.fontSize = 18;
            sub.color = new Color(0.85f, 0.9f, 1f, 1f);
            sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);
        }

        GameObject scrollGo = root.CreateGridScrollView(700, 400, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -100f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        int remainingPicks = pickCount;

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onClose?.Invoke();
        }

        if (content != null)
        {
            for (int i = 0; i < context.Entries.Count; i++)
            {
                LookedDeckEntry entry = context.Entries[i];
                if (entry?.Data == null)
                {
                    continue;
                }

                bool canPick = selectionMode && selectableIds.Contains(entry.CardId);
                GameObject go = Instantiate(CardImagePrefab, content);
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    if (canPick)
                    {
                        cc.SetUp(entry.Data, _ =>
                        {
                            if (remainingPicks <= 0)
                            {
                                return;
                            }

                            TakeLookedEntryToHand(context, handRule, handOwner, entry, addEffect);
                            remainingPicks--;
                            if (remainingPicks <= 0)
                            {
                                ClosePopup();
                            }
                        });
                    }
                    else
                    {
                        cc.SetUp(entry.Data, _ => { });
                    }
                }

                if (!canPick && selectionMode)
                {
                    CanvasGroup cg = go.GetComponent<CanvasGroup>();
                    if (cg == null)
                    {
                        cg = go.AddComponent<CanvasGroup>();
                    }

                    cg.alpha = 0.45f;
                    cg.blocksRaycasts = false;
                }

                TextMeshProUGUI info = go.CreateChildTextCustom("LookDeckCardOrder", UIAnchor.TopLeft, 120, 24);
                info.text = $"#{i + 1}";
                info.fontSize = 14;
                info.color = Color.white;
                info.GetComponent<RectTransform>().anchoredPosition = new Vector2(4f, -4f);
            }
        }

        Button closeBtn = root.CreateChildButton(selectionMode ? "Skip" : "Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(ClosePopup);
    }
}
