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
    /// <summary>このカードが敵ユニットを破壊した時（キル元ユニット本体＋搭乗パイロットの timedEffects）。</summary>
    OnEnemyUnitDestroyed = 16,
    /// <summary>Look 効果で山札を見た直後（見た枚の中から手札へ加える等の誘発効果用）。</summary>
    OnLook = 17,
    /// <summary>Link 条件を満たすパイロットがユニットに搭乗した時（OnPilotMounted とは別。任意搭乗では発動しない）。</summary>
    OnLink = 18,
    /// <summary>MarkObservedUnit で登録したユニットが監視イベントを起こした時（効果源カードの timedEffects）。</summary>
    OnObservedUnitTrigger = 19,
    /// <summary>
    /// 自分のユニットがカード効果で破壊された時。EffectType.Destroy と効果ダメージ破壊の両方を含む。
    /// 破壊した効果は自分・相手どちらのものでもよいが、破壊されたユニットは自分のものに限る。
    /// 戦闘（攻撃）ダメージ破壊は含まない。盤面にいるこの効果持ちが監視する（破壊された自身も含む）。
    /// </summary>
    OnUnitDestroyedByOwnerEffect = 20,
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
    /// <summary>
    /// ベースゾーンへ配備（value=枚数）。
    /// OnBurst 時は破壊された Base カード自身を配備。
    /// deployUnitSource=Trash のときはトラッシュから Base を選んで配備（targetFeature / filterByTargetCardType で絞り込み可）。
    /// </summary>
    DeployBase,
    /// <summary>バトルゾーンのユニットを手札に戻す（バウンス）。value=適用体数上限（0 で対象リスト全員）。</summary>
    Bounce,
    /// <summary>対象ユニットを REST にする。value=適用体数上限（0 で対象リスト全員）。</summary>
    Rest,
    /// <summary>高機動。攻撃時に敵ブロッカーを無視し、ブロックフェイズをスキップして OnAction へ進む。</summary>
    HighMobility,
    /// <summary>
    /// アクティブ攻撃。通常は REST の敵ユニットのみ攻撃可能だが、この効果を持つ攻撃者は ACTIVE な敵ユニットも攻撃できる。
    /// Permanent=カード常時。UntilEndOfTurn/UntilEndOfBattle は解決時に付与。
    /// target=AllyUnit/AllyOtherUnit のときは味方を手動選択して付与（targetFeature で候補絞り込み）。
    /// targetUnitFilterStat 等は「攻撃できる敵」の条件（例: AP≤4）。付与候補の絞り込みには使わない。
    /// </summary>
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
    MillTopToTrash,
    /// <summary>山札の上から value 枚を除外（EXILE）ゾーンに置く。target は MillTopToTrash と同様。観測カードはチェーンコンテキストに追加。</summary>
    ExileFromDeck,
    /// <summary>トラッシュから value 枚を除外（EXILE）ゾーンに置く。target で自分／相手のトラッシュ（SelfPlayer / EnemyPlayer）。filterByTargetCardType で種類絞り込み可。</summary>
    ExileFromTrash,
    /// <summary>
    /// バトルゾーンへユニットを配備する。deployUnitSource で出所（Token/Hand/Trash）を指定。
    /// value=配備体数（0 以下は 1 体扱い）。target で配備先プレイヤー（SelfPlayer / EnemyPlayer）。
    /// </summary>
    DeployUnit,
    /// <summary>
    /// 味方ユニットに AttackFlg=True を付与する（手動選択可）。
    /// target / targetFeature(s) / filterByTargetCardType で候補を絞り込み。
    /// grantAttackFlagOnlyIfOff=true のとき AttackFlg=False のユニットのみ UI 表示・対象。
    /// value=付与体数上限（0 以下は 1）。
    /// </summary>
    GrantAttackFlag,
    /// <summary>
    /// 手札から value 枚をトラッシュへ捨てる（SelfPlayer / オーナー手札）。
    /// revealDiscardedToOpponent=true で相手に公開。1枚以上は手札選択 UI。
    /// </summary>
    DiscardFromHand,
    /// <summary>REST のユニットを ACTIVE にする（レスト解除）。value=体数上限（0 で対象全員）。</summary>
    Activate,
    /// <summary>対象ユニットがそのターン（UntilEndOfTurn）相手プレイヤー／シールドへ直接攻撃できない。</summary>
    NotDirectAttack,
    /// <summary>OnBurst 時は破壊公開されたカード自身をオーナーの手札へ加える。OnDestroyed 時はトラッシュ経由で自身を手札へ戻す。</summary>
    AddSelfToHand,
    /// <summary>
    /// 手動選択した味方ユニットを監視登録する。observedUnitTriggerKind で監視する行動を指定。
    /// 報酬効果は同一カードの OnObservedUnitTrigger ブロックに記述する。
    /// </summary>
    MarkObservedUnit,
    /// <summary>対象ユニットをオーナーの山札の一番下へ戻す。トークンは消滅。value=体数上限（0 で対象全員）。</summary>
    ReturnUnitToDeckBottom,
    /// <summary>
    /// エフェクトバトル。発動元ユニットが選択した敵ユニットとダメージ交換する。
    /// 攻撃宣言・レスト・攻撃権消費・ブロックは行わない（ダメージステップ相当）。
    /// </summary>
    EffectBattle,
    /// <summary>
    /// 突破（Breach）。敵ユニットを破壊したとき、相手シールドエリアの先頭カードへ value ダメージ
    /// （配備ベース優先、無ければシールド1枚。余剰は溢れない）。
    /// </summary>
    Breach,
    /// <summary>
    /// 対象ユニットの HP を value 回復する（ダメージカウンタ除去。上限超過分は切り捨て）。
    /// 敵ユニット撃破時（戦闘／エフェクトバトル等）の回復にも使用する。
    /// </summary>
    RecoverHp,
    /// <summary>
    /// EXリソースを value 枚追加する（0 以下は 1 枚扱い）。
    /// target で SelfPlayer / EnemyPlayer を指定（未指定・ユニット対象は効果オーナー側）。
    /// 条件付き発動は timed / effect の activationConditions（例: TrashHasFeature）と組み合わせる。
    /// </summary>
    AddExResource,
    /// <summary>
    /// 同一チェーン中に山札からトラッシュへ送ったカード一覧から、条件に合うカードを選んで手札へ加える。
    /// 直前の MillTopToTrash と組み合わせて使う。対象は「その効果で送ったカード」のみ。
    /// </summary>
    AddObservedToHandFromTrash,
    /// <summary>
    /// 敵ユニットが攻撃するとき、可能ならこのユニットを攻撃対象に強制する（挑発）。
    /// Permanent。搭乗パイロット定義ならホストへ適用。
    /// timed.activationConditions でホスト側条件（例: リンク中・REST）、
    /// effectActivationConditions で攻撃者側条件（例: 非リンクユニット）を指定する。
    /// </summary>
    ForceEnemyAttackTarget,
    /// <summary>OnBurst 時は破壊公開されたカード自身をオーナーのシールドゾーンへ配備する。</summary>
    DeploySelfToShield,
    /// <summary>
    /// 効果破壊監視フラグをソースカードに立てる（Axis 等）。
    /// OnUnitDestroyedByOwnerEffect と組み合わせ、以降の OnMain 条件に使う。
    /// </summary>
    ArmOwnerEffectDestroyFlag,
    /// <summary>
    /// 複数の効果枝から1つを選んで発動する（XOR）。
    /// choiceBranches に日本語／英語ラベルと各枝の effects / effectsName を定義する。
    /// </summary>
    ChooseOne,
    /// <summary>
    /// リソースを value 個「レストで置く」（Place rested Resource）。
    /// level を増やし、追加分はレストのため当ターンの利用可能 resource には加えない。
    /// 次ターン開始のリフレッシュで resource = level となる。0 以下は 1 扱い。
    /// </summary>
    RestResource,
    /// <summary>
    /// トラッシュから value 枚を手札へ加える。
    /// filterByTargetCardType / targetFeature / targetUnitFilterStat（CardData 基準）で候補を絞る。
    /// </summary>
    AddFromTrashToHand,
    /// <summary>
    /// 【メイン】解決後専用。トラッシュに置かれた発動元カード自身を、
    /// targetFeature 等で絞った味方ユニットへパイロットとしてセットしてもよい（optionalPlayerConfirm）。
    /// OnMain チェーン中はスキップし、コマンドがトラッシュへ送られたあとに解決する。
    /// </summary>
    MountSelfFromTrashAsPilot,
    /// <summary>
    /// 搭乗中のカード（パイロット／コマンドパイロット）の【メイン】(OnMain) を、
    /// コスト支払いなし・セット維持のまま発動する。【リンク中】【アタック時】等で使用。
    /// MountSelfFromTrashAsPilot はスキップする。
    /// </summary>
    ActivateMountedCardOnMain
}

/// <summary><see cref="EffectType.ChooseOne"/> の選択肢1本。</summary>
[Serializable]
public class EffectChoiceBranch
{
    [Tooltip("選択肢の日本語文言（UI 表示）。")]
    public string labelJa;

    [Tooltip("選択肢の英語文言（UI 表示）。")]
    public string labelEn;

    [Tooltip("設定時は named_effect_master.json のプリセット。空なら effects を使用。")]
    public string effectsName;

    [Tooltip("effectsName が空のときのインライン効果。")]
    public EffectData[] effects = Array.Empty<EffectData>();
}

/// <summary><see cref="EffectChoiceBranch"/> の解決ヘルパー。</summary>
public static class EffectChoiceBranchExtensions
{
    public static IReadOnlyList<EffectData> GetResolvedEffects(this EffectChoiceBranch branch)
    {
        if (branch == null)
        {
            return Array.Empty<EffectData>();
        }

        if (!string.IsNullOrWhiteSpace(branch.effectsName))
        {
            IReadOnlyList<EffectData> named = NamedEffectSetRegistry.GetEffects(branch.effectsName.Trim());
            if (named != null && named.Count > 0)
            {
                return named;
            }
        }

        if (branch.effects == null || branch.effects.Length == 0)
        {
            return Array.Empty<EffectData>();
        }

        List<EffectData> list = new List<EffectData>(branch.effects.Length);
        for (int i = 0; i < branch.effects.Length; i++)
        {
            if (branch.effects[i] != null)
            {
                list.Add(branch.effects[i]);
            }
        }

        return list;
    }

    public static string GetDisplayLabelJa(this EffectChoiceBranch branch)
    {
        if (branch == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(branch.labelJa) ? branch.labelEn : branch.labelJa;
    }

    public static string GetDisplayLabelEn(this EffectChoiceBranch branch)
    {
        if (branch == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(branch.labelEn) ? branch.labelJa : branch.labelEn;
    }
}

/// <summary><see cref="EffectType.DeployUnit"/> の配備元ゾーン。</summary>
public enum DeployUnitSource
{
    /// <summary>カード ID から新規生成（ユニットトークン等）。deployCardId 必須。</summary>
    Token = 0,
    /// <summary>手札のユニットをバトルゾーンへ配備（リソース支払いなし）。</summary>
    Hand = 1,
    /// <summary>トラッシュのユニットをバトルゾーンへ配備（トラッシュから除去）。</summary>
    Trash = 2,
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
    AllyOtherUnit,
    /// <summary>味方バトルゾーンの生存ユニットトークン。</summary>
    TokenUnit,
    /// <summary>相手バトルゾーンの生存ユニットトークン。</summary>
    EnemyTokenUnit
}

/// <summary><see cref="EffectType"/> のヘルパー。</summary>
public static class EffectTypeExtensions
{
    /// <summary>value が適用体数上限として使われ、効果量 0 でも解決するタイプ。</summary>
    public static bool UsesTargetCountValue(this EffectType type)
    {
        return type == EffectType.Bounce
            || type == EffectType.Rest
            || type == EffectType.Activate
            || type == EffectType.Destroy
            || type == EffectType.ReturnUnitToDeckBottom;
    }

    /// <summary>対象ユニットの手動選択 UI が必要なタイプ。</summary>
    public static bool RequiresManualUnitSelection(this EffectType type)
    {
        return type == EffectType.Bounce
            || type == EffectType.Rest
            || type == EffectType.Activate
            || type == EffectType.Destroy
            || type == EffectType.GrantAttackFlag
            || type == EffectType.MarkObservedUnit
            || type == EffectType.EffectBattle;
    }

    /// <summary>手札から対象を選ぶ UI が必要なタイプ。</summary>
    public static bool RequiresManualHandSelection(this EffectType type)
    {
        return type == EffectType.DiscardFromHand;
    }
}

/// <summary><see cref="TargetType"/> の判定ヘルパー。</summary>
public static class EffectTargetTypeExtensions
{
    public static bool IsOpponentUnitTarget(this TargetType targetType)
    {
        return targetType == TargetType.EnemyUnit
            || targetType == TargetType.EnemyAllUnits
            || targetType == TargetType.RestEnemyUnit
            || targetType == TargetType.EnemyTokenUnit;
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

/// <summary>MarkObservedUnit で監視するユニットの行動種別。報酬は OnObservedUnitTrigger と observedUnitTriggerKind で対応付ける。</summary>
public enum ObservedUnitTriggerKind
{
    /// <summary>未指定（Mark 時は EnemyUnitDestroyed 扱い）。</summary>
    Unset = -1,
    /// <summary>監視ユニットが敵ユニットを破壊した時。</summary>
    EnemyUnitDestroyed = 0,
    /// <summary>監視ユニットがシールドを破壊した時。</summary>
    ShieldDestroyed = 1,
    /// <summary>監視ユニットが配備ベースを破壊した時。</summary>
    BaseDestroyed = 2,
    /// <summary>監視ユニットが EX ベースを 0 にした時。</summary>
    ExBaseDestroyed = 3,
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
    SelectMultipleEnemyUnits,
    /// <summary>直前の手動選択で選んだユニットに効果を適用（チェーン2段目以降用）。</summary>
    UsePriorChainPickedTarget = 4,
    /// <summary>対象候補から複数体を選択する（味方/敵どちらにも使用）。</summary>
    SelectMultipleUnits = 5
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
            || mode == EffectSelectionMode.SelectMultipleEnemyUnits
            || mode == EffectSelectionMode.SelectMultipleUnits;
    }

    /// <summary>複数体を選んで OK で確定するモード。</summary>
    public static bool IsMultipleUnitPickMode(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.SelectMultipleEnemyUnits
            || mode == EffectSelectionMode.SelectMultipleUnits;
    }

    /// <summary>攻撃対象ユニットにだけ効果を当てるモード。</summary>
    public static bool IsAttackedTargetOnlyMode(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.AttackedTargetOnly;
    }

    public static bool IsUsePriorChainPickedTargetMode(this EffectSelectionMode mode)
    {
        return mode == EffectSelectionMode.UsePriorChainPickedTarget;
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
    EffectDamageImmunity,
    /// <summary>
    /// 受けるダメージ軽減（Buff で付与。value=軽減量）。
    /// 戦闘ダメージ・効果ダメージの両方に適用（ApplyDamage 時）。UntilEndOfTurn 等と組み合わせる。
    /// </summary>
    IncomingDamageReduction
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
    OpponentHand,
    /// <summary>ソースオーナーのトラッシュ。</summary>
    OwnerTrash,
    /// <summary>相手側のトラッシュ。</summary>
    OpponentTrash
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
    ObservedCardHasFeature,
    /// <summary>
    /// 直前チェーンで観測したカードのうち、observedCardType と一致する枚数が minimumCount 以上。
    /// 例: 除外したカードがパイロットならダメージ、など。
    /// </summary>
    ObservedCardIsType,
    /// <summary>
    /// 指定トラッシュ（boardSide 未指定時はオーナーのトラッシュ）に、
    /// observedCardType と一致するカードが minimumCount 枚以上ある。
    /// </summary>
    TrashHasCardType,
    /// <summary>
    /// 指定トラッシュに trashCardId（未設定時は pilotCardId）のカードが minimumCount 枚以上ある。
    /// </summary>
    TrashHasCardId,
    /// <summary>
    /// 指定トラッシュに trashCardId のカードが minimumCount 枚未満（0 枚＝未存在）。
    /// </summary>
    TrashLacksCardId,
    /// <summary>
    /// 指定ゾーン（OwnerBattleZone 等）の生存ユニット体数を unitCountThreshold と unitCountCompareOp で比較。
    /// 例: 0 体 → Equal + threshold 0、2 体以上 → GreaterOrEqual + threshold 2。
    /// </summary>
    CompareFieldUnitCount,
    /// <summary>
    /// 攻撃ユニット（OnAttack 時は搭乗ホスト優先）がダメージを受けている（現在 HP &lt; 最大 HP）。
    /// </summary>
    SourceUnitDamaged,
    /// <summary>
    /// 同一チェーン内の直前効果で、少なくとも1体（またはプレイヤー領域）に実ダメージが入った。
    /// 相手へのダメージをフックにした条件付き自傷などに使用。
    /// </summary>
    PriorChainDealtDamage,
    /// <summary>
    /// 指定トラッシュに、features / featureIds のいずれか（OR）を持つカードが minimumCount 枚以上ある。
    /// </summary>
    TrashHasFeature,
    /// <summary>
    /// このカードを破壊したカード（DestroyedBy）が、features / featureIds のいずれか（OR）を持つ。
    /// 破壊元ユニットに搭乗パイロットがいる場合、パイロット側の Feature でも可。
    /// destroyedByOwnerRelation で味方のみ / 敵のみ / 両方を指定可能。
    /// </summary>
    DestroyedByHasFeature,
    /// <summary>
    /// ソースユニットがリンク中（搭乗パイロットが Link 条件を満たす）。
    /// SourceCard がパイロットなら MountedUnit / MountHostUnit を参照。
    /// </summary>
    SourceUnitIsLinked,
    /// <summary>
    /// ソースユニットがリンクしていない（搭乗なし、または Link 条件を満たさない）。
    /// 攻撃者フィルタ（例: 「リンクユニット以外」）に再利用する。
    /// </summary>
    SourceUnitIsNotLinked,
    /// <summary>ソースユニットが REST 状態。</summary>
    SourceUnitIsRest,
    /// <summary>
    /// ソースカードに「自分のユニットが自分の効果で破壊された」監視フラグが立っている。
    /// Axis 等: 配備前の同種破壊も対戦履歴経由でアームされ、この条件を満たす。
    /// </summary>
    SourceHasOwnerEffectDestroyArmed,
    /// <summary>ソース（ユニットまたは Base）が ACTIVE（非 REST）。</summary>
    SourceUnitIsNotRest,
    /// <summary>
    /// 破壊した効果のオーナーがソース（監視カード）と同じ陣営（自分の効果で破壊）。
    /// EffectActivationContext.DestroyingCardOwner を参照。
    /// </summary>
    DestroyingOwnerIsAlly,
    /// <summary>
    /// ソースカード（ユニット等）が features / featureIds のいずれか（OR）を持つ。
    /// パイロットの「このユニットが〔特徴〕の間」条件（搭乗ホストを Source にして評価）に使用。
    /// </summary>
    SourceHasFeature,
    /// <summary>
    /// ユニット戦闘ダメージ（ユニット対ユニット／ブロック戦闘）で破壊したときのみ。
    /// EffectActivationContext.DestroyedByBattleDamage を参照。
    /// </summary>
    DestroyedByBattleDamage
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

/// <summary>
/// <see cref="EffectActivationCheckKind.DestroyedByHasFeature"/> で、
/// 破壊者カードがソース（破壊された側）から見て味方か敵か。
/// </summary>
public enum EffectDestroyedByOwnerRelation
{
    /// <summary>味方・敵どちらでも可。</summary>
    Either = 0,
    /// <summary>破壊されたカードと同じオーナーのカードに破壊されたときのみ（味方効果など）。</summary>
    Ally = 1,
    /// <summary>相手オーナーのカードに破壊されたときのみ（敵効果など）。</summary>
    Enemy = 2
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

    [Tooltip(
        "HasFeature / ObservedCardHasFeature / TrashHasFeature / DestroyedByHasFeature 時に参照。"
        + "未設定なら featureId / features で解決。")]
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

    [Tooltip("CountUnitsAtExactLevel / CompareFieldUnitCount: 数えた体数と比較する閾値（例: LV6 が 0 体なら 0、ユニット 2 体以上なら 2）。")]
    public int unitCountThreshold;

    [Tooltip("MountedPilot のみ: パイロットカード ID（0 なら ID 条件なし）。")]
    public int pilotCardId;

    [Tooltip("TrashHasCardId / TrashLacksCardId: トラッシュ内で探すカード ID（0 かつ pilotCardId も 0 なら発動元カード ID）。")]
    public int trashCardId;

    [Tooltip("MountedPilot のみ: 搭乗パイロットの実効レベルと compareOp で比較。compareValue=0 かつ pilotCardId/feature も無い場合はレベル判定なし。")]
    public int pilotLevelThreshold;

    [Tooltip("SourceUnitStat / UnitStatOnField: 参照する実効ステータス（AP/HP/Cost/Lv）。未指定時は AP。")]
    public EffectTargetUnitFilterStat activationStatTarget = EffectTargetUnitFilterStat.Unset;

    [Tooltip("ObservedCardIsType: 観測カードの種類（Unit/Pilot/Command 等）。")]
    public Type observedCardType = Type.Unit;

    [Tooltip(
        "DestroyedByHasFeature 専用。"
        + "Ally=味方（同一オーナー）のカードに破壊されたときのみ / "
        + "Enemy=敵のカードに破壊されたときのみ / "
        + "Either=どちらでも可。")]
    public EffectDestroyedByOwnerRelation destroyedByOwnerRelation = EffectDestroyedByOwnerRelation.Either;
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

    [Tooltip("ExileFromTrash / バトルゾーン対象選択: true のとき targetCardType に一致するカードのみ対象。")]
    public bool filterByTargetCardType;

    [Tooltip("filterByTargetCardType: 対象とするカード種類（例: Pilot / UnitToken）。")]
    public Type targetCardType = Type.Pilot;

    [Tooltip("DeployUnit / DeployBase: 配備するカードの出所（Token=トークン生成 / Hand / Trash）。DeployBase は Trash 時にトラッシュから Base を配備。")]
    public DeployUnitSource deployUnitSource = DeployUnitSource.Token;

    [Tooltip("DeployUnit + Token: 配備するカード ID。Hand/Trash で特定 ID に限定する場合も使用。")]
    public int deployCardId;

    [Tooltip("DeployUnit + Hand/Trash: deployCardId で候補を絞る。false ならユニット系なら何でも可。")]
    public bool filterByDeployCardId;

    [Tooltip("DeployUnit + Hand/Trash: targetFeature(s) で候補を絞る（OR）。未設定なら種類のみ。")]
    public bool filterDeployCandidateByFeature;

    [Tooltip("DeployUnit: 配備したユニットの OnPlayed を発動するか（トークンは通常 false）。")]
    public bool deployUnitTriggerOnPlayed;

    [Tooltip("GrantAttackFlag: true のとき AttackFlg=False のユニットのみ候補・UI 表示（既に ON のユニットは選べない）。")]
    public bool grantAttackFlagOnlyIfOff = true;

    [Tooltip(
        "DiscardFromHand / AddToHandFromLooked: true のとき対象カードを相手に公開"
        + "（オンラインは OK まで進行停止）。")]
    public bool revealDiscardedToOpponent;

    [Tooltip("Draw: true のとき引いたカードをプレイヤーに公開してから次の効果へ進む。")]
    public bool revealDrawnToPlayer;

    [Tooltip("Activate 等: true のとき isBlocker の味方ユニットのみ候補。")]
    public bool filterTargetIsBlocker;

    [Tooltip("SelectMultipleUnits 等: 最低選択体数（0 なら複数選択時は 1）。")]
    public int selectMinCount;

    [Tooltip("SelectMultipleUnits 等: 最大選択体数（0 なら上限なし）。")]
    public int selectMaxCount;

    [Tooltip("MarkObservedUnit: 監視する行動種別。Unset なら EnemyUnitDestroyed（シールド／配備ベース／EXベース破壊でも同報酬が発動）。")]
    public ObservedUnitTriggerKind observedUnitTriggerKind = ObservedUnitTriggerKind.Unset;

    [Tooltip("true のとき対象候補から targetUnitFilterStat（未指定時は Lv）が最も低いユニット1体を自動選択。")]
    public bool autoSelectLowestUnitStat;

    [Tooltip(
        "true のとき、オーナー墓地に発動元と同 ID のカードが trashRelaxFilterMinCopies 枚以上あると、"
        + "対象ユニットのステータス絞り込み（Lv 等）を無効化する。")]
    public bool relaxTargetUnitStatFilterWhenTrashHasSourceCopies;

    [Tooltip("relaxTargetUnitStatFilterWhenTrashHasSourceCopies 時の必要枚数（0 以下は 2）。")]
    public int trashRelaxFilterMinCopies = 2;

    [Tooltip(
        "ExileFromTrash: true のとき候補が value 枚未満なら除外を行わない（部分除外しない）。"
        + "Nu Gundam のロンド・ベル3枚除外などに使用。")]
    public bool requireExactExileCount;

    [Tooltip(
        "true のとき対象選択の前にプレイヤーへ実行可否を確認する。"
        + "OnAttack の Destroy 等では Cancel で効果全体をスキップし攻撃／アクションステップへ続行。"
        + "通常チェーンでは Cancel なら後続を含まずその効果のみスキップ。")]
    public bool optionalPlayerConfirm;

    [Tooltip(
        "Destroy 等の手動ユニット選択: true のとき効果オーナーではなく相手プレイヤーが対象を選ぶ。"
        + "例: 攻撃側が Destroy(EnemyUnit) を解決し、相手が自分のユニット1体を選んで破壊する。")]
    public bool opponentChoosesTarget;

    [Tooltip("ChooseOne: 選択肢一覧。各枝から1つだけ発動する。")]
    public EffectChoiceBranch[] choiceBranches = Array.Empty<EffectChoiceBranch>();

    [Tooltip("ChooseOne: UI タイトル直下の日本語プロンプト。")]
    public string choicePromptJa;

    [Tooltip("ChooseOne: UI タイトル直下の英語プロンプト。")]
    public string choicePromptEn;
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

    public static bool MatchesTargetFeatureFilter(this EffectData effect, CardData card)
    {
        if (effect == null || !effect.HasTargetFeatureFilter())
        {
            return true;
        }

        return card != null && card.HasAnyFeature(effect.GetTargetFeatures());
    }
    public static bool MatchesTargetCardTypeFilter(this EffectData effect, CardData card)
    {
        if (effect == null || !effect.filterByTargetCardType)
        {
            return true;
        }

        return card != null && CardTypeExtensions.MatchesTypeFilter(effect.targetCardType, card.type);
    }

    /// <summary>
    /// トラッシュ等・カード実体がない候補向け。targetUnitFilterStat を CardData の印刷値で判定する。
    /// </summary>
    public static bool MatchesCardDataStatFilter(this EffectData effect, CardData card)
    {
        if (effect == null || !effect.HasTargetUnitStatFilter())
        {
            return true;
        }

        if (card == null)
        {
            return false;
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter == EffectTargetUnitFilterStat.Unset)
        {
            return true;
        }

        return EffectCompareHelper.Compare(
            GetCardDataFilterStatValue(card, statFilter),
            effect.targetUnitStatCompareValue,
            effect.targetUnitStatCompareOp);
    }

    public static int GetCardDataFilterStatValue(CardData card, EffectTargetUnitFilterStat stat)
    {
        if (card == null)
        {
            return 0;
        }

        switch (stat)
        {
            case EffectTargetUnitFilterStat.AP:
                return card.power;
            case EffectTargetUnitFilterStat.HP:
                return card.hp;
            case EffectTargetUnitFilterStat.Cost:
                return card.cost;
            case EffectTargetUnitFilterStat.Level:
                return card.IsUnitToken() ? 0 : card.level;
            default:
                return 0;
        }
    }

    public static bool IsChooseOneEffect(this EffectData effect)
    {
        return effect != null
            && effect.type == EffectType.ChooseOne
            && effect.choiceBranches != null
            && effect.choiceBranches.Length > 0;
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

    /// <summary>
    /// AttackActiveEnemyUnit の「攻撃できる ACTIVE 敵」条件に使うステータス絞り込みがあるか。
    /// Feature 絞り込みは付与先候補用のため、ここでは含めない。
    /// </summary>
    public static bool HasAttackActiveEnemyTargetStatFilter(this EffectData effect)
    {
        return effect != null
            && (effect.HasTargetUnitStatFilter() || effect.compareTargetStatToSource);
    }

    /// <summary>
    /// AttackActiveEnemyUnit 用。敵ユニットがステータス条件を満たすか（Feature は見ない）。
    /// 条件未設定なら true。
    /// </summary>
    public static bool MatchesAttackActiveEnemyTargetFilter(
        this EffectData effect,
        CardController targetUnit,
        CardController attacker)
    {
        if (effect == null || targetUnit == null || targetUnit.Data == null || !targetUnit.Data.IsUnitLike())
        {
            return false;
        }

        if (!effect.HasAttackActiveEnemyTargetStatFilter())
        {
            return true;
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter == EffectTargetUnitFilterStat.Unset && effect.compareTargetStatToSource)
        {
            statFilter = EffectTargetUnitFilterStat.AP;
        }

        if (statFilter == EffectTargetUnitFilterStat.Unset)
        {
            return true;
        }

        int compareValue = effect.targetUnitStatCompareValue;
        if (effect.compareTargetStatToSource)
        {
            if (attacker == null)
            {
                return false;
            }

            compareValue = GetTargetUnitFilterStatValue(attacker, statFilter);
        }

        return EffectCompareHelper.Compare(
            GetTargetUnitFilterStatValue(targetUnit, statFilter),
            compareValue,
            effect.targetUnitStatCompareOp);
    }

    /// <summary>AttackActiveEnemyUnit を味方ユニットへ手動選択付与するか。</summary>
    public static bool IsAttackActiveEnemyAllyGrant(this EffectData effect)
    {
        return effect != null
            && effect.type == EffectType.AttackActiveEnemyUnit
            && effect.target.IsAllyUnitPickTarget();
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
                // ユニットトークンはレベル 0 として扱う（最低 Lv 選択など）
                if (unit.Data != null && unit.Data.IsUnitToken())
                {
                    return 0;
                }

                return unit.CurrentLevel;
            default:
                return 0;
        }
    }

    /// <summary>
    /// オーナー墓地に発動元と同 ID が十分あるとき、対象ステータス絞り込みを緩和するか。
    /// </summary>
    public static bool ShouldRelaxTargetUnitStatFilter(
        this EffectData effect,
        CardController sourceCard,
        IReadOnlyList<int> ownerTrashCardIds)
    {
        if (effect == null || !effect.relaxTargetUnitStatFilterWhenTrashHasSourceCopies)
        {
            return false;
        }

        int cardId = sourceCard != null && sourceCard.Data != null ? sourceCard.Data.id : 0;
        if (cardId <= 0)
        {
            return false;
        }

        int need = effect.trashRelaxFilterMinCopies > 0 ? effect.trashRelaxFilterMinCopies : 2;
        return TrashCardQuery.HasAtLeast(ownerTrashCardIds, cardId, need);
    }

    /// <summary>バトルゾーンのユニットが対象フィルタ（Feature / ステータス）を満たすか。</summary>
    public static bool MatchesTargetUnitFilter(
        this EffectData effect,
        CardController unit,
        CardController sourceCard = null,
        IReadOnlyList<int> ownerTrashCardIds = null)
    {
        if (effect == null || unit == null || unit.Data == null || !unit.Data.IsUnitLike())
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
            if (effect.ShouldRelaxTargetUnitStatFilter(sourceCard, ownerTrashCardIds))
            {
                return true;
            }

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

    /// <summary>AttackActiveEnemyUnit の攻撃対象ステータス条件のみを文言化（Feature は含めない）。</summary>
    public static string FormatAttackActiveEnemyTargetStatDescription(this EffectData effect)
    {
        if (effect == null || !effect.HasAttackActiveEnemyTargetStatFilter())
        {
            return string.Empty;
        }

        EffectTargetUnitFilterStat statFilter = effect.GetTargetUnitFilterStat();
        if (statFilter == EffectTargetUnitFilterStat.Unset && effect.compareTargetStatToSource)
        {
            statFilter = EffectTargetUnitFilterStat.AP;
        }

        if (statFilter == EffectTargetUnitFilterStat.Unset)
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
        string statNote = effect.statTarget == EffectStatTarget.IncomingDamageReduction
            ? " / ダメージ軽減"
            : string.Empty;
        if (string.IsNullOrEmpty(filter))
        {
            return $"{effect.type} / {effect.target} / 値:{effect.value}{statNote}";
        }

        return $"{effect.type} / {effect.target} / 値:{effect.value}{statNote} / 条件:{filter}";
    }

    /// <summary>Look 直後に見た山札カードが effect の Feature／カード種類フィルタに合うか。</summary>
    public static bool MatchesLookedCardDataFeatureFilter(this EffectData effect, CardData card)
    {
        if (effect == null || card == null)
        {
            return false;
        }

        if (!effect.MatchesTargetCardTypeFilter(card))
        {
            return false;
        }

        return effect.MatchesTargetFeatureOnCard(card);
    }

    /// <summary>DeployUnit の配備体数（value≤0 は 1）。</summary>
    public static int GetDeployUnitCount(this EffectData effect, int resolvedMagnitude)
    {
        if (effect == null || effect.type != EffectType.DeployUnit)
        {
            return 0;
        }

        return resolvedMagnitude > 0 ? resolvedMagnitude : 1;
    }

    /// <summary>GrantAttackFlag の付与体数（value≤0 は 1）。</summary>
    public static int GetGrantAttackFlagCount(this EffectData effect, int resolvedMagnitude)
    {
        if (effect == null || effect.type != EffectType.GrantAttackFlag)
        {
            return 0;
        }

        return resolvedMagnitude > 0 ? resolvedMagnitude : 1;
    }

    /// <summary>手動ユニット選択の最低体数。</summary>
    public static int GetSelectMinCount(this EffectData effect)
    {
        if (effect == null)
        {
            return 1;
        }

        if (effect.selectMinCount > 0)
        {
            return effect.selectMinCount;
        }

        return effect.selectionMode.IsMultipleUnitPickMode() ? 1 : 1;
    }

    /// <summary>手動ユニット選択の最大体数（候補数で上限クリップ）。</summary>
    public static int GetSelectMaxCount(this EffectData effect, int candidateCount = int.MaxValue)
    {
        if (effect == null)
        {
            return 1;
        }

        if (effect.selectionMode.IsImmediateSinglePick())
        {
            return 1;
        }

        int configuredMax = effect.selectMaxCount > 0 ? effect.selectMaxCount : int.MaxValue;
        if (candidateCount <= 0 || candidateCount == int.MaxValue)
        {
            return configuredMax;
        }

        return Mathf.Min(configuredMax, candidateCount);
    }

    public static ObservedUnitTriggerKind ResolveObservedUnitTriggerKind(this EffectData effect)
    {
        if (effect == null || effect.observedUnitTriggerKind == ObservedUnitTriggerKind.Unset)
        {
            return ObservedUnitTriggerKind.EnemyUnitDestroyed;
        }

        return effect.observedUnitTriggerKind;
    }

    public static string FormatSelectCountRangeLabel(this EffectData effect)
    {
        if (effect == null)
        {
            return string.Empty;
        }

        int min = effect.GetSelectMinCount();
        int max = effect.GetSelectMaxCount();
        if (min == max)
        {
            return $"（{min}体）";
        }

        if (max >= 9999)
        {
            return $"（{min}体以上）";
        }

        return $"（{min}〜{max}体）";
    }

    /// <summary>バトルゾーンの手動選択候補がカード種類・AttackFlg 条件を満たすか。</summary>
    public static bool MatchesSelectableBattleZoneTarget(this EffectData effect, CardController unit)
    {
        if (effect == null || unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        if (effect.filterByTargetCardType && !effect.MatchesTargetCardTypeFilter(unit.Data))
        {
            return false;
        }

        if (effect.filterTargetIsBlocker && !unit.HasBlockerAbility)
        {
            return false;
        }

        if (effect.type == EffectType.GrantAttackFlag
            && effect.grantAttackFlagOnlyIfOff
            && unit.AttackFlgState != AttackFlg.False)
        {
            return false;
        }

        return true;
    }

    /// <summary>Hand/Trash 配備候補が deployCardId / Feature フィルタを満たすか。</summary>
    public static bool MatchesDeployCandidateFilter(this EffectData effect, CardData card)
    {
        if (effect == null || card == null || !card.IsUnitLike())
        {
            return false;
        }

        if (effect.filterByDeployCardId && effect.deployCardId > 0 && card.id != effect.deployCardId)
        {
            return false;
        }

        if (effect.filterDeployCandidateByFeature && effect.HasTargetFeatureFilter()
            && !effect.MatchesTargetFeatureOnCard(card))
        {
            return false;
        }

        if (effect.filterByTargetCardType && !effect.MatchesTargetCardTypeFilter(card))
        {
            return false;
        }

        return true;
    }

    /// <summary>トラッシュからの Base 配備候補が種類・Feature・ID フィルタを満たすか。</summary>
    public static bool MatchesDeployBaseCandidateFilter(this EffectData effect, CardData card)
    {
        if (effect == null || card == null || card.type != Type.Base)
        {
            return false;
        }

        if (effect.filterByDeployCardId && effect.deployCardId > 0 && card.id != effect.deployCardId)
        {
            return false;
        }

        if (effect.filterByTargetCardType && !effect.MatchesTargetCardTypeFilter(card))
        {
            return false;
        }

        if (!effect.MatchesTargetFeatureFilter(card))
        {
            return false;
        }

        return true;
    }

    /// <summary>DeployUnit が手札／トラッシュからの選択 UI を要するか。</summary>
    public static bool RequiresDeployUnitZoneSelection(this EffectData effect)
    {
        if (effect == null || effect.type != EffectType.DeployUnit)
        {
            return false;
        }

        if (effect.deployUnitSource == DeployUnitSource.Hand)
        {
            return effect.selectionMode.RequiresManualUnitPick();
        }

        return effect.deployUnitSource == DeployUnitSource.Trash;
    }

    /// <summary>DeployBase がトラッシュからの選択 UI を要するか。</summary>
    public static bool RequiresDeployBaseFromTrashSelection(this EffectData effect)
    {
        return effect != null
            && effect.type == EffectType.DeployBase
            && effect.deployUnitSource == DeployUnitSource.Trash;
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

    [Tooltip("能動発動（OnMain 等）時に支払うリソース。0 のとき手札コマンドはカードのコスト、場のユニットは無料。")]
    public int activationCost;

    [Tooltip("true のときこの timed ブロックは1ターンに1回まで（能動発動 / OnUnitDestroyedByOwnerEffect 等の受動トリガー）。")]
    public bool oncePerTurn;

    [Tooltip("OnObservedUnitTrigger: 応答する監視イベント種別。Unset なら全種別に応答。")]
    public ObservedUnitTriggerKind observedUnitTriggerKind = ObservedUnitTriggerKind.Unset;
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

    /// <summary>activationConditions に「自ターンのみ」(OwnerTurn) が含まれるか。</summary>
    public static bool HasOwnerTurnActivationRequirement(this TimedEffectData timed)
    {
        if (timed?.activationConditions == null)
        {
            return false;
        }

        for (int i = 0; i < timed.activationConditions.Count; i++)
        {
            EffectActivationCondition c = timed.activationConditions[i];
            if (c != null && c.turnCheck == EffectTurnCheckKind.OwnerTurn)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>盤面ユニットへ掛ける自ターン限定 Buff/Debuff（手札パッシブ以外）。</summary>
    public static bool IsFieldOwnerTurnStatPassiveBlock(this TimedEffectData timed)
    {
        if (timed == null || !timed.HasOwnerTurnActivationRequirement() || timed.IsHandConditionalPassiveBlock())
        {
            return false;
        }

        IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
        for (int i = 0; i < resolved.Count; i++)
        {
            EffectData effect = resolved[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
            {
                continue;
            }

            if (effect.target == TargetType.Self)
            {
                continue;
            }

            return true;
        }

        return false;
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

    /// <summary>自分のユニットの効果破壊（EffectType.Destroy／効果ダメージ、自分相手いずれの効果でも）で解決するブロック。</summary>
    public static bool IsOnUnitDestroyedByOwnerEffectResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null
            || timed.timing != EffectTiming.OnUnitDestroyedByOwnerEffect
            || !timed.HasResolvedEffects())
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

    /// <summary>監視ユニットの行動時（OnObservedUnitTrigger）に解決するブロック。</summary>
    public static bool IsOnObservedUnitTriggerResolutionBlock(this TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnObservedUnitTrigger || !timed.HasResolvedEffects())
        {
            return false;
        }

        return !timed.IsHandConditionalPassiveBlock();
    }

    public static bool MatchesObservedUnitTriggerKind(this TimedEffectData timed, ObservedUnitTriggerKind triggerKind)
    {
        if (timed == null)
        {
            return false;
        }

        if (timed.observedUnitTriggerKind == ObservedUnitTriggerKind.Unset)
        {
            return true;
        }

        if (timed.observedUnitTriggerKind == triggerKind)
        {
            return true;
        }

        // 敵ユニット破壊時報酬はシールド／配備ベース／EXベース破壊でも同様に発動する
        return timed.observedUnitTriggerKind == ObservedUnitTriggerKind.EnemyUnitDestroyed
            && (triggerKind == ObservedUnitTriggerKind.ShieldDestroyed
                || triggerKind == ObservedUnitTriggerKind.BaseDestroyed
                || triggerKind == ObservedUnitTriggerKind.ExBaseDestroyed);
    }
}

