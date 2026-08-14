using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>コマンド／効果発動時のカードプレビュー UI。</summary>
public partial class BattleGameMain
{
    private const float BattleCardPreviewWidth = 120f;
    private const float BattleCardPreviewHeight = 168f;
    private const float CommandUsePreviewHoldSeconds = 1.15f;
    /// <summary>ブロック／アクションタイミングの一覧・タイトル横幅。</summary>
    private const int AttackFlowPopupContentWidth = 480;
    /// <summary>バトル予定カードの左右オフセット（横幅480内に収める）。</summary>
    private const float AttackMatchupCardOffsetX = 110f;

    /// <summary>敵コマンド確認パネルを組み立てる。戻り値はルート（null なら失敗）。</summary>
    private GameObject BuildCommandUseAcknowledgementPanel(
        string titleText,
        CardController command,
        CardController attackingUnitInAttackFlow,
        List<CardController> targetCards)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || command == null || command.Data == null || CardImagePrefab == null)
        {
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("CommandUseAcknowledgement", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("AckTitle", UIAnchor.TopCenter, 760, 44);
        title.text = titleText;
        title.fontSize = 24;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -22f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("AckHint", UIAnchor.TopCenter, 760, 28);
        hint.text = "内容を確認して OK で続行";
        hint.fontSize = 16;
        hint.color = new Color(0.85f, 0.9f, 1f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -54f);

        bool showAttack = attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null;
        float cmdX = showAttack ? 70f : 0f;
        string playedLabel = command.Data.IsCommand() ? "発動コマンド" : "配備ユニット";
        AppendNonInteractiveCardPreview(root, command, playedLabel, new Vector2(cmdX, -100f));

        if (showAttack)
        {
            AppendNonInteractiveCardPreview(
                root,
                attackingUnitInAttackFlow,
                "攻撃中",
                new Vector2(-170f, -100f),
                new Color(1f, 0.45f, 0.45f, 1f));
        }

        if (targetCards != null && targetCards.Count > 0)
        {
            TextMeshProUGUI tgtTitle = root.CreateChildTextCustom("TargetsTitle", UIAnchor.TopCenter, 240, 26);
            tgtTitle.text = "→ 対象";
            tgtTitle.fontSize = 19;
            tgtTitle.color = Color.white;
            tgtTitle.alignment = TextAlignmentOptions.Center;
            tgtTitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(cmdX + 95f, -175f);

            int shown = Mathf.Min(targetCards.Count, 6);
            float startX = -(shown - 1) * 62f;
            for (int i = 0; i < shown; i++)
            {
                CardController t = targetCards[i];
                if (t == null || t.Data == null)
                {
                    continue;
                }

                bool hl = showAttack && t == attackingUnitInAttackFlow;
                AppendNonInteractiveCardPreview(
                    root,
                    t,
                    t.Data.cardName,
                    new Vector2(startX + i * 124f, -290f),
                    hl ? new Color(1f, 0.4f, 0.4f, 1f) : Color.white);
            }

            if (targetCards.Count > shown)
            {
                TextMeshProUGUI more = root.CreateChildTextCustom("MoreTargets", UIAnchor.TopCenter, 220, 22);
                more.text = $"他 {targetCards.Count - shown} 件";
                more.fontSize = 14;
                more.color = Color.gray;
                more.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -470f);
            }
        }
        else
        {
            TextMeshProUGUI noTarget = root.CreateChildTextCustom("NoTargets", UIAnchor.TopCenter, 400, 24);
            noTarget.text = "（対象ユニットなし / 全体効果）";
            noTarget.fontSize = 15;
            noTarget.color = Color.gray;
            noTarget.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -280f);
        }

        return root;
    }

    /// <summary>コマンドと対象を表示し、OK までゲーム進行を止める（<see cref="isOnActionPopupOpen"/>）。</summary>
    private IEnumerator ShowCommandUseAcknowledgementCoroutine(
        CardController command,
        CardController attackingUnitInAttackFlow,
        List<CardController> targetCards,
        string titleText)
    {
        GameObject root = BuildCommandUseAcknowledgementPanel(
            titleText,
            command,
            attackingUnitInAttackFlow,
            targetCards);
        if (root == null)
        {
            yield break;
        }

        bool acknowledged = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 42f);
        okBtn.onClick.AddListener(() =>
        {
            acknowledged = true;
            Destroy(root);
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
            }

            isOnActionPopupOpen = activeOnActionPopupRoot != null || _activeLookDeckPopupRoot != null;
        });

        yield return new WaitUntil(() => acknowledged);
    }

    private void AppendCardLiveStatOverlay(GameObject cardGo, CardController liveCard, Color statColor)
    {
        if (cardGo == null || liveCard == null || liveCard.Data == null)
        {
            return;
        }

        GameObject statBg = new GameObject("StatBg", typeof(RectTransform), typeof(Image));
        statBg.transform.SetParent(cardGo.transform, false);
        RectTransform statBgRt = statBg.GetComponent<RectTransform>();
        statBgRt.anchorMin = new Vector2(0f, 0f);
        statBgRt.anchorMax = new Vector2(1f, 0f);
        statBgRt.pivot = new Vector2(0.5f, 0f);
        statBgRt.sizeDelta = new Vector2(0f, 36f);
        statBgRt.anchoredPosition = Vector2.zero;
        Image statBgImg = statBg.GetComponent<Image>();
        statBgImg.color = new Color(0f, 0f, 0f, 0.6f);
        statBgImg.raycastTarget = false;

        string statLine = liveCard.Data.IsCommand()
            ? $"COST {liveCard.CurrentCost}"
            : $"AP {liveCard.CurrentPower}  HP {liveCard.CurrentHp}" + (liveCard.IsRestState ? "  REST" : "");

        TextMeshProUGUI statText = statBg.CreateChildTextCustom("StatText", UIAnchor.FullSize, 116, 34);
        statText.text = statLine;
        statText.fontSize = 13;
        statText.color = statColor;
        statText.alignment = TextAlignmentOptions.Center;
        statText.enableWordWrapping = true;
    }

    private GameObject AppendNonInteractiveCardPreview(
        GameObject parent,
        CardController liveCard,
        string caption,
        Vector2 anchoredPosition,
        Color? statColor = null)
    {
        if (parent == null || liveCard == null || liveCard.Data == null || CardImagePrefab == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(caption))
        {
            TextMeshProUGUI cap = parent.CreateChildTextCustom("CardCaption", UIAnchor.TopCenter, 220, 24);
            cap.text = caption;
            cap.fontSize = 15;
            cap.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            cap.alignment = TextAlignmentOptions.Center;
            cap.GetComponent<RectTransform>().anchoredPosition = anchoredPosition + new Vector2(0f, 10f);
        }

        GameObject go = Instantiate(CardImagePrefab, parent.transform);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(BattleCardPreviewWidth, BattleCardPreviewHeight);
        rt.anchoredPosition = anchoredPosition;

        CardController preview = go.GetComponent<CardController>();
        if (preview != null)
        {
            preview.SetUp(liveCard.Data, _ => { });
        }

        AppendCardLiveStatOverlay(go, liveCard, statColor ?? Color.white);

        Button btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.interactable = false;
        }

        return go;
    }

    /// <summary>
    /// 攻撃フロー用：攻撃元 → 攻撃先（またはシールド）を矢印付きで表示する。
    /// anchoredYFromTop はカード上端の位置（TopCenter 基準）。
    /// </summary>
    private bool AppendAttackMatchupPreview(
        GameObject parent,
        CardController attacker,
        CardController defender,
        float anchoredYFromTop,
        string headerText = "バトル予定",
        string defenderFallbackLabel = null)
    {
        return AppendAttackMatchupPreviewInternal(
            parent,
            attacker,
            defender,
            headerText,
            defenderFallbackLabel,
            useBottomAnchor: false,
            anchoredY: anchoredYFromTop);
    }

    /// <summary>Close/Cancel の直上（画面下部）に攻撃元 → 攻撃先を固定表示する。</summary>
    private bool AppendAttackMatchupPreviewAboveBottomButtons(
        GameObject parent,
        CardController attacker,
        CardController defender,
        string headerText = "バトル予定",
        string defenderFallbackLabel = null)
    {
        // ボタン高さ〜48 + 余白。カード下端がボタン上に来るよう Bottom 基準で配置
        return AppendAttackMatchupPreviewInternal(
            parent,
            attacker,
            defender,
            headerText,
            defenderFallbackLabel,
            useBottomAnchor: true,
            anchoredY: 220f);
    }

    private bool AppendAttackMatchupPreviewInternal(
        GameObject parent,
        CardController attacker,
        CardController defender,
        string headerText,
        string defenderFallbackLabel,
        bool useBottomAnchor,
        float anchoredY)
    {
        if (parent == null || attacker == null || attacker.Data == null || CardImagePrefab == null)
        {
            return false;
        }

        bool hasUnitDefender = defender != null && defender.Data != null;
        string fallback = !string.IsNullOrEmpty(defenderFallbackLabel)
            ? defenderFallbackLabel
            : (!hasUnitDefender ? "？" : null);
        bool showFallback = !hasUnitDefender && !string.IsNullOrEmpty(fallback);

        UIAnchor cardAnchor = useBottomAnchor ? UIAnchor.BottomCenter : UIAnchor.TopCenter;
        Vector2 attackerPos = new Vector2(-AttackMatchupCardOffsetX, anchoredY);
        Vector2 arrowPos = useBottomAnchor
            ? new Vector2(0f, anchoredY + 70f)
            : new Vector2(0f, anchoredY - 70f);
        Vector2 headerPos = useBottomAnchor
            ? new Vector2(0f, anchoredY + BattleCardPreviewHeight + 28f)
            : new Vector2(0f, anchoredY + 28f);
        Vector2 defenderPos = new Vector2(AttackMatchupCardOffsetX, anchoredY);

        if (!string.IsNullOrEmpty(headerText))
        {
            TextMeshProUGUI header = parent.CreateChildTextCustom(
                "MatchupHeader",
                cardAnchor,
                AttackFlowPopupContentWidth,
                26);
            header.text = headerText;
            header.fontSize = 18;
            header.fontStyle = FontStyles.Bold;
            header.color = new Color(1f, 0.92f, 0.55f, 1f);
            header.alignment = TextAlignmentOptions.Center;
            header.GetComponent<RectTransform>().anchoredPosition = headerPos;
        }

        AppendNonInteractiveCardPreviewAtAnchor(
            parent,
            attacker,
            "攻撃元",
            attackerPos,
            cardAnchor,
            new Color(1f, 0.45f, 0.45f, 1f));

        TextMeshProUGUI arrow = parent.CreateChildTextCustom("MatchupArrow", cardAnchor, 80, 40);
        arrow.text = "→";
        arrow.fontSize = 36;
        arrow.fontStyle = FontStyles.Bold;
        arrow.color = Color.white;
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.GetComponent<RectTransform>().anchoredPosition = arrowPos;

        if (hasUnitDefender)
        {
            string defenderCaption = attackFlowBlockRedirectUnit != null && defender == attackFlowBlockRedirectUnit
                ? "ブロック先"
                : "攻撃先";
            AppendNonInteractiveCardPreviewAtAnchor(
                parent,
                defender,
                defenderCaption,
                defenderPos,
                cardAnchor,
                new Color(0.45f, 0.85f, 1f, 1f));
        }
        else if (showFallback)
        {
            TextMeshProUGUI shieldCap = parent.CreateChildTextCustom("ShieldCaption", cardAnchor, 160, 24);
            shieldCap.text = "攻撃先";
            shieldCap.fontSize = 15;
            shieldCap.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            shieldCap.alignment = TextAlignmentOptions.Center;
            float capY = useBottomAnchor ? anchoredY + BattleCardPreviewHeight + 10f : anchoredY + 10f;
            shieldCap.GetComponent<RectTransform>().anchoredPosition = new Vector2(AttackMatchupCardOffsetX, capY);

            TextMeshProUGUI shieldLabel = parent.CreateChildTextCustom("ShieldFallback", cardAnchor, 160, 80);
            shieldLabel.text = fallback;
            shieldLabel.fontSize = 28;
            shieldLabel.fontStyle = FontStyles.Bold;
            shieldLabel.color = new Color(0.95f, 0.95f, 0.7f, 1f);
            shieldLabel.alignment = TextAlignmentOptions.Center;
            shieldLabel.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(AttackMatchupCardOffsetX, arrowPos.y);
        }

        return true;
    }

    private GameObject AppendNonInteractiveCardPreviewAtAnchor(
        GameObject parent,
        CardController liveCard,
        string caption,
        Vector2 anchoredPosition,
        UIAnchor anchor,
        Color? statColor = null)
    {
        if (parent == null || liveCard == null || liveCard.Data == null || CardImagePrefab == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(caption))
        {
            TextMeshProUGUI cap = parent.CreateChildTextCustom("CardCaption", anchor, 160, 24);
            cap.text = caption;
            cap.fontSize = 15;
            cap.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            cap.alignment = TextAlignmentOptions.Center;
            float captionOffset = anchor == UIAnchor.BottomCenter
                ? BattleCardPreviewHeight + 10f
                : 10f;
            cap.GetComponent<RectTransform>().anchoredPosition =
                anchoredPosition + new Vector2(0f, captionOffset);
        }

        GameObject go = Instantiate(CardImagePrefab, parent.transform);
        RectTransform rt = go.GetComponent<RectTransform>();
        if (anchor == UIAnchor.BottomCenter)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
        }

        rt.sizeDelta = new Vector2(BattleCardPreviewWidth, BattleCardPreviewHeight);
        rt.anchoredPosition = anchoredPosition;

        CardController preview = go.GetComponent<CardController>();
        if (preview != null)
        {
            preview.SetUp(liveCard.Data, _ => { });
        }

        AppendCardLiveStatOverlay(go, liveCard, statColor ?? Color.white);

        Button btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.interactable = false;
        }

        return go;
    }

    /// <summary>攻撃フロー中の表示用防御側（ブロック中ならブロッカー、否则宣言対象）。</summary>
    private CardController ResolveAttackFlowDefenderForPreview()
    {
        if (attackFlowBlockRedirectUnit != null
            && attackFlowBlockRedirectUnit.Data != null
            && attackFlowBlockRedirectUnit.CurrentHp > 0)
        {
            return attackFlowBlockRedirectUnit;
        }

        if (attackFlowDeclaredDefenderUnit != null
            && attackFlowDeclaredDefenderUnit.Data != null
            && attackFlowDeclaredDefenderUnit.CurrentHp > 0)
        {
            return attackFlowDeclaredDefenderUnit;
        }

        return null;
    }

    private void AppendSelectableTargetCardToGrid(
        RectTransform content,
        CardController target,
        bool highlightAsAttackingUnit,
        System.Action<CardController> onPicked,
        string roleLabel = null)
    {
        if (content == null || target == null || target.Data == null || CardImagePrefab == null)
        {
            return;
        }

        GameObject go = Instantiate(CardImagePrefab, content);
        CardController preview = go.GetComponent<CardController>();
        if (preview != null)
        {
            preview.SetUp(target.Data, _ => { });
        }

        Color statColor = highlightAsAttackingUnit
            ? new Color(1f, 0.35f, 0.35f, 1f)
            : Color.white;
        AppendCardLiveStatOverlay(go, target, statColor);

        if (!string.IsNullOrEmpty(roleLabel))
        {
            TextMeshProUGUI tag = go.CreateChildTextCustom("RoleTag", UIAnchor.TopCenter, 108, 20);
            tag.text = roleLabel;
            tag.fontSize = 12;
            tag.fontStyle = FontStyles.Bold;
            tag.color = new Color(1f, 0.88f, 0.35f, 1f);
            tag.alignment = TextAlignmentOptions.Center;
            tag.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -6f);
        }

        if (highlightAsAttackingUnit)
        {
            Image frame = go.GetComponent<Image>();
            if (frame != null)
            {
                frame.color = new Color(1f, 0.75f, 0.75f, 1f);
            }
        }

        Button btn = go.GetComponent<Button>();
        if (btn == null)
        {
            btn = go.AddComponent<Button>();
        }

        CardController picked = target;
        btn.onClick.AddListener(() => onPicked?.Invoke(picked));
    }

    /// <summary>発動カード＋対象候補をカード画像で並べる選択 UI（OnAction / OnMain 等で共用）。</summary>
    private GameObject OpenCommandWithTargetsSelectionUI(
        string titleText,
        string effectSummary,
        CardController sourceCard,
        List<CardController> targetCandidates,
        CardController attackingUnitInAttackFlow,
        System.Action<CardController> onTargetPicked,
        System.Action onCancel,
        CardController blockRedirectUnitInAttackFlow = null)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || sourceCard == null || sourceCard.Data == null || CardImagePrefab == null)
        {
            onCancel?.Invoke();
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("CommandTargetSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("Title", UIAnchor.TopCenter, 760, 44);
        title.text = titleText;
        title.color = Color.white;
        title.fontSize = 24;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        if (!string.IsNullOrEmpty(effectSummary))
        {
            TextMeshProUGUI effectLine = root.CreateChildTextCustom("EffectSummary", UIAnchor.TopCenter, 760, 28);
            effectLine.text = effectSummary;
            effectLine.color = new Color(0.85f, 0.95f, 1f, 1f);
            effectLine.fontSize = 16;
            effectLine.alignment = TextAlignmentOptions.Center;
            effectLine.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -52f);
        }

        bool showAttackContext = attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null;
        float commandCardX = showAttackContext ? 90f : 0f;
        AppendNonInteractiveCardPreview(root, sourceCard, "発動カード", new Vector2(commandCardX, -88f));

        if (showAttackContext)
        {
            AppendNonInteractiveCardPreview(
                root,
                attackingUnitInAttackFlow,
                "攻撃中",
                new Vector2(-150f, -88f),
                new Color(1f, 0.45f, 0.45f, 1f));
        }

        TextMeshProUGUI arrow = root.CreateChildTextCustom("ArrowLabel", UIAnchor.TopCenter, 120, 28);
        arrow.text = "→";
        arrow.fontSize = 28;
        arrow.color = Color.white;
        arrow.GetComponent<RectTransform>().anchoredPosition = new Vector2(commandCardX + 95f, -150f);

        TextMeshProUGUI targetLabel = root.CreateChildTextCustom("TargetLabel", UIAnchor.TopCenter, 320, 24);
        targetLabel.text = "対象を選択";
        targetLabel.fontSize = 17;
        targetLabel.color = Color.white;
        targetLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -268f);

        GameObject scrollGo = root.CreateGridScrollView(700, 340, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -300f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        if (content != null && targetCandidates != null)
        {
            for (int i = 0; i < targetCandidates.Count; i++)
            {
                CardController candidate = targetCandidates[i];
                bool highlight = showAttackContext && candidate == attackingUnitInAttackFlow;
                string roleLabel = blockRedirectUnitInAttackFlow != null && candidate == blockRedirectUnitInAttackFlow
                    ? "Blocked"
                    : null;
                AppendSelectableTargetCardToGrid(
                    content,
                    candidate,
                    highlight,
                    picked =>
                    {
                        Destroy(root);
                        activeOnActionPopupRoot = null;
                        isOnActionPopupOpen = false;
                        onTargetPicked?.Invoke(picked);
                    },
                    roleLabel);
            }
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onCancel?.Invoke();
        });

        return root;
    }

    /// <summary>プレイヤー向け：短時間プレビュー（自動で閉じる）。</summary>
    private IEnumerator ShowCommandUsePreviewCoroutine(
        CardController command,
        CardController attackingUnitInAttackFlow,
        List<CardController> targetCards,
        System.Action onComplete)
    {
        GameObject root = BuildCommandUseAcknowledgementPanel(
            "コマンド発動",
            command,
            attackingUnitInAttackFlow,
            targetCards);
        if (root == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        yield return new WaitForSeconds(CommandUsePreviewHoldSeconds);

        Destroy(root);
        activeOnActionPopupRoot = null;
        isOnActionPopupOpen = false;
        onComplete?.Invoke();
    }

    private void AppendSelectableCommandCardToGrid(
        RectTransform content,
        CardController liveCard,
        string typeLabel,
        HashSet<CardController> selectedSet,
        bool alreadyUsedInActionStep = false)
    {
        if (content == null || liveCard == null || liveCard.Data == null || CardImagePrefab == null)
        {
            return;
        }

        GameObject go = Instantiate(CardImagePrefab, content);
        CardController preview = go.GetComponent<CardController>();
        if (preview != null)
        {
            preview.SetUp(liveCard.Data, _ => { });
        }

        AppendCardLiveStatOverlay(go, liveCard, Color.white);

        if (!string.IsNullOrEmpty(typeLabel))
        {
            TextMeshProUGUI tag = go.CreateChildTextCustom("TypeTag", UIAnchor.TopCenter, 100, 20);
            tag.text = typeLabel;
            tag.fontSize = 12;
            tag.color = new Color(0.9f, 0.95f, 1f, 1f);
            tag.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -6f);
        }

        Image baseImage = go.GetComponent<Image>();
        Color originalColor = baseImage != null ? baseImage.color : Color.white;
        CardController captured = liveCard;
        Button btn = go.GetComponent<Button>();
        if (btn == null)
        {
            btn = go.AddComponent<Button>();
        }

        if (alreadyUsedInActionStep)
        {
            if (baseImage != null)
            {
                baseImage.color = new Color(0.42f, 0.42f, 0.42f, 0.85f);
            }

            btn.interactable = false;
            return;
        }

        btn.onClick.AddListener(() =>
        {
            if (selectedSet.Contains(captured))
            {
                selectedSet.Remove(captured);
                if (baseImage != null)
                {
                    baseImage.color = originalColor;
                }
            }
            else
            {
                selectedSet.Add(captured);
                if (baseImage != null)
                {
                    baseImage.color = new Color(0.7f, 1f, 0.7f, 1f);
                }
            }

        });
    }
}
