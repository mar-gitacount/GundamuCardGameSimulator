using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自分のユニットがカード効果で破壊された時の盤面監視。
/// EffectType.Destroy による破壊と、効果ダメージ（G-fred の全体ダメージや近接戦闘など）で
/// HP が 0 になった破壊の両方が対象。破壊した効果は自分・相手どちらのものでもよいが、
/// 破壊されたユニットは監視カードの持ち主のものに限る（相手が相手自身のユニットを破壊しても発動しない）。
/// Alpha Azieru 等: ターン1回・強制ドロー。戦闘（攻撃）ダメージによる破壊は対象外。
/// Axis 等: 配備ベースも監視対象。DestroyingOwnerIsAlly 条件で自分の効果破壊時のみフラグを立てる。
/// さらに「自分の効果で自分ユニットを破壊した」履歴は対戦中永続し、Axis 配備前の破壊でも
/// 配備後にメイン起動条件を満たせる（履歴→アーム）。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 監視カードのスナップショット。破壊で CardController が失われても解決できるよう
    /// CardData と EntityId を破壊前に保持する。効果はカードの持ち主に対して解決する。
    /// </summary>
    private readonly struct EffectDestroyWatcher
    {
        public EffectDestroyWatcher(CardController unit, PlayerType owner)
        {
            Unit = unit;
            Owner = owner;
            Data = unit != null ? unit.Data : null;
            EntityId = unit != null ? unit.GetEntityId() : default;
        }

        public CardController Unit { get; }
        public PlayerType Owner { get; }
        public CardData Data { get; }
        public EntityId EntityId { get; }
    }

    private readonly HashSet<PaidActivationUseKey> _ownerEffectDestroyWatchUsesThisTurn =
        new HashSet<PaidActivationUseKey>();

    /// <summary>プレイヤーが自分の効果で自分のユニットを破壊したことがある（対戦中永続）。</summary>
    private bool _playerDestroyedOwnUnitByOwnEffect;

    /// <summary>敵が自分の効果で自分のユニットを破壊したことがある（対戦中永続）。</summary>
    private bool _enemyDestroyedOwnUnitByOwnEffect;

    private void ClearOwnerEffectDestroyWatchUsesThisTurn()
    {
        _ownerEffectDestroyWatchUsesThisTurn.Clear();
    }

    private void ClearOwnEffectDestroyOfOwnUnitHistory()
    {
        _playerDestroyedOwnUnitByOwnEffect = false;
        _enemyDestroyedOwnUnitByOwnEffect = false;
    }

    private bool HasOwnEffectDestroyOfOwnUnitHistory(PlayerType owner)
    {
        return owner == PlayerType.Player
            ? _playerDestroyedOwnUnitByOwnEffect
            : _enemyDestroyedOwnUnitByOwnEffect;
    }

    /// <summary>
    /// 自分の効果で自分ユニットを破壊した事実を対戦中履歴に残し、
    /// 既に場にいる Axis 等へアームを伝播する。
    /// </summary>
    private void RecordOwnEffectDestroyOfOwnUnit(PlayerType owner)
    {
        if (owner == PlayerType.Player)
        {
            _playerDestroyedOwnUnitByOwnEffect = true;
        }
        else
        {
            _enemyDestroyedOwnUnitByOwnEffect = true;
        }

        Debug.Log($"[OwnEffectDestroyHistory] recorded owner:{owner}");
        TryArmDeployedBaseFromOwnEffectDestroyHistory(owner);
    }

    /// <summary>
    /// 配備ベースが「効果破壊アーム」ゲート付きメインを持つとき、履歴があればアームする。
    /// Axis 配備前の破壊でも、配備直後／メイン判定時に起動可能にする。
    /// </summary>
    private void TryArmDeployedBaseFromOwnEffectDestroyHistory(PlayerType owner)
    {
        CardController baseCard = GetDeployedBaseForRuleSide(ToRuleSide(owner));
        TryArmCardFromOwnEffectDestroyHistory(baseCard, owner);
    }

    private void TryArmCardFromOwnEffectDestroyHistory(CardController card, PlayerType owner)
    {
        if (card == null || card.Data == null || !HasOwnEffectDestroyOfOwnUnitHistory(owner))
        {
            return;
        }

        if (!CardUsesOwnerEffectDestroyArmedMainGate(card.Data))
        {
            return;
        }

        if (card.HasOwnerEffectDestroyArmed)
        {
            return;
        }

        card.ArmOwnerEffectDestroyWatch();
        Debug.Log(
            $"[OwnEffectDestroyHistory] armed {card.Data.cardName}(id:{card.Data.id}) owner:{owner}");
    }

    /// <summary>OnMain 等が SourceHasOwnerEffectDestroyArmed でゲートされているカードか。</summary>
    private static bool CardUsesOwnerEffectDestroyArmedMainGate(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed?.activationConditions == null)
            {
                continue;
            }

            for (int c = 0; c < timed.activationConditions.Count; c++)
            {
                EffectActivationCondition cond = timed.activationConditions[c];
                if (cond != null
                    && cond.checkKind == EffectActivationCheckKind.SourceHasOwnerEffectDestroyArmed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// EffectType.Destroy の適用前に、両陣営の盤面にいる監視カードを確保する。
    /// 先に破壊パイプラインが完了しても、破壊された Alpha Azieru 自身を発動対象に残すため。
    /// オンラインでは相手所有カードは相手クライアントが同期受信側で解決するため除外する。
    /// </summary>
    private List<EffectDestroyWatcher> CollectEffectDestroyWatchers()
    {
        List<EffectDestroyWatcher> watchers = new List<EffectDestroyWatcher>();
        AppendEffectDestroyWatchers(playerBattleZoneCards, PlayerType.Player, watchers);
        AppendDeployedBaseEffectDestroyWatcher(PlayerType.Player, watchers);
        if (!IsOnlineBattle())
        {
            AppendEffectDestroyWatchers(enemyBattleZoneCards, PlayerType.Enemy, watchers);
            AppendDeployedBaseEffectDestroyWatcher(PlayerType.Enemy, watchers);
        }

        return watchers;
    }

    /// <summary>
    /// オンラインで相手クライアントが解決した効果破壊を受信した時用。
    /// 自分所有の監視カードのみを確保する（破壊されたユニットが場から除去される前に呼ぶ）。
    /// </summary>
    private List<EffectDestroyWatcher> CollectLocalPlayerEffectDestroyWatchers()
    {
        List<EffectDestroyWatcher> watchers = new List<EffectDestroyWatcher>();
        AppendEffectDestroyWatchers(playerBattleZoneCards, PlayerType.Player, watchers);
        AppendDeployedBaseEffectDestroyWatcher(PlayerType.Player, watchers);
        return watchers;
    }

    private void AppendDeployedBaseEffectDestroyWatcher(PlayerType owner, List<EffectDestroyWatcher> watchers)
    {
        if (watchers == null)
        {
            return;
        }

        CardController baseCard = GetDeployedBaseForRuleSide(ToRuleSide(owner));
        if (baseCard == null || baseCard.Data == null || !HasOwnerEffectDestroyWatch(baseCard.Data))
        {
            return;
        }

        watchers.Add(new EffectDestroyWatcher(baseCard, owner));
    }

    private static void AppendEffectDestroyWatchers(
        List<CardController> zone,
        PlayerType owner,
        List<EffectDestroyWatcher> watchers)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            if (!HasOwnerEffectDestroyWatch(unit.Data))
            {
                continue;
            }

            watchers.Add(new EffectDestroyWatcher(unit, owner));
        }
    }

    /// <summary>
    /// カード効果でユニットを破壊する直前に呼ぶ。
    /// 適用前に確保した監視カードのうち、破壊されたユニットと同じ持ち主のものだけを解決するため、
    /// 他の Alpha Azieru と破壊された自身を含み、相手のユニット破壊では発動しない。
    /// </summary>
    private void NotifyOwnerEffectUnitDestroyed(
        PlayerType effectOwner,
        CardController effectSource,
        List<EffectDestroyWatcher> watchers,
        PlayerType destroyedUnitOwner)
    {
        int watcherCount = watchers != null ? watchers.Count : 0;
        Debug.Log(
            $"[OnUnitDestroyedByOwnerEffect] 破壊通知 effectOwner:{effectOwner} "
            + $"destroyedOwner:{destroyedUnitOwner} "
            + $"source:{(effectSource?.Data != null ? effectSource.Data.cardName : "?")} "
            + $"watchers:{watcherCount} online:{IsOnlineBattle()}");

        // Axis 等: 監視カードがまだ場にいなくても、自分効果による自ユニット破壊は対戦中履歴に残す
        if (effectOwner == destroyedUnitOwner)
        {
            RecordOwnEffectDestroyOfOwnUnit(effectOwner);
        }

        if (watcherCount == 0)
        {
            return;
        }

        for (int i = 0; i < watchers.Count; i++)
        {
            EffectDestroyWatcher watcher = watchers[i];
            if (watcher.Owner != destroyedUnitOwner)
            {
                // 「自分のユニットが破壊された時」なので、相手のユニット破壊ではターン1回を消費させない
                continue;
            }

            TryResolveOwnerEffectDestroyWatch(watcher, effectOwner, effectSource);
        }
    }

    /// <summary>
    /// 効果ダメージで同時に複数ユニットが破壊された場合用。持ち主ごとに解決する。
    /// </summary>
    private void NotifyOwnerEffectUnitsDestroyed(
        PlayerType effectOwner,
        CardController effectSource,
        List<EffectDestroyWatcher> watchers,
        List<CardController> destroyedUnits)
    {
        if (destroyedUnits == null || destroyedUnits.Count == 0)
        {
            return;
        }

        for (int i = 0; i < destroyedUnits.Count; i++)
        {
            CardController destroyed = destroyedUnits[i];
            if (destroyed == null || !IsCardControllerInstanceValid(destroyed))
            {
                continue;
            }

            NotifyOwnerEffectUnitDestroyed(
                effectOwner,
                effectSource,
                watchers,
                ResolveCardOwner(destroyed.transform));
        }
    }

    /// <summary>
    /// オンラインで相手クライアントが解決した効果破壊の同期受信時に呼ぶ。
    /// 破壊されたユニットが場から除去される前に、自分所有の監視カードを解決する。
    /// </summary>
    private void NotifyRemoteEffectDestroyForLocalWatchers(
        CardController destroyedUnit,
        OnlineBattleUnitEffectChange change)
    {
        if (destroyedUnit == null || change == null)
        {
            return;
        }

        // 既定は効果破壊。トークン消滅など効果破壊扱いしない除去だけ 1 が入る。
        if (change.nonEffectDestroy == 1)
        {
            return;
        }

        // 相手クライアントが相手自身のユニットを破壊した同期（自分視点では Enemy ゾーン）では発動しない
        PlayerType destroyedUnitOwner = ResolveCardOwner(destroyedUnit.transform);
        if (destroyedUnitOwner != PlayerType.Player)
        {
            return;
        }

        List<EffectDestroyWatcher> watchers = CollectLocalPlayerEffectDestroyWatchers();
        if (watchers.Count == 0)
        {
            return;
        }

        CardController destroyer = FindDestroyerForRemoteOnDestroyed(change.destroyerInstanceId);
        NotifyOwnerEffectUnitDestroyed(PlayerType.Enemy, destroyer, watchers, destroyedUnitOwner);
    }

    private static bool HasOwnerEffectDestroyWatch(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed != null && timed.IsOnUnitDestroyedByOwnerEffectResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    private void TryResolveOwnerEffectDestroyWatch(
        EffectDestroyWatcher watcher,
        PlayerType effectOwner,
        CardController effectSource)
    {
        CardData data = watcher.Data;
        if (data?.timedEffects == null)
        {
            return;
        }

        // 破壊された監視カード自身も対象。場から外れている場合は効果源なしで解決する。
        // Base（Axis 等）はベーススロット上でもソースとして扱う。
        CardController sourceCard =
            IsCardControllerInstanceValid(watcher.Unit)
            && (IsCardOnBattleZone(watcher.Unit) || IsCardInBaseSlot(watcher.Unit))
                ? watcher.Unit
                : null;
        PlayerType watcherOwner = watcher.Owner;
        EffectActivationContext activationContext = new EffectActivationContext(
            watcherOwner,
            sourceCard,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: watcherOwner == currentPlayerType,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            destroyingCard: effectSource,
            hasDestroyingCardOwner: true,
            destroyingCardOwner: effectOwner);
        for (int blockIndex = 0; blockIndex < data.timedEffects.Count; blockIndex++)
        {
            TimedEffectData timed = data.timedEffects[blockIndex];
            if (timed == null || !timed.IsOnUnitDestroyedByOwnerEffectResolutionBlock())
            {
                continue;
            }

            PaidActivationUseKey useKey = new PaidActivationUseKey(
                watcherOwner,
                watcher.EntityId,
                blockIndex);
            if (timed.oncePerTurn && _ownerEffectDestroyWatchUsesThisTurn.Contains(useKey))
            {
                Debug.Log(
                    $"[OnUnitDestroyedByOwnerEffect] ターン1回使用済みのためスキップ "
                    + $"{data.cardName}(id:{data.id}) block:{blockIndex}");
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnUnitDestroyedByOwnerEffect"))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            if (effects == null || effects.Count == 0)
            {
                Debug.LogWarning(
                    $"[OnUnitDestroyedByOwnerEffect] 効果が解決できません "
                    + $"{data.cardName}(id:{data.id}) effectsName:{timed.effectsName}");
                continue;
            }

            if (timed.oncePerTurn)
            {
                _ownerEffectDestroyWatchUsesThisTurn.Add(useKey);
            }

            string sourceName = effectSource?.Data != null ? effectSource.Data.cardName : "?";
            Debug.Log(
                $"[OnUnitDestroyedByOwnerEffect] 発動 {data.cardName}(id:{data.id}) "
                + $"owner:{watcherOwner} ← Destroy by {sourceName} effectOwner:{effectOwner} "
                + $"selfDestroyed:{sourceCard == null}");

            for (int e = 0; e < effects.Count; e++)
            {
                EffectData effect = effects[e];
                if (effect == null)
                {
                    continue;
                }

                if (!ShouldApplyChainedEffect(effect, activationContext, "OnUnitDestroyedByOwnerEffect"))
                {
                    continue;
                }

                if (effect.type == EffectType.ArmOwnerEffectDestroyFlag)
                {
                    CardController armTarget = sourceCard != null
                        ? sourceCard
                        : (IsCardControllerInstanceValid(watcher.Unit) ? watcher.Unit : null);
                    if (armTarget != null)
                    {
                        armTarget.ArmOwnerEffectDestroyWatch();
                        Debug.Log(
                            $"[OnUnitDestroyedByOwnerEffect] ArmOwnerEffectDestroyFlag "
                            + $"{armTarget.Data?.cardName}(id:{armTarget.Data?.id})");
                    }

                    continue;
                }

                // 強制効果。選択 UI は想定しない（ドロー等）
                // ドローは効果源ユニットを必要としないため、場から外れていても同じ結果になるよう常に直接適用する。
                if (effect.type == EffectType.Draw || sourceCard == null)
                {
                    ApplyEffectDestroyWatchEffectWithoutSource(data, watcherOwner, effect);
                }
                else
                {
                    ApplyEffect(sourceCard, watcherOwner, effect);
                }
            }
        }
    }

    /// <summary>
    /// 効果源ユニットを必要としない効果（ドロー等）を直接適用する。
    /// 監視カード自身が破壊されて場から外れている場合もこの経路で解決する。
    /// </summary>
    private void ApplyEffectDestroyWatchEffectWithoutSource(
        CardData data,
        PlayerType owner,
        EffectData effect)
    {
        if (effect.type == EffectType.Draw)
        {
            int count = effect.value > 0 ? effect.value : 1;
            CardGameRule rule = owner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
            int handBefore = owner == PlayerType.Player ? playerHandCards.Count : enemyHandCards.Count;
            for (int i = 0; i < count; i++)
            {
                CardAddtoHand(rule, owner);
            }

            SyncAllResourceViewsFromRule();
            int handAfter = owner == PlayerType.Player ? playerHandCards.Count : enemyHandCards.Count;
            Debug.Log(
                $"[OnUnitDestroyedByOwnerEffect] Draw x{count} owner:{owner} "
                + $"by {data.cardName}(id:{data.id}) hand:{handBefore}->{handAfter} "
                + $"deckRemain:{(rule != null ? rule.GetRemainingCount() : -1)}");
            return;
        }

        Debug.LogWarning(
            $"[OnUnitDestroyedByOwnerEffect] 破壊済みカードのため {effect.type} は適用できません "
            + $"{data.cardName}(id:{data.id})");
    }
}
