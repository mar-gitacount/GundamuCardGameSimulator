using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ユニットとパイロットの双方に同時タイミングの効果があるとき、解決順をプレイヤーが選ぶ UI。
/// OK / Skip / Clear。強制効果の有無で OK・Skip のグレーアウトが変わる。
/// </summary>
public partial class BattleGameMain
{
    private const string UnitPilotEffectOrderBadgeName = "UnitPilotEffectOrderBadge";
    private const string UnitPilotEffectOrderOutlineName = "UnitPilotEffectOrderOutline";

    private bool _unitPilotEffectOrderUiOpen;

    private sealed class UnitPilotEffectOrderEntry
    {
        public CardController Source;
        public List<TimedEffectData> Blocks;
        public bool IsPilot;
        /// <summary>OnAttack 等で効果を適用できないとき false（UI グレーアウト）。</summary>
        public bool Selectable = true;
    }

    private sealed class UnitPilotEffectOrderChoiceState
    {
        public readonly List<UnitPilotEffectOrderEntry> Candidates = new List<UnitPilotEffectOrderEntry>();
        public readonly List<int> SelectedOrderIndices = new List<int>();
        public bool HasMandatory;
    }

    /// <summary>
    /// ユニット／パイロット双方にブロックがある場合のみ順番 UI（プレイヤー）または自動順（敵）で解決する。
    /// 片方のみならそのままその側だけを返す。どちらも無ければ空リスト。
    /// </summary>
    /// <param name="titleJa">UI タイトル日本語（null/空なら既定）。</param>
    /// <param name="titleEn">UI タイトル英語（null/空なら既定）。</param>
    private void ResolveUnitPilotEffectOrder(
        PlayerType ownerType,
        CardController unitCard,
        CardController pilotCard,
        List<TimedEffectData> unitBlocks,
        List<TimedEffectData> pilotBlocks,
        CardData orderHintHostData,
        Action<List<UnitPilotEffectOrderEntry>> onResolved,
        bool autoPilotFirst = false,
        string titleJa = null,
        string titleEn = null,
        Func<CardController, List<TimedEffectData>, bool> entrySelectable = null)
    {
        List<TimedEffectData> safeUnit = unitBlocks ?? new List<TimedEffectData>();
        List<TimedEffectData> safePilot = pilotBlocks ?? new List<TimedEffectData>();
        bool hasUnit = safeUnit.Count > 0;
        bool hasPilot = safePilot.Count > 0;

        if (!hasUnit && !hasPilot)
        {
            onResolved?.Invoke(new List<UnitPilotEffectOrderEntry>());
            return;
        }

        if (hasUnit && !hasPilot)
        {
            if (entrySelectable != null && !entrySelectable(unitCard, safeUnit))
            {
                onResolved?.Invoke(new List<UnitPilotEffectOrderEntry>());
                return;
            }

            onResolved?.Invoke(new List<UnitPilotEffectOrderEntry>
            {
                new UnitPilotEffectOrderEntry
                {
                    Source = unitCard,
                    Blocks = safeUnit,
                    IsPilot = false,
                    Selectable = true
                }
            });
            return;
        }

        if (!hasUnit && hasPilot)
        {
            if (entrySelectable != null && !entrySelectable(pilotCard, safePilot))
            {
                onResolved?.Invoke(new List<UnitPilotEffectOrderEntry>());
                return;
            }

            onResolved?.Invoke(new List<UnitPilotEffectOrderEntry>
            {
                new UnitPilotEffectOrderEntry
                {
                    Source = pilotCard,
                    Blocks = safePilot,
                    IsPilot = true,
                    Selectable = true
                }
            });
            return;
        }

        // 双方あり
        if (ownerType != PlayerType.Player || _applyingRemoteBattleAction)
        {
            onResolved?.Invoke(BuildAutoUnitPilotEffectOrder(
                unitCard,
                pilotCard,
                safeUnit,
                safePilot,
                orderHintHostData,
                autoPilotFirst,
                entrySelectable));
            return;
        }

        OpenUnitPilotEffectOrderUi(
            unitCard,
            pilotCard,
            safeUnit,
            safePilot,
            onResolved,
            titleJa,
            titleEn,
            entrySelectable);
    }

    private static List<UnitPilotEffectOrderEntry> BuildAutoUnitPilotEffectOrder(
        CardController unitCard,
        CardController pilotCard,
        List<TimedEffectData> unitBlocks,
        List<TimedEffectData> pilotBlocks,
        CardData orderHintHostData,
        bool autoPilotFirst = false,
        Func<CardController, List<TimedEffectData>, bool> entrySelectable = null)
    {
        bool unitFirst = !autoPilotFirst;
        if (!autoPilotFirst)
        {
            CardData hint = orderHintHostData != null
                ? orderHintHostData
                : unitCard != null ? unitCard.Data : null;
            if (hint != null && hint.IsUnitLike())
            {
                UnitLinkExtensions.ResolveOnPilotMountedExecutionPlan(
                    hint,
                    out _,
                    out _,
                    out unitFirst);
            }
        }

        var unitEntry = new UnitPilotEffectOrderEntry
        {
            Source = unitCard,
            Blocks = unitBlocks,
            IsPilot = false,
            Selectable = entrySelectable == null || entrySelectable(unitCard, unitBlocks)
        };
        var pilotEntry = new UnitPilotEffectOrderEntry
        {
            Source = pilotCard,
            Blocks = pilotBlocks,
            IsPilot = true,
            Selectable = entrySelectable == null || entrySelectable(pilotCard, pilotBlocks)
        };

        var ordered = unitFirst
            ? new List<UnitPilotEffectOrderEntry> { unitEntry, pilotEntry }
            : new List<UnitPilotEffectOrderEntry> { pilotEntry, unitEntry };

        List<UnitPilotEffectOrderEntry> filtered = new List<UnitPilotEffectOrderEntry>();
        for (int i = 0; i < ordered.Count; i++)
        {
            UnitPilotEffectOrderEntry entry = ordered[i];
            if (entry.Blocks != null && entry.Blocks.Count > 0 && entry.Selectable)
            {
                filtered.Add(entry);
            }
        }

        return filtered;
    }

    /// <summary>ブロック内に optional でない効果が1つでもあれば強制あり。</summary>
    private static bool TimedBlocksContainMandatoryEffect(List<TimedEffectData> blocks)
    {
        if (blocks == null || blocks.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            TimedEffectData timed = blocks[i];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int j = 0; j < effects.Count; j++)
            {
                EffectData effect = effects[j];
                if (effect != null && !effect.optionalPlayerConfirm)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OpenUnitPilotEffectOrderUi(
        CardController unitCard,
        CardController pilotCard,
        List<TimedEffectData> unitBlocks,
        List<TimedEffectData> pilotBlocks,
        Action<List<UnitPilotEffectOrderEntry>> onResolved,
        string titleJa = null,
        string titleEn = null,
        Func<CardController, List<TimedEffectData>, bool> entrySelectable = null)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || CardImagePrefab == null)
        {
            onResolved?.Invoke(BuildAutoUnitPilotEffectOrder(
                unitCard,
                pilotCard,
                unitBlocks,
                pilotBlocks,
                unitCard != null ? unitCard.Data : null,
                false,
                entrySelectable));
            return;
        }

        DestroyActiveOnActionPopupIfAny();

        var state = new UnitPilotEffectOrderChoiceState();
        var unitEntry = new UnitPilotEffectOrderEntry
        {
            Source = unitCard,
            Blocks = unitBlocks,
            IsPilot = false,
            Selectable = entrySelectable == null || entrySelectable(unitCard, unitBlocks)
        };
        var pilotEntry = new UnitPilotEffectOrderEntry
        {
            Source = pilotCard,
            Blocks = pilotBlocks,
            IsPilot = true,
            Selectable = entrySelectable == null || entrySelectable(pilotCard, pilotBlocks)
        };
        state.Candidates.Add(unitEntry);
        state.Candidates.Add(pilotEntry);
        state.HasMandatory = false;
        for (int ci = 0; ci < state.Candidates.Count; ci++)
        {
            UnitPilotEffectOrderEntry candidate = state.Candidates[ci];
            if (!candidate.Selectable)
            {
                continue;
            }

            if (TimedBlocksContainMandatoryEffect(candidate.Blocks))
            {
                state.HasMandatory = true;
                break;
            }
        }

        GameObject root = new GameObject(
            "UnitPilotEffectOrder",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        _unitPilotEffectOrderUiOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OrderTitle", UIAnchor.TopCenter, 780, 52);
        string ja = string.IsNullOrEmpty(titleJa) ? "効果の解決順を選択" : titleJa;
        string en = string.IsNullOrEmpty(titleEn) ? "Choose effect resolution order" : titleEn;
        title.SetLocalizedText(ja, en);
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -48f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("OrderHint", UIAnchor.TopCenter, 780, 44);
        if (state.HasMandatory)
        {
            hint.SetLocalizedText(
                "カードをタップして順番を付ける → OK（強制効果は必ず選択） / Clear でやり直し",
                "Tap cards to set order → OK (mandatory effects must be selected) / Clear to reset");
        }
        else
        {
            hint.SetLocalizedText(
                "任意効果のみ：選ばずに OK／Skip 可。選ぶ場合はタップで順番 → Clear でやり直し",
                "Optional only: OK/Skip with none selected, or tap to order → Clear to reset");
        }

        hint.fontSize = 16;
        hint.color = new Color(0.85f, 0.9f, 1f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        List<GameObject> cardRoots = new List<GameObject>(2);
        Button okBtn = null;
        Button skipBtn = null;
        TextMeshProUGUI okLabel = null;
        TextMeshProUGUI skipLabel = null;

        Action refreshButtons = () =>
        {
            RefreshUnitPilotEffectOrderButtons(okBtn, skipBtn, okLabel, skipLabel, state);
        };

        float[] xs = { -160f, 160f };
        for (int i = 0; i < state.Candidates.Count; i++)
        {
            UnitPilotEffectOrderEntry entry = state.Candidates[i];
            CardData data = entry.Source != null ? entry.Source.Data : null;
            bool selectable = entry.Selectable;
            if (data == null)
            {
                cardRoots.Add(null);
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, root.transform);
            cardRoots.Add(cardItem);
            RectTransform cardRt = cardItem.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                cardRt.anchorMin = new Vector2(0.5f, 0.5f);
                cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.anchoredPosition = new Vector2(xs[i], 40f);
                cardRt.sizeDelta = new Vector2(180f, 252f);
            }

            CardController preview = cardItem.GetComponent<CardController>();
            if (preview != null)
            {
                preview.SetUp(data, _ => { });
            }

            TextMeshProUGUI role = cardItem.CreateChildTextCustom(
                "RoleLabel",
                UIAnchor.BottomCenter,
                170,
                28);
            role.text = entry.IsPilot ? "Pilot" : "Unit";
            role.fontSize = 16;
            role.color = new Color(1f, 0.92f, 0.45f, 1f);
            role.alignment = TextAlignmentOptions.Center;
            role.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -8f);

            bool mandatory = selectable && TimedBlocksContainMandatoryEffect(entry.Blocks);
            TextMeshProUGUI forceLabel = cardItem.CreateChildTextCustom(
                "ForceLabel",
                UIAnchor.TopCenter,
                170,
                24);
            if (!selectable)
            {
                forceLabel.SetLocalizedText("適用不可", "Unavailable");
                forceLabel.fontSize = 14;
                forceLabel.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            }
            else
            {
                forceLabel.SetLocalizedText(
                    mandatory ? "強制" : "任意",
                    mandatory ? "Mandatory" : "Optional");
                forceLabel.fontSize = 14;
                forceLabel.color = mandatory
                    ? new Color(1f, 0.45f, 0.4f, 1f)
                    : new Color(0.55f, 0.9f, 0.6f, 1f);
            }
            forceLabel.alignment = TextAlignmentOptions.Center;
            forceLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 8f);

            Button btn = cardItem.GetComponent<Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<Button>();
            }

            int capturedIndex = i;
            btn.interactable = selectable;
            btn.onClick.AddListener(() =>
            {
                if (!state.Candidates[capturedIndex].Selectable)
                {
                    return;
                }

                ToggleUnitPilotEffectOrderSelection(state, capturedIndex);
                RefreshUnitPilotEffectOrderVisuals(cardRoots, state);
                refreshButtons();
            });

            if (!selectable)
            {
                CanvasGroup gray = cardItem.GetComponent<CanvasGroup>();
                if (gray == null)
                {
                    gray = cardItem.AddComponent<CanvasGroup>();
                }

                gray.alpha = 0.38f;
            }
        }

        okBtn = root.CreateChildButton("OK");
        skipBtn = root.CreateChildButton("Skip");
        Button clearBtn = root.CreateChildButton("Clear");
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        skipLabel = skipBtn.GetComponentInChildren<TextMeshProUGUI>();
        TextMeshProUGUI clearLabel = clearBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        if (skipLabel != null)
        {
            skipLabel.text = "Skip";
        }

        if (clearLabel != null)
        {
            clearLabel.text = "Clear";
        }

        LayoutUnitPilotEffectOrderButton(okBtn, new Vector2(-200f, 56f));
        LayoutUnitPilotEffectOrderButton(skipBtn, new Vector2(0f, 56f));
        LayoutUnitPilotEffectOrderButton(clearBtn, new Vector2(200f, 56f));

        bool consumed = false;
        void CloseAndResolve(List<UnitPilotEffectOrderEntry> ordered)
        {
            if (consumed)
            {
                return;
            }

            consumed = true;
            _unitPilotEffectOrderUiOpen = false;
            ReleaseOnActionPopupState(root);
            Destroy(root);
            onResolved?.Invoke(ordered ?? new List<UnitPilotEffectOrderEntry>());
        }

        okBtn.onClick.AddListener(() =>
        {
            if (!okBtn.interactable)
            {
                return;
            }

            CloseAndResolve(BuildOrderedEntriesFromChoice(state));
        });

        skipBtn.onClick.AddListener(() =>
        {
            if (!skipBtn.interactable)
            {
                return;
            }

            // 任意のみ：全スキップ
            CloseAndResolve(new List<UnitPilotEffectOrderEntry>());
        });

        clearBtn.onClick.AddListener(() =>
        {
            state.SelectedOrderIndices.Clear();
            RefreshUnitPilotEffectOrderVisuals(cardRoots, state);
            refreshButtons();
        });

        RefreshUnitPilotEffectOrderVisuals(cardRoots, state);
        refreshButtons();

        Debug.Log(
            $"[UnitPilotOrder] UI open unit:{(unitCard != null && unitCard.Data != null ? unitCard.Data.cardName : "?")} "
            + $"pilot:{(pilotCard != null && pilotCard.Data != null ? pilotCard.Data.cardName : "?")} "
            + $"mandatory:{state.HasMandatory}");
    }

    private static void LayoutUnitPilotEffectOrderButton(Button btn, Vector2 anchoredPos)
    {
        if (btn == null)
        {
            return;
        }

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(170f, 50f);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos;
    }

    private static void ToggleUnitPilotEffectOrderSelection(
        UnitPilotEffectOrderChoiceState state,
        int candidateIndex)
    {
        if (state == null || candidateIndex < 0 || candidateIndex >= state.Candidates.Count)
        {
            return;
        }

        UnitPilotEffectOrderEntry entry = state.Candidates[candidateIndex];
        if (!entry.Selectable)
        {
            return;
        }

        int existing = state.SelectedOrderIndices.IndexOf(candidateIndex);
        if (existing >= 0)
        {
            state.SelectedOrderIndices.RemoveAt(existing);
            return;
        }

        if (state.SelectedOrderIndices.Count >= state.Candidates.Count)
        {
            return;
        }

        state.SelectedOrderIndices.Add(candidateIndex);
    }

    private static List<UnitPilotEffectOrderEntry> BuildOrderedEntriesFromChoice(
        UnitPilotEffectOrderChoiceState state)
    {
        var result = new List<UnitPilotEffectOrderEntry>();
        if (state == null)
        {
            return result;
        }

        for (int i = 0; i < state.SelectedOrderIndices.Count; i++)
        {
            int idx = state.SelectedOrderIndices[i];
            if (idx < 0 || idx >= state.Candidates.Count)
            {
                continue;
            }

            UnitPilotEffectOrderEntry entry = state.Candidates[idx];
            if (!entry.Selectable)
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }

    private static bool IsUnitPilotEffectOrderReady(UnitPilotEffectOrderChoiceState state)
    {
        if (state == null)
        {
            return false;
        }

        if (!state.HasMandatory)
        {
            // 任意のみ：未選択でも OK 可（＝全スキップ相当）
            return true;
        }

        // 強制がある候補はすべて順番に含まれていること
        for (int i = 0; i < state.Candidates.Count; i++)
        {
            UnitPilotEffectOrderEntry entry = state.Candidates[i];
            if (!TimedBlocksContainMandatoryEffect(entry.Blocks) || !entry.Selectable)
            {
                continue;
            }

            if (!state.SelectedOrderIndices.Contains(i))
            {
                return false;
            }
        }

        return true;
    }

    private static void RefreshUnitPilotEffectOrderButtons(
        Button okBtn,
        Button skipBtn,
        TextMeshProUGUI okLabel,
        TextMeshProUGUI skipLabel,
        UnitPilotEffectOrderChoiceState state)
    {
        bool okReady = IsUnitPilotEffectOrderReady(state);
        bool skipReady = state != null && !state.HasMandatory;

        if (okBtn != null)
        {
            okBtn.interactable = okReady;
        }

        if (okLabel != null)
        {
            okLabel.color = okReady ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
        }

        if (skipBtn != null)
        {
            skipBtn.interactable = skipReady;
        }

        if (skipLabel != null)
        {
            skipLabel.color = skipReady ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
        }
    }

    private void RefreshUnitPilotEffectOrderVisuals(
        List<GameObject> cardRoots,
        UnitPilotEffectOrderChoiceState state)
    {
        if (cardRoots == null || state == null)
        {
            return;
        }

        for (int i = 0; i < cardRoots.Count; i++)
        {
            GameObject cardRoot = cardRoots[i];
            if (cardRoot == null)
            {
                continue;
            }

            int order = state.SelectedOrderIndices.IndexOf(i);
            bool selected = order >= 0;

            Transform outline = cardRoot.transform.Find(UnitPilotEffectOrderOutlineName);
            if (selected)
            {
                if (outline == null)
                {
                    GameObject outlineGo = new GameObject(
                        UnitPilotEffectOrderOutlineName,
                        typeof(RectTransform),
                        typeof(Image));
                    outlineGo.transform.SetParent(cardRoot.transform, false);
                    outlineGo.transform.SetAsFirstSibling();
                    RectTransform outlineRt = outlineGo.GetComponent<RectTransform>();
                    outlineRt.anchorMin = Vector2.zero;
                    outlineRt.anchorMax = Vector2.one;
                    outlineRt.offsetMin = new Vector2(-5f, -5f);
                    outlineRt.offsetMax = new Vector2(5f, 5f);
                    Image outlineImg = outlineGo.GetComponent<Image>();
                    outlineImg.color = new Color(1f, 0.85f, 0.35f, 0.95f);
                    outlineImg.raycastTarget = false;
                }

                Transform badge = cardRoot.transform.Find(UnitPilotEffectOrderBadgeName);
                TextMeshProUGUI badgeText;
                if (badge == null)
                {
                    // TextMeshProUGUI と同じ GO に Image は載せられない（Graphic 二重）
                    GameObject badgeGo = new GameObject(
                        UnitPilotEffectOrderBadgeName,
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(TextMeshProUGUI));
                    badgeGo.transform.SetParent(cardRoot.transform, false);
                    badgeGo.transform.SetAsLastSibling();
                    RectTransform badgeRt = badgeGo.GetComponent<RectTransform>();
                    badgeRt.anchorMin = new Vector2(1f, 1f);
                    badgeRt.anchorMax = new Vector2(1f, 1f);
                    badgeRt.pivot = new Vector2(1f, 1f);
                    badgeRt.sizeDelta = new Vector2(36f, 36f);
                    badgeRt.anchoredPosition = new Vector2(-8f, -8f);
                    badgeText = badgeGo.GetComponent<TextMeshProUGUI>();
                    badgeText.fontSize = 22;
                    badgeText.fontStyle = FontStyles.Bold;
                    badgeText.color = new Color(1f, 0.85f, 0.2f, 1f);
                    badgeText.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    badgeText = badge.GetComponent<TextMeshProUGUI>();
                }

                if (badgeText != null)
                {
                    badgeText.text = (order + 1).ToString();
                    badgeText.gameObject.SetActive(true);
                }
            }
            else
            {
                if (outline != null)
                {
                    Destroy(outline.gameObject);
                }

                Transform badge = cardRoot.transform.Find(UnitPilotEffectOrderBadgeName);
                if (badge != null)
                {
                    badge.gameObject.SetActive(false);
                }
            }
        }
    }

    /// <summary>順番付きエントリを先頭から順にブロック実行する。</summary>
    private void RunOrderedUnitPilotEffectEntries(
        PlayerType ownerType,
        List<UnitPilotEffectOrderEntry> ordered,
        int index,
        CardController mountHostUnit,
        CardController mountPilot,
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
            RunOrderedUnitPilotEffectEntries(
                ownerType,
                ordered,
                index + 1,
                mountHostUnit,
                mountPilot,
                onComplete);
            return;
        }

        RunMountTimedBlocks(
            entry.Source,
            ownerType,
            entry.Blocks,
            0,
            () => RunOrderedUnitPilotEffectEntries(
                ownerType,
                ordered,
                index + 1,
                mountHostUnit,
                mountPilot,
                onComplete),
            mountHostUnit,
            mountPilot);
    }
}
