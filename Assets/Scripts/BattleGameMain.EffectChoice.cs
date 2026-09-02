using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <see cref="EffectType.ChooseOne"/> の選択 UI（日英文言・選択→OK）と
/// <see cref="EffectType.RestResource"/> / <see cref="EffectType.AddFromTrashToHand"/> の解決。
/// </summary>
public partial class BattleGameMain
{
    private bool _effectChoiceUiOpen;

    /// <summary>ChooseOne を Cancel したとき true。OnMain のトラッシュ／後続ブロックを抑止する。</summary>
    private bool _chooseOneCancelled;

    /// <summary>ChooseOne / RestResource / AddFromTrashToHand など、選択後に枝効果を順に解決する。</summary>
    private void ApplyChooseOneEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete)
    {
        if (effect == null || !effect.IsChooseOneEffect())
        {
            onComplete?.Invoke();
            return;
        }

        List<int> availableIndices = CollectAvailableChoiceBranchIndices(ownerType, sourceCard, effect);
        if (availableIndices.Count == 0)
        {
            Debug.Log(
                $"[ChooseOne] 選択可能な効果がありません (cardId:{sourceCard?.Data?.id})。");
            onComplete?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy || _applyingRemoteBattleAction)
        {
            int autoIndex = PickEnemyAiChoiceBranchIndex(ownerType, sourceCard, effect, availableIndices);
            ResolveChooseOneBranch(sourceCard, ownerType, effect, autoIndex, onComplete);
            return;
        }

        ShowEffectChoicePopup(sourceCard, ownerType, effect, availableIndices, chosenIndex =>
        {
            if (chosenIndex < 0)
            {
                // バースト中は Cancel でも第一候補（通常は手札へ）を解決し、トラッシュ落ちを防ぐ
                if (IsResolvingBurstEffect && availableIndices.Count > 0)
                {
                    ResolveChooseOneBranch(sourceCard, ownerType, effect, availableIndices[0], onComplete);
                    return;
                }

                _chooseOneCancelled = true;
                Debug.Log($"[ChooseOne] cancelled by player (cardId:{sourceCard?.Data?.id})");
                onComplete?.Invoke();
                return;
            }

            ResolveChooseOneBranch(sourceCard, ownerType, effect, chosenIndex, onComplete);
        });
    }

    private void ResolveChooseOneBranch(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData chooseEffect,
        int branchIndex,
        Action onComplete)
    {
        if (chooseEffect?.choiceBranches == null
            || branchIndex < 0
            || branchIndex >= chooseEffect.choiceBranches.Length)
        {
            onComplete?.Invoke();
            return;
        }

        EffectChoiceBranch branch = chooseEffect.choiceBranches[branchIndex];
        IReadOnlyList<EffectData> branchEffects = branch.GetResolvedEffects();
        Debug.Log(
            $"[ChooseOne] selected branch:{branchIndex} "
            + $"ja:{branch.GetDisplayLabelJa()} en:{branch.GetDisplayLabelEn()} "
            + $"by cardId:{sourceCard?.Data?.id}");
        TryExecuteBranchEffectChain(sourceCard, ownerType, branchEffects, 0, onComplete);
    }

    /// <summary>ChooseOne で選ばれた枝、および再利用可能な効果リストチェーン。</summary>
    private void TryExecuteBranchEffectChain(
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
            TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "ChooseOneBranch"))
        {
            TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.ChooseOne)
        {
            ApplyChooseOneEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (effect.type == EffectType.AddFromTrashToHand)
        {
            ApplyAddFromTrashToHandEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            ApplyDeployUnitEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            TryExecuteManualHandSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            if (ownerType == PlayerType.Enemy)
            {
                EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, sourceCard, null, null);
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
                }

                TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone),
                () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteBranchEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
    }

    private void ShowEffectChoicePopup(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<int> availableIndices,
        Action<int> onChosen)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onChosen?.Invoke(availableIndices.Count > 0 ? availableIndices[0] : 0);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "EffectChoiceSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        _effectChoiceUiOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.68f);
        dim.raycastTarget = true;

        string sourceName = sourceCard?.Data?.cardName ?? GameLocale.T("このカード", "this card");
        TextMeshProUGUI title = root.CreateChildTextCustom("EffectChoiceTitle", UIAnchor.TopCenter, 920, 52);
        title.SetLocalizedText(
            $"効果を選んで OK — {sourceName}",
            $"Choose an effect — OK — {sourceName}");
        title.fontSize = 28;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -36f);

        string promptJa = !string.IsNullOrWhiteSpace(effect.choicePromptJa)
            ? effect.choicePromptJa.Trim()
            : "効果を1つ選んでから OK を押すと発動します。Cancel で中止します。";
        string promptEn = !string.IsNullOrWhiteSpace(effect.choicePromptEn)
            ? effect.choicePromptEn.Trim()
            : "Select 1 effect, then press OK. Press Cancel to abort.";

        TextMeshProUGUI promptText = root.CreateChildTextCustom("EffectChoicePrompt", UIAnchor.TopCenter, 920, 52);
        promptText.SetLocalizedText(promptJa, promptEn);
        promptText.fontSize = GameLocale.IsJapanese ? 18 : 16;
        promptText.color = new Color(0.92f, 0.96f, 1f, 1f);
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.enableWordWrapping = true;
        promptText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -96f);

        int branchCount = effect.choiceBranches != null ? effect.choiceBranches.Length : 0;
        if (branchCount <= 0)
        {
            onChosen?.Invoke(-1);
            return;
        }

        int selectedIndex = availableIndices.Count > 0 ? availableIndices[0] : -1;
        List<Image> optionBgs = new List<Image>();
        List<int> optionBranchIndices = new List<int>();
        Button okBtn = null;
        TextMeshProUGUI okLabel = null;

        void RefreshSelection()
        {
            for (int i = 0; i < optionBgs.Count; i++)
            {
                bool selected = optionBranchIndices[i] == selectedIndex;
                optionBgs[i].color = selected
                    ? new Color(0.22f, 0.48f, 0.78f, 0.98f)
                    : new Color(0.16f, 0.28f, 0.48f, 0.98f);
            }

            bool canOk = selectedIndex >= 0 && availableIndices.Contains(selectedIndex);
            if (okBtn != null)
            {
                okBtn.interactable = canOk;
            }

            if (okLabel != null)
            {
                okLabel.color = canOk ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        float cardWidth = Mathf.Clamp(860f / branchCount - 16f, 280f, 420f);
        float cardHeight = 260f;
        float gap = 24f;
        float totalWidth = branchCount * cardWidth + (branchCount - 1) * gap;
        float startX = -totalWidth * 0.5f + cardWidth * 0.5f;

        bool consumed = false;
        void CloseWithResult(int index)
        {
            if (consumed)
            {
                return;
            }

            consumed = true;
            _effectChoiceUiOpen = false;
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onChosen?.Invoke(index);
        }

        for (int bi = 0; bi < branchCount; bi++)
        {
            EffectChoiceBranch branch = effect.choiceBranches[bi];
            if (branch == null)
            {
                continue;
            }

            bool available = availableIndices.Contains(bi);
            ResolveEffectChoiceLabels(branch, bi, out string ja, out string en);
            if (!available)
            {
                ja = "【選択不可】\n" + ja;
                en = "[Unavailable]\n" + en;
            }

            GameObject optionGo = new GameObject(
                $"ChoiceOption_{bi}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            optionGo.transform.SetParent(root.transform, false);
            RectTransform optionRt = optionGo.GetComponent<RectTransform>();
            optionRt.anchorMin = new Vector2(0.5f, 0.5f);
            optionRt.anchorMax = new Vector2(0.5f, 0.5f);
            optionRt.pivot = new Vector2(0.5f, 0.5f);
            optionRt.sizeDelta = new Vector2(cardWidth, cardHeight);
            optionRt.anchoredPosition = new Vector2(startX + bi * (cardWidth + gap), 10f);

            Image bg = optionGo.GetComponent<Image>();
            bg.color = available
                ? new Color(0.16f, 0.28f, 0.48f, 0.98f)
                : new Color(0.12f, 0.12f, 0.14f, 0.85f);
            bg.raycastTarget = true;

            Button optionBtn = optionGo.GetComponent<Button>();
            optionBtn.interactable = available;
            optionBtn.targetGraphic = bg;

            TextMeshProUGUI indexLabel = optionGo.CreateChildTextCustom($"Index_{bi}", UIAnchor.TopCenter, (int)cardWidth - 24, 36);
            indexLabel.SetLocalizedText($"効果 {bi + 1}", $"Effect {bi + 1}");
            indexLabel.fontSize = 20;
            indexLabel.fontStyle = FontStyles.Bold;
            indexLabel.color = available
                ? new Color(1f, 0.92f, 0.45f, 1f)
                : new Color(0.55f, 0.55f, 0.55f, 1f);
            indexLabel.alignment = TextAlignmentOptions.Center;
            indexLabel.raycastTarget = false;
            indexLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -12f);

            TextMeshProUGUI bodyLabel = optionGo.CreateChildTextCustom($"Body_{bi}", UIAnchor.TopCenter, (int)cardWidth - 28, 200);
            bodyLabel.SetLocalizedText(ja, en);
            bodyLabel.fontSize = GameLocale.IsJapanese ? 17 : 15;
            bodyLabel.color = available ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            bodyLabel.alignment = TextAlignmentOptions.Top;
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Overflow;
            bodyLabel.raycastTarget = false;
            bodyLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -52f);

            if (available)
            {
                int captured = bi;
                optionBtn.onClick.AddListener(() =>
                {
                    selectedIndex = captured;
                    RefreshSelection();
                });
                optionBgs.Add(bg);
                optionBranchIndices.Add(bi);
            }
        }

        okBtn = root.CreateChildButton(GameLocale.T("OK", "OK"));
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(-120f, 40f);
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.SetLocalizedText("OK", "OK");
            okLabel.fontSize = 22;
        }

        Button cancelBtn = root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(200f, 52f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(120f, 40f);
        TextMeshProUGUI cancelLabel = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (cancelLabel != null)
        {
            cancelLabel.SetLocalizedText("キャンセル", "Cancel");
            cancelLabel.fontSize = 22;
        }

        okBtn.onClick.AddListener(() =>
        {
            if (!okBtn.interactable || selectedIndex < 0)
            {
                return;
            }

            CloseWithResult(selectedIndex);
        });

        cancelBtn.onClick.AddListener(() => CloseWithResult(-1));

        RefreshSelection();
    }

    /// <summary>
    /// 選択肢の日英文言はカード／named effect の labelJa・labelEn を使う（カードごとに差し替え可能）。
    /// </summary>
    private static void ResolveEffectChoiceLabels(
        EffectChoiceBranch branch,
        int branchIndex,
        out string ja,
        out string en)
    {
        ja = branch != null ? branch.GetDisplayLabelJa() : string.Empty;
        en = branch != null ? branch.GetDisplayLabelEn() : string.Empty;

        if (string.IsNullOrWhiteSpace(ja))
        {
            ja = $"効果 {branchIndex + 1}";
        }
        else
        {
            ja = ja.Trim();
        }

        if (string.IsNullOrWhiteSpace(en))
        {
            en = $"Effect {branchIndex + 1}";
        }
        else
        {
            en = en.Trim();
        }
    }

    private List<int> CollectAvailableChoiceBranchIndices(
        PlayerType ownerType,
        CardController sourceCard,
        EffectData chooseEffect,
        int? resourceOverride = null)
    {
        List<int> indices = new List<int>();
        if (chooseEffect?.choiceBranches == null)
        {
            return indices;
        }

        for (int i = 0; i < chooseEffect.choiceBranches.Length; i++)
        {
            if (IsChoiceBranchAvailable(ownerType, sourceCard, chooseEffect.choiceBranches[i], resourceOverride))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private bool IsChoiceBranchAvailable(
        PlayerType ownerType,
        CardController sourceCard,
        EffectChoiceBranch branch,
        int? resourceOverride = null)
    {
        if (branch == null)
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = branch.GetResolvedEffects();
        if (effects.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type == EffectType.RestResource)
            {
                // Place rested Resource は追加配置のため、利用可能リソース不足では弾かない
                continue;
            }
            else if (effect.type == EffectType.AddFromTrashToHand)
            {
                if (CollectAddFromTrashToHandCandidates(ownerType, effect).Count <= 0)
                {
                    return false;
                }
            }
            else if (effect.type == EffectType.ChooseOne)
            {
                if (CollectAvailableChoiceBranchIndices(ownerType, sourceCard, effect, resourceOverride).Count <= 0)
                {
                    return false;
                }
            }
            else if (effect.type == EffectType.DeploySelfAsBattleUnit)
            {
                EffectActivationContext ctx = BuildActivationContext(ownerType, sourceCard);
                if (!ShouldApplyChainedEffect(effect, ctx, "ChooseOneBranch"))
                {
                    int mfInTrash = CountOwnerTrashFeatureMatches(ownerType, 12);
                    Debug.Log(
                        $"[ChooseOne] DeploySelfAsBattleUnit 不可: 条件未達 "
                        + $"(cardId:{sourceCard?.Data?.id} trashMF(id12)={mfInTrash} need>=3)");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>オーナーのトラッシュに指定 featureId を持つカードが何枚あるか（ChooseOne 診断用）。</summary>
    private int CountOwnerTrashFeatureMatches(PlayerType ownerType, int featureId)
    {
        if (featureId <= 0 || DeckSettinObject.Instance == null)
        {
            return 0;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        IReadOnlyList<int> trashIds = rule != null ? rule.GetTrashCardIds() : null;
        if (trashIds == null || trashIds.Count == 0)
        {
            return 0;
        }

        CardFeatureRegistry.EnsureLoaded();
        CardFeatureData feature = CardFeatureRegistry.GetById(featureId);
        if (feature == null)
        {
            return 0;
        }

        return TrashCardQuery.CountByAnyFeature(trashIds, new[] { feature });
    }

    private int PickEnemyAiChoiceBranchIndex(
        PlayerType ownerType,
        CardController sourceCard,
        EffectData chooseEffect,
        List<int> availableIndices)
    {
        if (availableIndices == null || availableIndices.Count == 0)
        {
            return 0;
        }

        // トラッシュ回収を優先（Mutual Attraction 等）
        for (int i = 0; i < availableIndices.Count; i++)
        {
            int branchIndex = availableIndices[i];
            EffectChoiceBranch branch = chooseEffect.choiceBranches[branchIndex];
            IReadOnlyList<EffectData> effects = branch.GetResolvedEffects();
            for (int e = 0; e < effects.Count; e++)
            {
                if (effects[e] != null && effects[e].type == EffectType.AddFromTrashToHand)
                {
                    return branchIndex;
                }
            }
        }

        return availableIndices[0];
    }

    /// <summary>
    /// OnMain ブロックに ChooseOne だけがあり、どの枝も選べないときは発動不可。
    /// </summary>
    private bool HasMeaningfulOnMainEffectsIncludingChooseOne(
        PlayerType side,
        CardController source,
        TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed?.GetResolvedEffects();
        if (effects == null || effects.Count == 0)
        {
            return false;
        }

        int? resourceAfterCost = EstimateResourceAfterOnMainCost(side, source, timed);
        EffectActivationContext activationContext = BuildActivationContext(side, source);
        bool hasAny = false;
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type == EffectType.ChooseOne)
            {
                if (CollectAvailableChoiceBranchIndices(side, source, effect, resourceAfterCost).Count > 0)
                {
                    hasAny = true;
                }

                continue;
            }

            if (effect.type == EffectType.AddFromTrashToHand)
            {
                // 対象選択必須のため、候補が無いときはメイン発動不可（NT研究所所長等）
                if (CollectAddFromTrashToHandCandidates(side, effect).Count > 0)
                {
                    hasAny = true;
                }

                continue;
            }

            if (EffectRequiresManualUnitSelection(effect))
            {
                if (effect.HasEffectActivationConditions()
                    && !EffectActivationEvaluator.AreAllConditionsMet(
                        effect.effectActivationConditions,
                        activationContext))
                {
                    continue;
                }

                if (ResolveSelectableEffectTargets(source, side, effect).Count > 0)
                {
                    hasAny = true;
                }

                continue;
            }

            hasAny = true;
        }

        return hasAny;
    }

    private int EstimateResourceAfterOnMainCost(PlayerType side, CardController source, TimedEffectData timed)
    {
        Gundam2024RuleScript.PlayerState state = side == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int cost = GetOnMainActivationCost(source, timed, side);
        if (cost <= 0)
        {
            return state.resource;
        }

        int exNeeded = Gundam2024RuleScript.GetExNeededForCost(state, cost);
        int fromNormal = Mathf.Max(0, cost - exNeeded);
        return Mathf.Max(0, state.resource - fromNormal);
    }

    private void ApplyRestResourceEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null || gundamRule == null)
        {
            return;
        }

        int amount = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (amount <= 0)
        {
            amount = 1;
        }

        PlayerType targetPlayer = ResolveAddExResourceTargetPlayer(ownerType, effect.target);
        Gundam2024RuleScript.PlayerSide side = ToRuleSide(targetPlayer);
        if (!gundamRule.TryPlaceRestedResource(side, amount))
        {
            Debug.Log(
                $"[Effect] PlaceRestedResource failed x{amount} target:{targetPlayer} "
                + $"by cardId:{sourceCard?.Data?.id}");
            return;
        }

        SyncResourceViewsFromRule(side);
        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            NotifyLocalPlayerResourceSnapshotAfterCost();
        }

        Gundam2024RuleScript.PlayerState after = targetPlayer == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        Debug.Log(
            $"[Effect] PlaceRestedResource x{amount} target:{targetPlayer} "
            + $"level:{after.level} resource:{after.resource} TotalLevel:{after.TotalLevel} "
            + $"by cardId:{sourceCard?.Data?.id}");
    }

    private void ApplyActivateResourceEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null || gundamRule == null)
        {
            return;
        }

        int amount = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (amount <= 0)
        {
            amount = 1;
        }

        PlayerType targetPlayer = ResolveAddExResourceTargetPlayer(ownerType, effect.target);
        Gundam2024RuleScript.PlayerSide side = ToRuleSide(targetPlayer);
        if (!gundamRule.TryActivateRestedResource(side, amount))
        {
            Debug.Log(
                $"[Effect] ActivateResource failed x{amount} target:{targetPlayer} "
                + $"by cardId:{sourceCard?.Data?.id}");
            return;
        }

        SyncResourceViewsFromRule(side);
        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            NotifyLocalPlayerResourceSnapshotAfterCost();
        }

        Gundam2024RuleScript.PlayerState after = targetPlayer == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        Debug.Log(
            $"[Effect] ActivateResource x{amount} target:{targetPlayer} "
            + $"level:{after.level} resource:{after.resource} TotalLevel:{after.TotalLevel} "
            + $"by cardId:{sourceCard?.Data?.id}");
    }

    private List<TrashExileCandidate> CollectAddFromTrashToHandCandidates(
        PlayerType ownerType,
        EffectData effect)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        return CollectTrashExileCandidates(trashRule, effect);
    }

    private void ApplyAddFromTrashToHandEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        PlayerType trashOwner = effect != null && effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        if (trashRule == null || effect == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<TrashExileCandidate> candidates = CollectTrashExileCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Max(1, ResolveEffectMagnitude(effect, ownerType, sourceCard));
        if (ownerType == PlayerType.Enemy)
        {
            StartCoroutine(ApplyAddFromTrashToHandEnemyCoroutine(
                trashRule,
                trashOwner,
                ownerType,
                effect,
                candidates,
                pickCount,
                onComplete));
            return;
        }

        StartCoroutine(ShowAddFromTrashToHandSelectionCoroutine(
            sourceCard,
            trashRule,
            trashOwner,
            effect,
            candidates,
            pickCount,
            onComplete));
    }

    private IEnumerator ApplyAddFromTrashToHandEnemyCoroutine(
        CardGameRule trashRule,
        PlayerType handOwner,
        PlayerType effectOwner,
        EffectData effect,
        List<TrashExileCandidate> candidates,
        int pickCount,
        Action onComplete)
    {
        List<int> takenCardIds = ResolveAddFromTrashToHandAuto(trashRule, handOwner, candidates, pickCount);
        if (takenCardIds.Count > 0 && effect != null && effect.revealDiscardedToOpponent)
        {
            yield return RevealTrashToHandAddedCardsCoroutine(handOwner, effectOwner, takenCardIds);
        }

        onComplete?.Invoke();
    }

    private List<int> ResolveAddFromTrashToHandAuto(
        CardGameRule trashRule,
        PlayerType handOwner,
        List<TrashExileCandidate> candidates,
        int pickCount)
    {
        List<int> takenCardIds = new List<int>();
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) =>
        {
            int levelCompare = (b.Data != null ? b.Data.level : 0).CompareTo(a.Data != null ? a.Data.level : 0);
            return levelCompare != 0 ? levelCompare : b.TrashIndex.CompareTo(a.TrashIndex);
        });

        int taken = 0;
        for (int i = 0; i < ordered.Count && taken < pickCount; i++)
        {
            int movedId = TryMoveTrashCandidateToHand(trashRule, handOwner, ordered[i]);
            if (movedId >= 0)
            {
                takenCardIds.Add(movedId);
                taken++;
            }
        }

        return takenCardIds;
    }

    private IEnumerator RevealTrashToHandAddedCardsCoroutine(
        PlayerType handOwner,
        PlayerType effectOwner,
        List<int> takenCardIds)
    {
        if (takenCardIds == null || takenCardIds.Count == 0)
        {
            yield break;
        }

        for (int i = 0; i < takenCardIds.Count; i++)
        {
            int cardId = takenCardIds[i];
            CardData data = DeckSettinObject.Instance != null
                ? DeckSettinObject.Instance.GetCardDataById(cardId)
                : null;
            string cardName = data != null ? data.cardName : $"id:{cardId}";

            if (handOwner == PlayerType.Player && data != null)
            {
                MemorizeEnemyAiPlayerPlayedCard(data, "AddFromTrashToHand");
            }

            string revealTitle = handOwner == PlayerType.Enemy
                ? GameLocale.T(
                    "相手がトラッシュから手札に加えたカード（公開）",
                    "Opponent added a card from Trash to hand (revealed)")
                : GameLocale.T(
                    "トラッシュから手札に加えたカードを相手に公開",
                    "Reveal card added from Trash to hand");
            yield return WaitForHandDiscardRevealAcknowledgedCoroutine(
                cardId,
                cardName,
                handOwner,
                effectOwner,
                isInitiator: handOwner == PlayerType.Player && effectOwner == PlayerType.Player,
                revealTitle);
        }
    }

    private System.Collections.IEnumerator ShowAddFromTrashToHandSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        PlayerType trashOwner,
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

        string filterLabel = FormatAddFromTrashToHandFilterLabel(effect);
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TrashToHandSelect",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("TrashToHandTitle", UIAnchor.TopCenter, 780, 48);
        title.SetLocalizedText("トラッシュから手札に加える", "Add from Trash to hand");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        string sourceName = sourceCard?.Data?.cardName ?? GameLocale.T("このカード", "this card");
        TextMeshProUGUI subtitle = root.CreateChildTextCustom("TrashToHandSubtitle", UIAnchor.TopCenter, 780, 40);
        if (effect != null && effect.revealDiscardedToOpponent)
        {
            subtitle.SetLocalizedText(
                $"{sourceName}: {filterLabel} を選んで OK（相手に公開）",
                $"{sourceName}: choose {filterLabel}, then OK (reveal to opponent)");
        }
        else
        {
            subtitle.SetLocalizedText(
                $"{sourceName}: {filterLabel} を選んで OK",
                $"{sourceName}: choose {filterLabel}, then OK");
        }
        subtitle.fontSize = 17;
        subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        GameObject scrollGo = root.CreateGridScrollView(760, 400, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -96f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        HashSet<int> selectedTrashIndices = new HashSet<int>();
        List<int> takenCardIds = new List<int>();
        Dictionary<int, GameObject> cardObjectsByTrashIndex = new Dictionary<int, GameObject>();
        bool dismissed = false;
        bool confirmed = false;
        Button okBtn = null;
        TextMeshProUGUI okLabel = null;

        void RefreshSelectionVisuals()
        {
            foreach (KeyValuePair<int, GameObject> pair in cardObjectsByTrashIndex)
            {
                SetExileTrashSelectionHighlight(pair.Value, selectedTrashIndices.Contains(pair.Key));
            }

            bool ready = selectedTrashIndices.Count >= pickCount;
            if (okBtn != null)
            {
                okBtn.interactable = ready;
            }

            if (okLabel != null)
            {
                okLabel.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        }

        if (content != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                TrashExileCandidate candidate = candidates[i];
                if (candidate.Data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                cardObjectsByTrashIndex[candidate.TrashIndex] = go;
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    int capturedIndex = candidate.TrashIndex;
                    cc.SetUp(candidate.Data, _ =>
                    {
                        selectedTrashIndices.Clear();
                        selectedTrashIndices.Add(capturedIndex);
                        RefreshSelectionVisuals();
                    });
                    go.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
                }

                SetExileTrashSelectionHighlight(go, false);
            }
        }

        okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(220f, 50f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 36f);
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.onClick.AddListener(() =>
        {
            if (!okBtn.interactable || confirmed)
            {
                return;
            }

            confirmed = true;
            dismissed = true;
            List<int> selected = new List<int>(selectedTrashIndices);
            selected.Sort((a, b) => b.CompareTo(a));
            for (int i = 0; i < selected.Count; i++)
            {
                int trashIndex = selected[i];
                for (int c = 0; c < candidates.Count; c++)
                {
                    if (candidates[c].TrashIndex != trashIndex)
                    {
                        continue;
                    }

                    int movedId = TryMoveTrashCandidateToHand(trashRule, trashOwner, candidates[c]);
                    if (movedId >= 0)
                    {
                        takenCardIds.Add(movedId);
                    }

                    break;
                }
            }

            ClosePopup();
        });

        RefreshSelectionVisuals();
        while (!dismissed && root != null)
        {
            yield return null;
        }

        if (takenCardIds.Count > 0 && effect != null && effect.revealDiscardedToOpponent)
        {
            yield return RevealTrashToHandAddedCardsCoroutine(trashOwner, PlayerType.Player, takenCardIds);
        }

        onComplete?.Invoke();
    }

    private static string FormatAddFromTrashToHandFilterLabel(EffectData effect)
    {
        if (effect == null)
        {
            return GameLocale.T("カード", "card");
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (effect.filterByTargetCardType)
        {
            sb.Append(CardTypeExtensions.GetDisplayName(effect.targetCardType));
        }

        if (effect.HasTargetUnitStatFilter())
        {
            if (sb.Length > 0)
            {
                sb.Append("・");
            }

            sb.Append(effect.FormatTargetUnitFilterDescription());
        }

        string featureLabel = effect.FormatTargetFeaturesLabel();
        if (!string.IsNullOrEmpty(featureLabel))
        {
            if (sb.Length > 0)
            {
                sb.Append("・");
            }

            sb.Append(featureLabel);
        }

        if (effect.HasTargetPilotIdFilter())
        {
            if (sb.Length > 0)
            {
                sb.Append("・");
            }

            CardPilotIdData pilotId = CardPilotIdRegistry.GetById(effect.targetPilotId);
            string pilotLabel = pilotId != null && !string.IsNullOrEmpty(pilotId.displayName)
                ? pilotId.displayName
                : $"PilotId {effect.targetPilotId}";
            sb.Append(pilotLabel);
        }

        return sb.Length > 0 ? sb.ToString() : GameLocale.T("カード", "card");
    }
}
