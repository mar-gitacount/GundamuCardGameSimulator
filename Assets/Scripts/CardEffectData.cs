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
    OnDestroyed,    // ユニット／カード破壊時（OnUnitDestroyed と同一）
    OnEndOfGame,    // ゲーム終了時
    OnEnemyAttack,  // 敵が攻撃してきた時（防御リアクション用）
    OnMain,         // メインフェイズ中・自分のターンでいつでも実行可能
    OnHandAuto,     // 手札に入った時に自動発動（操作不要）
    OnRest,         // ユニットが REST になった時
    OnPilotMounted, // パイロットをユニットに搭乗した時（ユニット・パイロット双方の timedEffects が対象）
    /// <summary>OnDestroyed の別名（ユニット破壊時）。Inspector / JSON どちらでも指定可。</summary>
    OnUnitDestroyed = OnDestroyed,
    /// <summary>このカードが敵ユニットを破壊した時（キルしたカード自身の timedEffects のみ発動）。</summary>
    OnEnemyUnitDestroyed = 16,
    /// <summary>Look 効果で山札を見た直後（見た枚の中から手札へ加える等の誘発効果用）。</summary>
    OnLook = 17,
    /// <summary>Link 条件を満たすパイロットがユニットに搭乗した時（OnPilotMounted とは別。任意搭乗では発動しない）。</summary>
    OnLink = 18,
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
    Rest,
    /// <summary>高機動。攻撃時に敵ブロッカーを無視し、ブロックフェイズをスキップして OnAction へ進む。</summary>
    HighMobility,
    /// <summary>アクティブ攻撃。通常は REST の敵ユニットのみ攻撃可能だが、この効果を持つ攻撃者は ACTIVE な敵ユニットも攻撃できる。Permanent=常時、UntilEndOfTurn/UntilEndOfBattle は OnPlayed 等の解決時に付与。</summary>
    AttackActiveEnemyUnit,
    /// <summary>山札の上から value 枚を見る（山札からは取り出さない）。target で自分／相手の山札を指定。</summary>
    Look,
    /// <summary>直前の Look で見た山札の中から value 枚を手札に加える。OnLook 専用。targetFeature / targetFeatureId 必須。</summary>
    AddToHandFromLooked,
    /// <summary>OnLook 専用。手札に加えなかった見た枚を、見た順のまま山札の上に戻す。</summary>
    ReturnLookedRemainderToDeckTop,
    /// <summary>OnLook 専用。手札に加えなかった見た枚をランダムな順で山札の下に送る。</summary>
    ShuffleLookedRemainderToDeckBottom,
    /// <summary>OnLook 専用。残りの見た枚を「山札の上に戻す」か「ランダムで下に送る」かプレイヤーが選ぶ。</summary>
    ChooseLookedRemainderDisposition,
    /// <summary>対象ユニットを破壊する（トラッシュへ）。value=適用体数上限（0 で対象リスト全員）。</summary>
    Destroy,
    /// <summary>山札の上から value 枚をトラッシュに置く。target で自分／相手の山札（SelfPlayer / EnemyPlayer）。観測カードはチェーンコンテキストに追加。</summary>
    MillTopToTrash
}

public enum TargetType
{
    Self,
    /// <summary>味方バトルゾーンの生存ユニット1体（自身または他味方のいずれか。手動選択で1体のみ）。</summary>
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
        return type == EffectType.Bounce || type == EffectType.Rest || type == EffectType.Destroy;
    }

    /// <summary>対象ユニットの手動選択 UI が必要なタイプ。</summary>
    public static bool RequiresManualUnitSelection(this EffectType type)
    {
        return type == EffectType.Bounce || type == EffectType.Rest || type == EffectType.Destroy;
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
    /// <summary>戦闘ダメージ以外の効果ダメージ量への補正（Buff/Debuff で付与。対象カード自身が受ける効果ダメージのみ）。</summary>
    EffectDamage,
    /// <summary>効果ダメージを完全無効化（Buff で付与。対象カード自身が受ける効果ダメージのみ0）。</summary>
    EffectDamageImmunity
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
    MountedPilot,
    /// <summary>効果の発動元カード（SourceCard）の実効 AP/HP/Lv/Cost を compareOp + compareValue と比較。boardSide は不要。</summary>
    SourceUnitStat,
    /// <summary>指定ゾーン内の生存ユニットのいずれか1体が、実効 AP/HP/Lv/Cost 条件を満たす。</summary>
    UnitStatOnField,
    /// <summary>
    /// 直前チェーンで観測したカード（MillTopToTrash 等）のうち、
    /// features / featureIds のいずれか（OR）を持つ枚数が minimumCount 以上。
    /// </summary>
    ObservedCardHasFeature
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

    [Tooltip("HasFeature / ObservedCardHasFeature 時に参照。未設定なら featureId / features で解決。")]
    public CardFeatureData feature;

    [Tooltip("JSON 用。feature 未設定時に ID で解決（0=未指定）。")]
    public int featureId;

    [Tooltip("HasFeature / ObservedCardHasFeature: 複数 Feature のいずれか（OR）。Inspector 用。")]
    public CardFeatureData[] features;

    [Tooltip("HasFeature / ObservedCardHasFeature: 複数 Feature のいずれか（OR）。JSON 用 ID 配列。")]
    public int[] featureIds;

    [Tooltip("HasFeature: その Feature を持つカードの最低枚数。UnitCountAtLeast: 生存ユニット最低体数。CountUnitsWithLevelAtLeast: レベル条件を満たすユニットの最低体数。ObservedCardHasFeature: 観測カードのうち条件を満たす最低枚数。")]
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

    [Tooltip("SourceUnitStat / UnitStatOnField: 参照する実効ステータス（AP/HP/Cost/Lv）。未指定時は AP。")]
    public EffectTargetUnitFilterStat activationStatTarget = EffectTargetUnitFilterStat.Unset;
}

/// <summary><see cref="EffectActivationCondition"/> の Feature 解決（複数は OR）。</summary>
public static class EffectActivationConditionExtensions
{
    public static IReadOnlyList<CardFeatureData> GetActivationFeatures(this EffectActivationCondition condition)
    {
        List<CardFeatureData> result = new List<CardFeatureData>();
        if (condition == null)
        {
            return result;
        }

        HashSet<int> seenIds = new HashSet<int>();
        void TryAdd(CardFeatureData featureData)
        {
            if (featureData == null || !seenIds.Add(featureData.id))
            {
                return;
            }

            result.Add(featureData);
        }

        TryAdd(condition.feature);
        if (condition.features != null)
        {
            for (int i = 0; i < condition.features.Length; i++)
            {
                TryAdd(condition.features[i]);
            }
        }

        if (condition.featureId > 0)
        {
            CardFeatureRegistry.EnsureLoaded();
            TryAdd(CardFeatureRegistry.GetById(condition.featureId));
        }

        if (condition.featureIds != null)
        {
            CardFeatureRegistry.EnsureLoaded();
            for (int i = 0; i < condition.featureIds.Length; i++)
            {
                int id = condition.featureIds[i];
                if (id > 0)
                {
                    TryAdd(CardFeatureRegistry.GetById(id));
                }
            }
        }

        return result;
    }

    public static bool HasActivationFeatureFilter(this EffectActivationCondition condition)
    {
        return condition != null && condition.GetActivationFeatures().Count > 0;
    }
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

    [Tooltip("複数 Feature のいずれか（OR）。Inspector 用。")]
    public CardFeatureData[] targetFeatures;

    [Tooltip("複数 Feature のいずれか（OR）。JSON 用 ID 配列。targetFeatureId とも併用可。")]
    public int[] targetFeatureIds;

    [Tooltip("Bounce / Rest 等：対象ユニットをこのステータス（実効値）で絞り込む。Unset=条件なし。")]
    public EffectTargetUnitFilterStat targetUnitFilterStat = EffectTargetUnitFilterStat.Unset;

    [Tooltip("targetUnitFilterStat 時の比較（例: LessOrEqual + 4 で Lv4以下）。")]
    public EffectCompareOperator targetUnitStatCompareOp = EffectCompareOperator.LessOrEqual;

    [Tooltip("targetUnitFilterStat 時の比較値。compareTargetStatToSource が true のときは無視し発動元の実効値を使う。")]
    public int targetUnitStatCompareValue;

    [Tooltip("true のとき targetUnitFilterStat（未指定時は AP）を発動元カードの実効値と比較する（例: 敵AP ≤ 自AP）。")]
    public bool compareTargetStatToSource;

    [Tooltip("true のとき effectActivationConditions はチェーン観測カードを参照。観測が空ならこの効果をスキップ。")]
    public bool requireChainObservationContext;

    [Tooltip("この効果のみの発動条件（空なら常に実行）。ObservedCardHasFeature はチェーン観測を参照。")]
    public List<EffectActivationCondition> effectActivationConditions = new List<EffectActivationCondition>();

    [HideInInspector]
    [Tooltip("旧フィールド。targetUnitFilterStat が Unset のとき Level 条件として読み替え。")]
    public bool filterTargetUnitLevel;
}

/// <summary><see cref="EffectData"/> のチェーン条件ヘルパー。</summary>
public static class EffectDataChainExtensions
{
    public static bool HasEffectActivationConditions(this EffectData effect)
    {
        return effect != null
            && effect.effectActivationConditions != null
            && effect.effectActivationConditions.Count > 0;
    }

    public static bool ShouldDeferEffectActivationToRunTime(this EffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (effect.requireChainObservationContext)
        {
            return true;
        }

        return EffectActivationEvaluator.ContainsObservedCardCondition(effect.effectActivationConditions);
    }
}

/// <summary><see cref="EffectData"/> の対象 Feature 解決。</summary>
public static class EffectDataExtensions
{
    public static CardFeatureData GetTargetFeature(this EffectData effect)
    {
        IReadOnlyList<CardFeatureData> features = effect.GetTargetFeatures();
        return features.Count > 0 ? features[0] : null;
    }

    public static IReadOnlyList<CardFeatureData> GetTargetFeatures(this EffectData effect)
    {
        List<CardFeatureData> result = new List<CardFeatureData>();
        if (effect == null)
        {
            return result;
        }

        HashSet<int> seenIds = new HashSet<int>();
        void TryAdd(CardFeatureData feature)
        {
            if (feature == null || !seenIds.Add(feature.id))
            {
                return;
            }

            result.Add(feature);
        }

        TryAdd(effect.targetFeature);
        if (effect.targetFeatures != null)
        {
            for (int i = 0; i < effect.targetFeatures.Length; i++)
            {
                TryAdd(effect.targetFeatures[i]);
            }
        }

        if (effect.targetFeatureId > 0)
        {
            CardFeatureRegistry.EnsureLoaded();
            TryAdd(CardFeatureRegistry.GetById(effect.targetFeatureId));
        }

        if (effect.targetFeatureIds != null)
        {
            CardFeatureRegistry.EnsureLoaded();
            for (int i = 0; i < effect.targetFeatureIds.Length; i++)
            {
                int featureId = effect.targetFeatureIds[i];
                if (featureId > 0)
                {
                    TryAdd(CardFeatureRegistry.GetById(featureId));
                }
            }
        }

        return result;
    }

    public static bool HasTargetFeatureFilter(this EffectData effect)
    {
        return effect != null && effect.GetTargetFeatures().Count > 0;
    }

    public static string FormatTargetFeaturesLabel(this EffectData effect, string separator = "・")
    {
        if (effect == null)
        {
            return string.Empty;
        }

        IReadOnlyList<CardFeatureData> features = effect.GetTargetFeatures();
        if (features.Count == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < features.Count; i++)
        {
            CardFeatureData feature = features[i];
            if (feature == null || string.IsNullOrEmpty(feature.displayName))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(separator);
            }

            sb.Append(feature.displayName);
        }

        return sb.ToString();
    }

    public static bool MatchesTargetFeatureOnCard(this EffectData effect, CardData card)
    {
        if (effect == null || card == null || !effect.HasTargetFeatureFilter())
        {
            return false;
        }

        return card.HasAnyFeature(effect.GetTargetFeatures());
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
        return effect != null
            && (effect.HasTargetFeatureFilter()
                || effect.HasTargetUnitStatFilter()
                || effect.compareTargetStatToSource);
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
    public static bool MatchesTargetUnitFilter(
        this EffectData effect,
        CardController unit,
        CardController sourceCard = null)
    {
        if (effect == null || unit == null || unit.Data == null || unit.Data.type != Type.Unit)
        {
            return false;
        }

        if (effect.HasTargetFeatureFilter() && !effect.MatchesTargetFeatureOnCard(unit.Data))
        {
            return false;
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter == EffectTargetUnitFilterStat.Unset && effect.compareTargetStatToSource)
        {
            statFilter = EffectTargetUnitFilterStat.AP;
        }

        if (statFilter != EffectTargetUnitFilterStat.Unset)
        {
            int compareValue = effect.targetUnitStatCompareValue;
            if (effect.compareTargetStatToSource)
            {
                if (sourceCard == null)
                {
                    return false;
                }

                compareValue = GetTargetUnitFilterStatValue(sourceCard, statFilter);
            }

            if (!EffectCompareHelper.Compare(
                GetTargetUnitFilterStatValue(unit, statFilter),
                compareValue,
                effect.targetUnitStatCompareOp))
            {
                return false;
            }
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
        string featureLabel = effect.FormatTargetFeaturesLabel("/");
        if (!string.IsNullOrEmpty(featureLabel))
        {
            sb.Append(featureLabel);
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter == EffectTargetUnitFilterStat.Unset && effect.compareTargetStatToSource)
        {
            statFilter = EffectTargetUnitFilterStat.AP;
        }

        if (statFilter != EffectTargetUnitFilterStat.Unset)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(FormatTargetUnitFilterStatLabel(statFilter));
            if (effect.compareTargetStatToSource)
            {
                sb.Append(FormatCompareOpSymbol(effect.targetUnitStatCompareOp))
                    .Append("自")
                    .Append(FormatTargetUnitFilterStatLabel(statFilter));
            }
            else
            {
                sb.Append(FormatCompareOpSymbol(effect.targetUnitStatCompareOp))
                    .Append(effect.targetUnitStatCompareValue);
            }
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

    /// <summary>Look 直後に見た山札カードが effect の targetFeature（複数可・OR）のいずれかと一致するか。</summary>
    public static bool MatchesLookedCardDataFeatureFilter(this EffectData effect, CardData card)
    {
        return effect.MatchesTargetFeatureOnCard(card);
    }
}

[Serializable]
public class TimedEffectData
{
    public EffectTiming timing;

    [Tooltip("空なら条件なし（常に発動）。1件以上はすべて満たすと発動（AND）。")]
    public List<EffectActivationCondition> activationConditions = new List<EffectActivationCondition>();

    [Tooltip("true のとき activationConditions はチェーン観測カードを参照。観測が空ならブロック全体をスキップ。")]
    public bool requireChainObservationContext;

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

    public static bool ShouldDeferActivationToRunTime(this TimedEffectData timed)
    {
        if (timed == null)
        {
            return false;
        }

        if (timed.requireChainObservationContext)
        {
            return true;
        }

        return EffectActivationEvaluator.ContainsObservedCardCondition(timed.activationConditions);
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

    /// <summary>Link 搭乗時（OnLink）に解決するブロック。</summary>
    public static bool IsOnLinkResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnLink || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }

    /// <summary>破壊時（OnDestroyed / OnUnitDestroyed）に解決するブロック。</summary>
    public static bool IsOnUnitDestroyedResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        if (timed.timing != EffectTiming.OnDestroyed && timed.timing != EffectTiming.OnUnitDestroyed)
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }

    /// <summary>このカードが敵ユニットを破壊した時（OnEnemyUnitDestroyed）に解決するブロック。</summary>
    public static bool IsOnEnemyUnitDestroyedResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnEnemyUnitDestroyed || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }

    /// <summary>Look 直後（OnLook）に解決するブロック。</summary>
    public static bool IsOnLookResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnLook || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }
}

