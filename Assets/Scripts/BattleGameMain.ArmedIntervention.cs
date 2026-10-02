using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST07-013 武力介入専用。【アクション】レストの〔CB〕味方1つを選び、
/// バトルしている相手ユニットのアタック先をそれにする。
/// </summary>
public partial class BattleGameMain
{
    private const int ArmedInterventionCardId = 1000608;
    private const string ArmedInterventionOfficialId = "ST07-013";

    private static bool IsArmedInterventionCard(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        if (data.id == ArmedInterventionCardId)
        {
            return true;
        }

        string officialId = data.gcgOfficialId;
        if (!string.IsNullOrEmpty(officialId)
            && string.Equals(officialId.Trim(), ArmedInterventionOfficialId, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = data.cardName;
        return !string.IsNullOrEmpty(name)
            && (name.IndexOf("Armed Intervention", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("武力介入", System.StringComparison.Ordinal) >= 0);
    }

    private static EffectData CreateArmedInterventionPickEffect()
    {
        return new EffectData
        {
            type = EffectType.BlockRedirect,
            target = TargetType.AllyUnit,
            selectionMode = EffectSelectionMode.SelectSingle,
            targetFeatureId = 25,
            requireTargetIsRest = true,
            forbidSkipUnitPick = true,
            selectMinCount = 1,
            selectMaxCount = 1
        };
    }

    /// <summary>アクション一覧の手札コマンド可否。武力介入は専用判定（種類フラグに依存しない）。</summary>
    private bool IsOnActionCommandSelectable(PlayerType side, CardController cc)
    {
        if (cc?.Data == null)
        {
            return false;
        }

        if (IsArmedInterventionCard(cc.Data))
        {
            return CanPlayArmedInterventionCommandNow(side, cc);
        }

        if (!cc.Data.IsCommand())
        {
            return false;
        }

        return HasEffectTiming(cc.Data, EffectTiming.OnAction) && CanExecuteOnActionCardNow(side, cc);
    }

    private bool CanPlayArmedInterventionCommandNow(PlayerType side, CardController source)
    {
        if (source?.Data == null || !IsArmedInterventionCard(source.Data))
        {
            return false;
        }

        if (!IsArmedInterventionAttackActionWindow())
        {
            return false;
        }

        if (CollectRestedCbAllyUnitsForAttackRedirect(side).Count == 0)
        {
            return false;
        }

        int cost = Mathf.Max(0, source.CurrentCost);
        if (cost <= 0 || gundamRule == null)
        {
            return true;
        }

        // コマンドの印刷レベルではなく、支払えるコストのみ見る（アクション中に一覧から消えないようにする）
        return gundamRule.CanPlayCardWithAnyEx(ToRuleSide(side), 0, cost);
    }

    /// <summary>
    /// 攻撃に紐づくアクションステップか。防御側・攻撃側のどちらでも true。
    /// TurnPlayerSide 比較は使わない（セッション引数が入れ替わると一覧から消える）。
    /// </summary>
    private bool IsArmedInterventionAttackActionWindow()
    {
        if (_actionStepSession != null)
        {
            if (_actionStepSession.IsAttackContext)
            {
                return true;
            }

            if (_actionStepSession.AttackingUnit != null)
            {
                return true;
            }
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.None)
        {
            return true;
        }

        if (attackFlowAttackerUnit != null || pendingUnitAttackAttacker != null)
        {
            return true;
        }

        return isShieldAttackResolving || blockShieldFlowDuringShieldAttack || deferredShieldBlockRedirectWait;
    }

    private static bool UnitHasCbFeature(CardController unit)
    {
        if (unit?.Data == null)
        {
            return false;
        }

        unit.Data.EnsureFeaturesResolved();
        if (unit.HasFeatureId(25) || unit.Data.HasFeatureKey("CB"))
        {
            return true;
        }

        if (unit.Data.features == null)
        {
            return false;
        }

        for (int i = 0; i < unit.Data.features.Count; i++)
        {
            CardFeatureData feature = unit.Data.features[i];
            if (feature == null)
            {
                continue;
            }

            if (feature.id == 25)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(feature.featureKey)
                && string.Equals(feature.featureKey, "CB", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(feature.displayName)
                && string.Equals(feature.displayName.Trim(), "CB", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnitLooksRested(CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.IsRestState)
        {
            return true;
        }

        RectTransform rt = unit.transform as RectTransform;
        if (rt == null)
        {
            return false;
        }

        return Mathf.Abs(Mathf.DeltaAngle(rt.localEulerAngles.z, -90f)) < 15f;
    }

    /// <summary>武力介入：REST かつ〔CB〕の味方ユニット（両ゾーン＋自配備パネル）。</summary>
    private List<CardController> CollectRestedCbAllyUnitsForAttackRedirect(PlayerType side)
    {
        List<CardController> result = new List<CardController>();
        AppendRestedCbAlliesFromZone(playerBattleZoneCards, side, result);
        AppendRestedCbAlliesFromZone(enemyBattleZoneCards, side, result);
        return result;
    }

    private void AppendRestedCbAlliesFromZone(
        List<CardController> zone,
        PlayerType side,
        List<CardController> result)
    {
        if (zone == null || result == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null
                || unit.Data == null
                || !unit.Data.IsUnitLike()
                || unit.CurrentHp <= 0
                || result.Contains(unit))
            {
                continue;
            }

            if (!IsOnDeployPanel(unit, side) && !IsUnitInSideBattleZoneList(unit, side))
            {
                continue;
            }

            if (!UnitLooksRested(unit) || !UnitHasCbFeature(unit))
            {
                continue;
            }

            result.Add(unit);
        }
    }

    private bool IsUnitInSideBattleZoneList(CardController unit, PlayerType side)
    {
        List<CardController> zone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        return zone != null && zone.Contains(unit);
    }

    /// <summary>視点入替でも手札から武力介入を拾う。</summary>
    private void AppendArmedInterventionFromAnyHand(PlayerType side, List<CardController> sources)
    {
        if (sources == null)
        {
            return;
        }

        AppendArmedInterventionFromHandRect(cardGameRule != null ? cardGameRule.HandScrollContent : null, side, sources);
        AppendArmedInterventionFromHandRect(
            enemyCardGameRule != null ? enemyCardGameRule.HandScrollContent : null,
            side,
            sources);
    }

    private void AppendArmedInterventionFromHandRect(
        RectTransform hand,
        PlayerType side,
        List<CardController> sources)
    {
        if (hand == null)
        {
            return;
        }

        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || !IsArmedInterventionCard(cc.Data))
            {
                continue;
            }

            if (sources.Contains(cc) || !CanPlayArmedInterventionCommandNow(side, cc))
            {
                continue;
            }

            sources.Add(cc);
        }
    }

    /// <summary>
    /// 武力介入適用後、アクション完了時に選んだ REST〔CB〕とユニット戦を開始する。
    /// シールド打撃へ戻さない。OnAction は既に終わっているので再ポーズしない。
    /// </summary>
    private bool TryResumeArmedInterventionUnitCombatAfterOnAction(CardController attackerHint)
    {
        if (!_armedInterventionRedirectPending)
        {
            return false;
        }

        CardController defender = attackFlowBlockRedirectUnit != null
            ? attackFlowBlockRedirectUnit
            : attackFlowDeclaredDefenderUnit;
        if (!IsUnitAvailableForAttackExchange(defender))
        {
            _armedInterventionRedirectPending = false;
            return false;
        }

        CardController attacker = attackerHint;
        if (attacker == null || attacker.CurrentHp <= 0)
        {
            attacker = attackFlowAttackerUnit != null ? attackFlowAttackerUnit : pendingUnitAttackAttacker;
        }

        if (attacker == null
            || attacker.Data == null
            || !attacker.Data.IsUnitLike()
            || attacker.CurrentHp <= 0)
        {
            _armedInterventionRedirectPending = false;
            return false;
        }

        PlayerType attackerOwner = attackFlowAttackerOwner;
        PlayerType defenderOwner = IsOnDeployPanel(defender, PlayerType.Player)
            || IsUnitInSideBattleZoneList(defender, PlayerType.Player)
            ? PlayerType.Player
            : PlayerType.Enemy;
        if (defenderOwner == attackerOwner)
        {
            defenderOwner = OpponentSide(attackerOwner);
        }

        attackFlowBlockRedirectEngaged = true;
        attackFlowBlockRedirectUnit = defender;
        attackFlowDeclaredDefenderUnit = defender;
        attackFlowBlockOnActionCompleted = true;
        _armedInterventionRedirectPending = false;
        deferredShieldBlockRedirectWait = false;

        Debug.Log(
            $"[ArmedIntervention] Resume unit combat {attacker.Data.cardName} vs {defender.Data.cardName} "
            + $"atkOwner:{attackerOwner} defOwner:{defenderOwner}");

        ExecuteBlockRedirectUnitCombat(attacker, defender, attackerOwner, defenderOwner);
        return true;
    }

    /// <summary>選んだ REST〔CB〕と攻撃ユニットを直ちにバトルし、元のシールド／宣言打撃を禁止する。</summary>
    private bool TryResolveArmedInterventionChosenUnitBattle(CardController picked, bool requireAttackWindow)
    {
        if (picked == null || picked.Data == null || !picked.Data.IsUnitLike() || picked.CurrentHp <= 0)
        {
            return false;
        }

        if (requireAttackWindow && !IsArmedInterventionAttackActionWindow())
        {
            return false;
        }

        CardController attacker = ResolveCurrentAttackFlowAttackerUnit();
        if (attacker == null || attacker == picked || attacker.CurrentHp <= 0)
        {
            Debug.LogWarning("[ArmedIntervention] バトル相手の攻撃ユニットが見つからないため未解決。");
            MarkArmedInterventionOriginalStrikeCancelled();
            return false;
        }

        PlayerType pickedOwner = IsOnDeployPanel(picked, PlayerType.Player)
            || IsUnitInSideBattleZoneList(picked, PlayerType.Player)
            ? PlayerType.Player
            : PlayerType.Enemy;
        PlayerType attackerOwner = ResolveCardOwner(attacker.transform);
        if (attackerOwner == pickedOwner)
        {
            attackerOwner = OpponentSide(pickedOwner);
        }

        attackFlowAttackerUnit = attacker;
        pendingUnitAttackAttacker = attacker;
        attackFlowAttackerOwner = attackerOwner;
        attackFlowDeclaredDefenderUnit = picked;
        AssignBattleInstanceIdIfNeeded(picked);
        AssignBattleInstanceIdIfNeeded(attacker);

        ResolveEffectBattleCombat(attacker, picked, attackerOwner);
        MarkArmedInterventionOriginalStrikeCancelled();
        Debug.Log(
            $"[ArmedIntervention] Effect battle resolved {attacker.Data.cardName} vs {picked.Data.cardName} "
            + $"atkOwner:{attackerOwner} pickOwner:{pickedOwner}");
        return true;
    }

    /// <summary>オンライン受信側：公開された武力介入の対象でバトルし、元のシールド打撃を止める。</summary>
    private void TryApplyArmedInterventionFromRemote(CardController pickedOrNull)
    {
        if (pickedOrNull != null
            && pickedOrNull.Data != null
            && pickedOrNull.Data.IsUnitLike()
            && pickedOrNull.CurrentHp > 0)
        {
            TryResolveArmedInterventionChosenUnitBattle(pickedOrNull, requireAttackWindow: false);
            return;
        }

        MarkArmedInterventionOriginalStrikeCancelled();
        Debug.LogWarning("[ArmedIntervention] Remote target missing — original shield/unit strike cancelled.");
    }

    private void MarkArmedInterventionOriginalStrikeCancelled()
    {
        _armedInterventionCombatResolvedThisAttack = true;
        _armedInterventionCancelOriginalStrike = true;
        _armedInterventionIgnoreRemoteShieldAttack = true;
        _armedInterventionRedirectPending = false;
        attackFlowBlockRedirectEngaged = false;
        deferredShieldBlockRedirectWait = true;
        shieldStrikeAbortedAfterBlockInterrupt = true;
    }

    /// <summary>選択直後にバトル済みなら、元のシールド／宣言打撃を打ち切る。</summary>
    private bool TryFinishArmedInterventionAlreadyResolvedCombat()
    {
        return TryAbortRemainingAttackAfterArmedIntervention();
    }

    private bool TryAbortRemainingAttackAfterArmedIntervention()
    {
        if (!_armedInterventionCancelOriginalStrike && !_armedInterventionCombatResolvedThisAttack)
        {
            return false;
        }

        _armedInterventionCancelOriginalStrike = false;
        _armedInterventionCombatResolvedThisAttack = false;
        _armedInterventionRedirectPending = false;
        deferredShieldBlockRedirectWait = false;
        attackFlowBlockRedirectEngaged = false;
        FinalizeBlockInterruptWithoutExchange();
        return true;
    }
}
