using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="TimedEffectData"/> のインライン effects と effectsName プリセットを解決する。
/// </summary>
public static class TimedEffectResolver
{
    private static readonly List<EffectData> EmptyEffects = new List<EffectData>();

    /// <summary>
    /// effectsName が設定されていればプリセットの effects。未設定・未解決時は timed.effects。
    /// </summary>
    public static IReadOnlyList<EffectData> GetResolvedEffects(this TimedEffectData timed)
    {
        if (timed == null)
        {
            return EmptyEffects;
        }

        if (!string.IsNullOrWhiteSpace(timed.effectsName))
        {
            IReadOnlyList<EffectData> presetEffects = NamedEffectSetRegistry.GetEffects(timed.effectsName);
            if (presetEffects.Count > 0)
            {
                return presetEffects;
            }

            Debug.LogWarning(
                $"[TimedEffectResolver] Unknown or empty effectsName '{timed.effectsName}' (timing:{timed.timing})");
        }

        return timed.effects != null && timed.effects.Count > 0 ? timed.effects : EmptyEffects;
    }

    public static bool HasResolvedEffects(this TimedEffectData timed)
    {
        IReadOnlyList<EffectData> list = timed.GetResolvedEffects();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    public static List<EffectData> CollectEffectsByTiming(CardData data, EffectTiming timing)
    {
        List<EffectData> result = new List<EffectData>();
        if (data == null || data.timedEffects == null)
        {
            return result;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != timing || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                if (resolved[j] != null)
                {
                    result.Add(resolved[j]);
                }
            }
        }

        return result;
    }

    public static bool HasEffectTiming(CardData data, EffectTiming timing)
    {
        if (data == null || data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed != null && timed.timing == timing && timed.HasResolvedEffects())
            {
                return true;
            }
        }

        return false;
    }
}
