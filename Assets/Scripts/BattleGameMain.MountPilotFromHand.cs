using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>手札のパイロットを発動元ユニットへセット（GD02-071 等）。</summary>
public partial class BattleGameMain
{
    private IEnumerator ExecuteMountPilotFromHandCoroutine(
        CardController hostUnit,
        PlayerType ownerType,
        EffectData effect,
        Action<bool> onDone)
    {
        if (hostUnit == null
            || hostUnit.Data == null
            || !hostUnit.CanMountPilot()
            || effect == null
            || effect.type != EffectType.MountPilotFromHand)
        {
            onDone?.Invoke(false);
            yield break;
        }

        PlayerType handOwner = ResolveHandDiscardOwner(ownerType, effect);
        List<CardController> candidates = CollectSelectableHandCards(handOwner, excludeSource: hostUnit);
        FilterHandCandidatesByEffect(candidates, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[MountPilotFromHand] 手札に対象パイロットなし host:{hostUnit.Data.cardName}(id:{hostUnit.Data.id})");
            onDone?.Invoke(false);
            yield break;
        }

        CardController selected = null;
        if (handOwner == PlayerType.Enemy && !RequiresInteractiveManualUnitSelectionUi(ownerType))
        {
            selected = candidates[0];
        }
        else
        {
            bool resolved = false;
            OpenManualHandTargetSelectionUI(
                hostUnit,
                handOwner,
                effect,
                CollectHandCardsForDiscardDisplay(handOwner),
                candidates,
                picked =>
                {
                    selected = picked;
                    resolved = true;
                });
            yield return new WaitUntil(() => resolved);
        }

        if (selected == null || selected.Data == null || !selected.Data.IsPilot())
        {
            Debug.Log($"[MountPilotFromHand] skipped host:{hostUnit.Data.cardName}");
            onDone?.Invoke(false);
            yield break;
        }

        if (!hostUnit.CanMountPilot())
        {
            onDone?.Invoke(false);
            yield break;
        }

        Gundam2024RuleScript.PlayerSide ownerSide = ToRuleSide(ownerType);
        RemoveCardFromHandLists(selected, handOwner);
        if (!hostUnit.TryAttachPilot(selected))
        {
            Debug.LogWarning(
                $"[MountPilotFromHand] TryAttachPilot 失敗 host:{hostUnit.Data.cardName} "
                + $"pilot:{selected.Data.cardName}");
            onDone?.Invoke(false);
            yield break;
        }

        ApplyOnPilotMountedAllyBuffsDirect(hostUnit, selected, ownerType);
        if (ownerType == PlayerType.Player)
        {
            NotifyLocalPilotMounted(hostUnit, selected);
            RecordEnemyAiObservedPlayerCardPlay(selected, "MountPilot");
        }

        ApplyUnitAttackFlgFromLink(hostUnit, ownerType);
        TryGrantOperationMeteorFirstStrikeOnPilotMount(hostUnit, selected, ownerType);
        CardGameRule handRule = ResolveHandRule(handOwner);
        handRule?.RefreshHandCountDisplay();
        SyncResourceViewsFromRule(ownerSide);

        yield return CoResolvePilotMountChainAfterAttachCoroutine(hostUnit, selected, ownerType, ownerSide);

        Debug.Log(
            $"[MountPilotFromHand] {selected.Data.cardName}(id:{selected.Data.id}) → "
            + $"{hostUnit.Data.cardName}(id:{hostUnit.Data.id})");
        onDone?.Invoke(true);
    }
}
