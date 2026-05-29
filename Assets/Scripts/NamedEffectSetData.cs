using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 共有効果の ScriptableObject 版（任意）。マスタの正は Resources/Data/Json/named_effect_master.json。
/// Tools/Game/Import Named Effect Sets From JSON で同期可能。
/// </summary>
[CreateAssetMenu(fileName = "NewNamedEffectSet", menuName = "Game/Named Effect Set")]
public class NamedEffectSetData : ScriptableObject
{
    [Tooltip("TimedEffectData.effectsName から参照する一意キー。")]
    public string effectSetName;

    [Tooltip("Inspector / デバッグ表示用。")]
    public string displayName;

    public List<EffectData> effects = new List<EffectData>();
}

[Serializable]
public class NamedEffectSetMasterJson
{
    public NamedEffectSetJsonEntry[] effectSets = Array.Empty<NamedEffectSetJsonEntry>();
}

[Serializable]
public class NamedEffectSetJsonEntry
{
    public string effectSetName;
    public string displayName;
    public EffectData[] effects = Array.Empty<EffectData>();
}
