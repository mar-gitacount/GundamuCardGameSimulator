using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// チェーン観測された〔必殺技〕コマンドの【メイン】をコストなしで任意発動する（Domon Kasshu 等）。
/// </summary>
public partial class BattleGameMain
{
    private void ApplyActivateObservedSpecialMoveCommandOnMain(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete)
    {
        CardData commandData = FindLatestObservedSpecialMoveCommand();
        if (commandData == null)
        {
            Debug.Log(
                $"[ActivateObservedSpecialMoveOnMain] 観測に〔必殺技〕コマンドなし "
                + $"(by:{sourceCard?.Data?.cardName ?? "?"})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        List<EffectData> mainEffects = CollectFreeOnMainEffectsFromCardData(commandData);
        if (mainEffects.Count == 0)
        {
            Debug.Log(
                $"[ActivateObservedSpecialMoveOnMain] OnMain なし: {commandData.cardName}(id:{commandData.id})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        TryBeginOptionalConfirmedEffect(
            sourceCard,
            ownerType,
            effect,
            onAccepted: () => RunObservedSpecialMoveCommandOnMain(ownerType, commandData, mainEffects, onComplete),
            onDeclined: () =>
            {
                Debug.Log(
                    $"[ActivateObservedSpecialMoveOnMain] プレイヤーが辞退: {commandData.cardName}(id:{commandData.id})");
                onComplete?.Invoke();
            });
    }

    private void RunObservedSpecialMoveCommandOnMain(
        PlayerType ownerType,
        CardData commandData,
        List<EffectData> mainEffects,
        Action onComplete)
    {
        if (commandData == null || mainEffects == null || mainEffects.Count == 0 || CardImagePrefab == null)
        {
            onComplete?.Invoke();
            return;
        }

        // 捨て札済みのため一時コントローラで【メイン】解決（場に戻さない）
        GameObject tempGo = Instantiate(CardImagePrefab);
        tempGo.name = $"TempSpecialMoveOnMain_{commandData.id}";
        tempGo.SetActive(false);
        CardController temp = tempGo.GetComponent<CardController>();
        if (temp == null)
        {
            Destroy(tempGo);
            onComplete?.Invoke();
            return;
        }

        temp.SetUp(commandData, _ => { });
        Debug.Log(
            $"[ActivateObservedSpecialMoveOnMain] {commandData.cardName}(id:{commandData.id}) の【メイン】を "
            + $"コストなしで発動 (effects:{mainEffects.Count})");

        EffectActivationContext chainContext = BuildActivationContext(ownerType, temp);
        TryExecuteOnMainEffectChain(
            ownerType,
            temp,
            mainEffects,
            0,
            activationCostAlreadyPaid: true,
            chainContext,
            () =>
            {
                if (tempGo != null)
                {
                    Destroy(tempGo);
                }

                NotifyOwnerSpecialMoveCommandActivated(ownerType, commandData, onComplete);
            });
    }

    private CardData FindLatestObservedSpecialMoveCommand()
    {
        IReadOnlyList<CardData> observed = GetActiveObservedCardsForActivation();
        if (observed == null || observed.Count == 0)
        {
            return null;
        }

        for (int i = observed.Count - 1; i >= 0; i--)
        {
            CardData data = observed[i];
            if (data == null || !data.IsCommand() || !data.HasFeatureId(SpecialMoveFeatureId))
            {
                continue;
            }

            return data;
        }

        return null;
    }

    private static List<EffectData> CollectFreeOnMainEffectsFromCardData(CardData cardData)
    {
        return CollectMountedCardOnMainEffectsForFreeActivation(cardData);
    }
}
