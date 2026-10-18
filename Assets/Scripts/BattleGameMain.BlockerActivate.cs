using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ブロッカーのアクティブ化と直接攻撃制限（Archangel 等）。</summary>
public partial class BattleGameMain
{
    private List<CardController> _effectChainLastPickedTargets = new List<CardController>();

    private void ClearEffectChainLastPickedTargets()
    {
        _effectChainLastPickedTargets.Clear();
    }

    private void SetEffectChainLastPickedTargets(IReadOnlyList<CardController> targets)
    {
        _effectChainLastPickedTargets.Clear();
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit != null
                && unit.Data != null
                && unit.CurrentHp > 0
                && (unit.Data.IsUnitLike() || unit.Data.type == Type.Base))
            {
                _effectChainLastPickedTargets.Add(unit);
            }
        }
    }

    private List<CardController> GetAliveEffectChainLastPickedTargets()
    {
        List<CardController> alive = new List<CardController>(_effectChainLastPickedTargets.Count);
        for (int i = 0; i < _effectChainLastPickedTargets.Count; i++)
        {
            CardController unit = _effectChainLastPickedTargets[i];
            if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (unit.Data.type == Type.Base)
            {
                alive.Add(unit);
                continue;
            }

            if (unit.Data.IsUnitLike() && IsUnitAliveOnAnyDeployField(unit))
            {
                alive.Add(unit);
            }
        }

        return alive;
    }

    private bool TryExecutePriorChainPickedTargetEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onChainContinue)
    {
        if (effect == null || !effect.selectionMode.IsUsePriorChainPickedTargetMode())
        {
            return false;
        }

        List<CardController> targets = GetAliveEffectChainLastPickedTargets();
        if (targets.Count == 0)
        {
            Debug.LogWarning(
                $"[Effect] 直前の選択対象がありません ({effect.type} cardId:{sourceCard?.Data?.id})。");
            return false;
        }

        ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, targets);
        onChainContinue?.Invoke();
        return true;
    }

    private static bool TryApplyActivateToUnit(CardController unit, bool grantAttack = true)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
        {
            return false;
        }

        if (!unit.IsRestState)
        {
            return false;
        }

        unit.SetUnitRestVisual(false);
        unit.SetAttackFlg(grantAttack ? AttackFlg.True : AttackFlg.False);
        return true;
    }

    private void ApplyActivateEffect(EffectData effect, PlayerType ownerType, List<CardController> targets)
    {
        // 後続の UsePriorChainPickedTarget は「実際に ACTIVE になったユニット」だけを参照する。
        // 既に ACTIVE の場合、直前の別効果で選んだ対象を誤って引き継がない。
        ClearEffectChainLastPickedTargets();
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        List<CardController> activated = new List<CardController>();
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (IsOnAttackTrashReturnChainSelfActivateBlocked(target, effect))
            {
                continue;
            }

            if (TryApplyActivateToUnit(target, grantAttack: !effect.activateWithoutGrantingAttack))
            {
                QueueOnlineUnitActivate(target);
                activated.Add(target);
                applied++;
            }
        }

        if (effect.denyAttackThisTurn)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                CardController denyTarget = targets[i];
                if (denyTarget == null || denyTarget.Data == null || !denyTarget.Data.IsUnitLike())
                {
                    continue;
                }

                denyTarget.SetAttackFlg(AttackFlg.False);
            }
        }

        if (applied > 0)
        {
            SetEffectChainLastPickedTargets(activated);
            Debug.Log($"[Effect] Activate applied:{applied} target:{effect.target}");
        }
    }

    private bool TryApplyNotDirectAttackMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.NotDirectAttack || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddNotDirectAttackUntilEndOfTurnGrant();
            }

            applied++;
            Debug.Log(
                $"[NotDirectAttack] {effect.duration} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private bool TryApplyCannotBeChosenAsAttackTargetMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null
            || effect.type != EffectType.CannotBeChosenAsAttackTarget
            || targets == null
            || targets.Count == 0)
        {
            return false;
        }

        // Permanent はカード常時パッシブ（攻撃対象判定のみ）。解決時付与は UntilEndOfTurn。
        if (effect.duration == EffectDuration.Permanent)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            GrantCannotBeChosenAsAttackUntilEndOfTurn(unit);
            applied++;
            Debug.Log(
                $"[CannotBeChosenAsAttackTarget] UntilEndOfTurn 付与: {unit.Data.cardName}(id:{unit.Data.id}) "
                + $"instance:{unit.BattleInstanceId}");
        }

        return applied > 0;
    }

    /// <summary>
    /// ST11-014 等。ユニットへ「このターンアタック先に選べない」を付与し、オンライン同期する。
    /// </summary>
    private void GrantCannotBeChosenAsAttackUntilEndOfTurn(CardController unit)
    {
        if (unit == null)
        {
            return;
        }

        if (!unit.HasCannotBeChosenAsAttackUntilEndOfTurnGrant)
        {
            unit.AddCannotBeChosenAsAttackUntilEndOfTurnGrant();
        }

        AssignBattleInstanceIdIfNeeded(unit);
        if (unit.BattleInstanceId > 0)
        {
            _cannotBeChosenAsAttackUntilEotInstanceIds.Add(unit.BattleInstanceId);
        }

        QueueOnlineCannotBeChosenAsAttackTargetGrant(unit);
    }

    /// <summary>ST08-009 等。次のオーナースタートフェイズで通常アクティブ化しない付与。</summary>
    private bool TryApplyPreventNextStartPhaseActiveMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null
            || effect.type != EffectType.PreventNextStartPhaseActive
            || targets == null
            || targets.Count == 0)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            GrantSkipNextStartPhaseActive(unit);
            applied++;
            Debug.Log(
                $"[PreventNextStartPhaseActive] 付与: {unit.Data.cardName}(id:{unit.Data.id}) "
                + $"instance:{unit.BattleInstanceId}");
        }

        return applied > 0;
    }

    private void GrantSkipNextStartPhaseActive(CardController unit)
    {
        if (unit == null)
        {
            return;
        }

        unit.AddSkipNextStartPhaseActiveGrant();
        AssignBattleInstanceIdIfNeeded(unit);
        QueueOnlineSkipNextStartPhaseActiveGrant(unit);
    }

    private bool TryApplyFirstStrikeMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.FirstStrike || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddFirstStrikeUntilEndOfTurnGrant();
            }

            applied++;
            Debug.Log(
                $"[FirstStrike] {effect.duration} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private bool TryApplyHighMobilityMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.HighMobility || targets == null || targets.Count == 0)
        {
            return false;
        }

        // Permanent はカード印刷のマーカー判定（HasHighMobilityAbility）に任せる
        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            // パイロット効果の Self は搭乗ユニットへ付与する（ST03-011 シャア等）
            if (unit != null && unit.Data != null && unit.Data.IsPilot())
            {
                unit = ResolveEffectSourceBattleHost(unit) ?? unit;
            }

            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            unit.AddHighMobilityUntilEndOfTurnGrant();
            applied++;
            Debug.Log(
                $"[HighMobility] UntilEndOfTurn 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private bool TryApplyBattleDamageImmunityFromLowApEnemyMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null
            || effect.type != EffectType.BattleDamageImmunityFromLowApEnemy
            || targets == null
            || targets.Count == 0)
        {
            return false;
        }

        // Permanent はカード印刷のパッシブ（ShouldIgnoreBattleDamageFromAttacker）に任せる
        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle)
        {
            return false;
        }

        int applied = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit != null && unit.Data != null && unit.Data.IsPilot())
            {
                unit = ResolveEffectSourceBattleHost(unit) ?? unit;
            }

            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddBattleDamageImmunityUntilEndOfTurnGrant(effect);
            }
            else
            {
                unit.AddBattleDamageImmunityUntilEndOfBattleGrant(effect);
            }

            applied++;
            Debug.Log(
                $"[BattleDamageImmunity] {effect.duration} 付与: {unit.Data.cardName}(id:{unit.Data.id}) "
                + $"value:{effect.value} stat:{effect.statTarget}");
        }

        return applied > 0;
    }

    private void ClearBattleDamageImmunityUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearBattleDamageImmunityUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearBattleDamageImmunityUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private void ClearBattleDamageImmunityUntilEndOfBattleGrantsForAllInPlayUnits()
    {
        ClearBattleDamageImmunityUntilEndOfBattleGrantsOnZone(playerBattleZoneCards);
        ClearBattleDamageImmunityUntilEndOfBattleGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearBattleDamageImmunityUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBattleDamageImmunityUntilEndOfTurnGrants();
        }
    }

    private static void ClearBattleDamageImmunityUntilEndOfBattleGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBattleDamageImmunityUntilEndOfBattleGrants();
        }
    }

    private void ClearHighMobilityUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearHighMobilityUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearHighMobilityUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearHighMobilityUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearHighMobilityUntilEndOfTurnGrants();
        }
    }

    private bool TryApplyGrantBreachMarker(
        EffectData effect,
        List<CardController> targets,
        CardController sourceCard = null)
    {
        if (effect == null || effect.type != EffectType.GrantBreach || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        CardController magnitudeSource = sourceCard != null ? sourceCard : targets[0];
        PlayerType ownerType = magnitudeSource != null
            ? ResolveCardOwner(magnitudeSource.transform)
            : PlayerType.Player;
        int amount = ResolveEffectMagnitude(effect, ownerType, magnitudeSource);
        if (amount <= 0)
        {
            amount = effect.value > 0 ? effect.value : 0;
        }

        if (amount <= 0)
        {
            return false;
        }

        int applied = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.requireTargetLacksBreach && unit.GetBreachAmount() > 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddBreachUntilEndOfTurnGrant(amount);
            }
            else if (effect.duration == EffectDuration.UntilEndOfBattle)
            {
                unit.AddBreachUntilEndOfBattleGrant(amount);
            }

            applied++;
            Debug.Log(
                $"[GrantBreach] {effect.duration} Breach{amount} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private readonly List<IncomingBattleDamageRedirectRecord> _incomingBattleDamageRedirects =
        new List<IncomingBattleDamageRedirectRecord>();

    /// <summary>今このバトル中の受け替え（オブジェクト破棄後も owner + cardId で再解決する）。</summary>
    private CardController _battleIncomingRedirectHost;
    private PlayerType _battleIncomingRedirectOwner;
    private int _battleIncomingRedirectHostCardId;
    private int _battleIncomingRedirectHostInstanceId;
    private bool _battleIncomingRedirectToBase;
    private int _battleIncomingRedirectRecipientCardId;
    private CardController _battleIncomingRedirectRecipientLive;

    private struct IncomingBattleDamageRedirectRecord
    {
        public int HostBattleInstanceId;
        public int HostCardId;
        public PlayerType HostOwner;
        public int RecipientCardId;
        public int RecipientBattleInstanceId;
        public bool RecipientIsBase;
        public PlayerType RecipientOwner;
    }

    private bool TryApplyIncomingBattleDamageRedirectMarker(
        EffectData effect,
        CardController sourceCard,
        List<CardController> targets,
        PlayerType? ownerHint = null)
    {
        if (sourceCard == null || sourceCard.Data == null || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect != null
            && effect.type != EffectType.RedirectIncomingBattleDamage
            && sourceCard.Data.id != Gd06018QubeleyMkIICardId
            && !ShouldBindIncomingBattleDamageRedirect(sourceCard, effect))
        {
            return false;
        }

        CardController picked = null;
        for (int i = 0; i < targets.Count; i++)
        {
            CardController candidate = targets[i];
            if (candidate != null && candidate.Data != null)
            {
                picked = candidate;
                break;
            }
        }

        if (picked == null || picked.Data == null)
        {
            return false;
        }

        PlayerType owner = ResolveIncomingRedirectHostOwner(sourceCard, ownerHint);
        ForceBindIncomingBattleDamageRedirect(sourceCard, picked, owner);
        return true;
    }

    /// <summary>
    /// Action で選んだカードを、このバトル中の「相手ユニットからの戦闘ダメージ」の行き先に固定する。
    /// ホスト照合に失敗しても cardId は必ず残す。
    /// </summary>
    private void ForceBindIncomingBattleDamageRedirect(
        CardController source,
        CardController picked,
        PlayerType owner)
    {
        CardController battleHost = ResolveEffectSourceBattleHost(source) ?? source;
        int hostCardId = battleHost != null && battleHost.Data != null
            ? battleHost.Data.id
            : source.Data.id;

        CardController liveHost = MatchIncomingRedirectHostInAttackFlow(hostCardId)
            ?? FindOwnerFieldUnitByCardId(owner, hostCardId)
            ?? FindOwnerFieldUnitByCardId(PlayerType.Player, hostCardId)
            ?? FindOwnerFieldUnitByCardId(PlayerType.Enemy, hostCardId)
            ?? (battleHost != null && battleHost.Data != null && battleHost.Data.IsUnitLike()
                ? battleHost
                : null);
        if (liveHost != null)
        {
            owner = ResolveIncomingRedirectHostOwner(liveHost, owner);
        }

        bool toBase = picked.Data.type == Type.Base || IsCardInBaseSlot(picked);
        int recipientId = picked.Data.id;
        ArmGd06018ReplacementDamageStep(source, picked, owner);

        if (liveHost != null)
        {
            AssignBattleInstanceIdIfNeeded(liveHost);
        }

        _battleIncomingRedirectHost = liveHost;
        _battleIncomingRedirectOwner = owner;
        _battleIncomingRedirectHostCardId = hostCardId;
        _battleIncomingRedirectHostInstanceId = liveHost != null ? liveHost.BattleInstanceId : 0;
        _battleIncomingRedirectToBase = toBase;
        _battleIncomingRedirectRecipientCardId = recipientId;
        _battleIncomingRedirectRecipientLive = toBase
            ? (FindOwnerBaseForIncomingRedirect(owner, recipientId) ?? picked)
            : (FindLiveBattleZoneUnitMatching(picked) ?? picked);

        StampIncomingRedirectIfHostCard(liveHost, picked, owner, hostCardId);
        StampIncomingRedirectIfHostCard(attackFlowAttackerUnit, picked, owner, hostCardId);
        StampIncomingRedirectIfHostCard(attackFlowDeclaredDefenderUnit, picked, owner, hostCardId);
        StampIncomingRedirectIfHostCard(attackFlowBlockRedirectUnit, picked, owner, hostCardId);
        StampIncomingRedirectIfHostCard(pendingUnitAttackAttacker, picked, owner, hostCardId);
        if (liveHost != null)
        {
            StampIncomingBattleDamageRedirectOnMatchingZoneUnits(owner, liveHost, picked);
        }

        IncomingBattleDamageRedirectRecord record = new IncomingBattleDamageRedirectRecord
        {
            HostBattleInstanceId = _battleIncomingRedirectHostInstanceId,
            HostCardId = hostCardId,
            HostOwner = owner,
            RecipientCardId = recipientId,
            RecipientBattleInstanceId = picked.BattleInstanceId,
            RecipientIsBase = toBase,
            RecipientOwner = owner
        };
        ReplaceIncomingBattleDamageRedirectRecord(record);

        Debug.Log(
            $"[RedirectBattleDmg] FORCE bind hostId:{hostCardId} inst:{_battleIncomingRedirectHostInstanceId} "
            + $"{liveHost?.Data?.cardName} → {picked.Data.cardName}(id:{recipientId}) "
            + $"owner:{owner} base:{toBase}");
    }

    private bool IsIncomingBattleDamageRedirectHost(CardController unit)
    {
        if (unit == null || unit.Data == null || _battleIncomingRedirectHostCardId <= 0)
        {
            return false;
        }

        if (_battleIncomingRedirectHostInstanceId > 0
            && unit.BattleInstanceId == _battleIncomingRedirectHostInstanceId)
        {
            return true;
        }

        return unit.Data.id == _battleIncomingRedirectHostCardId;
    }

    /// <summary>
    /// 戦闘ヒット時: ホストが受けるダメージを、Action で選んだカードへ移す。
    /// 受け替え先が取れないときは false（ホストが通常どおり受ける）。
    /// </summary>
    private bool TryApplyIncomingBattleDamageRedirectOnCombatHit(
        CardController damageTarget,
        CardController damageSource,
        int damage)
    {
        if (damageTarget == null || damage <= 0)
        {
            return false;
        }

        if (!IsIncomingBattleDamageRedirectHostUnit(damageTarget))
        {
            return false;
        }

        CardController recipient = ResolveLiveIncomingRedirectRecipientForHost(damageTarget);
        if (recipient == null || recipient == damageTarget)
        {
            Debug.LogWarning(
                $"[RedirectBattleDmg] 受け替え先なし host:{damageTarget.Data?.cardName} "
                + $"picked:{_gd06018PickedLive?.Data?.cardName} "
                + $"live:{_battleIncomingRedirectRecipientLive?.Data?.cardName} "
                + $"recipientId:{_battleIncomingRedirectRecipientCardId} base:{_battleIncomingRedirectToBase}");
            return false;
        }

        Debug.Log(
            $"[RedirectBattleDmg] 敵ダメージ {damage} を {damageTarget.Data?.cardName} から "
            + $"{recipient.Data?.cardName}(id:{recipient.Data?.id} type:{recipient.Data?.type} hp:{recipient.CurrentHp}) へ移す "
            + $"from:{damageSource?.Data?.cardName}");
        ApplyResolvedBattleDamageToRecipient(recipient, damageTarget, damageSource, damage);
        return true;
    }

    private bool HasActiveIncomingBattleDamageRedirect()
    {
        return _gd06018Active
            || _gd06018PickedLive != null
            || _gd06018RecipientCardId > 0
            || _battleIncomingRedirectRecipientLive != null
            || _battleIncomingRedirectRecipientCardId > 0
            || _battleIncomingRedirectHostCardId > 0;
    }

    private bool ShouldSuppressBattleTrashDueToIncomingRedirect(CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        CardController recipient = ResolveCombatIncomingDamageTarget(unit);
        if (recipient == null || recipient == unit)
        {
            return false;
        }

        if (unit.CurrentHp < 1)
        {
            unit.RestoreHpAfterRedirectAbsorb(Mathf.Max(1, unit.Data != null ? unit.Data.hp : 1));
        }

        Debug.Log(
            $"[GD06-018] 本体の破壊を抑止 {unit.Data?.cardName} → 受け替え先 {recipient.Data?.cardName}");
        return true;
    }

    private bool IsIncomingBattleDamageRedirectHostUnit(CardController unit)
    {
        if (unit == null || unit.Data == null || !HasActiveIncomingBattleDamageRedirect())
        {
            return false;
        }

        if (IsGd06018QubeleyMkII(unit))
        {
            if (unit.HasIncomingBattleDamageRedirectThisBattle
                || IsIncomingBattleDamageRedirectHost(unit)
                || IsBattleIncomingRedirectHost(unit))
            {
                return true;
            }

            // 相手ターンでも被攻撃／攻撃中のキュベレイをホストにする（Player=0 を未設定扱いしない）
            if (IsSameBattleUnitForRedirect(unit, attackFlowDeclaredDefenderUnit)
                || IsSameBattleUnitForRedirect(unit, attackFlowBlockRedirectUnit)
                || IsSameBattleUnitForRedirect(unit, attackFlowAttackerUnit))
            {
                return true;
            }

            PlayerType unitOwner = ResolveCardOwner(unit.transform);
            return unitOwner == _battleIncomingRedirectOwner || unitOwner == _gd06018Owner;
        }

        return IsBattleIncomingRedirectHost(unit)
            || IsIncomingBattleDamageRedirectHost(unit)
            || unit.HasIncomingBattleDamageRedirectThisBattle;
    }

    private CardController ResolveLiveIncomingRedirectRecipientForHost(CardController host)
    {
        CardController live = GetGd06018PickedLive();
        if (live != null && live != host && live.CurrentHp > 0)
        {
            return live;
        }

        if (_gd06018RecipientCardId > 0 && host != null)
        {
            live = FindIncomingRedirectRecipientByCardId(
                _gd06018RecipientCardId,
                _gd06018RecipientIsBase,
                _gd06018Owner,
                host);
            if (live != null && live != host && live.CurrentHp > 0)
            {
                return live;
            }
        }

        live = ResolveIncomingRedirectRecipientForCombat(host);
        if (live != null && live != host && live.CurrentHp > 0)
        {
            return live;
        }

        if (_battleIncomingRedirectToBase || (live != null && live.Data != null && live.Data.type == Type.Base))
        {
            PlayerType owner = _battleIncomingRedirectOwner != default
                ? _battleIncomingRedirectOwner
                : _gd06018Owner;
            CardController ownerBase = FindOwnerBaseForIncomingRedirect(
                owner,
                _battleIncomingRedirectRecipientCardId);
            if (ownerBase != null && ownerBase != host && ownerBase.CurrentHp > 0)
            {
                return ownerBase;
            }
        }

        return null;
    }

    /// <summary>相手ユニットがキュベレイへ与える戦闘ダメージを、Action で選んだカードへ適用する。</summary>
    private void ApplyRedirectedIncomingBattleDamage(
        CardController host,
        CardController damageSource,
        PlayerType hostOwner,
        PlayerType sourceOwner,
        int damage)
    {
        if (host == null || damage <= 0)
        {
            return;
        }

        CardController recipient = ResolveIncomingRedirectRecipientForCombat(host);
        if (recipient == null)
        {
            Debug.LogWarning(
                $"[RedirectBattleDmg] 受け替え先が見つからない host:{host.Data?.cardName} "
                + $"recipientId:{_battleIncomingRedirectRecipientCardId} base:{_battleIncomingRedirectToBase}");
            return;
        }

        Debug.Log(
            $"[RedirectBattleDmg] {host.Data?.cardName} が受ける {damage} "
            + $"from {damageSource?.Data?.cardName} → {recipient.Data?.cardName}");
        ApplyResolvedBattleDamageToRecipient(recipient, host, damageSource, damage);
    }

    /// <summary>Action 対象クリック直後に受け替えを結ぶ（効果タイプや後段コールバックに依存しない）。</summary>
    private void BindIncomingBattleDamageRedirectOnActionTargetClick(
        CardController sourceCard,
        CardController picked)
    {
        if (picked == null || picked.Data == null)
        {
            return;
        }

        int sourceId = sourceCard != null && sourceCard.Data != null ? sourceCard.Data.id : 0;
        bool shouldBind = IsGd06018QubeleyMkII(sourceCard)
            || ShouldBindIncomingBattleDamageRedirect(sourceCard, null);
        if (!shouldBind && sourceCard != null && sourceCard.Data != null)
        {
            List<EffectData> onActionEffects = GetEffectsByTiming(sourceCard.Data, EffectTiming.OnAction);
            for (int i = 0; i < onActionEffects.Count; i++)
            {
                if (onActionEffects[i] != null
                    && onActionEffects[i].type == EffectType.RedirectIncomingBattleDamage)
                {
                    shouldBind = true;
                    break;
                }
            }
        }

        if (!shouldBind)
        {
            return;
        }

        CardController liveHost = MatchIncomingRedirectHostInAttackFlow(sourceId)
            ?? FindOwnerFieldUnitByCardId(PlayerType.Player, sourceId)
            ?? FindOwnerFieldUnitByCardId(PlayerType.Enemy, sourceId);
        PlayerType owner = ResolveIncomingRedirectHostOwner(
            liveHost != null ? liveHost : sourceCard,
            null);

        CardController bindSource = liveHost != null ? liveHost : sourceCard;
        ArmGd06018ReplacementDamageStep(bindSource != null ? bindSource : sourceCard, picked, owner);
        if (bindSource != null && bindSource.Data != null)
        {
            ForceBindIncomingBattleDamageRedirect(bindSource, picked, owner);
        }
        else
        {
            _battleIncomingRedirectHostCardId = sourceId;
            _battleIncomingRedirectOwner = owner;
            _battleIncomingRedirectRecipientCardId = picked.Data.id;
            _battleIncomingRedirectToBase = picked.Data.type == Type.Base || IsCardInBaseSlot(picked);
            _battleIncomingRedirectRecipientLive = picked;
            ArmGd06018ReplacementDamageStep(sourceCard, picked, owner);
        }

        Debug.Log(
            $"[RedirectBattleDmg] click-bind sourceId:{sourceId} picked:{picked.Data.cardName} "
            + $"(id:{picked.Data.id} base:{_battleIncomingRedirectToBase}) owner:{owner}");
    }

    private CardController ResolveIncomingRedirectRecipientForCombat(CardController host)
    {
        if (IsCardControllerInstanceValid(_battleIncomingRedirectRecipientLive)
            && _battleIncomingRedirectRecipientLive.Data != null
            && _battleIncomingRedirectRecipientLive.CurrentHp > 0
            && _battleIncomingRedirectRecipientLive != host)
        {
            return _battleIncomingRedirectRecipientLive;
        }

        if (_battleIncomingRedirectRecipientCardId > 0)
        {
            CardController ally = FindIncomingRedirectRecipientByCardId(
                _battleIncomingRedirectRecipientCardId,
                _battleIncomingRedirectToBase,
                _battleIncomingRedirectOwner,
                host);
            if (IsUsableIncomingBattleDamageRedirectRecipient(ally, host))
            {
                return ally;
            }
        }

        return ResolveBoundIncomingRedirectRecipient(host)
            ?? ResolveIncomingBattleDamageRedirectTarget(host);
    }

    /// <summary>
    /// 受け替えホスト照合。相手がキュベレイへアタックしたときは防御側を優先する。
    /// </summary>
    private CardController MatchIncomingRedirectHostInAttackFlow(int cardId)
    {
        if (cardId <= 0)
        {
            return null;
        }

        if (UnitMatchesIncomingRedirectHostCard(attackFlowDeclaredDefenderUnit, cardId))
        {
            return attackFlowDeclaredDefenderUnit;
        }

        if (UnitMatchesIncomingRedirectHostCard(attackFlowBlockRedirectUnit, cardId))
        {
            return attackFlowBlockRedirectUnit;
        }

        if (UnitMatchesIncomingRedirectHostCard(attackFlowAttackerUnit, cardId))
        {
            return attackFlowAttackerUnit;
        }

        if (UnitMatchesIncomingRedirectHostCard(pendingUnitAttackAttacker, cardId))
        {
            return pendingUnitAttackAttacker;
        }

        return null;
    }

    private static bool UnitMatchesIncomingRedirectHostCard(CardController unit, int cardId)
    {
        return unit != null
            && unit.Data != null
            && unit.Data.id == cardId
            && unit.CurrentHp > 0;
    }

    /// <summary>ターンプレイヤーではなく、キュベレイ本体がいる盤面の所有者を使う。</summary>
    private PlayerType ResolveIncomingRedirectHostOwner(CardController host, PlayerType? ownerHint)
    {
        if (host != null)
        {
            if (playerBattleZoneCards != null && playerBattleZoneCards.Contains(host))
            {
                return PlayerType.Player;
            }

            if (enemyBattleZoneCards != null && enemyBattleZoneCards.Contains(host))
            {
                return PlayerType.Enemy;
            }

            PlayerType fromTransform = ResolveCardOwner(host.transform);
            if (fromTransform == PlayerType.Player || fromTransform == PlayerType.Enemy)
            {
                return fromTransform;
            }
        }

        if (ownerHint.HasValue)
        {
            return ownerHint.Value;
        }

        return PlayerType.Player;
    }

    private CardController FindIncomingRedirectRecipientByCardId(
        int cardId,
        bool isBase,
        PlayerType preferredOwner,
        CardController host)
    {
        if (cardId <= 0)
        {
            return null;
        }

        PlayerType hostOwner = host != null
            ? ResolveIncomingRedirectHostOwner(host, preferredOwner)
            : preferredOwner;
        PlayerType[] owners =
        {
            hostOwner,
            preferredOwner,
            PlayerType.Player,
            PlayerType.Enemy
        };
        for (int i = 0; i < owners.Length; i++)
        {
            PlayerType owner = owners[i];
            CardController found = isBase
                ? FindOwnerBaseForIncomingRedirect(owner, cardId)
                : FindOwnerFieldUnitByCardId(owner, cardId);
            if (found != null && found != host && found.CurrentHp > 0)
            {
                return found;
            }
        }

        return null;
    }

    private CardController MatchAttackFlowUnitByCardId(int cardId)
    {
        return MatchIncomingRedirectHostInAttackFlow(cardId);
    }

    private CardController FindOwnerFieldUnitByCardId(PlayerType owner, int cardId)
    {
        List<CardController> zone = owner == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        CardController fromZone = FindAliveUnitInZoneByCardId(zone, cardId, exclude: null);
        if (fromZone != null)
        {
            return fromZone;
        }

        CardGameRule rule = GetCardRuleForRuleSide(ToRuleSide(owner));
        Transform panel = rule != null ? rule.PlayerDeployPanel : null;
        if (panel == null || cardId <= 0)
        {
            return null;
        }

        CardController[] nested = panel.GetComponentsInChildren<CardController>(true);
        if (nested == null)
        {
            return null;
        }

        for (int i = 0; i < nested.Length; i++)
        {
            CardController unit = nested[i];
            if (unit == null
                || unit.Data == null
                || unit.Data.id != cardId
                || !unit.Data.IsUnitLike()
                || unit.CurrentHp <= 0)
            {
                continue;
            }

            return unit;
        }

        return null;
    }

    /// <summary>Action 発動者側の配備ベースを、選んだカード ID 優先で返す。</summary>
    private void StampIncomingRedirectIfHostCard(
        CardController unit,
        CardController redirectTarget,
        PlayerType owner,
        int hostCardId)
    {
        if (unit == null || unit.Data == null || hostCardId <= 0 || unit.Data.id != hostCardId)
        {
            return;
        }

        StampIncomingBattleDamageRedirectOnUnit(unit, redirectTarget, owner);
    }

    private void StampIncomingBattleDamageRedirectOnHostCopies(
        CardController unit,
        CardController host,
        CardController redirectTarget,
        PlayerType owner)
    {
        StampIncomingBattleDamageRedirectOnUnit(unit, redirectTarget, owner, host);
    }

    private CardController FindOwnerBaseForIncomingRedirect(PlayerType owner, int preferredCardId)
    {
        CardGameRule rule = GetCardRuleForRuleSide(ToRuleSide(owner));
        CardController preferred = null;
        CardController fallback = null;
        ConsiderIncomingRedirectBaseCandidate(
            GetDeployedBaseForRuleSide(ToRuleSide(owner)),
            preferredCardId,
            ref preferred,
            ref fallback);

        if (rule?.BaseSlotContent != null)
        {
            CardController[] nested = rule.BaseSlotContent.GetComponentsInChildren<CardController>(true);
            if (nested != null)
            {
                for (int i = 0; i < nested.Length; i++)
                {
                    ConsiderIncomingRedirectBaseCandidate(nested[i], preferredCardId, ref preferred, ref fallback);
                    if (preferred != null)
                    {
                        return preferred;
                    }
                }
            }
        }

        return preferred ?? fallback;
    }

    private static void ConsiderIncomingRedirectBaseCandidate(
        CardController candidate,
        int preferredCardId,
        ref CardController preferred,
        ref CardController fallback)
    {
        if (candidate == null
            || candidate.Data == null
            || candidate.Data.type != Type.Base
            || candidate.CurrentHp <= 0)
        {
            return;
        }

        if (preferredCardId > 0 && candidate.Data.id == preferredCardId)
        {
            preferred = candidate;
            return;
        }

        if (fallback == null)
        {
            fallback = candidate;
        }
    }

    private const int Gd06018QubeleyMkIICardId = 1000824;

    private bool ShouldBindIncomingBattleDamageRedirect(CardController source, EffectData effect)
    {
        if (effect != null && effect.type == EffectType.RedirectIncomingBattleDamage)
        {
            return true;
        }

        if (source?.Data == null)
        {
            return false;
        }

        List<EffectData> onActionEffects = GetEffectsByTiming(source.Data, EffectTiming.OnAction);
        for (int i = 0; i < onActionEffects.Count; i++)
        {
            if (onActionEffects[i] != null && onActionEffects[i].type == EffectType.RedirectIncomingBattleDamage)
            {
                return effect == null || ReferenceEquals(effect, onActionEffects[i]) || effect.type == onActionEffects[i].type;
            }
        }

        // JSON の type が enum とずれていても、018 の「このバトル中・1体選択」Action は受け替えとして結ぶ。
        return source.Data.id == Gd06018QubeleyMkIICardId
            && effect != null
            && effect.duration == EffectDuration.UntilEndOfBattle
            && effect.selectionMode == EffectSelectionMode.SelectSingle;
    }

    private void StampIncomingBattleDamageRedirectOnMatchingZoneUnits(
        PlayerType owner,
        CardController host,
        CardController redirectTarget)
    {
        if (host?.Data == null)
        {
            return;
        }

        List<CardController> zone = owner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            StampIncomingBattleDamageRedirectOnUnit(zone[i], redirectTarget, owner, host);
        }
    }

    private void StampIncomingBattleDamageRedirectOnUnit(
        CardController unit,
        CardController redirectTarget,
        PlayerType owner,
        CardController requiredMatch = null)
    {
        if (unit == null || redirectTarget == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return;
        }

        if (requiredMatch != null
            && !IsSameBattleUnitForRedirect(unit, requiredMatch)
            && (unit.Data == null
                || requiredMatch.Data == null
                || unit.Data.id != requiredMatch.Data.id
                || unit.Data.id <= 0))
        {
            return;
        }

        unit.SetIncomingBattleDamageRedirectUntilEndOfBattle(redirectTarget);
        unit.SetIncomingBattleDamageRedirectMeta(
            redirectTarget.Data != null ? redirectTarget.Data.id : 0,
            redirectTarget.Data != null && redirectTarget.Data.type == Type.Base,
            (int)owner);
    }

    private bool IsSameBattleUnitForRedirect(CardController a, CardController b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.BattleInstanceId > 0 && a.BattleInstanceId == b.BattleInstanceId)
        {
            return true;
        }

        return a.Data != null
            && b.Data != null
            && a.Data.id == b.Data.id
            && a.Data.id > 0
            && ResolveCardOwner(a.transform) == ResolveCardOwner(b.transform);
    }

    private void ReplaceIncomingBattleDamageRedirectRecord(IncomingBattleDamageRedirectRecord record)
    {
        for (int i = _incomingBattleDamageRedirects.Count - 1; i >= 0; i--)
        {
            IncomingBattleDamageRedirectRecord existing = _incomingBattleDamageRedirects[i];
            if ((record.HostBattleInstanceId > 0 && existing.HostBattleInstanceId == record.HostBattleInstanceId)
                || (record.HostCardId > 0
                    && existing.HostCardId == record.HostCardId
                    && existing.HostOwner == record.HostOwner))
            {
                _incomingBattleDamageRedirects.RemoveAt(i);
            }
        }

        _incomingBattleDamageRedirects.Add(record);
    }

    private CardController TryDivertIncomingBattleDamage(CardController intended)
    {
        if (intended == null || intended.Data == null)
        {
            return null;
        }

        if (_battleIncomingRedirectHostCardId > 0
            && intended.Data.id == _battleIncomingRedirectHostCardId)
        {
            CardController bound = ResolveBoundIncomingRedirectRecipient(intended);
            if (bound != null)
            {
                return bound;
            }
        }

        if (intended.HasIncomingBattleDamageRedirectThisBattle)
        {
            PlayerType flagOwner = (PlayerType)intended.IncomingBattleDamageRedirectOwnerValue;
            CardController fromFlags = intended.IncomingBattleDamageRedirectToBase
                ? FindOwnerBaseForIncomingRedirect(
                    flagOwner,
                    intended.IncomingBattleDamageRedirectToCardId)
                : FindAliveUnitInZoneByCardId(
                    flagOwner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards,
                    intended.IncomingBattleDamageRedirectToCardId,
                    intended);
            if (IsUsableIncomingBattleDamageRedirectRecipient(fromFlags, intended))
            {
                return fromFlags;
            }
        }

        return ResolveIncomingBattleDamageRedirectTarget(intended);
    }

    private CardController ResolveIncomingBattleDamageRedirectTarget(CardController damageTarget)
    {
        if (damageTarget == null)
        {
            return null;
        }

        if (IsBattleIncomingRedirectHost(damageTarget))
        {
            CardController bound = ResolveBoundIncomingRedirectRecipient(damageTarget);
            if (bound != null)
            {
                return bound;
            }
        }

        CardController fromUnit = damageTarget.IncomingBattleDamageRedirectTarget;
        if (IsUsableIncomingBattleDamageRedirectRecipient(fromUnit, damageTarget))
        {
            return fromUnit.Data != null && fromUnit.Data.type == Type.Base
                ? (FindLiveOwnerBaseMatching(fromUnit) ?? fromUnit)
                : (FindLiveBattleZoneUnitMatching(fromUnit) ?? fromUnit);
        }

        CardController liveHost = FindLiveBattleZoneUnitMatching(damageTarget);
        if (liveHost != null && liveHost != damageTarget)
        {
            fromUnit = liveHost.IncomingBattleDamageRedirectTarget;
            if (IsUsableIncomingBattleDamageRedirectRecipient(fromUnit, damageTarget))
            {
                return fromUnit.Data != null && fromUnit.Data.type == Type.Base
                    ? (FindLiveOwnerBaseMatching(fromUnit) ?? fromUnit)
                    : (FindLiveBattleZoneUnitMatching(fromUnit) ?? fromUnit);
            }
        }

        IncomingBattleDamageRedirectRecord? record = FindIncomingBattleDamageRedirectRecord(damageTarget);
        if (!record.HasValue)
        {
            return null;
        }

        return ResolveIncomingBattleDamageRedirectRecipient(record.Value, damageTarget);
    }

    private bool IsBattleIncomingRedirectHost(CardController damageTarget)
    {
        if (damageTarget == null || _battleIncomingRedirectHostCardId <= 0)
        {
            return false;
        }

        if (_battleIncomingRedirectHost != null
            && IsSameBattleUnitForRedirect(damageTarget, _battleIncomingRedirectHost))
        {
            return true;
        }

        if (_battleIncomingRedirectHostInstanceId > 0
            && damageTarget.BattleInstanceId == _battleIncomingRedirectHostInstanceId)
        {
            return true;
        }

        return damageTarget.Data != null
            && damageTarget.Data.id == _battleIncomingRedirectHostCardId
            && ResolveCardOwner(damageTarget.transform) == _battleIncomingRedirectOwner;
    }

    private CardController ResolveBoundIncomingRedirectRecipient(CardController originalTarget)
    {
        if (_battleIncomingRedirectToBase)
        {
            CardController ownerBase = FindOwnerBaseForIncomingRedirect(
                _battleIncomingRedirectOwner,
                _battleIncomingRedirectRecipientCardId);
            if (IsUsableIncomingBattleDamageRedirectRecipient(ownerBase, originalTarget))
            {
                return ownerBase;
            }
        }

        IncomingBattleDamageRedirectRecord? record = FindIncomingBattleDamageRedirectRecord(originalTarget);
        if (record.HasValue)
        {
            CardController fromRecord = ResolveIncomingBattleDamageRedirectRecipient(record.Value, originalTarget);
            if (fromRecord != null)
            {
                return fromRecord;
            }
        }

        return null;
    }

    private IncomingBattleDamageRedirectRecord? FindIncomingBattleDamageRedirectRecord(CardController host)
    {
        if (host == null)
        {
            return null;
        }

        int instanceId = host.BattleInstanceId;
        int cardId = host.Data != null ? host.Data.id : 0;
        PlayerType owner = ResolveCardOwner(host.transform);
        for (int i = 0; i < _incomingBattleDamageRedirects.Count; i++)
        {
            IncomingBattleDamageRedirectRecord record = _incomingBattleDamageRedirects[i];
            if (instanceId > 0 && record.HostBattleInstanceId == instanceId)
            {
                return record;
            }

            if (cardId > 0 && record.HostCardId == cardId && record.HostOwner == owner)
            {
                return record;
            }
        }

        return null;
    }

    private CardController ResolveIncomingBattleDamageRedirectRecipient(
        IncomingBattleDamageRedirectRecord record,
        CardController host)
    {
        if (record.RecipientIsBase)
        {
            CardController deployed = GetDeployedBaseForRuleSide(ToRuleSide(record.RecipientOwner));
            if (IsUsableIncomingBattleDamageRedirectRecipient(deployed, host)
                && (record.RecipientCardId <= 0
                    || deployed.Data == null
                    || deployed.Data.id == record.RecipientCardId))
            {
                return deployed;
            }

            CardGameRule rule = GetCardRuleForRuleSide(ToRuleSide(record.RecipientOwner));
            if (rule?.BaseSlotContent != null)
            {
                for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
                {
                    CardController occupant = rule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
                    if (IsUsableIncomingBattleDamageRedirectRecipient(occupant, host)
                        && occupant.Data != null
                        && occupant.Data.type == Type.Base
                        && (record.RecipientCardId <= 0 || occupant.Data.id == record.RecipientCardId))
                    {
                        return occupant;
                    }
                }
            }

            return null;
        }

        if (record.RecipientBattleInstanceId > 0)
        {
            CardController byInstance = FindBattleZoneUnitByInstanceIdAnySide(record.RecipientBattleInstanceId);
            if (IsUsableIncomingBattleDamageRedirectRecipient(byInstance, host))
            {
                return byInstance;
            }
        }

        List<CardController> zone = record.RecipientOwner == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        return FindAliveUnitInZoneByCardId(zone, record.RecipientCardId, host);
    }

    private CardController FindBattleZoneUnitByInstanceIdAnySide(int instanceId)
    {
        CardController player = FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Player);
        if (player != null)
        {
            return player;
        }

        return FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Enemy);
    }

    private static CardController FindAliveUnitInZoneByCardId(
        List<CardController> zone,
        int cardId,
        CardController exclude)
    {
        if (zone == null || cardId <= 0)
        {
            return null;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit == exclude || unit.Data == null || unit.Data.id != cardId || unit.CurrentHp <= 0)
            {
                continue;
            }

            return unit;
        }

        return null;
    }

    private static bool IsUsableIncomingBattleDamageRedirectRecipient(
        CardController recipient,
        CardController originalTarget)
    {
        return recipient != null
            && recipient != originalTarget
            && recipient.Data != null
            && recipient.CurrentHp > 0;
    }

    private CardController FindLiveBattleZoneUnitMatching(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            return null;
        }

        if (IsCardOnBattleZone(unit) && unit.CurrentHp > 0)
        {
            return unit;
        }

        if (unit.BattleInstanceId > 0)
        {
            CardController byInstance = FindBattleZoneUnitByInstanceIdAnySide(unit.BattleInstanceId);
            if (byInstance != null && byInstance.CurrentHp > 0)
            {
                return byInstance;
            }
        }

        PlayerType owner = ResolveCardOwner(unit.transform);
        List<CardController> zone = owner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        return FindAliveUnitInZoneByCardId(zone, unit.Data.id, exclude: null);
    }

    private CardController FindLiveOwnerBaseMatching(CardController baseCard)
    {
        if (baseCard == null || baseCard.Data == null || baseCard.Data.type != Type.Base)
        {
            return null;
        }

        if (baseCard.CurrentHp > 0 && baseCard.gameObject != null)
        {
            PlayerType owner = ResolveCardOwner(baseCard.transform);
            CardController deployed = GetDeployedBaseForRuleSide(ToRuleSide(owner));
            if (deployed == baseCard && deployed.CurrentHp > 0)
            {
                return deployed;
            }
        }

        PlayerType ownerType = ResolveCardOwner(baseCard.transform);
        CardController ownerBase = GetDeployedBaseForRuleSide(ToRuleSide(ownerType));
        if (ownerBase != null
            && ownerBase.Data != null
            && ownerBase.CurrentHp > 0
            && ownerBase.Data.id == baseCard.Data.id)
        {
            return ownerBase;
        }

        return baseCard.CurrentHp > 0 ? baseCard : null;
    }

    private const int NeoZeonFeatureId = 6;
    private const int RemoteWeaponFeatureId = 77;

    /// <summary>
    /// GD06-018: 〔ネオジオン〕か〔遠隔兵器〕の味方ユニット、または〔ネオジオン〕ベース。
    /// </summary>
    private List<CardController> CollectIncomingBattleDamageRedirectCandidates(
        CardController sourceCard,
        PlayerType ownerType)
    {
        AbsorbDeployedFieldUnitsIntoBattleZoneList(ownerType);
        List<CardController> result = new List<CardController>();
        HashSet<CardController> seen = new HashSet<CardController>();
        CardController exclude = ResolveEffectSourceBattleHost(sourceCard) ?? sourceCard;
        List<CardController> allies = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (allies != null)
        {
            for (int i = 0; i < allies.Count; i++)
            {
                CardController unit = allies[i];
                if (!IsIncomingBattleDamageRedirectUnitCandidate(unit, exclude) || !seen.Add(unit))
                {
                    continue;
                }

                result.Add(unit);
            }
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule != null && rule.PlayerDeployPanel != null)
        {
            CardController[] nested = rule.PlayerDeployPanel.GetComponentsInChildren<CardController>(true);
            for (int i = 0; nested != null && i < nested.Length; i++)
            {
                CardController unit = nested[i];
                if (!IsIncomingBattleDamageRedirectUnitCandidate(unit, exclude) || !seen.Add(unit))
                {
                    continue;
                }

                result.Add(unit);
            }
        }

        TryAddOwnerDeployedBaseToEffectTargets(ownerType, result, requiredFeatures: null);
        return result;
    }

    private static bool IsIncomingBattleDamageRedirectUnitCandidate(
        CardController unit,
        CardController exclude)
    {
        if (unit == null
            || unit == exclude
            || unit.Data == null
            || !unit.Data.IsUnitLike()
            || unit.CurrentHp <= 0)
        {
            return false;
        }

        return unit.HasFeatureId(NeoZeonFeatureId)
            || unit.HasFeatureId(RemoteWeaponFeatureId)
            || unit.Data.HasFeatureKey("Neo_Zeon")
            || unit.Data.HasFeatureKey("Long_Range_Weapon");
    }

    /// <summary>
    /// 戦闘ダメージ受け替え先として、配備中の〔ネオジオン〕ベース（アクシズ等）を候補へ加える。
    /// DeployedBase が EX のままでも BaseSlot 上の Type.Base を拾う。
    /// </summary>
    private void TryAddOwnerDeployedBaseToEffectTargets(
        PlayerType ownerType,
        List<CardController> result,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (result == null)
        {
            return;
        }

        TryAddRedirectEligibleOwnerBase(
            result,
            GetDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            requiredFeatures);

        CardGameRule rule = GetCardRuleForRuleSide(ToRuleSide(ownerType));
        if (rule?.BaseSlotContent == null)
        {
            return;
        }

        for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
        {
            CardController occupant = rule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            TryAddRedirectEligibleOwnerBase(result, occupant, requiredFeatures);
        }
    }

    private static void TryAddRedirectEligibleOwnerBase(
        List<CardController> result,
        CardController baseCard,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (result == null
            || baseCard == null
            || baseCard.Data == null
            || baseCard.Data.type != Type.Base
            || baseCard.CurrentHp <= 0)
        {
            return;
        }

        // ベースは公式どおり〔ネオジオン〕のみ（ユニット側の〔遠隔兵器〕OR 条件は使わない）。
        if (!baseCard.HasFeatureId(NeoZeonFeatureId)
            && (requiredFeatures == null
                || requiredFeatures.Count == 0
                || !MatchesRequiredFeatures(baseCard.Data, requiredFeatures)))
        {
            return;
        }

        if (!result.Contains(baseCard))
        {
            result.Add(baseCard);
        }
    }

    private void ClearIncomingBattleDamageRedirectUntilEndOfBattleForAllInPlayUnits()
    {
        ClearIncomingBattleDamageRedirectOnZone(playerBattleZoneCards);
        ClearIncomingBattleDamageRedirectOnZone(enemyBattleZoneCards);
        _incomingBattleDamageRedirects.Clear();
        _battleIncomingRedirectHost = null;
        _battleIncomingRedirectHostCardId = 0;
        _battleIncomingRedirectHostInstanceId = 0;
        _battleIncomingRedirectToBase = false;
        _battleIncomingRedirectRecipientCardId = 0;
        _battleIncomingRedirectRecipientLive = null;
        ClearGd06018ReplacementDamageStep();
    }

    private static void ClearIncomingBattleDamageRedirectOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearIncomingBattleDamageRedirectUntilEndOfBattle();
        }
    }

    private static bool HaveDistinctCardNames(IReadOnlyList<CardController> units)
    {
        if (units == null || units.Count <= 1)
        {
            return true;
        }

        HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < units.Count; i++)
        {
            string name = units[i]?.Data?.cardName;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!names.Add(name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsCardName(IReadOnlyList<CardController> units, CardController candidate)
    {
        if (units == null || candidate == null || candidate.Data == null)
        {
            return false;
        }

        string name = candidate.Data.cardName;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (int i = 0; i < units.Count; i++)
        {
            string existing = units[i]?.Data?.cardName;
            if (!string.IsNullOrEmpty(existing)
                && string.Equals(existing, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountDistinctCardNames(IReadOnlyList<CardController> units)
    {
        if (units == null || units.Count == 0)
        {
            return 0;
        }

        HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < units.Count; i++)
        {
            string name = units[i]?.Data?.cardName;
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names.Count;
    }

    private bool TryApplyGrantTurnEndRepair(EffectData effect, List<CardController> targets)
    {
        if (effect == null
            || effect.type != EffectType.GrantTurnEndRepair
            || targets == null
            || targets.Count == 0)
        {
            return false;
        }

        int amount = effect.value > 0 ? effect.value : 1;
        int applied = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit == null || !unit.IsRepairEligibleUnit())
            {
                continue;
            }

            unit.AddTurnEndRepairBonus(amount);
            applied++;
            Debug.Log(
                $"[GrantTurnEndRepair] {unit.Data?.cardName}(id:{unit.Data?.id}) "
                + $"+{amount} (total turn-end repair:{unit.GetTurnEndRepairAmount()})");
        }

        return applied > 0;
    }

    private bool TryApplyGrantTurnEndActivateResource(EffectData effect, CardController sourceCard)
    {
        if (effect == null
            || effect.type != EffectType.GrantTurnEndActivateResource
            || sourceCard == null
            || sourceCard.Data == null
            || !sourceCard.Data.IsUnitLike())
        {
            return false;
        }

        int amount = effect.value > 0 ? effect.value : 2;
        GrantTurnEndActivateResourceIfUnset(sourceCard, amount);
        GrantTurnEndActivateResourceToOtherFriendlyCopies(sourceCard, amount);
        return true;
    }

    /// <summary>
    /// 同名ユニットが後から出たとき、先に出ていたコピーにも「他に〔特徴〕がいれば」の終了時効果を付与する。
    /// 既に付与済みのコピーは重ねない。
    /// </summary>
    private void GrantTurnEndActivateResourceToOtherFriendlyCopies(CardController sourceCard, int amount)
    {
        if (sourceCard?.Data == null || amount <= 0)
        {
            return;
        }

        PlayerType owner = ResolveCardOwner(sourceCard.transform);
        List<CardController> zone = owner == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null)
        {
            return;
        }

        int sourceId = sourceCard.Data.id;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null
                || unit == sourceCard
                || unit.Data == null
                || unit.Data.id != sourceId
                || unit.CurrentHp <= 0)
            {
                continue;
            }

            GrantTurnEndActivateResourceIfUnset(unit, amount);
        }
    }

    private static void GrantTurnEndActivateResourceIfUnset(CardController unit, int amount)
    {
        if (unit == null || amount <= 0 || unit.GetTurnEndActivateResourceBonus() > 0)
        {
            return;
        }

        unit.AddTurnEndActivateResourceBonus(amount);
        Debug.Log(
            $"[GrantTurnEndActivateResource] {unit.Data?.cardName}(id:{unit.Data?.id}) "
            + $"+{amount} (total:{unit.GetTurnEndActivateResourceBonus()})");
    }

    private void ClearBreachUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearBreachUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearBreachUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private void ClearBreachUntilEndOfBattleGrantsForAllInPlayUnits()
    {
        ClearBreachUntilEndOfBattleGrantsOnZone(playerBattleZoneCards);
        ClearBreachUntilEndOfBattleGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearBreachUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBreachUntilEndOfTurnGrants();
        }
    }

    private static void ClearBreachUntilEndOfBattleGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBreachUntilEndOfBattleGrants();
        }
    }

    private void ClearNotDirectAttackGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn && duration != EffectDuration.UntilEndOfBattle)
        {
            return;
        }

        ClearNotDirectAttackGrantsOnZone(playerBattleZoneCards, duration);
        ClearNotDirectAttackGrantsOnZone(enemyBattleZoneCards, duration);
    }

    private void ClearCannotBeChosenAsAttackGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn && duration != EffectDuration.UntilEndOfBattle)
        {
            return;
        }

        ClearCannotBeChosenAsAttackGrantsOnZone(playerBattleZoneCards, duration);
        ClearCannotBeChosenAsAttackGrantsOnZone(enemyBattleZoneCards, duration);
        if (duration == EffectDuration.UntilEndOfTurn)
        {
            _cannotBeChosenAsAttackUntilEotInstanceIds.Clear();
        }
    }

    private static void ClearCannotBeChosenAsAttackGrantsOnZone(List<CardController> zone, EffectDuration duration)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null)
            {
                continue;
            }

            if (duration == EffectDuration.UntilEndOfTurn)
            {
                unit.ClearCannotBeChosenAsAttackUntilEndOfTurnGrants();
            }
        }
    }

    private static void ClearNotDirectAttackGrantsOnZone(List<CardController> zone, EffectDuration duration)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null)
            {
                continue;
            }

            if (duration == EffectDuration.UntilEndOfTurn)
            {
                unit.ClearNotDirectAttackUntilEndOfTurnGrants();
            }
        }
    }

    private static void FilterOutNonRestedUnits(List<CardController> targets)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            CardController unit = targets[i];
            if (unit == null || !unit.IsRestState)
            {
                targets.RemoveAt(i);
            }
        }
    }
}
