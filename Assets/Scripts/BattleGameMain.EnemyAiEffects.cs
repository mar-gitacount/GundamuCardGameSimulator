using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>敵 AI：手札コマンドは効果を参照し仮想盤面に適用してスコア化し、高ければ本番実行。</summary>
public partial class BattleGameMain
{
    /// <summary>手札コマンド仮想シミュ後のスコア差分がこの値以上なら実行（OnAction / OnMain 共通）。</summary>
    private const int EnemyAiHandCommandMinScoreToExecute = 1;

    /// <summary>コマンド後のプレイヤー攻撃シミュで相手（攻撃ユニット）が落ちたときの実行補正。</summary>
    private const int EnemyAiPostAttackExecuteBonusPlayerUnitKilled = 28;

    /// <summary>コマンド後もプレイヤー攻撃ユニットが生き残るときの benefit 上限。</summary>
    private const int EnemyAiPostAttackMaxBenefitWhenPlayerSurvives = 8;

    /// <summary>交換後スコア：プレイヤー攻撃ユニット撃破。</summary>
    private const int EnemyAiPostAttackScorePlayerUnitDies = 78;

    /// <summary>交換後スコア：プレイヤー攻撃ユニット生存（少なめ）。</summary>
    private const int EnemyAiPostAttackScorePlayerUnitSurvives = 12;

    /// <summary>交換後スコア：敵防御ユニット生存。</summary>
    private const int EnemyAiPostAttackScoreEnemyDefenderSurvives = 42;

    /// <summary>交換後スコア：敵防御ユニット撃破。</summary>
    private const int EnemyAiPostAttackScoreEnemyDefenderDies = -72;

    private sealed class EnemyAiEffectPickContext
    {
        public PlayerType OwnerSide;
        public CardController SourceCard;
        public CardController AttackingUnitInAttackFlow;
        public List<CardController> PlayerRestTargets;
    }

    private EnemyAiEffectPickContext BuildEnemyAiEffectPickContext(
        PlayerType ownerSide,
        CardController sourceCard,
        CardController attackingUnitInAttackFlow,
        List<CardController> playerRestTargetsOrNull)
    {
        return new EnemyAiEffectPickContext
        {
            OwnerSide = ownerSide,
            SourceCard = sourceCard,
            AttackingUnitInAttackFlow = attackingUnitInAttackFlow,
            PlayerRestTargets = playerRestTargetsOrNull,
        };
    }

    private static int ComputeEnemyAiUnitThreatScore(int ap, int hp)
    {
        return ap * 2 - hp;
    }

    /// <summary>1体選択用。候補が空なら null。</summary>
    private CardController PickEnemyAiEffectTarget(
        EffectData effect,
        EnemyAiEffectPickContext ctx,
        List<CardController> candidatesOrNull)
    {
        List<CardController> picked = PickEnemyAiEffectTargets(effect, ctx, candidatesOrNull, singleOnly: true);
        return picked.Count > 0 ? picked[0] : null;
    }

    private List<CardController> PickEnemyAiEffectTargets(
        EffectData effect,
        EnemyAiEffectPickContext ctx,
        List<CardController> candidatesOrNull,
        bool singleOnly)
    {
        List<CardController> candidates = candidatesOrNull
            ?? ResolveSelectableEffectTargets(ctx.SourceCard, ctx.OwnerSide, effect);
        List<CardController> result = new List<CardController>();
        if (effect == null || candidates == null || candidates.Count == 0)
        {
            return result;
        }

        if (!singleOnly && (effect.target == TargetType.EnemyAllUnits || effect.target == TargetType.AllyAllUnits))
        {
            result.AddRange(candidates);
            return result;
        }

        if (!singleOnly && effect.selectionMode.IsMultipleUnitPickMode())
        {
            int min = effect.GetSelectMinCount();
            int max = effect.GetSelectMaxCount(candidates.Count);
            List<CardController> ranked = new List<CardController>(candidates);
            ranked.Sort((a, b) => ComputeEnemyAiUnitThreatScore(b.CurrentPower, b.CurrentHp)
                .CompareTo(ComputeEnemyAiUnitThreatScore(a.CurrentPower, a.CurrentHp)));
            if (ranked.Count < min)
            {
                return result;
            }

            int pickCount = Mathf.Clamp(ranked.Count, min, max);
            for (int i = 0; i < pickCount; i++)
            {
                result.Add(ranked[i]);
            }

            return result;
        }

        CardController pick = null;
        if (TryPickPrioritizedPlayerAttackerForEnemyOnAction(effect, ctx, candidates, out CardController prioritizedAttacker))
        {
            pick = prioritizedAttacker;
        }
        else switch (effect.type)
        {
            case EffectType.Damage:
                pick = PickEnemyAiDamageTargetDuringPlayerAttack(ctx, candidates);
                break;
            case EffectType.Debuff:
                pick = PickEnemyAiDebuffTarget(effect, ctx, candidates);
                break;
            case EffectType.Buff:
                pick = PickHighestThreatOrFirst(candidates);
                break;
            case EffectType.Bounce:
                pick = PickHighestThreatOrFirst(candidates);
                break;
            case EffectType.Rest:
                pick = PickHighestThreatOrFirst(candidates);
                break;
            case EffectType.GrantAttackFlag:
                pick = candidates[0];
                break;
            case EffectType.Destroy:
                pick = PickHighestThreatOrFirst(candidates);
                break;
            case EffectType.ReturnUnitToDeckBottom:
                pick = PickLowestLevelEnemyUnit(candidates);
                break;
            case EffectType.MarkObservedUnit:
                pick = PickHighestThreatOrFirst(candidates);
                break;
            default:
                pick = candidates[0];
                break;
        }

        if (pick != null)
        {
            result.Add(pick);
        }

        return result;
    }

    private static CardController PickLowestHpUnit(List<CardController> candidates)
    {
        CardController best = null;
        int bestHp = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController c = candidates[i];
            if (c == null || c.Data == null)
            {
                continue;
            }

            if (c.CurrentHp < bestHp)
            {
                bestHp = c.CurrentHp;
                best = c;
            }
        }

        return best;
    }

    private static CardController PickLowestLevelEnemyUnit(List<CardController> candidates)
    {
        CardController best = null;
        int bestLevel = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController c = candidates[i];
            if (c == null || c.Data == null || c.Data.IsUnitToken())
            {
                continue;
            }

            if (c.CurrentLevel < bestLevel)
            {
                bestLevel = c.CurrentLevel;
                best = c;
            }
        }

        return best != null ? best : PickHighestThreatOrFirst(candidates);
    }

    private static CardController PickHighestThreatOrFirst(List<CardController> candidates)
    {
        CardController best = candidates[0];
        int bestThreat = ComputeEnemyAiUnitThreatScore(best.CurrentPower, best.CurrentHp);
        for (int i = 1; i < candidates.Count; i++)
        {
            CardController c = candidates[i];
            if (c == null || c.Data == null)
            {
                continue;
            }

            int threat = ComputeEnemyAiUnitThreatScore(c.CurrentPower, c.CurrentHp);
            if (threat > bestThreat)
            {
                bestThreat = threat;
                best = c;
            }
        }

        return best;
    }

    /// <summary>プレイヤー攻撃ウィンドウ中は、シミュ／本番とも攻撃中ユニットへのデバフ・ダメージを優先。</summary>
    private bool TryPickPrioritizedPlayerAttackerForEnemyOnAction(
        EffectData effect,
        EnemyAiEffectPickContext ctx,
        List<CardController> candidates,
        out CardController picked)
    {
        picked = null;
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None
            || attackFlowAttackerOwner != PlayerType.Player
            || effect == null
            || candidates == null
            || candidates.Count == 0)
        {
            return false;
        }

        if (effect.type != EffectType.Damage && effect.type != EffectType.Debuff)
        {
            return false;
        }

        CardController focus = ctx.AttackingUnitInAttackFlow != null
            ? ctx.AttackingUnitInAttackFlow
            : attackFlowAttackerUnit;
        if (focus == null || focus.Data == null)
        {
            return false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] == focus)
            {
                picked = focus;
                return true;
            }
        }

        return false;
    }

    private static CardController PickEnemyAiDamageTargetDuringPlayerAttack(
        EnemyAiEffectPickContext ctx,
        List<CardController> candidates)
    {
        if (ctx.AttackingUnitInAttackFlow != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == ctx.AttackingUnitInAttackFlow)
                {
                    return ctx.AttackingUnitInAttackFlow;
                }
            }
        }

        return PickLowestHpUnit(candidates);
    }

    private CardController PickEnemyAiDebuffTarget(
        EffectData effect,
        EnemyAiEffectPickContext ctx,
        List<CardController> candidates)
    {
        if (TryPickPrioritizedPlayerAttackerForEnemyOnAction(effect, ctx, candidates, out CardController atkPick))
        {
            return atkPick;
        }

        bool affectsAp = effect.statTarget == EffectStatTarget.AP || effect.statTarget == EffectStatTarget.Both;
        if (!affectsAp)
        {
            return PickLowestHpUnit(candidates);
        }

        return PickHighestThreatOrFirst(candidates);
    }

    private List<CardController> ResolveEnemyAiEffectTargetsForVirtual(
        CardController sourceCard,
        PlayerType ownerSide,
        EffectData effect,
        CardController attackingUnitInAttackFlow)
    {
        if (effect == null)
        {
            return new List<CardController>();
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(ownerSide, sourceCard, attackingUnitInAttackFlow, null);
            return PickEnemyAiEffectTargets(effect, ctx, null, singleOnly: true);
        }

        return ResolveEffectTargets(sourceCard, ownerSide, effect);
    }

    /// <summary>カードの効果リストを仮想盤面に適用（本番 ApplyEffect と同じ対象解決）。</summary>
    private void ApplyEnemyHandCommandVirtualEffects(
        List<VirtualBattleUnitSnap> working,
        List<EffectData> effects,
        CardController command,
        PlayerType commandOwnerSide,
        EnemyAiEffectPickContext ctx)
    {
        if (working == null || effects == null || command == null || command.Data == null)
        {
            return;
        }

        for (int ei = 0; ei < effects.Count; ei++)
        {
            EffectData eff = effects[ei];
            if (eff == null || eff.type == EffectType.Draw || eff.type == EffectType.Look || eff.type == EffectType.AddToHandFromLooked
                || eff.type == EffectType.ReturnLookedRemainderToDeckTop
                || eff.type == EffectType.ShuffleLookedRemainderToDeckBottom
                || eff.type == EffectType.ChooseLookedRemainderDisposition
                || eff.type == EffectType.MillTopToTrash
                || eff.type == EffectType.ExileFromDeck
                || eff.type == EffectType.ExileFromTrash
                || eff.type == EffectType.EffectBattle
                || eff.type == EffectType.BlockRedirect || eff.type == EffectType.HighMobility
                || eff.type == EffectType.AttackActiveEnemyUnit
                || eff.type == EffectType.ForceEnemyAttackTarget
                || eff.type == EffectType.AddShieldToHand || eff.type == EffectType.AddSelfToHand
                || eff.type == EffectType.DeploySelfToShield || eff.type == EffectType.DeployShieldFromHand
                || eff.type == EffectType.DeployBase
                || eff.type == EffectType.DeployUnit
                || eff.type == EffectType.GrantAttackFlag
                || eff.type == EffectType.DiscardFromHand
                || eff.type == EffectType.Activate
                || eff.type == EffectType.NotDirectAttack
                || eff.type == EffectType.Suppress
                || eff.type == EffectType.Breach
                || eff.type == EffectType.RecoverHp
                || eff.type == EffectType.AddExResource
                || eff.type == EffectType.ChooseOne
                || eff.type == EffectType.RestResource
                || eff.type == EffectType.AddFromTrashToHand
                || eff.type == EffectType.MountSelfFromTrashAsPilot
                || eff.type == EffectType.ActivateMountedCardOnMain)
            {
                continue;
            }

            int magnitude = ResolveEffectMagnitude(eff, commandOwnerSide, command);
            if (magnitude == 0 && !eff.type.UsesTargetCountValue())
            {
                continue;
            }

            List<CardController> targets;
            if (EffectRequiresManualUnitSelection(eff))
            {
                targets = PickEnemyAiEffectTargets(eff, ctx, null, singleOnly: true);
            }
            else
            {
                targets = ResolveEffectTargets(command, commandOwnerSide, eff);
            }

            if (targets == null || targets.Count == 0)
            {
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, eff, targets, magnitude, command);
        }
    }

    private void ApplyEnemyOnActionVirtualChainWithPickContext(
        List<VirtualBattleUnitSnap> working,
        CardController command,
        PlayerType commandOwnerSide,
        EnemyAiEffectPickContext ctx)
    {
        if (working == null || command == null || command.Data == null)
        {
            return;
        }

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        ApplyEnemyHandCommandVirtualEffects(working, onActionEffects, command, commandOwnerSide, ctx);
    }

    /// <summary>手札コマンド：効果を読み、仮想適用後の盤面スコア差分を返す（高いほど敵に有利）。</summary>
    private int ScoreEnemyHandCommandByEffectSimulation(
        CardController command,
        EffectTiming timing,
        CardController attackingUnitInAttackFlow)
    {
        if (command == null || command.Data == null)
        {
            return int.MinValue / 2;
        }

        List<EffectData> effects = timing == EffectTiming.OnMain
            ? BuildOnMainExecutableEffects(PlayerType.Enemy, command)
            : GetEffectsByTiming(command.Data, timing);
        if (effects == null || effects.Count == 0)
        {
            return int.MinValue / 2;
        }

        List<CardController> restTargets = timing == EffectTiming.OnAction
            ? GetEnemyAiRestTargets(PlayerType.Enemy)
            : null;
        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
            PlayerType.Enemy,
            command,
            attackingUnitInAttackFlow,
            restTargets);

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();

        if (attackFlowAttackerOwner == PlayerType.Player
            && attackFlowStrikeKind != AttackFlowStrikeKind.None)
        {
            int scoreWithoutCommand = SimulatePostCommandThenPlayerAttackScore(before, null, null, null);
            int scoreWithCommand = SimulatePostCommandThenPlayerAttackScore(
                before,
                command,
                effects,
                pickCtx);
            int benefit = scoreWithCommand - scoreWithoutCommand;

            if (DidPlayerAttackerDieAfterPostCommandPlayerAttackSim(before, command, effects, pickCtx))
            {
                benefit += EnemyAiPostAttackExecuteBonusPlayerUnitKilled;
            }
            else if (DidPlayerAttackerSurviveAfterPostCommandPlayerAttackSim(before, command, effects, pickCtx))
            {
                benefit = Mathf.Min(benefit, EnemyAiPostAttackMaxBenefitWhenPlayerSurvives);
            }

            LogEnemyOnActionCommandSimulationForPlayerAttack(
                command,
                benefit,
                scoreWithoutCommand,
                scoreWithCommand,
                before,
                effects,
                pickCtx);
            return benefit;
        }

        return ScoreEnemyHandCommandBenefitOnSnaps(
            before,
            command,
            effects,
            pickCtx,
            attackingUnitInAttackFlow);
    }

    /// <summary>既存の仮想盤面に対して手札コマンド効果のスコア差分（高いほど敵に有利）。</summary>
    private int ScoreEnemyHandCommandBenefitOnSnaps(
        List<VirtualBattleUnitSnap> beforeSnaps,
        CardController command,
        List<EffectData> effects,
        EnemyAiEffectPickContext pickCtx,
        CardController attackingUnitInAttackFlow)
    {
        if (beforeSnaps == null || command == null || effects == null || pickCtx == null)
        {
            return int.MinValue / 2;
        }

        int baseline = ScoreEnemyAiSimulatedBoardValue(beforeSnaps, attackingUnitInAttackFlow);
        List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(beforeSnaps);
        ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, pickCtx);
        int after = ScoreEnemyAiSimulatedBoardValue(work, attackingUnitInAttackFlow);
        return after - baseline;
    }

    /// <summary>コマンド（任意）適用 → 直後のプレイヤー攻撃交換を仮想適用したあとのスコア。</summary>
    private int SimulatePostCommandThenPlayerAttackScore(
        List<VirtualBattleUnitSnap> beforeCommandSnaps,
        CardController command,
        List<EffectData> effects,
        EnemyAiEffectPickContext pickCtx)
    {
        List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(beforeCommandSnaps);
        if (command != null && effects != null && pickCtx != null)
        {
            ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, pickCtx);
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit
            && TryGetPlayerAttackExchangeVirtualSnaps(work, out VirtualBattleUnitSnap _, out VirtualBattleUnitSnap enemyDef)
            && enemyDef != null)
        {
            ApplyVirtualPlayerAttackExchangeOnSnaps(work);
            return ScorePostPlayerAttackExchangeOutcome(work);
        }

        return ScoreEnemyDefenseVsPlayerAttack(work);
    }

    /// <summary>仮想盤面の敵有利度（大きいほど敵に良い）。</summary>
    private int ScoreEnemyAiSimulatedBoardValue(
        List<VirtualBattleUnitSnap> snaps,
        CardController attackingUnitInAttackFlow)
    {
        if (snaps == null)
        {
            return int.MinValue / 2;
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.None && attackFlowAttackerOwner == PlayerType.Player)
        {
            List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(snaps);
            if (attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit
                && TryGetPlayerAttackExchangeVirtualSnaps(work, out _, out VirtualBattleUnitSnap ed)
                && ed != null)
            {
                ApplyVirtualPlayerAttackExchangeOnSnaps(work);
                return ScorePostPlayerAttackExchangeOutcome(work);
            }

            return ScoreEnemyDefenseVsPlayerAttack(work);
        }

        if (attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null)
        {
            PlayerType atkOwner = ResolveCardOwner(attackingUnitInAttackFlow.transform);
            if (atkOwner == PlayerType.Enemy)
            {
                VirtualBattleUnitSnap enemyAtk = FindBattleVirtualSnap(snaps, attackingUnitInAttackFlow);
                if (enemyAtk != null)
                {
                    List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
                    int bestExchange = int.MinValue;
                    for (int i = 0; i < restTargets.Count; i++)
                    {
                        CardController t = restTargets[i];
                        if (t == null || t.Data == null)
                        {
                            continue;
                        }

                        VirtualBattleUnitSnap playerSnap = FindBattleVirtualSnap(snaps, t);
                        if (playerSnap == null)
                        {
                            continue;
                        }

                        int sc = ScoreVirtualAttackAgainstPlayerUnit(enemyAtk, playerSnap);
                        if (sc > bestExchange)
                        {
                            bestExchange = sc;
                        }
                    }

                    if (bestExchange > int.MinValue / 4)
                    {
                        return bestExchange;
                    }
                }
            }
        }

        return ComputeEnemyAiFieldAdvantageScore(snaps);
    }

    private bool TryGetPlayerAttackExchangeVirtualSnaps(
        List<VirtualBattleUnitSnap> snaps,
        out VirtualBattleUnitSnap playerAtk,
        out VirtualBattleUnitSnap enemyDef)
    {
        playerAtk = attackFlowAttackerUnit != null
            ? FindBattleVirtualSnap(snaps, attackFlowAttackerUnit)
            : null;
        enemyDef = null;
        if (attackFlowBlockRedirectUnit != null)
        {
            enemyDef = FindBattleVirtualSnap(snaps, attackFlowBlockRedirectUnit);
        }
        else if (attackFlowDeclaredDefenderUnit != null)
        {
            enemyDef = FindBattleVirtualSnap(snaps, attackFlowDeclaredDefenderUnit);
        }

        return playerAtk != null;
    }

    /// <summary>仮想盤面上でプレイヤー攻撃の相互ダメージを適用（コマンド後の HP/AP で交換）。</summary>
    private void ApplyVirtualPlayerAttackExchangeOnSnaps(List<VirtualBattleUnitSnap> snaps)
    {
        if (!TryGetPlayerAttackExchangeVirtualSnaps(snaps, out VirtualBattleUnitSnap playerAtk, out VirtualBattleUnitSnap enemyDef)
            || playerAtk == null
            || enemyDef == null)
        {
            return;
        }

        int playerStrike = Mathf.Max(0, playerAtk.Ap);
        int enemyStrike = Mathf.Max(0, enemyDef.Ap);
        ApplyVirtualUnitVsUnitCombatHpExchange(playerAtk, enemyDef);
    }

    /// <summary>コマンド後＋プレイヤー攻撃交換**後**の結果をスコア化（敵視点・高いほど有利）。</summary>
    private int ScorePostPlayerAttackExchangeOutcome(List<VirtualBattleUnitSnap> snapsAfterExchange)
    {
        if (!TryGetPlayerAttackExchangeVirtualSnaps(
                snapsAfterExchange,
                out VirtualBattleUnitSnap playerAtk,
                out VirtualBattleUnitSnap enemyDef))
        {
            return -SumPlayerFieldThreat(snapsAfterExchange);
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            int shieldWeight = attackFlowDefenderShieldCountAtStrike > 0 ? 8 : 12;
            return 40 - playerAtk.Ap * shieldWeight;
        }

        if (enemyDef == null)
        {
            return -SumPlayerFieldThreat(snapsAfterExchange);
        }

        int score = 0;
        if (playerAtk.Hp <= 0)
        {
            score += EnemyAiPostAttackScorePlayerUnitDies;
        }
        else
        {
            score += EnemyAiPostAttackScorePlayerUnitSurvives;
        }

        if (enemyDef.Hp <= 0)
        {
            score += EnemyAiPostAttackScoreEnemyDefenderDies;
        }
        else
        {
            score += EnemyAiPostAttackScoreEnemyDefenderSurvives + enemyDef.Hp;
        }

        if (playerAtk.Hp <= 0 && enemyDef.Hp > 0)
        {
            score += 25;
        }

        if (playerAtk.Hp > 0 && enemyDef.Hp <= 0)
        {
            score -= 35;
        }

        return score;
    }

    private bool DidPlayerAttackerDieAfterPostCommandPlayerAttackSim(
        List<VirtualBattleUnitSnap> beforeCommandSnaps,
        CardController command,
        List<EffectData> effects,
        EnemyAiEffectPickContext pickCtx)
    {
        List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(beforeCommandSnaps);
        if (command != null && effects != null && pickCtx != null)
        {
            ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, pickCtx);
        }

        if (!TryGetPlayerAttackExchangeVirtualSnaps(work, out VirtualBattleUnitSnap playerAtk, out VirtualBattleUnitSnap enemyDef)
            || enemyDef == null)
        {
            return false;
        }

        ApplyVirtualPlayerAttackExchangeOnSnaps(work);
        return playerAtk != null && playerAtk.Hp <= 0;
    }

    private bool DidPlayerAttackerSurviveAfterPostCommandPlayerAttackSim(
        List<VirtualBattleUnitSnap> beforeCommandSnaps,
        CardController command,
        List<EffectData> effects,
        EnemyAiEffectPickContext pickCtx)
    {
        List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(beforeCommandSnaps);
        if (command != null && effects != null && pickCtx != null)
        {
            ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, pickCtx);
        }

        if (!TryGetPlayerAttackExchangeVirtualSnaps(work, out VirtualBattleUnitSnap playerAtk, out VirtualBattleUnitSnap enemyDef)
            || enemyDef == null)
        {
            return false;
        }

        ApplyVirtualPlayerAttackExchangeOnSnaps(work);
        return playerAtk != null && playerAtk.Hp > 0;
    }

    /// <summary>プレイヤー攻撃に対する防御側ユニットが交換後も生きるか。</summary>
    private static bool EnemyDefenderSurvivesPlayerAttackExchange(
        VirtualBattleUnitSnap playerAtk,
        VirtualBattleUnitSnap enemyDef)
    {
        if (playerAtk == null || enemyDef == null)
        {
            return false;
        }

        return Mathf.Max(0, enemyDef.Hp - playerAtk.Ap) > 0;
    }

    /// <summary>プレイヤーだけが残る一方的交換か（敵防御ユニットが落ちる）。</summary>
    private static bool IsOneSidedPlayerAttackTrade(
        VirtualBattleUnitSnap playerAtk,
        VirtualBattleUnitSnap enemyDef)
    {
        if (playerAtk == null || enemyDef == null)
        {
            return false;
        }

        int enemyHpAfter = Mathf.Max(0, enemyDef.Hp - playerAtk.Ap);
        int playerHpAfter = Mathf.Max(0, playerAtk.Hp - enemyDef.Ap);
        return enemyHpAfter <= 0 && playerHpAfter > 0;
    }

    /// <summary>プレイヤー攻撃中の敵評価（大きいほど敵に有利）。防御ユニット生存・攻撃者弱体化を重視。</summary>
    private int ScoreEnemyDefenseVsPlayerAttack(List<VirtualBattleUnitSnap> snaps)
    {
        if (!TryGetPlayerAttackExchangeVirtualSnaps(snaps, out VirtualBattleUnitSnap playerAtk, out VirtualBattleUnitSnap enemyDef))
        {
            return -SumPlayerFieldThreat(snaps);
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            int shieldWeight = attackFlowDefenderShieldCountAtStrike > 0 ? 8 : 12;
            return 40 - playerAtk.Ap * shieldWeight;
        }

        if (enemyDef == null)
        {
            return -SumPlayerFieldThreat(snaps);
        }

        int enemyHpAfter = Mathf.Max(0, enemyDef.Hp - playerAtk.Ap);
        int playerHpAfter = Mathf.Max(0, playerAtk.Hp - enemyDef.Ap);
        int score = 0;
        if (enemyHpAfter > 0)
        {
            score += 60 + enemyHpAfter * 2 + enemyDef.Ap;
        }
        else
        {
            score -= 80;
        }

        if (playerHpAfter <= 0)
        {
            score += 55;
        }
        else
        {
            score -= playerHpAfter;
        }

        if (IsOneSidedPlayerAttackTrade(playerAtk, enemyDef))
        {
            score -= 70;
        }

        score -= playerAtk.Ap * 3;
        return score;
    }

    private static string FormatPostPlayerAttackExchangeResultLine(
        VirtualBattleUnitSnap playerAtk,
        VirtualBattleUnitSnap enemyDef)
    {
        if (playerAtk == null)
        {
            return "(no player attacker)";
        }

        if (enemyDef == null)
        {
            return $"afterAttack player:{playerAtk.Name} HP{playerAtk.Hp} AP{playerAtk.Ap} (shield/no def unit)";
        }

        string playerOutcome = playerAtk.Hp <= 0 ? "playerKILLED" : "playerSURVIVES";
        string enemyOutcome = enemyDef.Hp <= 0 ? "enemyDefKILLED" : "enemyDefSURVIVES";
        return $"afterAttack player:{playerAtk.Name} HP{playerAtk.Hp} AP{playerAtk.Ap}({playerOutcome}) "
            + $"enemyDef:{enemyDef.Name} HP{enemyDef.Hp} AP{enemyDef.Ap}({enemyOutcome})";
    }

    private void LogEnemyOnActionCommandSimulationForPlayerAttack(
        CardController command,
        int simBenefit,
        int scoreWithoutCommand,
        int scoreWithCommand,
        List<VirtualBattleUnitSnap> beforeCommand,
        List<EffectData> effects,
        EnemyAiEffectPickContext pickCtx)
    {
        if (command == null || command.Data == null || attackFlowAttackerOwner != PlayerType.Player)
        {
            return;
        }

        List<VirtualBattleUnitSnap> noCmd = CloneVirtualBattleSnaps(beforeCommand);
        if (attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit)
        {
            ApplyVirtualPlayerAttackExchangeOnSnaps(noCmd);
        }

        List<VirtualBattleUnitSnap> withCmd = CloneVirtualBattleSnaps(beforeCommand);
        ApplyEnemyHandCommandVirtualEffects(withCmd, effects, command, PlayerType.Enemy, pickCtx);
        if (attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit)
        {
            ApplyVirtualPlayerAttackExchangeOnSnaps(withCmd);
        }

        TryGetPlayerAttackExchangeVirtualSnaps(noCmd, out VirtualBattleUnitSnap paNo, out VirtualBattleUnitSnap edNo);
        TryGetPlayerAttackExchangeVirtualSnaps(withCmd, out VirtualBattleUnitSnap paCmd, out VirtualBattleUnitSnap edCmd);
        Debug.Log(
            $"[EnemyAI] OnActionSim(playerAttack+cmdThenAtk) cmd:{command.Data.cardName}(id:{command.Data.id}) "
            + $"benefit:{simBenefit} postScore noCmd:{scoreWithoutCommand} withCmd:{scoreWithCommand} | "
            + $"noCommand→{FormatPostPlayerAttackExchangeResultLine(paNo, edNo)} | "
            + $"withCommand→{FormatPostPlayerAttackExchangeResultLine(paCmd, edCmd)}");
    }

    /// <summary>プレイヤー攻撃中：敵側から見た「受けるプレッシャー」（大きいほど危険）。</summary>
    private int ScoreIncomingPlayerAttackThreat(List<VirtualBattleUnitSnap> snaps)
    {
        VirtualBattleUnitSnap playerAtk = attackFlowAttackerUnit != null
            ? FindBattleVirtualSnap(snaps, attackFlowAttackerUnit)
            : null;
        if (playerAtk == null)
        {
            return SumPlayerFieldThreat(snaps);
        }

        VirtualBattleUnitSnap enemyDef = null;
        if (attackFlowBlockRedirectUnit != null)
        {
            enemyDef = FindBattleVirtualSnap(snaps, attackFlowBlockRedirectUnit);
        }
        else if (attackFlowDeclaredDefenderUnit != null)
        {
            enemyDef = FindBattleVirtualSnap(snaps, attackFlowDeclaredDefenderUnit);
        }

        if (enemyDef != null && attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit)
        {
            int blockHpAfter = Mathf.Max(0, enemyDef.Hp - playerAtk.Ap);
            int atkHpAfter = Mathf.Max(0, playerAtk.Hp - enemyDef.Ap);
            int pressure = playerAtk.Ap * 2 + playerAtk.Hp;
            if (blockHpAfter <= 0)
            {
                pressure += EnemyAiAttackScoreBonusRawKillPlayer;
            }

            if (atkHpAfter <= 0)
            {
                pressure -= EnemyAiAttackScorePenaltyOneSidedEnemyDeath + 20;
            }
            else if (atkHpAfter < playerAtk.Hp)
            {
                pressure -= 15;
            }

            return pressure;
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            int shieldWeight = attackFlowDefenderShieldCountAtStrike > 0 ? 8 : 12;
            return playerAtk.Ap * shieldWeight + (attackFlowDefenderShieldCountAtStrike > 0 ? 20 : 35);
        }

        return playerAtk.Ap * 2 + playerAtk.Hp + SumPlayerFieldThreat(snaps) / 4;
    }

    private static int SumPlayerFieldThreat(List<VirtualBattleUnitSnap> snaps)
    {
        int sum = 0;
        if (snaps == null)
        {
            return sum;
        }

        for (int i = 0; i < snaps.Count; i++)
        {
            VirtualBattleUnitSnap s = snaps[i];
            if (s != null && s.FieldOwner == PlayerType.Player)
            {
                sum += ComputeEnemyAiUnitThreatScore(s.Ap, s.Hp);
            }
        }

        return sum;
    }

    private static int ComputeEnemyAiFieldAdvantageScore(List<VirtualBattleUnitSnap> snaps)
    {
        int playerHp = 0;
        int playerThreat = 0;
        int enemyThreat = 0;
        if (snaps == null)
        {
            return 0;
        }

        for (int i = 0; i < snaps.Count; i++)
        {
            VirtualBattleUnitSnap s = snaps[i];
            if (s == null)
            {
                continue;
            }

            if (s.FieldOwner == PlayerType.Player)
            {
                playerHp += Mathf.Max(0, s.Hp);
                playerThreat += ComputeEnemyAiUnitThreatScore(s.Ap, s.Hp);
            }
            else if (s.FieldOwner == PlayerType.Enemy)
            {
                enemyThreat += ComputeEnemyAiUnitThreatScore(s.Ap, s.Hp);
            }
        }

        return enemyThreat - playerThreat - playerHp;
    }

    private static int SumPlayerFieldHp(List<VirtualBattleUnitSnap> snaps)
    {
        int sum = 0;
        if (snaps == null)
        {
            return sum;
        }

        for (int i = 0; i < snaps.Count; i++)
        {
            VirtualBattleUnitSnap s = snaps[i];
            if (s != null && s.FieldOwner == PlayerType.Player)
            {
                sum += Mathf.Max(0, s.Hp);
            }
        }

        return sum;
    }

    private static int ScoreVirtualAttackAgainstPlayerUnit(VirtualBattleUnitSnap attacker, VirtualBattleUnitSnap playerTarget)
    {
        if (attacker == null || playerTarget == null)
        {
            return int.MinValue;
        }

        int score = ComputeEnemyAiUnitThreatScore(playerTarget.Ap, playerTarget.Hp);
        int rawPlayerHpAfter = Mathf.Max(0, playerTarget.Hp - attacker.Ap);
        int rawEnemyHpAfter = Mathf.Max(0, attacker.Hp - playerTarget.Ap);
        if (rawPlayerHpAfter <= 0)
        {
            score += EnemyAiAttackScoreBonusRawKillPlayer;
        }

        if (rawEnemyHpAfter <= 0 && rawPlayerHpAfter > 0)
        {
            score -= EnemyAiAttackScorePenaltyOneSidedEnemyDeath;
        }

        return score;
    }

    private bool TryEnemyExecuteOnMainFromHand()
    {
        RectTransform hand = enemyCardGameRule != null ? enemyCardGameRule.HandScrollContent : null;
        if (hand == null)
        {
            return false;
        }

        CardController best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || !cc.Data.IsCommand())
            {
                continue;
            }

            if (!CanExecuteOnMainCardNow(PlayerType.Enemy, cc))
            {
                continue;
            }

            int score = ScoreEnemyHandCommandByEffectSimulation(cc, EffectTiming.OnMain, null);
            if (score > bestScore)
            {
                bestScore = score;
                best = cc;
            }
        }

        if (best == null || bestScore < EnemyAiHandCommandMinScoreToExecute)
        {
            return false;
        }

        Debug.Log(
            $"[EnemyAI] OnMain execute:{best.Data.cardName}(id:{best.Data.id}) simScore:{bestScore} (effects→virtual→score)");
        StartCoroutine(EnemyOnMainCommandAckThenExecute(best));
        return true;
    }

    private List<CardController> BuildEnemyCommandPreviewTargetList(
        CardController command,
        PlayerType side,
        EffectTiming timing,
        CardController attackingUnitInAttackFlow)
    {
        List<CardController> preview = new List<CardController>();
        if (command == null || command.Data == null)
        {
            return preview;
        }

        List<EffectData> effects = timing == EffectTiming.OnMain
            ? BuildOnMainExecutableEffects(side, command)
            : GetEffectsByTiming(command.Data, timing);
        List<CardController> restTargets = timing == EffectTiming.OnAction
            ? GetEnemyAiRestTargets(PlayerType.Enemy)
            : null;
        EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(side, command, attackingUnitInAttackFlow, restTargets);
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (EffectRequiresManualUnitSelection(effect))
            {
                List<CardController> pickCandidates = effect.target.IsSingleOpponentUnitPickTarget()
                    ? GetAliveEnemyUnitsForEffectTarget(side, effect.target)
                    : ResolveSelectableEffectTargets(command, side, effect);
                CardController picked = PickEnemyAiEffectTarget(effect, ctx, pickCandidates);
                if (picked != null && !preview.Contains(picked))
                {
                    preview.Add(picked);
                }
            }
            else
            {
                List<CardController> resolved = ResolveEffectTargets(command, side, effect);
                for (int r = 0; r < resolved.Count; r++)
                {
                    CardController t = resolved[r];
                    if (t != null && !preview.Contains(t))
                    {
                        preview.Add(t);
                    }
                }
            }
        }

        return preview;
    }

    private IEnumerator EnemyOnMainCommandAckThenExecute(CardController command)
    {
        List<CardController> previewTargets = BuildEnemyCommandPreviewTargetList(
            command,
            PlayerType.Enemy,
            EffectTiming.OnMain,
            null);
        BeginOnDestroyedLatencyHold();
        yield return ShowCommandUseAcknowledgementCoroutine(
            command,
            null,
            previewTargets,
            "敵 — コマンド（OnMain）");
        TryExecuteOnMainCard(PlayerType.Enemy, command, null);
        EndOnDestroyedLatencyHold();
        yield return WaitUntilBlockingChoiceOrTrashUiCleared();
    }

    private bool TryExecuteEnemyOnActionStep(
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow)
    {
        List<CardController> eligible = CollectEligibleEnemyHandCommandsForEnemyAiSim();
        if (eligible.Count == 0)
        {
            return false;
        }

        if (enableEnemyOnActionDebugPopupOnly)
        {
            return TryOpenOnActionCommandSelection(PlayerType.Enemy, context, onStepDone, attackingUnitInAttackFlow);
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        CardController bestCmd = null;
        int bestScore = int.MinValue;
        bool playerIsAttacking = attackFlowAttackerOwner == PlayerType.Player && attackFlowAttackerUnit != null;
        if (playerIsAttacking)
        {
            Debug.Log(
                $"[EnemyAI] OnActionSim(playerAttack+cmdThenAtk) start context:{context} eligibleCmds:{eligible.Count} "
                + $"(score=postCommand+playerAttack exchange, high if opponent unit dies)");
        }

        for (int i = 0; i < eligible.Count; i++)
        {
            CardController cmd = eligible[i];
            if (cmd == null || cmd.Data == null)
            {
                continue;
            }

            int score = ScoreEnemyHandCommandByEffectSimulation(cmd, EffectTiming.OnAction, attackingUnitInAttackFlow);
            if (score > bestScore)
            {
                bestScore = score;
                bestCmd = cmd;
            }
        }

        if (bestCmd != null && bestScore >= EnemyAiHandCommandMinScoreToExecute)
        {
            Debug.Log(
                $"[EnemyAI] OnAction execute:{bestCmd.Data.cardName}(id:{bestCmd.Data.id}) simScore:{bestScore} context:{context} (effects→virtual→score)");
            TryExecuteEnemyOnActionCommand(PlayerType.Enemy, bestCmd, onStepDone, attackingUnitInAttackFlow);
            return true;
        }

        if (bestCmd != null && EnemyAiHandCommandSimWorthExecuteOnTie(bestCmd, attackingUnitInAttackFlow, restTargets))
        {
            Debug.Log(
                $"[EnemyAI] OnAction execute(special):{bestCmd.Data.cardName}(id:{bestCmd.Data.id}) simScore:{bestScore} context:{context}");
            TryExecuteEnemyOnActionCommand(PlayerType.Enemy, bestCmd, onStepDone, attackingUnitInAttackFlow);
            return true;
        }

        Debug.Log($"[EnemyAI] OnAction skip context:{context} (bestSimScore:{bestScore} min:{EnemyAiHandCommandMinScoreToExecute})");
        onStepDone?.Invoke();
        return true;
    }

    /// <summary>スコアが閾値未満でも、仮想適用後に撃破・攻撃無力化など明確な利益があるときだけ実行。</summary>
    private bool EnemyAiHandCommandSimWorthExecuteOnTie(
        CardController command,
        CardController attackingUnitInAttackFlow,
        List<CardController> restTargets)
    {
        if (command == null)
        {
            return false;
        }

        if (attackFlowAttackerOwner == PlayerType.Player && attackFlowAttackerUnit != null)
        {
            return EnemyAiHandCommandSimWorthExecuteDuringPlayerAttack(command, attackingUnitInAttackFlow, restTargets);
        }

        if (attackingUnitInAttackFlow != null
            && ResolveCardOwner(attackingUnitInAttackFlow.transform) == PlayerType.Enemy
            && restTargets != null
            && restTargets.Count > 0)
        {
            return EnemyAiCommandEnablesKillAfterVirtualApply(
                command,
                attackingUnitInAttackFlow,
                restTargets,
                new List<CardController> { command });
        }

        return false;
    }

    /// <summary>一方的に取られる見込みのとき、コマンド後に防御側が生き残る／攻撃者が無力化されれば実行。</summary>
    private bool EnemyAiHandCommandSimWorthExecuteDuringPlayerAttack(
        CardController command,
        CardController attackingUnitInAttackFlow,
        List<CardController> restTargets)
    {
        if (command == null || command.Data == null)
        {
            return false;
        }

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        List<EffectData> effects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(
            PlayerType.Enemy,
            command,
            attackingUnitInAttackFlow,
            restTargets);

        if (DidPlayerAttackerDieAfterPostCommandPlayerAttackSim(before, command, effects, ctx))
        {
            return true;
        }

        List<VirtualBattleUnitSnap> noCmd = CloneVirtualBattleSnaps(before);
        List<VirtualBattleUnitSnap> withCmd = CloneVirtualBattleSnaps(before);
        ApplyEnemyHandCommandVirtualEffects(withCmd, effects, command, PlayerType.Enemy, ctx);
        if (attackFlowStrikeKind != AttackFlowStrikeKind.UnitVsUnit)
        {
            return false;
        }

        ApplyVirtualPlayerAttackExchangeOnSnaps(noCmd);
        ApplyVirtualPlayerAttackExchangeOnSnaps(withCmd);
        if (!TryGetPlayerAttackExchangeVirtualSnaps(noCmd, out VirtualBattleUnitSnap paNo, out VirtualBattleUnitSnap edNo)
            || !TryGetPlayerAttackExchangeVirtualSnaps(withCmd, out VirtualBattleUnitSnap paCmd, out VirtualBattleUnitSnap edCmd)
            || edNo == null
            || edCmd == null)
        {
            return false;
        }

        bool playerDiesWithCmd = paCmd.Hp <= 0;
        bool playerSurvivesWithCmd = paCmd.Hp > 0;
        bool enemySurvivesWithCmd = edCmd.Hp > 0;
        bool enemyDiesWithoutCmd = edNo.Hp <= 0;
        bool enemyDiesWithCmd = edCmd.Hp <= 0;

        if (playerDiesWithCmd)
        {
            return true;
        }

        if (enemyDiesWithoutCmd && enemySurvivesWithCmd && playerSurvivesWithCmd)
        {
            return true;
        }

        return false;
    }

    private bool EnemyAiCommandEnablesKillAfterVirtualApply(
        CardController command,
        CardController attacker,
        List<CardController> restTargets,
        List<CardController> eligibleCommands)
    {
        if (command == null || attacker == null || restTargets == null || restTargets.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < restTargets.Count; i++)
        {
            CardController t = restTargets[i];
            if (t == null)
            {
                continue;
            }

            if (EnemyAiAnyHandCommandSimAllowsKillPlayerUnit(attacker, t, new List<CardController> { command }, out _))
            {
                return true;
            }
        }

        return false;
    }

    private List<CardController> BuildEnemyOnActionPreviewTargetList(
        CardController command,
        PlayerType side,
        CardController attackingUnitInAttackFlow)
    {
        return BuildEnemyCommandPreviewTargetList(command, side, EffectTiming.OnAction, attackingUnitInAttackFlow);
    }

    private void TryExecuteEnemyOnActionCommand(
        PlayerType side,
        CardController command,
        System.Action onDone,
        CardController attackingUnitInAttackFlow = null)
    {
        if (command == null || command.Data == null)
        {
            onDone?.Invoke();
            return;
        }

        List<CardController> previewTargets = BuildEnemyOnActionPreviewTargetList(command, side, attackingUnitInAttackFlow);
        StartCoroutine(EnemyOnActionCommandPreviewThenExecute(
            side,
            command,
            previewTargets,
            attackingUnitInAttackFlow,
            onDone));
    }

    private IEnumerator EnemyOnActionCommandPreviewThenExecute(
        PlayerType side,
        CardController command,
        List<CardController> previewTargets,
        CardController attackingUnitInAttackFlow,
        System.Action onDone)
    {
        BeginOnDestroyedLatencyHold();
        yield return ShowCommandUseAcknowledgementCoroutine(
            command,
            attackingUnitInAttackFlow,
            previewTargets,
            "敵 — コマンド（OnAction）");

        if (!gundamRule.TryConsumeResource(
                ToRuleSide(side),
                command.CurrentCost,
                0,
                command.Data.id,
                command.CurrentLevel))
        {
            EndOnDestroyedLatencyHold();
            Debug.Log("[EnemyAI] OnAction: リソース不足で実行できません。");
            onDone?.Invoke();
            yield break;
        }

        SyncResourceViewsFromRule(ToRuleSide(side));

        MarkActionStepCardUsed(side, command);

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        if (onActionEffects.Count == 0)
        {
            EndOnDestroyedLatencyHold();
            FinalizeOnActionSourceCard(command, side);
            onDone?.Invoke();
            yield break;
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(side, command, attackingUnitInAttackFlow, restTargets);
        ExecuteEnemyOnActionEffectsChain(
            command,
            side,
            onActionEffects,
            0,
            ctx,
            () =>
            {
                EndOnDestroyedLatencyHold();
                StartCoroutine(CoFinishEnemyOnActionAfterTrashUi(
                    command,
                    side,
                    onDone));
            },
            attackingUnitInAttackFlow);
    }

    private IEnumerator CoFinishEnemyOnActionAfterTrashUi(
        CardController command,
        PlayerType side,
        System.Action onDone)
    {
        yield return WaitUntilBlockingChoiceOrTrashUiCleared();
        FinalizeOnActionSourceCard(command, side);
        SyncAllResourceViewsFromRule();
        onDone?.Invoke();
    }

    private void ExecuteEnemyOnActionEffectsChain(
        CardController command,
        PlayerType side,
        List<EffectData> effects,
        int index,
        EnemyAiEffectPickContext ctx,
        System.Action onAllDone,
        CardController attackingUnitInAttackFlow)
    {
        if (effects == null || index >= effects.Count)
        {
            onAllDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            ExecuteEnemyOnActionEffectsChain(command, side, effects, index + 1, ctx, onAllDone, attackingUnitInAttackFlow);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = effect.target.IsSingleOpponentUnitPickTarget()
                ? GetAliveEnemyUnitsForEffectTarget(side, effect.target)
                : ResolveSelectableEffectTargets(command, side, effect);
            CardController picked = PickEnemyAiEffectTarget(effect, ctx, candidates);
            if (picked == null)
            {
                Debug.Log(
                    $"[EnemyAI] OnAction skip effect (no target) cmd:{command.Data.cardName} target:{effect.target}");
                ExecuteEnemyOnActionEffectsChain(command, side, effects, index + 1, ctx, onAllDone, attackingUnitInAttackFlow);
                return;
            }

            ApplyEffectToSpecificTargets(command, side, effect, new List<CardController> { picked });
            LogOnActionCommandAppliedToUnitsBattleOutcome(
                command,
                side,
                effect,
                "EnemyAI_OnAction_AfterApplyEnemyUnitTarget",
                SnapUnitStatsForOnActionCommandLog(new List<CardController> { picked }));
            ExecuteEnemyOnActionEffectsChain(command, side, effects, index + 1, ctx, onAllDone, attackingUnitInAttackFlow);
            return;
        }

        List<CardController> resolvedBefore = ResolveEffectTargets(command, side, effect);
        List<UnitStatSnapForCommandLog> beforeSnaps = SnapUnitStatsForOnActionCommandLog(resolvedBefore);
        ApplyEffect(command, side, effect);
        LogOnActionCommandAppliedToUnitsBattleOutcome(command, side, effect, "EnemyAI_OnAction_AfterApplyDirectEffect", beforeSnaps);
        ExecuteEnemyOnActionEffectsChain(command, side, effects, index + 1, ctx, onAllDone, attackingUnitInAttackFlow);
    }
}
