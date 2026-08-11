using System;
using UnityEngine;

/// <summary>
/// パイロット識別子（PilotId）1件分のマスタ。
/// 同一人物の別カード（例: 複数のアムロ・レイ）は同じ PilotId を共有する。
/// 一覧の正本は Resources/Data/Json/pilot_master.json。
/// Tools/Game/Import Card Pilot Ids From JSON で Resources/Data/PilotIds へ反映する。
/// </summary>
[CreateAssetMenu(fileName = "NewCardPilotId", menuName = "Game/Card Pilot Id")]
public class CardPilotIdData : ScriptableObject
{
    [Tooltip("ゲーム内で参照する一意ID（整数）。")]
    public int id;

    [Tooltip("コード・JSON・条件式から参照するキー（例: Amuro_Ray）。")]
    public string pilotKey;

    [Tooltip("UI表示用の名前。")]
    public string displayName;

    [TextArea(2, 6)]
    public string description;
}

[Serializable]
public class CardPilotIdMasterJson
{
    public CardPilotIdJsonEntry[] pilots = Array.Empty<CardPilotIdJsonEntry>();
}

[Serializable]
public class CardPilotIdJsonEntry
{
    public int id;
    public string pilotKey;
    public string displayName;
    public string description;
}
