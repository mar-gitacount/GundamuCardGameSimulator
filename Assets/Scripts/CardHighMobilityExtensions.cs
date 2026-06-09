using System.Collections.Generic;

/// <summary>
/// 高機動（ブロック無視）のデータ判定。
/// </summary>
public static class CardHighMobilityExtensions
{
    /// <summary>カード定義に高機動マーカー効果があるか（いずれかのタイミングの effects / effectsName プリセット）。</summary>
    public static bool HasHighMobilityAbility(this CardData card)
    {
        if (card == null || card.timedEffects == null)
        {
            return false;
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
                if (effect != null && effect.type == EffectType.HighMobility)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>フィールド上のユニットが高機動を持つか。</summary>
    public static bool HasHighMobilityAbility(this CardController unit)
    {
        return unit != null && unit.Data != null && unit.Data.HasHighMobilityAbility();
    }
}
