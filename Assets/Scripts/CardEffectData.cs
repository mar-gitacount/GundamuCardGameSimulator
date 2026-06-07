using System;
using System.Collections.Generic;
using UnityEngine;

public enum EffectTiming
{
    OnPlayed,       // 手札から出した時（召喚時）
    OnTurnStart,    // 自分のターン開始時
    OnTurnEnd,      // 自分のターン終了時
    OnAttack,       // 攻撃する時（ユニット戦など。シールド攻撃の制圧は OnShieldAttack）
    OnShieldAttack, // シールド攻撃のダメージ解決時のみ
    OnBurst,        // シールド破壊公開時。DeployBase で自身をベースゾーンへ配備、Draw 等も可
    OnBaseDeployed, // ベース配備時。AddShieldToHand でゾーン先頭を手札へ
    OnShieldDeployed, // 手札のシールドカードをシールドゾーンに配備したとき
    OnAction,       // 任意アクション（攻撃時/ターン終了時に手札から実行可能）
    OnDestroyed,    // 破壊された時
    OnEndOfGame,    // ゲーム終了時
    OnEnemyAttack,  // 敵が攻撃してきた時（防御リアクション用）
    OnMain,         // メインフェイズ中・自分のターンでいつでも実行可能
    OnHandAuto,     // 手札に入った時に自動発動（操作不要）
    OnRest,         // ユニットが REST になった時
    OnPilotMounted  // パイロットをユニットに搭乗した時（ユニット・パイロット双方の timedEffects が対象）
}

public enum EffectType
{
    Damage,
    Draw,
    Buff,
    Debuff,
    BlockRedirect,
    /// <summary>制圧。シールド攻撃時のみ。EXあり→通常シールド攻撃に任せる。シールドのみ→実シールドを value 枚破壊。</summary>
    Suppress,
    /// <summary>手札へ移す（value=枚数）。ベース配備時はシールドゾーン先頭＋枚数減算。</summary>
    AddShieldToHand,
    /// <summary>手札のシールドをシールドゾーンへ配備（value=枚数）。</summary>
    DeployShieldFromHand,
    /// <summary>ベースゾーンへ配備（value=枚数）。OnBurst 時は破壊された Base カード自身を配備。</summary>
    DeployBase,
    /// <summary>バトルゾーンのユニットを手札に戻す（バウンス）。value=適用体数上限（0 で対象リスト全員）。</summary>
    Bounce,
    /// <summary>対象ユニットを REST にする。value=適用体数上限（0 で対象リスト全員）。</summary>
    Rest
}

public enum TargetType
{
    Self,
    AllyUnit,
    EnemyUnit,
    AllyAllUnits,
    EnemyAllUnits,
    SelfPlayer,
    EnemyPlayer,
    /// <summary>相手バトルゾーンの REST ユニットのみ（ACTIVE は対象外）。既存 target 数値互換のため末尾に追加。</summary>
    RestEnemyUnit,
    /// <summary>味方バトルゾーンの生存ユニット（効果の発動元カード自身は対象外）。</summary>
    AllyOtherUnit
}

/// <summary><see cref="EffectType"/> のヘルパー。</summary>
public static class EffectTypeExtensions
{
    /// <summary>value が適用体数上限として使われ、効果量 0 でも解決するタイプ。</summary>
    public static bool UsesTargetCountValue(this EffectType type)
    {
        return type == EffectType.Bounce || type == EffectType.Rest;
    }

    /// <summary>対象ユニットの手動選択 UI が必要なタイプ。</summary>
    public static bool RequiresManualUnitSelection(this EffectType type)
    {
        return type == EffectType.Bounce || type == EffectType.Rest;
    }
}

/// <summary><see cref="TargetType"/> の判定ヘルパー。</summary>
public static class EffectTargetTypeExtensions
{
    public static bool IsOpponentUnitTarget(this TargetType targetType)
    {
        return targetType == TargetType.EnemyUnit
            || targetType == TargetType.EnemyAllUnits
            || targetType == TargetType.RestEnemyUnit;
    }

    /// <summary>1体選択 UI が必要な相手ユニット対象（REST 限定含む）。</summary>
    public static bool IsSingleOpponentUnitPickTarget(this TargetType targetType)
    {
        return targetType == TargetType.EnemyUnit
            || targetType == TargetType.RestEnemyUnit;
    }

    /// <summary>味方ユニット1体を選ぶ対象（自身を含む／含まない）。</summary>
    public static bool IsAllyUnitPickTarget(this TargetType targetType)
    {
        return targetType == TargetType.AllyUnit
            || targetType == TargetType.AllyOtherUnit;
    }
}

public enum EffectSelectionMode
{
    /// <summary>未指定。target の定義どおり自動解決（選択 UI なし）。</summary>
    Unset = -1,
    AttackedTargetOnly,
    /// <summary>対象候補から1体を選択する（味方/敵どちらにも使用）。</summary>
    SelectSingle = 1,
    /// <summary>旧名称互換。挙動は SelectSingle と同一。</summary>
    SelectSingleEnemyUnit,
    SelectMultipleEnemyUnits
}

/// <summary><see cref="EffectSelectionMode"/> の選択 UI ヘルパー。</summary>
public static class EffectSelectionModeExtensions
{
    /// <summary>1体選んだら即確定するモード（SelectSingle / SelectSingleEnemyUnit）。</summary>
    public static bool IsImmediateSinglePick(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.SelectSingle
            || mode == EffectSelectionMode.SelectSingleEnemyUnit;
    }

    /// <summary>プレイヤーがユニットを選ぶ UI が必要なモード。</summary>
    public static bool RequiresManualUnitPick(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.SelectSingle
            || mode == EffectSelectionMode.SelectSingleEnemyUnit
            || mode == EffectSelectionMode.SelectMultipleEnemyUnits;
    }

    /// <summary>攻撃対象ユニットにだけ効果を当てるモード。</summary>
    public static bool IsAttackedTargetOnlyMode(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.AttackedTargetOnly;
    }
}

public enum EffectStatTarget
{
    AP,
    HP,
    Cost,
    Level,
    Both,
    /// <summary>戦闘ダメージ以外の効果ダメージ量への補正（Buff/Debuff で付与。盤面全体に常時適用）。</summary>
    EffectDamage
}

/// <summary>バウンス等の対象ユニット絞り込みに使うステータス（実効値で比較）。</summary>
public enum EffectTargetUnitFilterStat
{
    /// <summary>未指定（ステータス条件なし）。</summary>
    Unset = -1,
    AP = 0,
    HP = 1,
    Cost = 2,
    Level = 3
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
    /// <summary>未指定（判定をスキップ）。</summary>
    Unset = -1,
    OwnerBattleZone,
    OpponentBattleZone,
    OwnerHand,
    OpponentHand
}

public enum EffectActivationCheckKind
{
    /// <summary>未指定（判定をスキップ）。</summary>
    Unset = -1,
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
    CountUnitsAtExactLevel,
    /// <summary>
    /// 搭乗先ユニットに載っているパイロットを参照（OnPilotMounted 推奨）。
    /// boardSide 未指定時は MountHostUnit（搭乗イベントのホスト）のパイロットのみ。
    /// boardSide 指定時はそのゾーン内で条件を満たす搭乗ユニットが minimumCount 体以上。
    /// feature / pilotCardId で絞り込み。
    /// compareValue + compareOp で搭乗パイロットの実効レベル（CurrentLevel）を比較（例: 4 以上 → compareValue=4, GreaterOrEqual）。
    /// </summary>
    MountedPilot
}

public enum EffectTurnCheckKind
{
    /// <summary>未指定（ターン判定をスキップ）。</summary>
    Unset = -1,
    /// <summary>現在がソースカードのオーナー側のターン。</summary>
    OwnerTurn = 0,
    /// <summary>現在が相手側のターン。</summary>
    NotOwnerTurn = 1
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

    [Tooltip("Unset ならターン判定しない。OwnerTurn/NotOwnerTurn を指定した場合のみ判定する。")]
    public EffectTurnCheckKind turnCheck = EffectTurnCheckKind.Unset;

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

    [Tooltip("MountedPilot のみ: パイロットカード ID（0 なら ID 条件なし）。")]
    public int pilotCardId;

    [Tooltip("MountedPilot のみ: 搭乗パイロットの実効レベルと compareOp で比較。compareValue=0 かつ pilotCardId/feature も無い場合はレベル判定なし。")]
    public int pilotLevelThreshold;
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

    [Tooltip("（未使用）AddShieldToHand はシールドゾーン先頭の実カードを手札へ移す。")]
    public int shieldTokenCardId;

    [Tooltip("カウント対象ゾーンに source がいる場合、source を数から除外。")]
    public bool valueCountExcludeSource;

    [Tooltip("Buff/Debuff 等の対象を、この Feature を持つユニットに限定。未設定なら target のみで解決。")]
    public CardFeatureData targetFeature;

    [Tooltip("JSON 用。targetFeature 未設定時に ID で解決（0=未指定）。")]
    public int targetFeatureId;

    [Tooltip("Bounce / Rest 等：対象ユニットをこのステータス（実効値）で絞り込む。Unset=条件なし。")]
    public EffectTargetUnitFilterStat targetUnitFilterStat = EffectTargetUnitFilterStat.Unset;

    [Tooltip("targetUnitFilterStat 時の比較（例: LessOrEqual + 4 で Lv4以下）。")]
    public EffectCompareOperator targetUnitStatCompareOp = EffectCompareOperator.LessOrEqual;

    [Tooltip("targetUnitFilterStat 時の比較値。")]
    public int targetUnitStatCompareValue;

    [HideInInspector]
    [Tooltip("旧フィールド。targetUnitFilterStat が Unset のとき Level 条件として読み替え。")]
    public bool filterTargetUnitLevel;
}

/// <summary><see cref="EffectData"/> の対象 Feature 解決。</summary>
public static class EffectDataExtensions
{
    public static CardFeatureData GetTargetFeature(this EffectData effect)
    {
        if (effect == null)
        {
            return null;
        }

        if (effect.targetFeature != null)
        {
            return effect.targetFeature;
        }

        if (effect.targetFeatureId > 0)
        {
            CardFeatureRegistry.EnsureLoaded();
            return CardFeatureRegistry.GetById(effect.targetFeatureId);
        }

        return null;
    }

    public static bool HasTargetFeatureFilter(this EffectData effect)
    {
        return effect != null && (effect.targetFeature != null || effect.targetFeatureId > 0);
    }

    public static EffectTargetUnitFilterStat GetTargetUnitFilterStat(this EffectData effect)
    {
        if (effect == null)
        {
            return EffectTargetUnitFilterStat.Unset;
        }

        if (effect.targetUnitFilterStat != EffectTargetUnitFilterStat.Unset)
        {
            return effect.targetUnitFilterStat;
        }

        if (effect.filterTargetUnitLevel)
        {
            return EffectTargetUnitFilterStat.Level;
        }

        return EffectTargetUnitFilterStat.Unset;
    }

    public static bool HasTargetUnitStatFilter(this EffectData effect)
    {
        return effect != null && effect.GetTargetUnitFilterStat() != EffectTargetUnitFilterStat.Unset;
    }

    public static bool HasTargetUnitFilter(this EffectData effect)
    {
        return effect != null && (effect.HasTargetFeatureFilter() || effect.HasTargetUnitStatFilter());
    }

    public static int GetTargetUnitFilterStatValue(CardController unit, EffectTargetUnitFilterStat stat)
    {
        if (unit == null)
        {
            return 0;
        }

        switch (stat)
        {
            case EffectTargetUnitFilterStat.AP:
                return unit.CurrentPower;
            case EffectTargetUnitFilterStat.HP:
                return unit.CurrentHp;
            case EffectTargetUnitFilterStat.Cost:
                return unit.CurrentCost;
            case EffectTargetUnitFilterStat.Level:
                return unit.CurrentLevel;
            default:
                return 0;
        }
    }

    /// <summary>バトルゾーンのユニットが対象フィルタ（Feature / ステータス）を満たすか。</summary>
    public static bool MatchesTargetUnitFilter(this EffectData effect, CardController unit)
    {
        if (effect == null || unit == null || unit.Data == null || unit.Data.type != Type.Unit)
        {
            return false;
        }

        CardFeatureData feature = effect.GetTargetFeature();
        if (feature != null && !unit.Data.HasFeature(feature))
        {
            return false;
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter != EffectTargetUnitFilterStat.Unset
            && !EffectCompareHelper.Compare(
                GetTargetUnitFilterStatValue(unit, statFilter),
                effect.targetUnitStatCompareValue,
                effect.targetUnitStatCompareOp))
        {
            return false;
        }

        return true;
    }

    public static string FormatTargetUnitFilterStatLabel(EffectTargetUnitFilterStat stat)
    {
        switch (stat)
        {
            case EffectTargetUnitFilterStat.AP:
                return "AP";
            case EffectTargetUnitFilterStat.HP:
                return "HP";
            case EffectTargetUnitFilterStat.Cost:
                return "Cost";
            case EffectTargetUnitFilterStat.Level:
                return "Lv";
            default:
                return string.Empty;
        }
    }

    public static string FormatTargetUnitFilterDescription(this EffectData effect)
    {
        if (effect == null || !effect.HasTargetUnitFilter())
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        CardFeatureData feature = effect.GetTargetFeature();
        if (feature != null)
        {
            sb.Append(feature.displayName);
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter != EffectTargetUnitFilterStat.Unset)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(FormatTargetUnitFilterStatLabel(statFilter))
                .Append(FormatCompareOpSymbol(effect.targetUnitStatCompareOp))
                .Append(effect.targetUnitStatCompareValue);
        }

        return sb.ToString();
    }

    private static string FormatCompareOpSymbol(EffectCompareOperator op)
    {
        switch (op)
        {
            case EffectCompareOperator.GreaterOrEqual:
                return "≥";
            case EffectCompareOperator.Greater:
                return ">";
            case EffectCompareOperator.Equal:
                return "=";
            case EffectCompareOperator.LessOrEqual:
                return "≤";
            case EffectCompareOperator.Less:
                return "<";
            default:
                return "?";
        }
    }

    public static string FormatEffectSelectionSummary(this EffectData effect)
    {
        if (effect == null)
        {
            return string.Empty;
        }

        string filter = effect.FormatTargetUnitFilterDescription();
        if (string.IsNullOrEmpty(filter))
        {
            return $"{effect.type} / {effect.target} / 値:{effect.value}";
        }

        return $"{effect.type} / {effect.target} / 値:{effect.value} / 条件:{filter}";
    }
}

[Serializable]
public class TimedEffectData
{
    public EffectTiming timing;

    [Tooltip("空なら条件なし（常に発動）。1件以上はすべて満たすと発動（AND）。")]
    public List<EffectActivationCondition> activationConditions = new List<EffectActivationCondition>();

    [Tooltip("設定時は named_effect_master.json のプリセットを使用。空なら effects を使用。")]
    public string effectsName;

    [Tooltip("effectsName が空のときのインライン効果。effectsName 設定時は参照されない。")]
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
        IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
        if (resolved.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < resolved.Count; i++)
        {
            EffectData e = resolved[i];
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

    /// <summary>配備時（OnPlayed）に解決するブロック。手札条件付きパッシブ専用は除外。</summary>
    public static bool IsOnFieldPlayedResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnPlayed || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }

    /// <summary>パイロット搭乗時（OnPilotMounted）に解決するブロック。</summary>
    public static bool IsOnPilotMountedResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnPilotMounted || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }
}

