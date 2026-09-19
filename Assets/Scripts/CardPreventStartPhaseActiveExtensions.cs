using System.Collections.Generic;

/// <summary>
/// スタートフェイズのアクティブステップで、レスト中の最低 Lv ユニットを起こさない常時パッシブ（ST14-001 ジ・O 等）。
/// スタートフェイズの通常アクティブ化のみを阻害し、効果による Activate は対象外。
/// </summary>
public static class CardPreventStartPhaseActiveExtensions
{
    /// <summary>
    /// 相手場に PreventOpponentStartPhaseActiveLowestRestUnits を持つ生存ユニットがあるか。
    /// </summary>
    public static bool HasPreventOpponentStartPhaseActiveLowestRestAbility(
        IReadOnlyList<CardController> opponentBattleZone)
    {
        if (opponentBattleZone == null)
        {
            return false;
        }

        for (int i = 0; i < opponentBattleZone.Count; i++)
        {
            CardController unit = opponentBattleZone[i];
            if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (UnitHasPreventOpponentStartPhaseActiveLowestRestAbility(unit))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ユニット本体に該当常時パッシブがあるか（Permanent マーカー）。</summary>
    public static bool UnitHasPreventOpponentStartPhaseActiveLowestRestAbility(CardController unit)
    {
        if (unit?.Data?.timedEffects == null)
        {
            return false;
        }

        IReadOnlyList<TimedEffectData> timedEffects = unit.Data.timedEffects;
        for (int ti = 0; ti < timedEffects.Count; ti++)
        {
            TimedEffectData timed = timedEffects[ti];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect != null
                    && effect.type == EffectType.PreventOpponentStartPhaseActiveLowestRestUnits
                    && effect.duration == EffectDuration.Permanent)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// スタートフェイズでアクティブにしないユニットか。
    /// レスト中のユニットのうち CurrentLevel が最小のすべて（同値複数含む）。
    /// Unit / UnitToken は IsUnitLike で対象。
    /// </summary>
    public static bool ShouldSkipStartPhaseActive(
        CardController unit,
        int lowestRestedLevel,
        bool preventionActive)
    {
        if (!preventionActive || unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        if (!unit.IsRestState || unit.CurrentHp <= 0)
        {
            return false;
        }

        return unit.CurrentLevel == lowestRestedLevel;
    }

    /// <summary>
    /// レスト中のユニット（Unit / UnitToken）のうち、最も低い CurrentLevel を返す。
    /// レスト中がいなければ -1。
    /// </summary>
    public static int FindLowestRestedUnitLevel(IReadOnlyList<CardController> battleZone)
    {
        int lowest = -1;
        if (battleZone == null)
        {
            return lowest;
        }

        for (int i = 0; i < battleZone.Count; i++)
        {
            CardController unit = battleZone[i];
            if (unit == null
                || unit.Data == null
                || !unit.Data.IsUnitLike()
                || unit.CurrentHp <= 0
                || !unit.IsRestState)
            {
                continue;
            }

            int lv = unit.CurrentLevel;
            if (lowest < 0 || lv < lowest)
            {
                lowest = lv;
            }
        }

        return lowest;
    }
}
