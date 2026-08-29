using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public partial class BattleGameMain
{
    private CardController _onDestroyedPendingDetachedPilot;

    /// <summary>
    /// 代替コスト配備（Unicorn Mode 破壊）が可能なら UI を出し、不可なら false。
    /// </summary>
    private bool TryOpenHandDeployAlternateCostFlow(
        GameObject filterPanel,
        GameObject filterContent,
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ownerSide,
        RectTransform closeBtnRect,
        float handActionY,
        int currentLevel,
        bool allowNormalDeploy)
    {
        if (filterContent == null
            || cardController?.Data == null
            || !CardHandDeployAlternateCost.TryGetProfile(cardController.Data, out CardHandDeployAlternateCost.Profile profile))
        {
            return false;
        }

        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        List<CardController> candidates = CardHandDeployAlternateCost.CollectSacrificeCandidates(
            ownerType,
            profile,
            zone,
            cc => ResolveCardOwner(cc.transform),
            IsCardOnBattleZone);
        if (candidates.Count == 0)
        {
            return false;
        }

        PinFilterCloseButton(closeBtnRect);
        float y = handActionY;

        if (allowNormalDeploy)
        {
            Button normalBtn = filterContent.CreateChildButton(
                GameLocale.T("通常配備", "Deploy normally"));
            RectTransform normalRt = normalBtn.GetComponent<RectTransform>();
            normalRt.sizeDelta = new Vector2(320f, 50f);
            normalRt.anchoredPosition = new Vector2(0f, y);
            y -= 60f;
            normalBtn.onClick.AddListener(() =>
            {
                BeginStandardHandUnitDeployPayment(
                    filterPanel,
                    filterContent,
                    cardController,
                    ownerType,
                    ownerRule,
                    ownerSide,
                    closeBtnRect);
            });
        }

        Button altBtn = filterContent.CreateChildButton(
            GameLocale.T(
                "Unicorn Mode を破壊して Lv.0/Cost 0 で配備",
                "Destroy Unicorn Mode to deploy at Lv.0 / Cost 0"));
        RectTransform altRt = altBtn.GetComponent<RectTransform>();
        altRt.sizeDelta = new Vector2(420f, 50f);
        altRt.anchoredPosition = new Vector2(0f, y);
        altBtn.onClick.AddListener(() =>
        {
            OpenHandDeploySacrificeUnitPicker(
                filterPanel,
                cardController,
                ownerType,
                ownerRule,
                ownerSide,
                profile,
                candidates);
        });

        if (!allowNormalDeploy)
        {
            TextMeshProUGUI hint = filterContent.CreateChildTextCustom(
                "AlternateDeployHint",
                UIAnchor.TopCenter,
                440,
                48);
            hint.text = GameLocale.T(
                "レベル不足のため、Unicorn Mode 破壊でのみ配備できます。",
                "Level too low — deploy only by destroying Unicorn Mode.");
            RectTransform hintRt = hint.GetComponent<RectTransform>();
            if (hintRt != null)
            {
                hintRt.anchoredPosition = new Vector2(0f, y - 56f);
            }
        }

        return true;
    }

    private void BeginStandardHandUnitDeployPayment(
        GameObject filterPanel,
        GameObject filterContent,
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ownerSide,
        RectTransform closeBtnRect)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        int cost = cardController.CurrentCost;
        PinFilterCloseButton(closeBtnRect);
        EmbedResourcePaymentUI(
            filterContent,
            ownerType,
            cost,
            cardController.CurrentLevel,
            -10f,
            exToUse =>
            {
                if (!TryPayHandDeployCost(ownerSide, cardController, exToUse))
                {
                    Debug.Log("リソースポイントが足りません！");
                    return;
                }

                SendCardToField(cardController, ownerType, ownerRule);
                SyncResourceViewsFromRule(ownerSide);
                Destroy(filterPanel);
            },
            () => Destroy(filterPanel));
    }

    private void OpenHandDeploySacrificeUnitPicker(
        GameObject filterPanel,
        CardController deployingCard,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ownerSide,
        CardHandDeployAlternateCost.Profile profile,
        List<CardController> candidates)
    {
        if (deployingCard == null || candidates == null || candidates.Count == 0)
        {
            return;
        }

        EffectData pickEffect = new EffectData
        {
            type = EffectType.Destroy,
            target = TargetType.AllyUnit,
            selectionMode = EffectSelectionMode.SelectSingle,
            value = 1,
            targetCardNameContains = profile.sacrificeNameContains,
            filterTargetUnitLevel = true,
            targetUnitFilterStat = EffectTargetUnitFilterStat.Level,
            targetUnitStatCompareOp = EffectCompareOperator.Equal,
            targetUnitStatCompareValue = profile.sacrificePrintedLevel
        };

        List<CardController> filtered = new List<CardController>();
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (CardHandDeployAlternateCost.IsSacrificeCandidate(
                    candidate,
                    ownerType,
                    profile,
                    cc => ResolveCardOwner(cc.transform),
                    IsCardOnBattleZone))
            {
                filtered.Add(candidate);
            }
        }

        if (filtered.Count == 0)
        {
            return;
        }

        OpenManualUnitTargetSelectionUI(
            deployingCard,
            ownerType,
            pickEffect,
            filtered,
            null,
            picked =>
            {
                Destroy(filterPanel);
                if (picked == null)
                {
                    return;
                }

                StartCoroutine(HandDeployWithAlternateCostCoroutine(
                    deployingCard,
                    picked,
                    ownerType,
                    ownerRule,
                    ownerSide,
                    profile));
            });
    }

    private IEnumerator HandDeployWithAlternateCostCoroutine(
        CardController deployingCard,
        CardController sacrifice,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ownerSide,
        CardHandDeployAlternateCost.Profile profile)
    {
        if (deployingCard == null || sacrifice == null || profile == null)
        {
            yield break;
        }

        int pipelineBefore = _pendingSendToTrashPipelines;
        SendCardToTrash(sacrifice, ownerType, deployingCard);
        yield return new WaitUntil(
            () => _pendingSendToTrashPipelines <= pipelineBefore
                  && (sacrifice == null || !unitsPendingSendToTrash.Contains(sacrifice)));

        if (!TryPayHandDeployCost(
                ownerSide,
                deployingCard,
                0,
                profile.alternateLevel,
                profile.alternateCost))
        {
            Debug.LogWarning("[HandDeployAlternateCost] 代替コスト支払いに失敗しました。");
            yield break;
        }

        SendCardToField(deployingCard, ownerType, ownerRule);
        SyncResourceViewsFromRule(ownerSide);
    }

    private void ApplyReturnMountedPilotToHandEffect(CardController sourceCard, PlayerType ownerType)
    {
        CardController pilot = _onDestroyedPendingDetachedPilot;
        if (pilot == null || pilot.Data == null)
        {
            Debug.LogWarning(
                $"[ReturnMountedPilotToHand] 対象パイロットなし source:{sourceCard?.Data?.cardName ?? "?"}");
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (!TryReturnCardInstanceToHand(pilot, ownerType, rule))
        {
            Debug.LogWarning(
                $"[ReturnMountedPilotToHand] 手札戻し失敗 pilot:{pilot.Data.cardName}(id:{pilot.Data.id})");
            return;
        }

        _onDestroyedPendingDetachedPilot = null;
        MarkOnDestroyedCardReturnedToHand(pilot);
        Debug.Log(
            $"[ReturnMountedPilotToHand] {pilot.Data.cardName}(id:{pilot.Data.id}) → {ownerType} hand "
            + $"(from unit:{sourceCard?.Data?.cardName ?? "?"})");
    }

    private void MarkOnDestroyedCardReturnedToHand(CardController card)
    {
        if (card?.Data == null)
        {
            return;
        }

        _pendingOnDestroyedReturnedToHandCardId = card.Data.id;
    }
}
