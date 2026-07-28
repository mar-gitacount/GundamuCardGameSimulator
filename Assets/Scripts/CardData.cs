
using UnityEngine;   // ← これが必須
using System;        // ← これも必要（[Serializable]属性のため）
using System.Collections.Generic; // ← これも必要（List<T>のため）
[CreateAssetMenu(menuName = "Game/Card")]
public class CardData : ScriptableObject
{
    public int id;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public Sprite imageName;
    public Sprite image;
    public int version;
    public CardSourceType sourceType;
    public FilterType filterType;
    public CardColor color;
    [Tooltip("カード種類（ユニット / パイロット / コマンド / ベース / EXリソース / ユニットトークン）。")]
    public Type type;
    /// <summary>ユニット（Type.Unit）向け。アセット上の既定値。実行時は CardController で上書き。</summary>
    [Tooltip("ユニットのみ。配備ターンは False（isDeployTurnAttack 時は True）。Link 条件搭乗で同日 True。次の自分ターン開始で True。")]
    public AttackFlg attackFlg = AttackFlg.False;
    [Tooltip("カード効果定義（タイミング別）。")]
    public List<TimedEffectData> timedEffects = new List<TimedEffectData>();

    [Tooltip("カード特性（複数可）。マスタは Game/Card Feature または Resources/Data/Features。")]
    public List<CardFeatureData> features = new List<CardFeatureData>();

    [Tooltip("ユニット／ユニットトークン向け。Link＝条件パイロット定義。任意搭乗可。条件一致で出したターンから攻撃可。次ターン以降は通常どおり。")]
    public List<UnitLinkPilotSlot> link = new List<UnitLinkPilotSlot>();

    [Tooltip("ユニット／ユニットトークン向け。搭乗時 OnPilotMounted / OnLink をユニット/パイロット/両方のどれで解決するか。")]
    public PilotMountOnPilotMountedSource pilotMountOnPilotMountedSource = PilotMountOnPilotMountedSource.Both;

    [Tooltip("Both 時の AI／自動解決用フォールバック順（UnitFirst / PilotFirst）。プレイヤーは順番選択 UI で上書き可能。")]
    public PilotMountOnPilotMountedOrder pilotMountOnPilotMountedOrder = PilotMountOnPilotMountedOrder.UnitFirst;

    [Tooltip("敵の攻撃をブロックし、身代わりのユニット戦にできる（ACTIVE 時のみ選択可）。")]
    public bool isBlocker;

    [Tooltip("ユニット／ユニットトークン向け。true のとき配備したターンから攻撃可能（AttackFlg=True）。false は次の自分ターン開始まで攻撃不可。")]
    public bool isDeployTurnAttack;
    
    public bool isNotDirectAttack;

    [Tooltip("シールドトークン。手札からシールドゾーンへ配備できる。")]
    public bool isShieldToken;

    [Tooltip("true のとき、ターン終了（双方 OnAction 完了後）に repairAmount だけ HP を回復する。")]
    public bool isRepair;

    [Tooltip("isRepair 時のターン終了回復量（Inspector で設定）。")]
    public int repairAmount;
}


[Serializable]
public class CardJson
{
    public int id;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public string imageName;
    public int version;
    public int sourceType;
    public int color; // カードの色を追加
    public int type;
    public int[] featureIds;
    public bool isBlocker;
    public bool isDeployTurnAttack;
    public bool isNotDirectAttack;
    public bool isShieldToken;
    public bool isRepair;
    public int repairAmount;
}

[Serializable]
public class CardMasterJson
{
    public List<CardJson> cards = new List<CardJson>();
}

