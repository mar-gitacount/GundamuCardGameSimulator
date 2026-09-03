using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// <see cref="EffectType.CopyKeywordsFromTrashUnit"/>（∀ Gundam 等）：
/// Self / Enemy 切替でトラッシュを参照し、キーワード持ちユニット1枚を選んで
/// 発動元へ AP+1 とそのキーワードをターン終了まで付与する。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>コピーした《援護》用 OnMain ブロック index（カードデータの timedEffects と衝突しない値）。</summary>
    private const int CopiedSupportOnMainBlockIndex = 900001;

    private readonly struct KeywordTrashCandidate
    {
        public KeywordTrashCandidate(PlayerType trashOwner, int trashIndex, int cardId, CardData data)
        {
            TrashOwner = trashOwner;
            TrashIndex = trashIndex;
            CardId = cardId;
            Data = data;
        }

        public PlayerType TrashOwner { get; }
        public int TrashIndex { get; }
        public int CardId { get; }
        public CardData Data { get; }
    }

    private void ApplyCopyKeywordsFromTrashUnitEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete)
    {
        if (sourceCard == null || sourceCard.Data == null || !sourceCard.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        List<KeywordTrashCandidate> allCandidates = CollectKeywordTrashCandidates();
        if (allCandidates.Count == 0)
        {
            Debug.Log(
                $"[CopyKeywordsFromTrash] 候補なし (cardId:{sourceCard.Data.id})");
            onComplete?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy || _applyingRemoteBattleAction)
        {
            KeywordTrashCandidate auto = PickEnemyAiKeywordTrashCandidate(ownerType, allCandidates);
            ApplyCopiedKeywordsFromSelectedTrashCard(sourceCard, auto.Data);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowCopyKeywordsFromTrashSelectionCoroutine(
            sourceCard,
            allCandidates,
            onComplete));
    }

    private List<KeywordTrashCandidate> CollectKeywordTrashCandidates()
    {
        List<KeywordTrashCandidate> list = new List<KeywordTrashCandidate>();
        AppendKeywordTrashCandidates(list, PlayerType.Player, cardGameRule);
        AppendKeywordTrashCandidates(list, PlayerType.Enemy, enemyCardGameRule);
        return list;
    }

    private static void AppendKeywordTrashCandidates(
        List<KeywordTrashCandidate> list,
        PlayerType trashOwner,
        CardGameRule rule)
    {
        if (list == null || rule == null || DeckSettinObject.Instance == null)
        {
            return;
        }

        IReadOnlyList<int> ids = rule.GetTrashCardIds();
        if (ids == null || ids.Count == 0)
        {
            return;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            int cardId = ids[i];
            CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
            if (data == null || !data.HasAnyCopyableKeyword())
            {
                continue;
            }

            list.Add(new KeywordTrashCandidate(trashOwner, i, cardId, data));
        }
    }

    private bool HasAnyKeywordTrashCandidates()
    {
        return CollectKeywordTrashCandidates().Count > 0;
    }

    private static KeywordTrashCandidate PickEnemyAiKeywordTrashCandidate(
        PlayerType ownerType,
        List<KeywordTrashCandidate> candidates)
    {
        // 自分側トラッシュを優先し、無ければ相手側
        for (int pass = 0; pass < 2; pass++)
        {
            PlayerType prefer = pass == 0
                ? ownerType
                : (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].TrashOwner == prefer && candidates[i].Data != null)
                {
                    return candidates[i];
                }
            }
        }

        return candidates[0];
    }

    private void ApplyCopiedKeywordsFromSelectedTrashCard(CardController sourceCard, CardData trashCard)
    {
        if (sourceCard == null || trashCard == null)
        {
            return;
        }

        CardPrintedKeywordExtensions.PrintedKeywords keywords = trashCard.GetPrintedKeywords();
        if (!keywords.HasAny)
        {
            return;
        }

        string sourceKey =
            $"copyKeywordsFromTrash:{sourceCard.GetEntityId()}:{trashCard.id}:{Time.frameCount}";

        // 公式：このターン AP+1 ＋ 選んだユニットカードの該当キーワードすべて
        ApplyStatEffect(
            sourceCard,
            1,
            EffectStatTarget.AP,
            EffectDuration.UntilEndOfTurn,
            sourceKey);
        sourceCard.ApplyCopiedPrintedKeywordsUntilEndOfTurn(keywords, sourceKey);

        Debug.Log(
            $"[CopyKeywordsFromTrash] {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) "
            + $"<- trash {trashCard.cardName}(id:{trashCard.id}) AP+1 + keywords until EOT");
    }

    private IEnumerator ShowCopyKeywordsFromTrashSelectionCoroutine(
        CardController sourceCard,
        List<KeywordTrashCandidate> allCandidates,
        Action onComplete)
    {
        if (allCandidates == null || allCandidates.Count == 0 || CardImagePrefab == null)
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

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "CopyKeywordsTrashSelect",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("CopyKwTitle", UIAnchor.TopCenter, 820, 44);
        title.SetLocalizedText(
            "トラッシュからキーワードを得る",
            "Gain keywords from Trash");
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -16f);

        string sourceName = sourceCard?.Data?.cardName ?? GameLocale.T("このユニット", "this Unit");
        TextMeshProUGUI subtitle = root.CreateChildTextCustom("CopyKwSubtitle", UIAnchor.TopCenter, 820, 36);
        subtitle.SetLocalizedText(
            $"{sourceName}: Self / Enemy で参照先を切替え、カードを押すとそのキーワードを得る（このターンのみ）",
            $"{sourceName}: Switch Self/Enemy trash, tap a Unit to gain its keywords (this turn only)");
        subtitle.fontSize = 15;
        subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -52f);

        PlayerType viewingSide = PlayerType.Player;
        bool hasPlayerCandidate = false;
        bool hasEnemyCandidate = false;
        for (int i = 0; i < allCandidates.Count; i++)
        {
            if (allCandidates[i].TrashOwner == PlayerType.Player)
            {
                hasPlayerCandidate = true;
            }
            else if (allCandidates[i].TrashOwner == PlayerType.Enemy)
            {
                hasEnemyCandidate = true;
            }
        }

        if (!hasPlayerCandidate && hasEnemyCandidate)
        {
            viewingSide = PlayerType.Enemy;
        }

        bool dismissed = false;
        bool confirmed = false;
        CardData selectedData = null;

        Button selfBtn = root.CreateChildButton(GameLocale.T("自分", "Self"));
        RectTransform selfRt = selfBtn.GetComponent<RectTransform>();
        selfRt.sizeDelta = new Vector2(160f, 44f);
        selfRt.anchorMin = new Vector2(0.5f, 1f);
        selfRt.anchorMax = new Vector2(0.5f, 1f);
        selfRt.pivot = new Vector2(0.5f, 1f);
        selfRt.anchoredPosition = new Vector2(-100f, -96f);

        Button enemyBtn = root.CreateChildButton(GameLocale.T("相手", "Enemy"));
        RectTransform enemyRt = enemyBtn.GetComponent<RectTransform>();
        enemyRt.sizeDelta = new Vector2(160f, 44f);
        enemyRt.anchorMin = new Vector2(0.5f, 1f);
        enemyRt.anchorMax = new Vector2(0.5f, 1f);
        enemyRt.pivot = new Vector2(0.5f, 1f);
        enemyRt.anchoredPosition = new Vector2(100f, -96f);

        TextMeshProUGUI sideLabel = root.CreateChildTextCustom("CopyKwSideLabel", UIAnchor.TopCenter, 400, 28);
        sideLabel.fontSize = 16;
        sideLabel.color = new Color(1f, 0.95f, 0.7f, 1f);
        sideLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -148f);

        GameObject scrollGo = root.CreateGridScrollView(760, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -180f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
            }

            Destroy(root);
        }

        void RefreshSideButtonColors()
        {
            ColorBlock selfColors = selfBtn.colors;
            ColorBlock enemyColors = enemyBtn.colors;
            Color active = new Color(0.55f, 0.85f, 1f, 1f);
            Color idle = Color.white;
            selfColors.normalColor = viewingSide == PlayerType.Player ? active : idle;
            enemyColors.normalColor = viewingSide == PlayerType.Enemy ? active : idle;
            selfBtn.colors = selfColors;
            enemyBtn.colors = enemyColors;
            Image selfImg = selfBtn.GetComponent<Image>();
            Image enemyImg = enemyBtn.GetComponent<Image>();
            if (selfImg != null)
            {
                selfImg.color = viewingSide == PlayerType.Player ? active : idle;
            }

            if (enemyImg != null)
            {
                enemyImg.color = viewingSide == PlayerType.Enemy ? active : idle;
            }

            sideLabel.SetLocalizedText(
                viewingSide == PlayerType.Player ? "表示中: 自分のトラッシュ" : "表示中: 相手のトラッシュ",
                viewingSide == PlayerType.Player ? "Viewing: Self trash" : "Viewing: Enemy trash");
        }

        void RebuildGrid()
        {
            if (content == null)
            {
                return;
            }

            for (int c = content.childCount - 1; c >= 0; c--)
            {
                Destroy(content.GetChild(c).gameObject);
            }

            for (int i = 0; i < allCandidates.Count; i++)
            {
                KeywordTrashCandidate candidate = allCandidates[i];
                if (candidate.TrashOwner != viewingSide || candidate.Data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                CardController cc = go.GetComponent<CardController>();
                if (cc == null)
                {
                    continue;
                }

                CardData capturedData = candidate.Data;
                cc.SetUp(candidate.Data, _ =>
                {
                    if (confirmed || dismissed)
                    {
                        return;
                    }

                    confirmed = true;
                    dismissed = true;
                    selectedData = capturedData;
                    ClosePopup();
                });
                go.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
            }
        }

        selfBtn.onClick.AddListener(() =>
        {
            viewingSide = PlayerType.Player;
            RefreshSideButtonColors();
            RebuildGrid();
        });
        enemyBtn.onClick.AddListener(() =>
        {
            viewingSide = PlayerType.Enemy;
            RefreshSideButtonColors();
            RebuildGrid();
        });

        Button cancelBtn = root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(220f, 50f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        cancelBtn.onClick.AddListener(() =>
        {
            if (confirmed || dismissed)
            {
                return;
            }

            dismissed = true;
            confirmed = false;
            selectedData = null;
            // ChooseOne と同様：キャンセル時は oncePerTurn / コストを戻す
            _chooseOneCancelled = true;
            ClosePopup();
        });

        RefreshSideButtonColors();
        RebuildGrid();

        while (!dismissed && root != null)
        {
            yield return null;
        }

        if (confirmed && selectedData != null)
        {
            ApplyCopiedKeywordsFromSelectedTrashCard(sourceCard, selectedData);
        }

        onComplete?.Invoke();
    }

    private void ClearCopiedKeywordsUntilEndOfTurnForAllInPlayUnits()
    {
        ClearCopiedKeywordsOnZone(playerBattleZoneCards);
        ClearCopiedKeywordsOnZone(enemyBattleZoneCards);
    }

    private static void ClearCopiedKeywordsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearCopiedKeywordsUntilEndOfTurn();
        }
    }

    private void TryAppendCopiedSupportOnMainBlock(
        PlayerType side,
        CardController source,
        List<OnMainExecutableBlock> blocks)
    {
        if (source == null || blocks == null)
        {
            return;
        }

        TimedEffectData copied = source.CopiedSupportTimedUntilEndOfTurn;
        if (copied == null || !copied.HasResolvedEffects())
        {
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(side, source);
        if (!IsOnMainTimedBlockAvailableNow(
                side,
                source,
                copied,
                CopiedSupportOnMainBlockIndex,
                activationContext))
        {
            return;
        }

        blocks.Add(new OnMainExecutableBlock(copied, CopiedSupportOnMainBlockIndex));
    }
}
