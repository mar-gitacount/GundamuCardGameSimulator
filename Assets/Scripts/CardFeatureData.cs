using System;
using UnityEngine;

/// <summary>
/// カード特性（Feature）1件分のマスタ。100種類以上は Resources/Data/Features にアセットを追加して管理する。
/// </summary>
[CreateAssetMenu(fileName = "NewCardFeature", menuName = "Game/Card Feature")]
public class CardFeatureData : ScriptableObject
{
    [Tooltip("ゲーム内で参照する一意ID（整数）。")]
    public int id;

    [Tooltip("コード・JSON・条件式から参照するキー（例: Mobile_Suit）。")]
    public string featureKey;

    [Tooltip("UI表示用の名前。")]
    public string displayName;

    [TextArea(2, 6)]
    public string description;
}

[Serializable]
public class CardFeatureMasterJson
{
    public CardFeatureJsonEntry[] features = Array.Empty<CardFeatureJsonEntry>();
}

[Serializable]
public class CardFeatureJsonEntry
{
    public int id;
    public string featureKey;
    public string displayName;
    public string description;
}
