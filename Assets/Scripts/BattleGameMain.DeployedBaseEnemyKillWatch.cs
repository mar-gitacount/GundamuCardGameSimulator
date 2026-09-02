using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配備ベースの OnEnemyUnitDestroyed（Peacemillion 等）。
/// 味方ユニットが戦闘ダメージで敵を破壊したとき、ベース上の効果を解決する。
/// </summary>
public partial class BattleGameMain
{
  private void NotifyDeployedBaseOnAllyEnemyUnitDestroyed(
      CardController destroyedUnit,
      PlayerType destroyedOwner,
      CardController destroyedBy,
      bool destroyedByBattleDamage,
      Action onComplete)
  {
    if (!TryResolveEnemyUnitKillContext(
            destroyedUnit,
            destroyedOwner,
            destroyedBy,
            out CardController killer,
            out PlayerType killerOwner))
    {
      onComplete?.Invoke();
      return;
    }

    CardController baseCard = GetDeployedBaseForRuleSide(ToRuleSide(killerOwner));
    if (baseCard == null || baseCard.Data == null || !IsCardInBaseSlot(baseCard))
    {
      onComplete?.Invoke();
      return;
    }

    EffectActivationContext activationContext = new EffectActivationContext(
        killerOwner,
        baseCard,
        playerBattleZoneCards,
        enemyBattleZoneCards,
        CollectHandControllers(cardGameRule),
        CollectHandControllers(enemyCardGameRule),
        isOwnerTurn: killerOwner == currentPlayerType,
        mountHostUnit: killer,
        mountedPilot: killer.MountedPilot,
        observedCards: GetActiveObservedCardsForActivation(),
        ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
        opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
        priorChainDealtDamage: GetEffectChainDealtDamage(),
        destroyingCard: killer,
        hasDestroyingCardOwner: true,
        destroyingCardOwner: killerOwner,
        destroyedByBattleDamage: destroyedByBattleDamage,
        sourceAttackingEnemyUnit: IsSourceAttackingEnemyUnit(killer, allowDestroyedDefender: true));

    List<TimedEffectData> blocks = new List<TimedEffectData>();
    AppendDeployedBaseOnEnemyUnitDestroyedBlocks(baseCard, activationContext, blocks);
    if (blocks.Count == 0)
    {
      onComplete?.Invoke();
      return;
    }

    Debug.Log(
        $"[DeployedBaseOnEnemyKill] base:{baseCard.Data.cardName}(id:{baseCard.Data.id}) "
        + $"killer:{killer.Data.cardName}(id:{killer.Data.id}) owner:{killerOwner} "
        + $"battleDmg:{destroyedByBattleDamage} blocks:{blocks.Count}");

    RunDeployedBaseOnEnemyKillTimedBlocks(
        baseCard,
        killerOwner,
        killer,
        blocks,
        0,
        onComplete);
  }

  private void AppendDeployedBaseOnEnemyUnitDestroyedBlocks(
      CardController baseCard,
      EffectActivationContext activationContext,
      List<TimedEffectData> blocks)
  {
    CardData data = baseCard?.Data;
    if (data?.timedEffects == null || blocks == null)
    {
      return;
    }

    for (int i = 0; i < data.timedEffects.Count; i++)
    {
      TimedEffectData timed = data.timedEffects[i];
      if (timed == null || !timed.IsOnEnemyUnitDestroyedResolutionBlock())
      {
        continue;
      }

      if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(activationContext.OwnerType, baseCard, i))
      {
        continue;
      }

      if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
      {
        continue;
      }

      blocks.Add(timed);
    }
  }

  private void RunDeployedBaseOnEnemyKillTimedBlocks(
      CardController baseCard,
      PlayerType ownerType,
      CardController killer,
      List<TimedEffectData> blocks,
      int blockIndex,
      Action onComplete)
  {
    if (blocks == null || blockIndex >= blocks.Count)
    {
      onComplete?.Invoke();
      return;
    }

    TimedEffectData block = blocks[blockIndex];
    if (block != null && block.oncePerTurn && baseCard != null)
    {
      int timedIndex = IndexOfTimedEffectOnCard(baseCard, block);
      if (timedIndex >= 0)
      {
        MarkPaidActivationUsedThisTurn(ownerType, baseCard, timedIndex);
      }
    }

    IReadOnlyList<EffectData> effects = block?.GetResolvedEffects();
    if (effects == null || effects.Count == 0)
    {
      RunDeployedBaseOnEnemyKillTimedBlocks(
          baseCard, ownerType, killer, blocks, blockIndex + 1, onComplete);
      return;
    }

    RunDeployedBaseOnEnemyKillEffectChain(
        baseCard,
        ownerType,
        killer,
        effects,
        0,
        () => RunDeployedBaseOnEnemyKillTimedBlocks(
            baseCard,
            ownerType,
            killer,
            blocks,
            blockIndex + 1,
            onComplete));
  }

  private void RunDeployedBaseOnEnemyKillEffectChain(
      CardController baseCard,
      PlayerType ownerType,
      CardController killer,
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
      RunDeployedBaseOnEnemyKillEffectChain(baseCard, ownerType, killer, effects, index + 1, onDone);
      return;
    }

    EffectActivationContext activationContext = new EffectActivationContext(
        ownerType,
        baseCard,
        playerBattleZoneCards,
        enemyBattleZoneCards,
        CollectHandControllers(cardGameRule),
        CollectHandControllers(enemyCardGameRule),
        isOwnerTurn: ownerType == currentPlayerType,
        mountHostUnit: killer,
        mountedPilot: killer?.MountedPilot,
        ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
        opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
        destroyingCard: killer,
        hasDestroyingCardOwner: true,
        destroyingCardOwner: ownerType,
        destroyedByBattleDamage: true);

    if (!ShouldApplyChainedEffect(effect, activationContext, "DeployedBaseOnEnemyKill"))
    {
      RunDeployedBaseOnEnemyKillEffectChain(baseCard, ownerType, killer, effects, index + 1, onDone);
      return;
    }

    if (effect.type == EffectType.RecoverHp && killer != null && killer.IsRepairEligibleUnit())
    {
      int amount = effect.value > 0 ? effect.value : 1;
      void ApplyRecover()
      {
        ApplyRecoverHpEffect(new List<CardController> { killer }, amount);
        BeginOnlineEffectSyncBatch(ownerType);
        FlushOnlineEffectSyncBatch();
        SyncAllResourceViewsFromRule();
        RunDeployedBaseOnEnemyKillEffectChain(baseCard, ownerType, killer, effects, index + 1, onDone);
      }

      if (effect.optionalPlayerConfirm)
      {
        TryBeginOptionalConfirmedEffect(
            baseCard,
            ownerType,
            effect,
            onAccepted: ApplyRecover,
            onDeclined: () => RunDeployedBaseOnEnemyKillEffectChain(
                baseCard, ownerType, killer, effects, index + 1, onDone));
        return;
      }

      ApplyRecover();
      return;
    }

    ApplyEffectRespectingLookAsync(
        baseCard,
        ownerType,
        effect,
        () => RunDeployedBaseOnEnemyKillEffectChain(baseCard, ownerType, killer, effects, index + 1, onDone));
  }
}
