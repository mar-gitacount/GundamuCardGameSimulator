using System;
using System.Collections.Generic;
using UnityEngine;

public enum EffectTiming
{
    OnPlayed,       // 手札から出した時（召喚時）
    OnTurnStart,    // 自分のターン開始時
    OnTurnEnd,      // 自分のターン終了時
    OnAttack,       // 攻撃する時
    OnAction,       // 任意アクション（攻撃時/ターン終了時に手札から実行可能）
    OnDestroyed,    // 破壊された時
    OnEndOfGame,    // ゲーム終了時
    OnEnemyAttack,  // 敵が攻撃してきた時（防御リアクション用）
    OnMain,         // メインフェイズ中・自分のターンでいつでも実行可能
    OnHandAuto      // 手札に入った時に自動発動（操作不要）
}

public enum EffectType
{
    Damage,
    Draw,
    Buff,
    Debuff,
    BlockRedirect
}

public enum TargetType
{
    Self,
    AllyUnit,
    EnemyUnit,
    AllyAllUnits,
    EnemyAllUnits,
    SelfPlayer,
    EnemyPlayer
}

public enum EffectSelectionMode
{
    AttackedTargetOnly,
    SelectSingleEnemyUnit,
    SelectMultipleEnemyUnits
}

public enum EffectStatTarget
{
    AP,
    HP,
    Cost,
    Level,
    Both
}

public enum EffectDuration
{
    Permanent,
    UntilEndOfTurn,
    UntilEndOfBattle
}

/// <summary>発動条件で参照する盤面・手札の領域。</summary>
public enum EffectBoardSide
{
    OwnerBattleZone,
    OpponentBattleZone,
    OwnerHand,
    OpponentHand
}

public enum EffectActivationCheckKind
{
    /// <summary>Feature を持つカードが minimumCount 枚以上いる（主にユニット想定）。</summary>
    HasFeature,
    /// <summary>生存ユニットが minimumCount 体以上いる。</summary>
    UnitCountAtLeast,
    /// <summary>指定ゾーンのユニットのレベル（CardData.level）を集計して compareValue と比較。</summary>
    UnitLevelOnField,
    /// <summary>
    /// CardData.level が compareValue と一致する生存ユニットの体数を数え、
    /// unitCountCompareOp で unitCountThreshold と比較（例: LV6が0体 → Equal + threshold 0）。
    /// </summary>
    CountUnitsAtExactLevel
}

public enum EffectLevelAggregate
{
    MaxLevel,
    MinLevel,
    SumLevel,
    /// <summary>Data.level が compareValue 以上のユニットが minimumCount 体以上。</summary>
    CountUnitsWithLevelAtLeast,
    /// <summary>いずれか1体の Data.level が compareValue と compareOp で一致。</summary>
    AnyUnitLevelCompare
}

public enum EffectCompareOperator
{
    GreaterOrEqual,
    Greater,
    Equal,
    LessOrEqual,
    Less
}

/// <summary>効果量の決め方。Fixed は従来どおり value がそのまま効く。</summary>
public enum EffectValueMode
{
    /// <summary>SerializeField の value をそのまま使用（既存カード互換）。</summary>
    Fixed,
    /// <summary>value × 盤面カウント（1体あたり value）。相手ユニット数などに連動。</summary>
    MultiplyByBoardCount
}

/// <summary>MultiplyByBoardCount 時に何を数えるか。</summary>
public enum EffectValueCountKind
{
    /// <summary>指定ゾーンの生存ユニット体数。</summary>
    AliveUnits,
    /// <summary>指定 Feature を持つカード枚数（ユニット以外も含む）。</summary>
    CardsWithFeature,
    /// <summary>Data.level が valueCountMinUnitLevel 以上の生存ユニット体数。</summary>
    UnitsWithLevelAtLeast
}

[Serializable]
public class EffectActivationCondition
{
    public EffectBoardSide boardSide = EffectBoardSide.OwnerBattleZone;
    public EffectActivationCheckKind checkKind = EffectActivationCheckKind.HasFeature;

    [Tooltip("HasFeature 時に参照。未設定なら HasFeature は常に false。")]
    public CardFeatureData feature;

    [Tooltip("HasFeature: その Feature を持つカードの最低枚数。UnitCountAtLeast: 生存ユニット最低体数。CountUnitsWithLevelAtLeast: レベル条件を満たすユニットの最低体数。")]
    public int minimumCount = 1;

    public EffectLevelAggregate levelAggregate = EffectLevelAggregate.MaxLevel;
    public EffectCompareOperator compareOp = EffectCompareOperator.GreaterOrEqual;

    [Tooltip("レベル閾値、または Sum と比較する値など。CountUnitsAtExactLevel では「この Lv のユニットを数える」対象 Lv。")]
    public int compareValue;

    [Tooltip("CountUnitsAtExactLevel のみ: 体数を unitCountThreshold と比較するときの演算子。")]
    public EffectCompareOperator unitCountCompareOp = EffectCompareOperator.Equal;

    [Tooltip("CountUnitsAtExactLevel のみ: 数えた体数と比較する閾値（例: LV6 が 0 体なら 0）。")]
    public int unitCountThreshold;
}

[Serializable]
public class EffectData
{
    public EffectType type;

    [Tooltip("Fixed: 効果量そのもの。MultiplyByBoardCount: 1体あたりの量（合計 = value × カウント）。")]
    public int value;

    public TargetType target;
    public EffectSelectionMode selectionMode = EffectSelectionMode.AttackedTargetOnly;
    public EffectStatTarget statTarget = EffectStatTarget.Both;
    public EffectDuration duration = EffectDuration.Permanent;

    [Tooltip("Fixed=従来どおり。MultiplyByBoardCount=盤面の数に応じて value を倍率。")]
    public EffectValueMode valueMode = EffectValueMode.Fixed;

    [Tooltip("MultiplyByBoardCount: 数えるゾーン（例: 相手バトルゾーン）。")]
    public EffectBoardSide valueCountBoardSide = EffectBoardSide.OpponentBattleZone;

    [Tooltip("MultiplyByBoardCount: カウントの種類。")]
    public EffectValueCountKind valueCountKind = EffectValueCountKind.AliveUnits;

    [Tooltip("CardsWithFeature のとき参照する Feature。")]
    public CardFeatureData valueCountFeature;

    [Tooltip("UnitsWithLevelAtLeast のときの最低 Lv（Data.level）。")]
    public int valueCountMinUnitLevel;

    [Tooltip("MultiplyByBoardCount の上限（0 で上限なし）。")]
    public int valueScaleMaximum;

    [Tooltip("カウント対象ゾーンに source がいる場合、source を数から除外。")]
    public bool valueCountExcludeSource;
}

[Serializable]
public class TimedEffectData
{
    public EffectTiming timing;

    [Tooltip("空なら条件なし（常に発動）。1件以上はすべて満たすと発動（AND）。")]
    public List<EffectActivationCondition> activationConditions = new List<EffectActivationCondition>();

    public List<EffectData> effects = new List<EffectData>();
}

public static class TimedEffectDataExtensions
{
    public static bool HasActivationConditions(this TimedEffectData timed)
    {
        return timed != null
            && timed.activationConditions != null
            && timed.activationConditions.Count > 0;
    }

    /// <summary>Self 向けの Buff/Debuff（コスト・レベル等）だけのブロックか。</summary>
    public static bool ContainsOnlySelfStatBuffDebuffEffects(this TimedEffectData timed)
    {
        if (timed?.effects == null || timed.effects.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < timed.effects.Count; i++)
        {
            EffectData e = timed.effects[i];
            if (e == null)
            {
                continue;
            }

            if (e.type != EffectType.Buff && e.type != EffectType.Debuff)
            {
                return false;
            }

            if (e.target != TargetType.Self)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>手札にいる間、場の状態で ON/OFF する条件付きパッシブ（OnHandAuto または OnPlayed+Self stat のみ）。</summary>
    public static bool IsHandConditionalPassiveBlock(this TimedEffectData timed)
    {
        if (timed == null || !timed.HasActivationConditions() || !timed.ContainsOnlySelfStatBuffDebuffEffects())
        {
            return false;
        }

        return timed.timing == EffectTiming.OnHandAuto || timed.timing == EffectTiming.OnPlayed;
    }

    /// <summary>配備時（OnPlayed）に解決するブロック。手札パッシブ専用ブロックは除外。</summary>
    public static bool IsOnFieldPlayedResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnPlayed || timed.effects == null || timed.effects.Count == 0)
        {
            return false;
        }

        return !timed.ContainsOnlySelfStatBuffDebuffEffects();
    }
}

