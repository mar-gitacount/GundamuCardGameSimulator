using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// バースト等から「このカードの【メイン】を発動する」（コストなし・手札トラッシュなし）。
/// </summary>
public partial class BattleGameMain
{
    private void ApplyActivateSelfOnMain(
        CardController sourceCard,
        PlayerType ownerType,
        Action onComplete)
    {
        if (sourceCard == null || sourceCard.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<EffectData> mainEffects = CollectFreeOnMainEffectsFromCardData(sourceCard.Data);
        if (mainEffects.Count == 0)
        {
            Debug.Log(
                $"[ActivateSelfOnMain] OnMain なし: {sourceCard.Data.cardName}(id:{sourceCard.Data.id})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ActivateSelfOnMain] {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) の【メイン】を "
            + $"コストなしで発動 (effects:{mainEffects.Count})");

        CardData commandData = sourceCard.Data;
        EffectActivationContext chainContext = BuildOnMainChainActivationContext(ownerType, sourceCard);
        BeginEffectChainObservationScope();
        TryExecuteOnMainEffectChain(
            ownerType,
            sourceCard,
            mainEffects,
            0,
            activationCostAlreadyPaid: true,
            chainContext,
            () =>
            {
                EndEffectChainObservationScope();
                NotifyOwnerSpecialMoveCommandActivated(ownerType, commandData, onComplete);
            });
    }
}
