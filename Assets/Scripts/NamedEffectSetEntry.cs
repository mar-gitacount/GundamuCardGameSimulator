using System.Collections.Generic;

/// <summary>
/// JSON から解決した共有効果セット 1 件（ランタイムキャッシュ用）。
/// </summary>
public sealed class NamedEffectSetEntry
{
    public string EffectSetName { get; }
    public string DisplayName { get; }
    public IReadOnlyList<EffectData> Effects { get; }

    public NamedEffectSetEntry(string effectSetName, string displayName, List<EffectData> effects)
    {
        EffectSetName = effectSetName ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        Effects = effects ?? new List<EffectData>();
    }
}
