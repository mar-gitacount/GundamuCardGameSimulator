using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// OnMain 解決後：トラッシュの発動元を味方ユニットへパイロットセット（任意）。
/// </summary>
public partial class BattleGameMain
{
    private static List<EffectData> CollectMountSelfFromTrashAsPilotEffects(IReadOnlyList<EffectData> effects)
    {
        List<EffectData> result = new List<EffectData>();
        if (effects == null)
        {
            return result;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect != null && effect.type == EffectType.MountSelfFromTrashAsPilot)
            {
                result.Add(effect);
            }
        }

        return result;
    }

    private void TryExecuteDeferredMountSelfFromTrashChain(
        PlayerType ownerType,
        int sourceCardId,
        CardData sourceData,
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
        if (effect == null || effect.type != EffectType.MountSelfFromTrashAsPilot)
        {
            TryExecuteDeferredMountSelfFromTrashChain(
                ownerType, sourceCardId, sourceData, effects, index + 1, onDone);
            return;
        }

        ApplyMountSelfFromTrashAsPilot(
            ownerType,
            sourceCardId,
            sourceData,
            effect,
            () => TryExecuteDeferredMountSelfFromTrashChain(
                ownerType, sourceCardId, sourceData, effects, index + 1, onDone));
    }

    private void ApplyMountSelfFromTrashAsPilot(
        PlayerType ownerType,
        int sourceCardId,
        CardData sourceData,
        EffectData effect,
        Action onComplete)
    {
        if (sourceCardId <= 0 || effect == null)
        {
            onComplete?.Invoke();
            return;
        }

        CardGameRule trashRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (trashRule == null || !TrashContainsCardId(trashRule, sourceCardId))
        {
            Debug.Log(
                $"[MountSelfFromTrash] トラッシュに発動元がありません (cardId:{sourceCardId})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        List<CardController> hosts = CollectMountSelfFromTrashHostCandidates(ownerType, effect);
        if (hosts.Count == 0)
        {
            Debug.Log(
                $"[MountSelfFromTrash] 搭乗可能な対象ユニットがありません (cardId:{sourceCardId})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        TryBeginOptionalConfirmedEffect(
            null,
            ownerType,
            effect,
            onAccepted: () =>
            {
                // Yes 時点で再収集（確認中に場が変わった場合に備える）。
                List<CardController> liveHosts = CollectMountSelfFromTrashHostCandidates(ownerType, effect);
                if (liveHosts.Count == 0)
                {
                    Debug.Log(
                        $"[MountSelfFromTrash] 確認後に搭乗可能な対象がなくなった (cardId:{sourceCardId})。");
                    onComplete?.Invoke();
                    return;
                }

                if (ownerType == PlayerType.Enemy || liveHosts.Count == 1)
                {
                    TryCommitMountSelfFromTrash(ownerType, sourceCardId, sourceData, liveHosts[0]);
                    onComplete?.Invoke();
                    return;
                }

                StartCoroutine(ShowMountSelfFromTrashHostSelectionCoroutine(
                    ownerType,
                    sourceCardId,
                    sourceData,
                    liveHosts,
                    onComplete));
            },
            onDeclined: () =>
            {
                Debug.Log($"[MountSelfFromTrash] プレイヤーが搭乗を見送った (cardId:{sourceCardId})。");
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// OnMain コマンドをトラッシュへ送る。〔必殺技〕監視が終わるまで待ってから搭乗 UI を出す。
    /// </summary>
    private IEnumerator CoFinalizeOnMainSourceCardAndWaitSpecialMoveWatch(
        CardController source,
        PlayerType side)
    {
        if (source == null || source.Data == null)
        {
            yield break;
        }

        if (!source.Data.IsCommand())
        {
            FinalizeOnMainSourceCard(source, side);
            yield break;
        }

        CardData commandData = source.Data;
        if (side == PlayerType.Player)
        {
            string playKind = HasEffectTiming(commandData, EffectTiming.OnMain)
                ? "CommandOnMain"
                : "CommandOnAction";
            RecordEnemyAiObservedPlayerCardPlay(source, playKind);
        }

        bool watchDone = false;
        NotifyOwnerSpecialMoveCommandActivated(side, commandData, () => watchDone = true);
        yield return new WaitUntil(() => watchDone);
        SendUsedCommandToTrash(source, side);
    }

    private static bool TrashContainsCardId(CardGameRule trashRule, int cardId)
    {
        if (trashRule == null || cardId <= 0)
        {
            return false;
        }

        IReadOnlyList<int> ids = trashRule.GetTrashCardIds();
        if (ids == null)
        {
            return false;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == cardId)
            {
                return true;
            }
        }

        return false;
    }

    private List<CardController> CollectMountSelfFromTrashHostCandidates(
        PlayerType ownerType,
        EffectData effect)
    {
        List<CardController> result = new List<CardController>();
        IReadOnlyList<CardFeatureData> requiredFeatures = effect != null
            ? effect.GetTargetFeatures()
            : Array.Empty<CardFeatureData>();
        List<CardController> allies = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (allies == null)
        {
            return result;
        }

        for (int i = 0; i < allies.Count; i++)
        {
            CardController unit = allies[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!unit.CanMountPilot())
            {
                continue;
            }

            if (!MatchesMountSelfFromTrashHostFeatures(unit.Data, effect, requiredFeatures))
            {
                continue;
            }

            if (effect != null && effect.filterByTargetCardType
                && !effect.MatchesTargetCardTypeFilter(unit.Data))
            {
                continue;
            }

            result.Add(unit);
        }

        return result;
    }

    /// <summary>
    /// targetFeature / targetFeatureId / 〔MF〕キーのいずれかで搭乗先を判定する。
    /// </summary>
    private static bool MatchesMountSelfFromTrashHostFeatures(
        CardData unitData,
        EffectData effect,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        bool hasFeatureFilter = (requiredFeatures != null && requiredFeatures.Count > 0)
            || (effect != null && effect.targetFeatureId > 0);
        if (!hasFeatureFilter)
        {
            return true;
        }

        if (MatchesRequiredFeatures(unitData, requiredFeatures))
        {
            return true;
        }

        if (effect != null && effect.targetFeatureId > 0 && unitData != null)
        {
            if (unitData.HasFeatureId(effect.targetFeatureId))
            {
                return true;
            }

            // JSON の 12 = 〔MF〕。レジストリ未解決時のフォールバック。
            if (effect.targetFeatureId == 12 && unitData.HasFeatureKey("MF"))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator ShowMountSelfFromTrashHostSelectionCoroutine(
        PlayerType ownerType,
        int sourceCardId,
        CardData sourceData,
        List<CardController> hosts,
        Action onComplete)
    {
        if (hosts == null || hosts.Count == 0 || CardImagePrefab == null)
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
            "MountSelfFromTrashSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        EnsureOnActionPopupOverlayCanvas(root);
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("MountTrashTitle", UIAnchor.TopCenter, 760, 48);
        title.SetLocalizedText("パイロットをセットするユニットを選ぶ", "Set Pilot onto Unit");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("MountTrashSubtitle", UIAnchor.TopCenter, 760, 36);
        string pilotLabel = sourceData != null ? sourceData.cardName : $"id:{sourceCardId}";
        subtitle.SetLocalizedText(
            $"トラッシュの {pilotLabel} をセットする味方ユニットを選んでください",
            $"Choose an ally Unit to set {pilotLabel} from Trash");
        subtitle.fontSize = 17;
        subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        GameObject scrollGo = root.CreateGridScrollView(760, 400, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -96f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        bool dismissed = false;
        CardController selected = null;

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        }

        if (content != null)
        {
            for (int i = 0; i < hosts.Count; i++)
            {
                CardController host = hosts[i];
                if (host == null || host.Data == null)
                {
                    continue;
                }

                // 敵 AP-2 と同じ Button.onClick。CardController の IPointerClickHandler だけだと反応しない。
                AppendSelectableTargetCardToGrid(
                    content,
                    host,
                    highlightAsAttackingUnit: false,
                    onPicked: picked =>
                    {
                        if (dismissed)
                        {
                            return;
                        }

                        selected = picked;
                        dismissed = true;
                        ClosePopup();
                    });
            }
        }

        Button cancelBtn = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 50f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        TextMeshProUGUI cancelLabel = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (cancelLabel != null)
        {
            cancelLabel.text = "Cancel";
        }

        cancelBtn.onClick.AddListener(() =>
        {
            selected = null;
            dismissed = true;
            ClosePopup();
        });

        yield return new WaitUntil(() => dismissed);

        if (selected != null)
        {
            TryCommitMountSelfFromTrash(ownerType, sourceCardId, sourceData, selected);
        }

        onComplete?.Invoke();
    }

    private bool TryCommitMountSelfFromTrash(
        PlayerType ownerType,
        int sourceCardId,
        CardData sourceData,
        CardController hostUnit)
    {
        if (hostUnit == null || !hostUnit.CanMountPilot() || CardImagePrefab == null)
        {
            return false;
        }

        CardGameRule trashRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (trashRule == null || !trashRule.TryRemoveCardFromTrash(sourceCardId, out int removedId))
        {
            Debug.LogWarning(
                $"[MountSelfFromTrash] トラッシュ除去失敗 (cardId:{sourceCardId})。");
            return false;
        }

        CardData pilotData = sourceData
            ?? (DeckSettinObject.Instance != null
                ? DeckSettinObject.Instance.GetCardDataById(removedId > 0 ? removedId : sourceCardId)
                : null);
        if (pilotData == null || !pilotData.IsPilot())
        {
            trashRule.AddCardToTrash(removedId > 0 ? removedId : sourceCardId);
            Debug.LogWarning(
                $"[MountSelfFromTrash] パイロットデータ不正 (cardId:{sourceCardId})。トラッシュへ戻す。");
            return false;
        }

        GameObject pilotObject = Instantiate(CardImagePrefab, hostUnit.transform);
        CardController pilotController = pilotObject.GetComponent<CardController>();
        if (pilotController == null)
        {
            Destroy(pilotObject);
            trashRule.AddCardToTrash(pilotData.id);
            return false;
        }

        pilotController.SetUp(pilotData, OnCardClicked);
        if (!hostUnit.TryAttachPilot(pilotController))
        {
            Destroy(pilotObject);
            trashRule.AddCardToTrash(pilotData.id);
            Debug.LogWarning(
                $"[MountSelfFromTrash] TryAttachPilot 失敗 host:{hostUnit.Data?.cardName}");
            return false;
        }

        ApplyOnPilotMountedAllyBuffsDirect(hostUnit, pilotController, ownerType);

        if (ownerType == PlayerType.Player)
        {
            NotifyLocalPilotMounted(hostUnit, pilotController);
        }

        ApplyUnitAttackFlgFromLink(hostUnit, ownerType);
        TryGrantOperationMeteorFirstStrikeOnPilotMount(hostUnit, pilotController, ownerType);
        TriggerOnPilotMountedEffects(hostUnit, pilotController, ownerType, () =>
        {
            TriggerOnLinkEffects(hostUnit, pilotController, ownerType, () =>
            {
                TriggerOnPlayedEffects(pilotController, ownerType, RefreshAllHandsConditionalOnHandAuto);
            });
        });

        Debug.Log(
            $"[MountSelfFromTrash] {pilotData.cardName}(id:{pilotData.id}) → "
            + $"{hostUnit.Data.cardName} AP:{hostUnit.CurrentPower} HP:{hostUnit.CurrentHp}");
        return true;
    }
}
