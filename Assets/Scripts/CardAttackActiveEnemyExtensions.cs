using System.Collections.Generic;

/// <summary>
/// アクティブ（REST でない）敵ユニットを攻撃できる能力のデータ判定。
/// </summary>
public static class CardAttackActiveEnemyExtensions
{
    /// <summary>カード定義の Permanent AttackActiveEnemyUnit 効果を収集。</summary>
    public static void CollectPermanentAttackActiveEnemyEffects(CardData card, List<EffectData> results)
    {
        if (card == null || results == null || card.timedEffects == null)
        {
            return;
        }

        for (int i = 0; i < card.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.timedEffects[i];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect != null
                    && effect.type == EffectType.AttackActiveEnemyUnit
                    && effect.duration == EffectDuration.Permanent)
                {
                    results.Add(effect);
                }
            }
        }
    }

    /// <summary>カード定義に常時（Permanent）のアクティブ攻撃マーカーがあるか。</summary>
    public static bool HasPermanentAttackActiveEnemyAbility(this CardData card)
    {
        if (card == null)
        {
            return false;
        }

        var scratch = new List<EffectData>(2);
        CollectPermanentAttackActiveEnemyEffects(card, scratch);
        return scratch.Count > 0;
    }

    /// <summary>攻撃者が持つ AttackActiveEnemyUnit 効果を収集（本体・搭乗パイロット・ランタイム付与）。</summary>
    public static void CollectAttackActiveEnemyEffects(CardController unit, List<EffectData> results)
    {
        if (unit == null || results == null)
        {
            return;
        }

        AppendAttackActiveEnemyGrants(unit.AttackActiveEnemyUntilEndOfTurnGrants, results);
        AppendAttackActiveEnemyGrants(unit.AttackActiveEnemyUntilEndOfBattleGrants, results);

        if (unit.Data != null)
        {
            CollectPermanentAttackActiveEnemyEffects(unit.Data, results);
        }

        CardController pilot = unit.MountedPilot;
        if (pilot == null)
        {
            return;
        }

        AppendAttackActiveEnemyGrants(pilot.AttackActiveEnemyUntilEndOfTurnGrants, results);
        AppendAttackActiveEnemyGrants(pilot.AttackActiveEnemyUntilEndOfBattleGrants, results);

        if (pilot.Data != null)
        {
            CollectPermanentAttackActiveEnemyEffects(pilot.Data, results);
        }
    }

    private static void AppendAttackActiveEnemyGrants(
        IReadOnlyList<EffectData> grants,
        List<EffectData> results)
    {
        if (grants == null)
        {
            return;
        }

        for (int i = 0; i < grants.Count; i++)
        {
            EffectData effect = grants[i];
            if (effect != null && effect.type == EffectType.AttackActiveEnemyUnit)
            {
                results.Add(effect);
            }
        }
    }

    /// <summary>フィールド上のユニット（搭乗パイロット含む）がアクティブ攻撃を持つか。</summary>
    public static bool HasAttackActiveEnemyAbility(this CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        var scratch = new List<EffectData>(4);
        CollectAttackActiveEnemyEffects(unit, scratch);
        return scratch.Count > 0;
    }

    /// <summary>REST でない敵ユニットを、この攻撃者の AttackActiveEnemyUnit 効果で攻撃できるか。</summary>
    public static bool CanAttackerTargetActiveEnemy(this CardController attacker, CardController target)
    {
        if (attacker == null || target == null || target.IsRestState)
        {
            return false;
        }

        var effects = new List<EffectData>(4);
        CollectAttackActiveEnemyEffects(attacker, effects);
        if (effects.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            // Feature は付与先候補用。攻撃可能敵の条件はステータスのみ。
            if (!effect.HasAttackActiveEnemyTargetStatFilter())
            {
                return true;
            }

            if (effect.MatchesAttackActiveEnemyTargetFilter(target, attacker))
            {
                return true;
            }
        }

        return false;
    }
}
