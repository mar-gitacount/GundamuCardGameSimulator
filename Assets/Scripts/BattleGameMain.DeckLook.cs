using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>山札トップを見る（Look）効果と OnLook 誘発。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// Look / OnLook UI 専用ルート。
    /// OnAction の activeOnActionPopupRoot と共有すると、アクション終了や相手ターン進行で破壊時 Look が消えるため分離する。
    /// </summary>
    private GameObject _activeLookDeckPopupRoot;

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

    private enum LookDeckPickCommitKind
    {
        AddToHand,
        ChooseToDeckTopThenTrashRemainder,
        DeployToBattle
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

    private void BeginLookDeckPopup(GameObject root)
    {
        DestroyActiveLookDeckPopupIfAny();
        _activeLookDeckPopupRoot = root;
        // OnAction UI は残したまま前面に Look を重ねる（アクション／相手ターンでも破壊時 Look を消さない）
        isOnActionPopupOpen = true;
    }

    private void EndLookDeckPopup(GameObject root)
    {
        if (_activeLookDeckPopupRoot == root)
        {
            _activeLookDeckPopupRoot = null;
        }

        if (root != null)
        {
            Destroy(root);
        }

        // OnAction ポップアップが残っていれば開いたまま、無ければ閉じる
        isOnActionPopupOpen = activeOnActionPopupRoot != null || _activeLookDeckPopupRoot != null;
    }

    private void DestroyActiveLookDeckPopupIfAny()
    {
        if (_activeLookDeckPopupRoot == null)
        {
            return;
        }

        Destroy(_activeLookDeckPopupRoot);
        _activeLookDeckPopupRoot = null;
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
        return deckOwner == PlayerType.Player
            ? GameLocale.T("あなた", "Your")
            : GameLocale.T("相手", "Opponent");
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
            if (effect.abortRemainingChainOnSkip)
            {
                ApplyExileFromTrashEffect(
                    sourceCard,
                    ownerType,
                    effect,
                    onComplete: onChainContinue,
                    onSkipped: () =>
                    {
                        Debug.Log(
                            $"[EffectChain] ExileFromTrash skipped — abort remaining (cardId:{sourceCard?.Data?.id})");
                    });
                return;
            }

            ApplyExileFromTrashEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.AddObservedToHandFromTrash)
        {
            ApplyAddObservedToHandFromTrashEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ChooseOne)
        {
            ApplyChooseOneEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.DeploySelfAsBattleUnit)
        {
            TryApplyDeploySelfAsBattleUnit(sourceCard, ownerType, effect);
            onChainContinue?.Invoke();
            return;
        }

        if (effect != null && effect.type == EffectType.AddFromTrashToHand)
        {
            ApplyAddFromTrashToHandEffect(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ActivateMountedCardOnMain)
        {
            ApplyActivateMountedCardOnMain(sourceCard, ownerType, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ActivateObservedSpecialMoveCommandOnMain)
        {
            ApplyActivateObservedSpecialMoveCommandOnMain(sourceCard, ownerType, effect, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ActivateSelfOnMain)
        {
            ApplyActivateSelfOnMain(sourceCard, ownerType, onChainContinue);
            return;
        }

        if (effect != null && effect.type == EffectType.ReturnMountedPilotToHand)
        {
            ApplyReturnMountedPilotToHandEffect(sourceCard, ownerType);
            onChainContinue?.Invoke();
            return;
        }

        if (effect != null && effect.type == EffectType.DeployBase
            && effect.RequiresDeployBaseFromTrashSelection())
        {
            int baseMagnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
            ApplyDeployBaseEffect(
                sourceCard,
                ownerType,
                effect,
                baseMagnitude > 0 ? baseMagnitude : 1,
                allowBurstSource: false,
                onComplete: onChainContinue);
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
            && !OnLookBlocksContainPickFromLooked(blocks)
            && OnLookBlocksContainRemainderDisposition(blocks))
        {
            ShowLookDeckViewOnlyPopup(context, runChain);
            return;
        }

        runChain();
    }

    private static bool OnLookBlocksContainPickFromLooked(List<TimedEffectData> blocks)
    {
        return OnLookBlocksContainEffectType(blocks, EffectType.AddToHandFromLooked)
            || OnLookBlocksContainEffectType(blocks, EffectType.DeployUnitFromLooked);
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

        if (effect.type == EffectType.DeployUnitFromLooked)
        {
            ApplyDeployUnitFromLookedEffect(
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

        if (effect.type == EffectType.ChooseLookedToDeckTopThenTrashRemainder)
        {
            ApplyChooseLookedToDeckTopThenTrashRemainderEffect(
                context,
                effect,
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
            featureLabel = GameLocale.T("未指定", "Any");
        }

        string typeLabel = effect.FormatTargetCardTypeFilterLabel();
        string filterLabel = string.IsNullOrEmpty(typeLabel)
            ? featureLabel
            : $"{typeLabel}・{featureLabel}";

        if (context.OwnerType == PlayerType.Enemy)
        {
            for (int i = 0; i < pickCount && selectable.Count > 0; i++)
            {
                LookedDeckEntry pick = selectable[0];
                TakeLookedEntryToHand(context, handRule, handOwner, pick, effect);
                selectable.RemoveAt(0);
            }

            ContinueAfterAddToHandFromLooked(context, effect, onComplete);
            return;
        }

        if (selectable.Count == 0)
        {
            Debug.Log(
                $"[OnLook] 手札に加えられるカードなし（条件:{filterLabel}）— 閲覧のみ");
            ShowLookDeckViewOnlyPopup(
                context,
                onComplete,
                GameLocale.T(
                    $"条件「{filterLabel}」に合うカードはありませんでした",
                    $"No cards matched filter \"{filterLabel}\""));
            return;
        }

        string subtitle = effect.revealDiscardedToOpponent
            ? GameLocale.T(
                $"条件に合うカードを1枚選んで OK（相手に公開して手札へ）— {filterLabel}",
                $"Choose 1 matching card, then OK (reveal to opponent, add to hand) — {filterLabel}")
            : GameLocale.T(
                $"条件に合うカードを1枚選んで OK（手札へ）— {filterLabel}",
                $"Choose 1 matching card, then OK (add to hand) — {filterLabel}");
        if (pickCount > 1)
        {
            subtitle = effect.revealDiscardedToOpponent
                ? GameLocale.T(
                    $"手札に加えるカードを選び OK（最大{pickCount}枚・相手に公開）— {filterLabel}",
                    $"Choose cards to add to hand, then OK (up to {pickCount}, reveal) — {filterLabel}")
                : GameLocale.T(
                    $"手札に加えるカードを選び OK（最大{pickCount}枚）— {filterLabel}",
                    $"Choose cards to add to hand, then OK (up to {pickCount}) — {filterLabel}");
        }

        ShowLookDeckPickToHandPopup(
            context,
            effect,
            selectable,
            pickCount,
            handOwner,
            handRule,
            () => ContinueAfterAddToHandFromLooked(context, effect, onComplete),
            subtitle);
    }

    /// <summary>
    /// 見た山札から条件に合うユニットをバトルゾーンへ配備（コストなし・してもよい）。
    /// </summary>
    private void ApplyDeployUnitFromLookedEffect(
        LookResolutionContext context,
        EffectData effect,
        System.Action onComplete)
    {
        if (context == null || effect == null || context.Entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, context.OwnerType, context.SourceCard);
        int pickCount = magnitude > 0 ? magnitude : 1;

        List<LookedDeckEntry> selectable = FilterLookedEntriesForDeployEffect(context.Entries, effect);
        string filterLabel = FormatLookedDeployFilterLabel(effect);

        if (context.OwnerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(context.OwnerType);
            for (int i = 0; i < pickCount && selectable.Count > 0; i++)
            {
                LookedDeckEntry pick = selectable[0];
                TakeLookedEntryToBattle(context, effect, pick);
                selectable.RemoveAt(0);
            }

            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            InvokeAfterOnlineDeployConfirmIfNeeded(onComplete);
            return;
        }

        if (selectable.Count == 0)
        {
            Debug.Log(
                $"[OnLook] 配備できるユニットなし（条件:{filterLabel}）— 閲覧のみ");
            ShowLookDeckViewOnlyPopup(
                context,
                onComplete,
                GameLocale.T(
                    $"条件「{filterLabel}」に合うユニットはありませんでした",
                    $"No units matched filter \"{filterLabel}\""));
            return;
        }

        string subtitle = GameLocale.T(
            $"条件に合うユニットを選んで配備してもよい（最大{pickCount}体）— {filterLabel}",
            $"You may deploy up to {pickCount} matching unit(s) — {filterLabel}");

        ShowLookDeckPickToDeployPopup(
            context,
            effect,
            selectable,
            pickCount,
            () => InvokeAfterOnlineDeployConfirmIfNeeded(onComplete),
            subtitle);
    }

    private static string FormatLookedDeployFilterLabel(EffectData effect)
    {
        if (effect == null)
        {
            return GameLocale.T("未指定", "Any");
        }

        string featureLabel = effect.FormatTargetFeaturesLabel();
        if (string.IsNullOrEmpty(featureLabel))
        {
            featureLabel = GameLocale.T("特徴なし", "Any trait");
        }

        string typeLabel = effect.FormatTargetCardTypeFilterLabel();
        if (string.IsNullOrEmpty(typeLabel))
        {
            typeLabel = GameLocale.T("ユニット", "Unit");
        }

        string statLabel = effect.FormatTargetUnitFilterDescription();
        if (string.IsNullOrEmpty(statLabel))
        {
            return $"{typeLabel}・{featureLabel}";
        }

        return $"{typeLabel}・{featureLabel}・{statLabel}";
    }

    private static List<LookedDeckEntry> FilterLookedEntriesForDeployEffect(
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
            if (entry?.Data != null && effect.MatchesLookedCardDataDeployFilter(entry.Data))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private void TakeLookedEntryToBattle(
        LookResolutionContext context,
        EffectData effect,
        LookedDeckEntry entry)
    {
        if (context?.DeckRule == null || entry?.Data == null || effect == null)
        {
            return;
        }

        if (!entry.Data.IsUnitLike())
        {
            Debug.LogWarning($"[OnLook] DeployUnitFromLooked 非ユニット id:{entry.CardId}");
            return;
        }

        if (!context.DeckRule.TryTakeCardById(entry.CardId, out _))
        {
            Debug.LogWarning($"[OnLook] 山札からの取得に失敗 id:{entry.CardId}");
            return;
        }

        context.TakenCardIds.Add(entry.CardId);

        PlayerType recipient = ResolveDeployRecipientPlayerType(context.OwnerType, effect);
        CardGameRule rule = ResolveDeployRecipientRule(recipient);
        if (rule?.PlayerDeployPanel == null)
        {
            Debug.LogWarning("[OnLook] DeployUnitFromLooked: deploy panel missing");
            return;
        }

        CardController spawned = InstantiateBattleUnit(entry.Data, rule.PlayerDeployPanel);
        if (spawned == null)
        {
            Debug.LogWarning($"[OnLook] DeployUnitFromLooked: spawn failed id:{entry.CardId}");
            return;
        }

        DeployUnitToBattleZone(
            spawned,
            recipient,
            rule,
            effect.deployUnitTriggerOnPlayed,
            fromHand: false,
            deployAsRested: effect.deployUnitAsRested);

        Debug.Log(
            $"[Effect] DeployUnitFromLooked {entry.Data.cardName}(id:{entry.CardId}) → {recipient} "
            + $"by cardId:{context.SourceCard?.Data?.id}");
    }

    /// <summary>手札追加後、必要なら相手公開してから OnLook チェーンを続行する。</summary>
    private void ContinueAfterAddToHandFromLooked(
        LookResolutionContext context,
        EffectData effect,
        System.Action onComplete)
    {
        if (context == null
            || effect == null
            || !effect.revealDiscardedToOpponent
            || context.TakenCardIds == null
            || context.TakenCardIds.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RevealLookedCardsAddedToHandCoroutine(context, onComplete));
    }

    private IEnumerator RevealLookedCardsAddedToHandCoroutine(
        LookResolutionContext context,
        System.Action onComplete)
    {
        if (context?.TakenCardIds == null || context.TakenCardIds.Count == 0)
        {
            onComplete?.Invoke();
            yield break;
        }

        PlayerType handOwner = context.OwnerType;
        List<int> takenIds = new List<int>(context.TakenCardIds);
        for (int i = 0; i < takenIds.Count; i++)
        {
            int cardId = takenIds[i];
            CardData data = DeckSettinObject.Instance != null
                ? DeckSettinObject.Instance.GetCardDataById(cardId)
                : null;
            string cardName = data != null ? data.cardName : $"id:{cardId}";

            if (handOwner == PlayerType.Player && data != null)
            {
                MemorizeEnemyAiPlayerPlayedCard(data, "AddToHandFromLooked");
            }

            string revealTitle = handOwner == PlayerType.Enemy
                ? GameLocale.T(
                    "相手が山札から手札に加えたカード（公開）",
                    "Opponent added a card from deck to hand (revealed)")
                : GameLocale.T(
                    "山札から手札に加えたカードを相手に公開",
                    "Reveal card added from deck to hand");
            yield return WaitForHandDiscardRevealAcknowledgedCoroutine(
                cardId,
                cardName,
                handOwner,
                context.OwnerType,
                isInitiator: handOwner == PlayerType.Player && context.OwnerType == PlayerType.Player,
                revealTitle);
        }

        onComplete?.Invoke();
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
        if (targetType == PlayerType.Player)
        {
            NotifyLocalPlayerHandDeckSnapshot();
        }
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

        DestroyActiveLookDeckPopupIfAny();
        GameObject root = new GameObject(
            "LookRemainderDispositionChoice",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        BeginLookDeckPopup(root);
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("DispositionTitle", UIAnchor.TopCenter, 720, 56);
        if (remainderCount <= 1)
        {
            title.SetLocalizedText(
                "見たカードの置き場所を選んでください",
                "Choose where to place the looked card");
        }
        else
        {
            title.SetLocalizedText(
                $"残りの{remainderCount}枚の置き場所を選んでください",
                $"Choose where to place the remaining {remainderCount} cards");
        }

        title.fontSize = 24;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("DispositionSubtitle", UIAnchor.TopCenter, 700, 40);
        sub.SetLocalizedText($"山札: {context.DeckLabel}", $"Deck: {context.DeckLabel}");
        sub.fontSize = 18;
        sub.color = new Color(0.85f, 0.9f, 1f, 1f);
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -130f);

        void CloseAndChoose(LookedRemainderDispositionChoice choice)
        {
            EndLookDeckPopup(root);
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
            topLabel.SetLocalizedText("山札の上に戻す", "Put on top of deck");
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
            // 1枚ならランダム順は意味がない
            if (remainderCount <= 1)
            {
                bottomLabel.SetLocalizedText("山札の下に置く", "Put on bottom of deck");
            }
            else
            {
                bottomLabel.SetLocalizedText(
                    "山札の下に置く（順番ランダム）",
                    "Put on bottom of deck (random order)");
            }
        }

        bottomBtn.onClick.AddListener(() => CloseAndChoose(LookedRemainderDispositionChoice.ShuffleToDeckBottom));
    }

    private void ApplyChooseLookedToDeckTopThenTrashRemainderEffect(
        LookResolutionContext context,
        EffectData effect,
        System.Action onComplete)
    {
        if (context?.DeckRule == null || context.Entries == null || context.Entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Max(1, ResolveEffectMagnitude(effect, context.OwnerType, context.SourceCard));
        pickCount = Mathf.Min(pickCount, context.Entries.Count);

        if (context.OwnerType == PlayerType.Enemy || context.Entries.Count <= pickCount)
        {
            List<LookedDeckEntry> autoPicks = new List<LookedDeckEntry>(pickCount);
            for (int i = 0; i < pickCount; i++)
            {
                autoPicks.Add(context.Entries[i]);
            }

            CommitLookedToDeckTopThenTrashRemainder(context, autoPicks);
            onComplete?.Invoke();
            return;
        }

        ShowLookDeckPickToTopThenTrashPopup(context, pickCount, onComplete);
    }

    private void CommitLookedToDeckTopThenTrashRemainder(
        LookResolutionContext context,
        List<LookedDeckEntry> chosenTopFirst)
    {
        if (context?.DeckRule == null)
        {
            return;
        }

        HashSet<int> chosenDeckIndexes = new HashSet<int>();
        List<int> topIds = new List<int>();
        if (chosenTopFirst != null)
        {
            for (int i = 0; i < chosenTopFirst.Count; i++)
            {
                LookedDeckEntry pick = chosenTopFirst[i];
                if (pick == null || !chosenDeckIndexes.Add(pick.DeckIndex))
                {
                    continue;
                }

                topIds.Add(pick.CardId);
            }
        }

        List<LookedDeckEntry> toRemove = new List<LookedDeckEntry>();
        List<int> trashIds = new List<int>();
        for (int i = 0; i < context.Entries.Count; i++)
        {
            LookedDeckEntry entry = context.Entries[i];
            if (entry == null)
            {
                continue;
            }

            toRemove.Add(entry);
            if (!chosenDeckIndexes.Contains(entry.DeckIndex))
            {
                trashIds.Add(entry.CardId);
            }
        }

        toRemove.Sort((a, b) => b.DeckIndex.CompareTo(a.DeckIndex));

        WithZoneSyncSuppressed(() =>
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                context.DeckRule.TryTakeCardAtDeckIndex(toRemove[i].DeckIndex, out _);
            }

            if (topIds.Count > 0)
            {
                context.DeckRule.PrependCardsToTopInOrder(topIds);
            }

            for (int i = 0; i < trashIds.Count; i++)
            {
                context.DeckRule.AddCardToTrash(trashIds[i]);
            }
        });

        int deckRemain = context.DeckRule.GetRemainingCount();
        SyncGundamRuleDeckCount(context.DeckOwnerType, deckRemain);
        if (trashIds.Count > 0)
        {
            NotifyLocalZoneDeckToTrash(context.DeckOwnerType, trashIds, deckRemain);
        }

        Debug.Log(
            $"[OnLook] ChooseLookedToDeckTopThenTrashRemainder top:{topIds.Count} trash:{trashIds.Count} "
            + $"deck:{context.DeckLabel} by cardId:{context.SourceCard?.Data?.id}");
    }

    private void ShowLookDeckPickToTopThenTrashPopup(
        LookResolutionContext context,
        int pickCount,
        System.Action onComplete)
    {
        string subtitle = pickCount <= 1
            ? GameLocale.T(
                "1枚選んで OK（山札の上へ。残りはトラッシュ）",
                "Choose 1 card, then OK (put on top; rest to trash)")
            : GameLocale.T(
                $"{pickCount}枚選んで OK（山札の上へ。残りはトラッシュ）",
                $"Choose {pickCount} cards, then OK (put on top; rest to trash)");

        ShowLookDeckPopupCore(
            context,
            context.Entries,
            pickCount,
            context.OwnerType,
            handRule: null,
            addEffect: null,
            onComplete,
            subtitle,
            LookDeckPickCommitKind.ChooseToDeckTopThenTrashRemainder,
            allowSkip: false);
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
        System.Action onComplete,
        string subtitle = null)
    {
        if (string.IsNullOrEmpty(subtitle))
        {
            string featureLabel = addEffect?.FormatTargetFeaturesLabel();
            subtitle = string.IsNullOrEmpty(featureLabel)
                ? GameLocale.T(
                    $"見たカードから{pickCount}枚選んで手札に加えられます",
                    $"Choose up to {pickCount} looked card(s) to add to hand")
                : GameLocale.T(
                    $"特性「{featureLabel}」のカードを{pickCount}枚選んで手札に加えられます",
                    $"Choose up to {pickCount} card(s) with trait \"{featureLabel}\" to add to hand");
        }

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

    private void ShowLookDeckPickToDeployPopup(
        LookResolutionContext context,
        EffectData deployEffect,
        List<LookedDeckEntry> selectableEntries,
        int pickCount,
        System.Action onComplete,
        string subtitle = null)
    {
        if (string.IsNullOrEmpty(subtitle))
        {
            subtitle = GameLocale.T(
                $"見たカードから{pickCount}体まで配備できます",
                $"You may deploy up to {pickCount} looked unit(s)");
        }

        ShowLookDeckPopupCore(
            context,
            selectableEntries,
            pickCount,
            handOwner: context.OwnerType,
            handRule: null,
            addEffect: deployEffect,
            onComplete,
            subtitle,
            LookDeckPickCommitKind.DeployToBattle,
            allowSkip: true);
    }

    private void ShowLookDeckPopupCore(
        LookResolutionContext context,
        List<LookedDeckEntry> selectableEntries,
        int pickCount,
        PlayerType handOwner,
        CardGameRule handRule,
        EffectData addEffect,
        System.Action onClose,
        string subtitle = null,
        LookDeckPickCommitKind commitKind = LookDeckPickCommitKind.AddToHand,
        bool allowSkip = true)
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

        DestroyActiveLookDeckPopupIfAny();
        GameObject root = new GameObject("LookDeckTopPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        BeginLookDeckPopup(root);
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("LookDeckTitle", UIAnchor.TopCenter, 720, 52);
        if (selectionMode)
        {
            title.SetLocalizedText(
                $"山札を見る — {context.DeckLabel}（上から{context.RequestedLookCount}枚）",
                $"Look at deck — {context.DeckLabel} (top {context.RequestedLookCount})");
        }
        else
        {
            title.SetLocalizedText(
                $"山札を見る（{context.DeckLabel} · 上から{context.RequestedLookCount}枚中{context.Entries.Count}枚）",
                $"Look at deck ({context.DeckLabel} · {context.Entries.Count} of top {context.RequestedLookCount})");
        }

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

        // 選択確定用（クリック即手札追加ではなく、選んで OK）
        List<LookedDeckEntry> pendingPicks = new List<LookedDeckEntry>();
        Dictionary<int, GameObject> cardVisualById = new Dictionary<int, GameObject>();

        void ClosePopup()
        {
            // Look OK／確定時に破壊側 effectthink を解除する完了通知を送る
            NotifyOnlineOnDestroyedPlayerAcknowledged();
            EndLookDeckPopup(root);
            onClose?.Invoke();
        }

        void RefreshSelectionVisuals()
        {
            foreach (KeyValuePair<int, GameObject> kv in cardVisualById)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                bool isSelected = false;
                for (int p = 0; p < pendingPicks.Count; p++)
                {
                    if (pendingPicks[p] != null && pendingPicks[p].CardId == kv.Key)
                    {
                        isSelected = true;
                        break;
                    }
                }

                Image img = kv.Value.GetComponent<Image>();
                if (img != null)
                {
                    img.color = isSelected
                        ? new Color(0.55f, 1f, 0.65f, 1f)
                        : Color.white;
                }
            }
        }

        void TogglePendingPick(LookedDeckEntry entry)
        {
            if (entry == null || !selectionMode)
            {
                return;
            }

            for (int i = 0; i < pendingPicks.Count; i++)
            {
                if (pendingPicks[i] != null && pendingPicks[i].CardId == entry.CardId)
                {
                    pendingPicks.RemoveAt(i);
                    RefreshSelectionVisuals();
                    return;
                }
            }

            if (pendingPicks.Count >= pickCount)
            {
                if (pickCount == 1)
                {
                    pendingPicks.Clear();
                    pendingPicks.Add(entry);
                }

                RefreshSelectionVisuals();
                return;
            }

            pendingPicks.Add(entry);
            RefreshSelectionVisuals();
        }

        void ConfirmPendingPicks()
        {
            if (!selectionMode || pendingPicks.Count == 0)
            {
                return;
            }

            if (commitKind == LookDeckPickCommitKind.ChooseToDeckTopThenTrashRemainder)
            {
                if (pendingPicks.Count != pickCount)
                {
                    return;
                }

                CommitLookedToDeckTopThenTrashRemainder(context, pendingPicks);
                ClosePopup();
                return;
            }

            if (commitKind == LookDeckPickCommitKind.DeployToBattle)
            {
                BeginOnlineEffectSyncBatch(context.OwnerType);
                for (int i = 0; i < pendingPicks.Count; i++)
                {
                    TakeLookedEntryToBattle(context, addEffect, pendingPicks[i]);
                }

                FlushOnlineEffectSyncBatch();
                SyncAllResourceViewsFromRule();
                ClosePopup();
                return;
            }

            for (int i = 0; i < pendingPicks.Count; i++)
            {
                TakeLookedEntryToHand(context, handRule, handOwner, pendingPicks[i], addEffect);
            }

            ClosePopup();
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
                cardVisualById[entry.CardId] = go;
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    if (canPick)
                    {
                        LookedDeckEntry entryRef = entry;
                        cc.SetUp(entry.Data, _ => TogglePendingPick(entryRef));
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

        if (selectionMode)
        {
            Button okBtn = root.CreateChildButton("LookDeckOk");
            RectTransform okRt = okBtn.GetComponent<RectTransform>();
            okRt.sizeDelta = new Vector2(180f, 48f);
            okRt.anchorMin = new Vector2(0.5f, 0f);
            okRt.anchorMax = new Vector2(0.5f, 0f);
            okRt.pivot = new Vector2(0.5f, 0f);
            okRt.anchoredPosition = allowSkip ? new Vector2(-110f, 36f) : new Vector2(0f, 36f);
            TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (okLabel != null)
            {
                okLabel.text = "OK";
            }

            okBtn.onClick.AddListener(ConfirmPendingPicks);

            if (allowSkip)
            {
                Button skipBtn = root.CreateChildButton("LookDeckSkip");
                RectTransform skipRt = skipBtn.GetComponent<RectTransform>();
                skipRt.sizeDelta = new Vector2(180f, 48f);
                skipRt.anchorMin = new Vector2(0.5f, 0f);
                skipRt.anchorMax = new Vector2(0.5f, 0f);
                skipRt.pivot = new Vector2(0.5f, 0f);
                skipRt.anchoredPosition = new Vector2(110f, 36f);
                TextMeshProUGUI skipLabel = skipBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (skipLabel != null)
                {
                    skipLabel.text = "Skip";
                }

                skipBtn.onClick.AddListener(ClosePopup);
            }
        }
        else
        {
            Button closeBtn = root.CreateChildButton("Close");
            RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
            closeRt.sizeDelta = new Vector2(180f, 48f);
            closeRt.anchorMin = new Vector2(0.5f, 0f);
            closeRt.anchorMax = new Vector2(0.5f, 0f);
            closeRt.pivot = new Vector2(0.5f, 0f);
            closeRt.anchoredPosition = new Vector2(0f, 36f);
            closeBtn.onClick.AddListener(ClosePopup);
        }
    }
}
