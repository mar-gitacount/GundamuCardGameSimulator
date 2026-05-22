using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>敵 AI：手札コマンドは効果を参照し仮想盤面に適用してスコア化し、高ければ本番実行。</summary>
public partial class BattleGameMain
{
    /// <summary>手札コマンド仮想シミュ後のスコア差分がこの値以上なら実行（OnAction / OnMain 共通）。</summary>
    private const int EnemyAiHandCommandMinScoreToExecute = 1;

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
            ?? ResolveSelectableEffectTargets(ctx.SourceCard, ctx.OwnerSide, effect.target);
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

        CardController pick = null;
        switch (effect.type)
        {
            case EffectType.Damage:
                pick = PickLowestHpUnit(candidates);
                break;
            case EffectType.Debuff:
                pick = PickEnemyAiDebuffTarget(effect, ctx, candidates);
                break;
            case EffectType.Buff:
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

    private CardController PickEnemyAiDebuffTarget(
        EffectData effect,
        EnemyAiEffectPickContext ctx,
        List<CardController> candidates)
    {
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

        if (effect.target.IsSingleOpponentUnitPickTarget())
        {
            EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(ownerSide, sourceCard, attackingUnitInAttackFlow, null);
            return PickEnemyAiEffectTargets(effect, ctx, null, singleOnly: true);
        }

        return ResolveEffectTargets(sourceCard, ownerSide, effect.target);
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
            if (eff == null || eff.type == EffectType.Draw || eff.type == EffectType.BlockRedirect)
            {
                continue;
            }

            int magnitude = ResolveEffectMagnitude(eff, commandOwnerSide, command);
            if (magnitude == 0)
            {
                continue;
            }

            List<CardController> targets;
            if (eff.target.IsSingleOpponentUnitPickTarget())
            {
                targets = PickEnemyAiEffectTargets(eff, ctx, null, singleOnly: true);
            }
            else
            {
                targets = ResolveEffectTargets(command, commandOwnerSide, eff.target);
            }

            if (targets == null || targets.Count == 0)
            {
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, eff, targets, magnitude);
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
        int baseline = ScoreEnemyAiSimulatedBoardValue(before, attackingUnitInAttackFlow);

        List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(before);
        ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, pickCtx);

        int after = ScoreEnemyAiSimulatedBoardValue(work, attackingUnitInAttackFlow);
        return after - baseline;
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
            return -ScoreIncomingPlayerAttackThreat(snaps);
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
            if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
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

            if (effect.target.IsSingleOpponentUnitPickTarget())
            {
                CardController picked = PickEnemyAiEffectTarget(
                    effect,
                    ctx,
                    GetAliveEnemyUnitsForEffectTarget(side, effect.target));
                if (picked != null && !preview.Contains(picked))
                {
                    preview.Add(picked);
                }
            }
            else
            {
                List<CardController> resolved = ResolveEffectTargets(command, side, effect.target);
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
        yield return ShowCommandUseAcknowledgementCoroutine(
            command,
            null,
            previewTargets,
            "敵 — コマンド（OnMain）");
        TryExecuteOnMainCard(PlayerType.Enemy, command, null);
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
            return TryShowEnemyOnActionCommandCandidatesPopup(context, onStepDone, attackingUnitInAttackFlow);
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        CardController bestCmd = null;
        int bestScore = int.MinValue;
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
        return false;
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
            List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(BuildFullBattleVirtualSnapshot());
            List<EffectData> effects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
            EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(
                PlayerType.Enemy,
                command,
                attackingUnitInAttackFlow,
                restTargets);
            ApplyEnemyHandCommandVirtualEffects(work, effects, command, PlayerType.Enemy, ctx);
            VirtualBattleUnitSnap pa = FindBattleVirtualSnap(work, attackFlowAttackerUnit);
            if (pa != null && (pa.Hp <= 0 || pa.Ap <= 0))
            {
                return true;
            }

            return false;
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
        yield return ShowCommandUseAcknowledgementCoroutine(
            command,
            attackingUnitInAttackFlow,
            previewTargets,
            "敵 — コマンド（OnAction）");

        if (!gundamRule.TryConsumeResource(ToRuleSide(side), command.CurrentCost, 0, command.Data.id))
        {
            Debug.Log("[EnemyAI] OnAction: リソース不足で実行できません。");
            onDone?.Invoke();
            yield break;
        }

        SyncResourceViewsFromRule(ToRuleSide(side));

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        if (onActionEffects.Count == 0)
        {
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
                FinalizeOnActionSourceCard(command, side);
                SyncAllResourceViewsFromRule();
                onDone?.Invoke();
            },
            attackingUnitInAttackFlow);
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

        if (effect.target.IsSingleOpponentUnitPickTarget())
        {
            List<CardController> candidates = GetAliveEnemyUnitsForEffectTarget(side, effect.target);
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

        List<CardController> resolvedBefore = ResolveEffectTargets(command, side, effect.target);
        List<UnitStatSnapForCommandLog> beforeSnaps = SnapUnitStatsForOnActionCommandLog(resolvedBefore);
        ApplyEffect(command, side, effect);
        LogOnActionCommandAppliedToUnitsBattleOutcome(command, side, effect, "EnemyAI_OnAction_AfterApplyDirectEffect", beforeSnaps);
        ExecuteEnemyOnActionEffectsChain(command, side, effects, index + 1, ctx, onAllDone, attackingUnitInAttackFlow);
    }
}
