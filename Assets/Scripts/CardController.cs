using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using System;
using TMPro;
public class CardController : MonoBehaviour,IPointerClickHandler
{
    [Serializable]
    private struct StatModifier
    {
        public int value;
        public EffectDuration duration;
        /// <summary>空文字は従来の修飾。非空は <see cref="RemoveStatModifiersBySource"/> でまとめて除去。</summary>
        public string sourceKey;
    }

    /// <summary>パイロット搭乗時の AllyAllUnits 永続バフを、後から配備された味方ユニットにも適用するためのオーラ定義。</summary>
    public struct PilotMountAllyFieldAuraEntry
    {
        public EffectStatTarget StatTarget;
        public int SignedMagnitude;
        public EffectDuration Duration;
    }

    [SerializeField] private Image cardImage;
    private GameObject _battleStatOverlayRoot;
    private TextMeshProUGUI _battleStatText;
    private int _lastDisplayedPower = int.MinValue;
    private int _lastDisplayedHp = int.MinValue;
    

    // !バトルパネルの参照

    public CardData Data { get; private set; }
    private Action<CardController> onClickCallback;
    
    public Sprite cardSprite{ get; private set; }

    /// <summary>Addressables 経由で保持している Sprite ハンドル（未使用時は無効）。</summary>
    private AsyncOperationHandle<Sprite> _addressableSpriteHandle;
    private int _spriteLoadGeneration;

    /// <summary>ユニットの現在 HP（配備・ドロー時に Data.hp で初期化）。</summary>
    public int CurrentHp { get; private set; }

    /// <summary>バトル中のユニット識別子（オンライン同期用）。0 は未割当。</summary>
    public int BattleInstanceId { get; private set; }
    public int CurrentPower
    {
        get
        {
            int basePower = Data != null ? Data.power : 0;
            int modified = basePower + pilotPowerBonus + SumModifierValues(powerModifiers);
            if (IsDamagedForWhileDamagedEffects())
            {
                modified += GetWhileDamagedApBonusFromData(Data);
            }

            return Mathf.Max(0, modified);
        }
    }

    /// <summary>ダメージを受けている（現在 HP &lt; 最大 HP）か。While Damaged AP 等で使用。</summary>
    public bool IsDamagedForWhileDamagedEffects()
    {
        if (Data == null || !Data.IsUnitLike())
        {
            return false;
        }

        return CurrentHp < GetRepairHpCap();
    }

    /// <summary>
    /// timedEffects の effectsName「BuffSelfApN_WhileDamaged」から、ダメージ中の AP ボーナスを得る。
    /// </summary>
    public static int GetWhileDamagedApBonusFromData(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return 0;
        }

        int total = 0;
        const string prefix = "BuffSelfAp";
        const string suffix = "_WhileDamaged";
        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || string.IsNullOrWhiteSpace(timed.effectsName))
            {
                continue;
            }

            string name = timed.effectsName.Trim();
            if (!name.StartsWith(prefix, System.StringComparison.Ordinal)
                || !name.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                continue;
            }

            string mid = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            if (int.TryParse(mid, out int amount) && amount > 0)
            {
                total += amount;
            }
        }

        return total;
    }

    /// <summary>実効コスト（Data.cost + ランタイム補正）。アセットは変更しない。</summary>
    public int CurrentCost
    {
        get
        {
            int baseCost = Data != null ? Data.cost : 0;
            return Mathf.Max(0, baseCost + SumModifierValues(costModifiers));
        }
    }

    /// <summary>実効レベル要件（Data.level + ランタイム補正）。アセットは変更しない。</summary>
    public int CurrentLevel
    {
        get
        {
            int baseLevel = Data != null ? Data.level : 0;
            return Mathf.Max(0, baseLevel + SumModifierValues(levelModifiers));
        }
    }
    public CardController MountedPilot { get; private set; }
    public CardController MountedUnit { get; private set; }

    /// <summary>シールド用：表が隠れている間は true（カバーを破棄すると false）。</summary>
    public bool IsShieldFaceHidden => shieldFaceCoverRoot != null;

    /// <summary>オンライン相手手札の伏せプレースホルダ（カード内容非公開）。</summary>
    public bool IsOnlineOpponentHandPlaceholder { get; private set; }

    private GameObject shieldFaceCoverRoot;
    private int pilotPowerBonus;
    private readonly List<StatModifier> powerModifiers = new List<StatModifier>();
    private readonly List<StatModifier> hpModifiers = new List<StatModifier>();
    private readonly List<StatModifier> costModifiers = new List<StatModifier>();
    private readonly List<StatModifier> levelModifiers = new List<StatModifier>();
    private readonly List<StatModifier> effectDamageModifiers = new List<StatModifier>();
    private readonly List<StatModifier> effectDamageImmunityModifiers = new List<StatModifier>();
    private readonly List<StatModifier> incomingDamageReductionModifiers = new List<StatModifier>();
    private readonly List<PilotMountAllyFieldAuraEntry> _pilotMountAllyFieldAuras = new List<PilotMountAllyFieldAuraEntry>();

    /// <summary>効果ダメージ（戦闘交換以外）への実効補正。</summary>
    public int CurrentEffectDamageModifier => SumModifierValues(effectDamageModifiers);

    /// <summary>効果ダメージ無効化レイヤー数（Buff/Debuff の EffectDamageImmunity）。</summary>
    public int CurrentEffectDamageImmunityCount => Mathf.Max(0, SumModifierValues(effectDamageImmunityModifiers));

    /// <summary>受けるダメージの軽減量（正の値ほど軽減。戦闘・効果ダメージ共通）。</summary>
    public int CurrentIncomingDamageReduction => Mathf.Max(0, SumModifierValues(incomingDamageReductionModifiers));

    /// <summary>効果ダメージ無効化が有効か。</summary>
    public bool HasEffectDamageImmunity => CurrentEffectDamageImmunityCount > 0;
    private Image unitFaceTopLayer;

    /// <summary>ランタイムの攻撃フラグ（カードデータのアセットは変更しない）。</summary>
    private AttackFlg _attackFlg = AttackFlg.False;
    public AttackFlg AttackFlgState => _attackFlg;
    public bool IsRestState { get; private set; }

    /// <summary>
    /// 配備後に「自分のユニットが自分プレイヤーの効果で破壊された」ことを記録する監視フラグ（Axis 等）。
    /// 場を離れる／ランタイム初期化でクリアする。
    /// </summary>
    private bool _ownerEffectDestroyArmed;
    public bool HasOwnerEffectDestroyArmed => _ownerEffectDestroyArmed;

    public void ArmOwnerEffectDestroyWatch()
    {
        _ownerEffectDestroyArmed = true;
    }

    public void ClearOwnerEffectDestroyArmed()
    {
        _ownerEffectDestroyArmed = false;
    }

    /// <summary>
    /// バースト等で一時的にバトルゾーン用ユニット化した状態。
    /// 場を離れたら印刷タイプ（パイロット等）へ戻す。
    /// </summary>
    private bool _isTemporaryBurstBattleUnit;
    private Type _printedTypeBeforeBurstBattleUnit;
    private int _printedPowerBeforeBurstBattleUnit;
    private int _printedHpBeforeBurstBattleUnit;

    public bool IsTemporaryBurstBattleUnit => _isTemporaryBurstBattleUnit;

    public Type TemporaryBurstBattleUnitPrintedType => _printedTypeBeforeBurstBattleUnit;

    public int TemporaryBurstBattleUnitPrintedPower => _printedPowerBeforeBurstBattleUnit;

    public int TemporaryBurstBattleUnitPrintedHp => _printedHpBeforeBurstBattleUnit;

    public void MarkTemporaryBurstBattleUnit(Type printedType, int printedPower, int printedHp)
    {
        _isTemporaryBurstBattleUnit = true;
        _printedTypeBeforeBurstBattleUnit = printedType;
        _printedPowerBeforeBurstBattleUnit = printedPower;
        _printedHpBeforeBurstBattleUnit = printedHp;
    }

    /// <summary>
    /// 一時ユニット形態を印刷定義へ戻す。戻り値が true なら復元した。
    /// </summary>
    public bool TryRestoreFromTemporaryBurstBattleUnit()
    {
        if (!_isTemporaryBurstBattleUnit || Data == null)
        {
            return false;
        }

        Data.type = _printedTypeBeforeBurstBattleUnit;
        Data.power = _printedPowerBeforeBurstBattleUnit;
        Data.hp = _printedHpBeforeBurstBattleUnit;
        if (!string.IsNullOrEmpty(Data.name) && Data.name.EndsWith(" (BattleUnit)", StringComparison.Ordinal))
        {
            Data.name = Data.name.Substring(0, Data.name.Length - " (BattleUnit)".Length);
        }

        _isTemporaryBurstBattleUnit = false;
        ResetRuntimeStatsFromData();
        return true;
    }

    /// <summary>
    /// 盤面条件を含めて現在《ブロッカー》が有効か。
    /// CardData は共有アセットなので変更せず、カードインスタンスごとに保持する。
    /// </summary>
    private bool _runtimeBlockerAbilityEnabled;
    public bool HasBlockerAbility =>
        Data != null && Data.IsUnitLike() && _runtimeBlockerAbilityEnabled;

    public void SetRuntimeBlockerAbility(bool enabled)
    {
        _runtimeBlockerAbilityEnabled = enabled;
    }

    /// <summary>【リンク中】等で付与された Feature ID（CardData を変更しない）。</summary>
    private readonly HashSet<int> _runtimeGrantedFeatureIds = new HashSet<int>();

    public bool HasRuntimeFeatureId(int featureId) =>
        featureId > 0 && _runtimeGrantedFeatureIds.Contains(featureId);

    public void SetRuntimeFeatureGrant(int featureId, bool enabled)
    {
        if (featureId <= 0)
        {
            return;
        }

        if (enabled)
        {
            _runtimeGrantedFeatureIds.Add(featureId);
        }
        else
        {
            _runtimeGrantedFeatureIds.Remove(featureId);
        }
    }

    public void ClearRuntimeFeatureGrants()
    {
        _runtimeGrantedFeatureIds.Clear();
    }

    /// <summary>AttackActiveEnemyUnit（UntilEndOfTurn）のランタイム付与（効果定義ごとに保持）。</summary>
    private readonly List<EffectData> _attackActiveEnemyUntilEndOfTurnGrants = new List<EffectData>();

    public bool HasAttackActiveEnemyUntilEndOfTurnGrant => _attackActiveEnemyUntilEndOfTurnGrants.Count > 0;

    public IReadOnlyList<EffectData> AttackActiveEnemyUntilEndOfTurnGrants => _attackActiveEnemyUntilEndOfTurnGrants;

    public void AddAttackActiveEnemyUntilEndOfTurnGrant(EffectData effect)
    {
        if (effect != null)
        {
            _attackActiveEnemyUntilEndOfTurnGrants.Add(effect);
        }
    }

    public void ClearAttackActiveEnemyUntilEndOfTurnGrants()
    {
        _attackActiveEnemyUntilEndOfTurnGrants.Clear();
    }

    /// <summary>AttackActiveEnemyUnit（UntilEndOfBattle）のランタイム付与（効果定義ごとに保持）。</summary>
    private readonly List<EffectData> _attackActiveEnemyUntilEndOfBattleGrants = new List<EffectData>();

    public bool HasAttackActiveEnemyUntilEndOfBattleGrant => _attackActiveEnemyUntilEndOfBattleGrants.Count > 0;

    public IReadOnlyList<EffectData> AttackActiveEnemyUntilEndOfBattleGrants => _attackActiveEnemyUntilEndOfBattleGrants;

    public void AddAttackActiveEnemyUntilEndOfBattleGrant(EffectData effect)
    {
        if (effect != null)
        {
            _attackActiveEnemyUntilEndOfBattleGrants.Add(effect);
        }
    }

    public void ClearAttackActiveEnemyUntilEndOfBattleGrants()
    {
        _attackActiveEnemyUntilEndOfBattleGrants.Clear();
    }

    private int _notDirectAttackUntilEndOfTurnDepth;

    public bool HasNotDirectAttackUntilEndOfTurnGrant => _notDirectAttackUntilEndOfTurnDepth > 0;

    private int _firstStrikeUntilEndOfTurnDepth;

    public bool HasFirstStrikeUntilEndOfTurnGrant => _firstStrikeUntilEndOfTurnDepth > 0;

    /// <summary>制圧（UntilEndOfTurn）の破壊枚数。0 は未付与。</summary>
    private int _suppressUntilEndOfTurnBreakCount;

    public bool HasSuppressUntilEndOfTurnGrant => _suppressUntilEndOfTurnBreakCount > 0;

    public int SuppressUntilEndOfTurnBreakCount => _suppressUntilEndOfTurnBreakCount;

    public void AddSuppressUntilEndOfTurnGrant(int breakCount)
    {
        int next = breakCount > 0 ? breakCount : 2;
        if (next > _suppressUntilEndOfTurnBreakCount)
        {
            _suppressUntilEndOfTurnBreakCount = next;
        }
    }

    public void ClearSuppressUntilEndOfTurnGrants()
    {
        _suppressUntilEndOfTurnBreakCount = 0;
    }

    /// <summary>∀ Gundam 等：トラッシュからコピーしたキーワード（ターン終了まで）。</summary>
    private bool _copiedBlockerUntilEndOfTurn;
    private bool _copiedGrantedFirstStrike;
    private bool _copiedGrantedHighMobility;
    private int _copiedGrantedBreachAmount;
    private int _copiedGrantedSuppressBreaks;
    private int _copiedGrantedRepairBonus;
    private int _copiedSupportApUntilEndOfTurn;
    private TimedEffectData _copiedSupportTimedUntilEndOfTurn;
    private string _copiedKeywordStatSourceKey;

    public bool HasCopiedBlockerUntilEndOfTurn => _copiedBlockerUntilEndOfTurn;

    public int CopiedSupportApUntilEndOfTurn => _copiedSupportApUntilEndOfTurn;

    public TimedEffectData CopiedSupportTimedUntilEndOfTurn => _copiedSupportTimedUntilEndOfTurn;

    public string CopiedKeywordStatSourceKey => _copiedKeywordStatSourceKey;

    public void ClearCopiedKeywordsUntilEndOfTurn()
    {
        if (_copiedGrantedFirstStrike)
        {
            if (_firstStrikeUntilEndOfTurnDepth > 0)
            {
                _firstStrikeUntilEndOfTurnDepth--;
            }

            _copiedGrantedFirstStrike = false;
        }

        if (_copiedGrantedHighMobility)
        {
            if (_highMobilityUntilEndOfTurnDepth > 0)
            {
                _highMobilityUntilEndOfTurnDepth--;
            }

            _copiedGrantedHighMobility = false;
        }

        if (_copiedGrantedBreachAmount > 0)
        {
            if (_breachUntilEndOfTurnAmount <= _copiedGrantedBreachAmount)
            {
                _breachUntilEndOfTurnAmount = 0;
            }

            _copiedGrantedBreachAmount = 0;
        }

        if (_copiedGrantedSuppressBreaks > 0)
        {
            if (_suppressUntilEndOfTurnBreakCount <= _copiedGrantedSuppressBreaks)
            {
                _suppressUntilEndOfTurnBreakCount = 0;
            }

            _copiedGrantedSuppressBreaks = 0;
        }

        if (_copiedGrantedRepairBonus > 0)
        {
            _turnEndRepairBonus = Mathf.Max(0, _turnEndRepairBonus - _copiedGrantedRepairBonus);
            _copiedGrantedRepairBonus = 0;
        }

        bool hadBlocker = _copiedBlockerUntilEndOfTurn;
        _copiedBlockerUntilEndOfTurn = false;
        _copiedSupportApUntilEndOfTurn = 0;
        _copiedSupportTimedUntilEndOfTurn = null;
        _copiedKeywordStatSourceKey = null;
        if (hadBlocker && Data != null)
        {
            _runtimeBlockerAbilityEnabled = Data.IsBlockerUnit();
        }
    }

    /// <summary>
    /// トラッシュ選択カードの印刷キーワードをターン終了まで付与する（AP+1 は呼び出し側で付与）。
    /// </summary>
    public void ApplyCopiedPrintedKeywordsUntilEndOfTurn(
        CardPrintedKeywordExtensions.PrintedKeywords keywords,
        string statSourceKey)
    {
        ClearCopiedKeywordsUntilEndOfTurn();
        _copiedKeywordStatSourceKey = statSourceKey;

        if (keywords.HasRepair && keywords.RepairAmount > 0)
        {
            AddTurnEndRepairBonus(keywords.RepairAmount);
            _copiedGrantedRepairBonus = keywords.RepairAmount;
        }

        if (keywords.HasBreach && keywords.BreachAmount > 0)
        {
            AddBreachUntilEndOfTurnGrant(keywords.BreachAmount);
            _copiedGrantedBreachAmount = keywords.BreachAmount;
        }

        if (keywords.HasFirstStrike)
        {
            AddFirstStrikeUntilEndOfTurnGrant();
            _copiedGrantedFirstStrike = true;
        }

        if (keywords.HasHighMobility)
        {
            AddHighMobilityUntilEndOfTurnGrant();
            _copiedGrantedHighMobility = true;
        }

        if (keywords.HasSuppress)
        {
            int breaks = keywords.SuppressBreaks > 0 ? keywords.SuppressBreaks : 2;
            AddSuppressUntilEndOfTurnGrant(breaks);
            _copiedGrantedSuppressBreaks = breaks;
        }

        if (keywords.HasBlocker)
        {
            _copiedBlockerUntilEndOfTurn = true;
            SetRuntimeBlockerAbility(true);
        }

        if (keywords.HasSupport && keywords.SupportAp > 0)
        {
            _copiedSupportApUntilEndOfTurn = keywords.SupportAp;
            _copiedSupportTimedUntilEndOfTurn = BuildCopiedSupportTimedEffect(keywords.SupportAp);
        }
    }

    private static TimedEffectData BuildCopiedSupportTimedEffect(int supportAp)
    {
        int ap = supportAp > 0 ? supportAp : 1;
        return new TimedEffectData
        {
            timing = EffectTiming.OnMain,
            oncePerTurn = true,
            effects = new List<EffectData>
            {
                new EffectData
                {
                    type = EffectType.Rest,
                    value = 1,
                    target = TargetType.Self,
                    selectionMode = EffectSelectionMode.Unset
                },
                new EffectData
                {
                    type = EffectType.Buff,
                    value = ap,
                    target = TargetType.AllyOtherUnit,
                    selectionMode = EffectSelectionMode.SelectSingle,
                    statTarget = EffectStatTarget.AP,
                    duration = EffectDuration.UntilEndOfTurn
                }
            }
        };
    }

    public void AddFirstStrikeUntilEndOfTurnGrant()
    {
        _firstStrikeUntilEndOfTurnDepth++;
    }

    public void ClearFirstStrikeUntilEndOfTurnGrants()
    {
        _firstStrikeUntilEndOfTurnDepth = 0;
    }

    /// <summary>《高機動》のターン終了まで付与回数。0 は未付与。</summary>
    private int _highMobilityUntilEndOfTurnDepth;

    public bool HasHighMobilityUntilEndOfTurnGrant => _highMobilityUntilEndOfTurnDepth > 0;

    public void AddHighMobilityUntilEndOfTurnGrant()
    {
        _highMobilityUntilEndOfTurnDepth++;
    }

    public void ClearHighMobilityUntilEndOfTurnGrants()
    {
        _highMobilityUntilEndOfTurnDepth = 0;
    }

    /// <summary>《突破》のターン終了まで付与量。0 は未付与。</summary>
    private int _breachUntilEndOfTurnAmount;

    public bool HasBreachUntilEndOfTurnGrant => _breachUntilEndOfTurnAmount > 0;

    public int BreachUntilEndOfTurnAmount => _breachUntilEndOfTurnAmount;

    public void AddBreachUntilEndOfTurnGrant(int amount)
    {
        int next = amount > 0 ? amount : 0;
        if (next > _breachUntilEndOfTurnAmount)
        {
            _breachUntilEndOfTurnAmount = next;
        }
    }

    public void ClearBreachUntilEndOfTurnGrants()
    {
        _breachUntilEndOfTurnAmount = 0;
    }

    /// <summary>《突破》のこのバトル中付与量。0 は未付与。</summary>
    private int _breachUntilEndOfBattleAmount;

    public bool HasBreachUntilEndOfBattleGrant => _breachUntilEndOfBattleAmount > 0;

    public int BreachUntilEndOfBattleAmount => _breachUntilEndOfBattleAmount;

    public void AddBreachUntilEndOfBattleGrant(int amount)
    {
        int next = amount > 0 ? amount : 0;
        if (next > _breachUntilEndOfBattleAmount)
        {
            _breachUntilEndOfBattleAmount = next;
        }
    }

    public void ClearBreachUntilEndOfBattleGrants()
    {
        _breachUntilEndOfBattleAmount = 0;
    }

    public void AddNotDirectAttackUntilEndOfTurnGrant()
    {
        _notDirectAttackUntilEndOfTurnDepth++;
    }

    public void ClearNotDirectAttackUntilEndOfTurnGrants()
    {
        _notDirectAttackUntilEndOfTurnDepth = 0;
    }

    /// <summary>カード定義またはターン限定付与により、相手プレイヤー／シールドへ直接攻撃不可。</summary>
    public bool CannotDirectAttackPlayerOrShield()
    {
        return (Data != null && Data.isNotDirectAttack) || HasNotDirectAttackUntilEndOfTurnGrant;
    }

    /// <summary>シールドゾーンから手札へ移したカードのみ、再配備可能。</summary>
    private bool eligibleForShieldZoneDeploy;
    public bool IsEligibleForShieldZoneDeploy => eligibleForShieldZoneDeploy;

    public void SetEligibleForShieldZoneDeploy(bool eligible)
    {
        eligibleForShieldZoneDeploy = eligible;
    }

    public void RebindClickHandler(Action<CardController> callback)
    {
        onClickCallback = callback;
    }

    public void SetUp(CardData carddata,Action<CardController> callback)
    {
        if (carddata != null)
        {
            carddata.EnsureFeaturesResolved();
        }

        this.Data = carddata;
        
        this.onClickCallback = callback;
        BeginResolveCardSprite(carddata);

        // 手札・新規生成時は常に False（ユニット以外は攻撃フラグを使わない）
        _attackFlg = AttackFlg.False;
        eligibleForShieldZoneDeploy = false;
        BattleInstanceId = 0;
        _isTemporaryBurstBattleUnit = false;
        ResetRuntimeStatsFromData();
        SetBattleStatOverlayVisible(false);
    }

    private void OnDestroy()
    {
        ReleaseAddressableSpriteHandle();
    }

    /// <summary>バトルゾーン上のユニットに現在 AP / HP 表示を出す。</summary>
    public void SetBattleStatOverlayVisible(bool visible)
    {
        bool shouldShow = visible && Data != null && Data.IsUnitLike();
        if (shouldShow)
        {
            EnsureBattleStatOverlay();
        }

        if (_battleStatOverlayRoot != null)
        {
            _battleStatOverlayRoot.SetActive(shouldShow);
        }

        if (shouldShow)
        {
            RefreshBattleStatOverlay(force: true);
            BringBattleStatOverlayToFront();
        }
    }

    private void LateUpdate()
    {
        if (IsRestState)
        {
            // GridLayout 等のレイアウト再計算後も横倒しを維持する
            ApplyRestRotationVisual();
        }

        if (_battleStatOverlayRoot == null || !_battleStatOverlayRoot.activeSelf)
        {
            return;
        }

        // ローカル効果・戦闘・オンライン同期のどの経路で値が変化しても同フレームに追従する。
        RefreshBattleStatOverlay(force: false);
    }

    private void EnsureBattleStatOverlay()
    {
        if (_battleStatOverlayRoot != null)
        {
            return;
        }

        _battleStatOverlayRoot = new GameObject(
            "BattleStatOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rootRt = _battleStatOverlayRoot.GetComponent<RectTransform>();
        rootRt.SetParent(transform, false);
        rootRt.anchorMin = new Vector2(0.03f, 0.82f);
        rootRt.anchorMax = new Vector2(0.97f, 0.98f);
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        Image background = _battleStatOverlayRoot.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.78f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject(
            "BattleStatText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform textRt = textObject.GetComponent<RectTransform>();
        textRt.SetParent(rootRt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(2f, 0f);
        textRt.offsetMax = new Vector2(-2f, 0f);

        _battleStatText = textObject.GetComponent<TextMeshProUGUI>();
        _battleStatText.alignment = TextAlignmentOptions.Center;
        _battleStatText.color = Color.white;
        _battleStatText.enableAutoSizing = true;
        _battleStatText.fontSizeMin = 9f;
        _battleStatText.fontSizeMax = 20f;
        _battleStatText.raycastTarget = false;
        _battleStatText.richText = true;
    }

    private void RefreshBattleStatOverlay(bool force)
    {
        if (_battleStatText == null || Data == null || !Data.IsUnitLike())
        {
            return;
        }

        int power = CurrentPower;
        int hp = CurrentHp;
        if (!force && power == _lastDisplayedPower && hp == _lastDisplayedHp)
        {
            return;
        }

        _lastDisplayedPower = power;
        _lastDisplayedHp = hp;
        int turnAp = GetPowerModifierSumByDuration(EffectDuration.UntilEndOfTurn);
        int battleAp = GetPowerModifierSumByDuration(EffectDuration.UntilEndOfBattle);
        if (turnAp != 0 || battleAp != 0)
        {
            string suffix = string.Empty;
            if (battleAp != 0)
            {
                string battleSign = battleAp > 0 ? "+" : string.Empty;
                suffix += $"<color=#81D4FA>({battleSign}{battleAp}B)</color>";
            }

            if (turnAp != 0)
            {
                string turnSign = turnAp > 0 ? "+" : string.Empty;
                suffix += $"<color=#FFAB91>({turnSign}{turnAp}T)</color>";
            }

            _battleStatText.text =
                $"AP <color=#FFD54F>{power}</color>{suffix}  HP <color=#80CBC4>{hp}</color>";
        }
        else
        {
            _battleStatText.text =
                $"AP <color=#FFD54F>{power}</color>  HP <color=#80CBC4>{hp}</color>";
        }
    }

    private void BringBattleStatOverlayToFront()
    {
        if (_battleStatOverlayRoot != null)
        {
            _battleStatOverlayRoot.transform.SetAsLastSibling();
        }
    }

    public void AssignBattleInstanceId(int instanceId)
    {
        BattleInstanceId = Mathf.Max(0, instanceId);
    }

    public void SetCurrentHpForSync(int hp)
    {
        CurrentHp = Mathf.Max(0, hp);
        RefreshBattleStatOverlay(force: true);
    }

    /// <summary>効果等で付与されるターン終了リペア量（isRepair 定義に加算）。</summary>
    private int _turnEndRepairBonus;

    public void AddTurnEndRepairBonus(int amount)
    {
        if (amount > 0)
        {
            _turnEndRepairBonus += amount;
        }
    }

    public void ClearTurnEndRepairBonus()
    {
        _turnEndRepairBonus = 0;
    }

    /// <summary>ターン終了時に回復する合計量（カード定義 isRepair + 付与分）。</summary>
    public int GetTurnEndRepairAmount()
    {
        int fromDefinition = Data != null && Data.isRepair ? Mathf.Max(0, Data.repairAmount) : 0;
        return fromDefinition + _turnEndRepairBonus;
    }

    public bool ShouldRepairAtTurnEnd()
    {
        return GetTurnEndRepairAmount() > 0;
    }

    public int GetRepairHpCap()
    {
        if (Data == null)
        {
            return CurrentHp;
        }

        int cap = Data.hp + SumModifierValues(hpModifiers);
        if (MountedPilot?.Data != null)
        {
            cap += MountedPilot.Data.hp;
        }

        return Mathf.Max(cap, CurrentHp);
    }

    /// <summary>HP を回復し、実際に回復した量を返す（上限は <see cref="GetRepairHpCap"/>）。</summary>
    public int TryApplyRepair(int amount)
    {
        if (amount <= 0 || Data == null || CurrentHp <= 0)
        {
            return 0;
        }

        int cap = GetRepairHpCap();
        if (CurrentHp >= cap)
        {
            return 0;
        }

        int before = CurrentHp;
        CurrentHp = Mathf.Min(cap, CurrentHp + amount);
        return CurrentHp - before;
    }

    /// <summary>Data に基づきランタイム HP を初期化（ユニット以外は hp を参照しない想定）。</summary>
    public void ResetRuntimeStatsFromData()
    {
        if (Data == null)
        {
            CurrentHp = 0;
            return;
        }

        CurrentHp = Mathf.Max(0, Data.hp);
        pilotPowerBonus = 0;
        powerModifiers.Clear();
        hpModifiers.Clear();
        costModifiers.Clear();
        levelModifiers.Clear();
        effectDamageModifiers.Clear();
        effectDamageImmunityModifiers.Clear();
        incomingDamageReductionModifiers.Clear();
        _pilotMountAllyFieldAuras.Clear();
        MountedPilot = null;
        MountedUnit = null;
        _notDirectAttackUntilEndOfTurnDepth = 0;
        _firstStrikeUntilEndOfTurnDepth = 0;
        _highMobilityUntilEndOfTurnDepth = 0;
        _breachUntilEndOfTurnAmount = 0;
        _breachUntilEndOfBattleAmount = 0;
        _turnEndRepairBonus = 0;
        _ownerEffectDestroyArmed = false;
        _runtimeBlockerAbilityEnabled = Data.IsBlockerUnit();
        WasDeployedFromTrash = false;
        _copiedBlockerUntilEndOfTurn = false;
        _copiedGrantedFirstStrike = false;
        _copiedGrantedHighMobility = false;
        _copiedGrantedBreachAmount = 0;
        _copiedGrantedSuppressBreaks = 0;
        _copiedGrantedRepairBonus = 0;
        _copiedSupportApUntilEndOfTurn = 0;
        _copiedSupportTimedUntilEndOfTurn = null;
        _copiedKeywordStatSourceKey = null;
    }

    /// <summary>今回の配備がトラッシュからだったか（【配備時】条件用）。</summary>
    public bool WasDeployedFromTrash { get; private set; }

    public void SetDeployedFromTrash(bool fromTrash)
    {
        WasDeployedFromTrash = fromTrash;
    }

    /// <summary>戦闘ダメージ。ユニット以外では呼ばない想定。IncomingDamageReduction を適用する。</summary>
    public void ApplyDamage(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int reduction = CurrentIncomingDamageReduction;
        int applied = Mathf.Max(0, amount - reduction);
        if (applied <= 0)
        {
            Debug.Log(
                $"[DamageReduction] {Data?.cardName} blocked {amount} (reduction:{reduction})");
            return;
        }

        if (reduction > 0)
        {
            Debug.Log(
                $"[DamageReduction] {Data?.cardName} {amount} → {applied} (reduction:{reduction})");
        }

        CurrentHp = Mathf.Max(0, CurrentHp - applied);
        RefreshBattleStatOverlay(force: true);
    }

    /// <summary>シールドとして裏向き表示する（カード画像の上に全面カバーを重ねる）。</summary>
    public void SetShieldFaceHidden(bool hidden)
    {
        if (!hidden)
        {
            RevealShieldFace();
            return;
        }

        if (shieldFaceCoverRoot != null)
        {
            shieldFaceCoverRoot.SetActive(true);
            return;
        }

        shieldFaceCoverRoot = new GameObject("ShieldFaceCover", typeof(RectTransform), typeof(Image));
        RectTransform rt = shieldFaceCoverRoot.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.SetAsLastSibling();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = shieldFaceCoverRoot.GetComponent<Image>();
        // シールドの裏面表示は視認しやすい青にする
        img.color = new Color(0.20f, 0.45f, 0.95f, 1f);
        img.raycastTarget = true;
    }

    /// <summary>シールド破壊などで表を表示する。</summary>
    public void RevealShieldFace()
    {
        if (shieldFaceCoverRoot != null)
        {
            Destroy(shieldFaceCoverRoot);
            shieldFaceCoverRoot = null;
        }
    }

    /// <summary>攻撃フラグを設定し、デバッグログを出す。</summary>
    public void SetAttackFlg(AttackFlg value)
    {
        _attackFlg = value;
        string name = Data != null ? Data.cardName : "?";
        int id = Data != null ? Data.id : -1;
        Debug.Log($"[AttackFlg] {name} (id:{id}) => {_attackFlg}");
    }

    /// <summary>レスト時の Z 回転角（画面上でカードが横向きになる）。</summary>
    private const float RestAngleZDegrees = -90f;

    /// <summary>
    /// ユニット／ベースの表示状態を更新する。
    /// isRest=true: 中心を軸に 90 度横倒し / false: 正立（ACTIVE）
    /// </summary>
    public void SetUnitRestVisual(bool isRest)
    {
        if (this == null || gameObject == null)
        {
            return;
        }

        if (Data == null)
        {
            return;
        }

        IsRestState = isRest;
        ApplyRestRotationVisual();
    }

    /// <summary>中心ピボットで REST/ACTIVE の回転を適用する（レイアウト後の再適用にも使う）。</summary>
    private void ApplyRestRotationVisual()
    {
        RectTransform rt = transform as RectTransform;
        if (rt == null)
        {
            return;
        }

        EnsureCenterPivotForRestTilt(rt);

        float targetZ = IsRestState ? RestAngleZDegrees : 0f;
        if (Mathf.Abs(Mathf.DeltaAngle(rt.localEulerAngles.z, targetZ)) > 0.05f)
        {
            rt.localRotation = Quaternion.Euler(0f, 0f, targetZ);
        }
    }

    /// <summary>
    /// プレハブが左上ピボットのままだと 90 度回転がセル外へ飛び、横倒しに見えない。
    /// 中心ピボットへ変え、見た目の位置はできるだけ保つ。
    /// </summary>
    private static void EnsureCenterPivotForRestTilt(RectTransform rt)
    {
        Vector2 center = new Vector2(0.5f, 0.5f);
        if ((rt.pivot - center).sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector2 size = rt.rect.size;
        if (size.x < 0.01f || size.y < 0.01f)
        {
            // レイアウト前などサイズ未確定時はピボットだけ先に合わせる
            rt.pivot = center;
            return;
        }

        Vector2 deltaPivot = center - rt.pivot;
        Vector2 delta = new Vector2(deltaPivot.x * size.x, deltaPivot.y * size.y);
        delta = rt.localRotation * delta;
        rt.pivot = center;
        rt.anchoredPosition += delta;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsOnlineOpponentHandPlaceholder || Data == null)
        {
            return;
        }

        Debug.Log($"カードがクリックされました。カード名前: {Data.cardName}");
        Debug.Log($"カードがクリックされました。カードコスト: {CurrentCost} (base:{Data.cost})");
        Debug.Log("クリックされました");
        onClickCallback?.Invoke(this);
    }

    /// <summary>オンライン相手手札用：裏向きで中身を見せないトークンとして構成する。</summary>
    public void ConfigureAsOnlineOpponentHandPlaceholder()
    {
        IsOnlineOpponentHandPlaceholder = true;
        Data = null;
        onClickCallback = null;
        BattleInstanceId = 0;
        _attackFlg = AttackFlg.False;
        eligibleForShieldZoneDeploy = false;
        SetBattleStatOverlayVisible(false);
        SetShieldFaceHidden(true);
    }

    /// <summary>伏せプレースホルダを、公開された実カード表示へ差し替える。</summary>
    public void ConvertOnlineOpponentHandPlaceholderToKnownCard(CardData carddata, Action<CardController> callback)
    {
        if (carddata == null)
        {
            return;
        }

        IsOnlineOpponentHandPlaceholder = false;
        SetUp(carddata, callback);
        SetShieldFaceHidden(true);
        // 公開帰手が必要な場合は呼び出し側で RevealShieldFace する
    }

    public int GetCardcost()
    {
        return CurrentCost;
    }

    public void AddEffectStatBonus(
        int powerDelta,
        int hpDelta,
        int costDelta,
        int levelDelta,
        EffectDuration duration = EffectDuration.Permanent,
        string statModifierSourceKey = null,
        int effectDamageDelta = 0,
        int effectDamageImmunityDelta = 0,
        int incomingDamageReductionDelta = 0)
    {
        string key = statModifierSourceKey ?? string.Empty;
        if (powerDelta != 0)
        {
            powerModifiers.Add(new StatModifier { value = powerDelta, duration = duration, sourceKey = key });
        }

        if (hpDelta != 0)
        {
            // AP と同様に sourceKey 付きで残し、解除時に現在 HP も戻せるようにする
            hpModifiers.Add(new StatModifier { value = hpDelta, duration = duration, sourceKey = key });
            CurrentHp = Mathf.Max(0, CurrentHp + hpDelta);
        }

        if (costDelta != 0)
        {
            costModifiers.Add(new StatModifier { value = costDelta, duration = duration, sourceKey = key });
        }

        if (levelDelta != 0)
        {
            levelModifiers.Add(new StatModifier { value = levelDelta, duration = duration, sourceKey = key });
        }

        if (effectDamageDelta != 0)
        {
            effectDamageModifiers.Add(new StatModifier { value = effectDamageDelta, duration = duration, sourceKey = key });
        }

        if (effectDamageImmunityDelta != 0)
        {
            effectDamageImmunityModifiers.Add(new StatModifier { value = effectDamageImmunityDelta, duration = duration, sourceKey = key });
        }

        if (incomingDamageReductionDelta != 0)
        {
            incomingDamageReductionModifiers.Add(new StatModifier
            {
                value = incomingDamageReductionDelta,
                duration = duration,
                sourceKey = key
            });
        }

        if (powerDelta != 0 || hpDelta != 0)
        {
            RefreshBattleStatOverlay(force: true);
        }
    }

    /// <summary>パイロット搭乗中のみ有効な味方フィールド全体オーラ（後配備ユニット向け）。</summary>
    public bool HasActivePilotMountAllyFieldAuras =>
        MountedPilot != null && _pilotMountAllyFieldAuras.Count > 0;

    public IReadOnlyList<PilotMountAllyFieldAuraEntry> GetPilotMountAllyFieldAuras() => _pilotMountAllyFieldAuras;

    /// <summary>ユニットがフィールド上で付与した Buff/Debuff の除去用キー（搭乗オーラと共通）。</summary>
    public string MakePilotMountFieldAuraSourceKey() => MakeUnitGrantedSourceKey(BattleInstanceId);

    public static string MakeUnitGrantedSourceKey(int battleInstanceId) => $"UnitGranted:{battleInstanceId}";

    public static string MakeOwnerTurnFieldPassiveSourceKey(int battleInstanceId, int blockIndex) =>
        $"OwnerTurnField:{battleInstanceId}:{blockIndex}";

    public struct StatModifierRemoval
    {
        public EffectStatTarget StatTarget;
        public int SignedTotal;
        public EffectDuration Duration;
    }

    public void RegisterPilotMountAllyFieldAura(
        EffectStatTarget statTarget,
        int signedMagnitude,
        EffectDuration duration)
    {
        for (int i = _pilotMountAllyFieldAuras.Count - 1; i >= 0; i--)
        {
            if (_pilotMountAllyFieldAuras[i].StatTarget == statTarget)
            {
                _pilotMountAllyFieldAuras.RemoveAt(i);
            }
        }

        _pilotMountAllyFieldAuras.Add(new PilotMountAllyFieldAuraEntry
        {
            StatTarget = statTarget,
            SignedMagnitude = signedMagnitude,
            Duration = duration,
        });
    }

    public void ClearPilotMountAllyFieldAuras()
    {
        _pilotMountAllyFieldAuras.Clear();
    }

    public bool HasStatModifierFromSource(string sourceKey, EffectStatTarget statTarget)
    {
        if (string.IsNullOrEmpty(sourceKey))
        {
            return false;
        }

        List<StatModifier> modifiers = GetModifierListForStatTarget(statTarget);
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].sourceKey == sourceKey)
            {
                return true;
            }
        }

        return false;
    }

    private List<StatModifier> GetModifierListForStatTarget(EffectStatTarget statTarget)
    {
        switch (statTarget)
        {
            case EffectStatTarget.AP:
                return powerModifiers;
            case EffectStatTarget.HP:
                return hpModifiers;
            case EffectStatTarget.Cost:
                return costModifiers;
            case EffectStatTarget.Level:
                return levelModifiers;
            case EffectStatTarget.EffectDamage:
                return effectDamageModifiers;
            case EffectStatTarget.EffectDamageImmunity:
                return effectDamageImmunityModifiers;
            case EffectStatTarget.IncomingDamageReduction:
                return incomingDamageReductionModifiers;
            default:
                return powerModifiers;
        }
    }

    /// <summary>sourceKey が一致するランタイム修飾のみ除去（手札条件付きパッシブ用）。除去した AP 修飾合計を返す。</summary>
    public int RemoveStatModifiersBySource(string sourceKey)
    {
        return SumRemovalByStatTarget(RemoveStatModifiersBySourceDetailed(sourceKey), EffectStatTarget.AP);
    }

    /// <summary>sourceKey が一致するランタイム修飾を除去し、種別ごとの除去量を返す。</summary>
    public List<StatModifierRemoval> RemoveStatModifiersBySourceDetailed(string sourceKey)
    {
        List<StatModifierRemoval> removed = new List<StatModifierRemoval>();
        if (string.IsNullOrEmpty(sourceKey))
        {
            return removed;
        }

        AppendRemovalIfNonZero(removed, EffectStatTarget.AP, RemoveKeyedModifiers(powerModifiers, sourceKey));
        int removedHp = RemoveKeyedModifiers(hpModifiers, sourceKey);
        if (removedHp != 0)
        {
            CurrentHp = Mathf.Max(0, CurrentHp - removedHp);
            AppendRemovalIfNonZero(removed, EffectStatTarget.HP, removedHp);
        }

        AppendRemovalIfNonZero(removed, EffectStatTarget.Cost, RemoveKeyedModifiers(costModifiers, sourceKey));
        AppendRemovalIfNonZero(removed, EffectStatTarget.Level, RemoveKeyedModifiers(levelModifiers, sourceKey));
        AppendRemovalIfNonZero(removed, EffectStatTarget.EffectDamage, RemoveKeyedModifiers(effectDamageModifiers, sourceKey));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.EffectDamageImmunity,
            RemoveKeyedModifiers(effectDamageImmunityModifiers, sourceKey));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.IncomingDamageReduction,
            RemoveKeyedModifiers(incomingDamageReductionModifiers, sourceKey));
        return removed;
    }

    /// <summary>指定ユニットが付与した UnitGranted / OwnerTurnField 修飾をまとめて除去する。</summary>
    public List<StatModifierRemoval> RemoveStatModifiersGrantedByBattleInstance(int grantingBattleInstanceId)
    {
        List<StatModifierRemoval> removed = new List<StatModifierRemoval>();
        if (grantingBattleInstanceId <= 0)
        {
            return removed;
        }

        removed.AddRange(RemoveStatModifiersBySourceDetailed(MakeUnitGrantedSourceKey(grantingBattleInstanceId)));

        string ownerTurnPrefix = $"OwnerTurnField:{grantingBattleInstanceId}:";
        AppendRemovalIfNonZero(removed, EffectStatTarget.AP, RemoveKeyedModifiersByPrefix(powerModifiers, ownerTurnPrefix));
        int removedOwnerTurnHp = RemoveKeyedModifiersByPrefix(hpModifiers, ownerTurnPrefix);
        if (removedOwnerTurnHp != 0)
        {
            CurrentHp = Mathf.Max(0, CurrentHp - removedOwnerTurnHp);
            AppendRemovalIfNonZero(removed, EffectStatTarget.HP, removedOwnerTurnHp);
        }

        AppendRemovalIfNonZero(removed, EffectStatTarget.Cost, RemoveKeyedModifiersByPrefix(costModifiers, ownerTurnPrefix));
        AppendRemovalIfNonZero(removed, EffectStatTarget.Level, RemoveKeyedModifiersByPrefix(levelModifiers, ownerTurnPrefix));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.EffectDamage,
            RemoveKeyedModifiersByPrefix(effectDamageModifiers, ownerTurnPrefix));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.EffectDamageImmunity,
            RemoveKeyedModifiersByPrefix(effectDamageImmunityModifiers, ownerTurnPrefix));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.IncomingDamageReduction,
            RemoveKeyedModifiersByPrefix(incomingDamageReductionModifiers, ownerTurnPrefix));
        return removed;
    }

    private static void AppendRemovalIfNonZero(
        List<StatModifierRemoval> removed,
        EffectStatTarget statTarget,
        int signedTotal)
    {
        if (signedTotal == 0)
        {
            return;
        }

        removed.Add(new StatModifierRemoval
        {
            StatTarget = statTarget,
            SignedTotal = signedTotal,
            Duration = EffectDuration.Permanent,
        });
    }

    private static int SumRemovalByStatTarget(List<StatModifierRemoval> removed, EffectStatTarget statTarget)
    {
        int sum = 0;
        for (int i = 0; i < removed.Count; i++)
        {
            if (removed[i].StatTarget == statTarget)
            {
                sum += removed[i].SignedTotal;
            }
        }

        return sum;
    }

    private static int RemoveKeyedModifiersByPrefix(List<StatModifier> modifiers, string prefix)
    {
        if (modifiers == null || string.IsNullOrEmpty(prefix))
        {
            return 0;
        }

        int sum = 0;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            string key = modifiers[i].sourceKey;
            if (key != null && key.StartsWith(prefix))
            {
                sum += modifiers[i].value;
                modifiers.RemoveAt(i);
            }
        }

        return sum;
    }

    private static int RemoveKeyedModifiers(List<StatModifier> modifiers, string sourceKey)
    {
        int sum = 0;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].sourceKey == sourceKey)
            {
                sum += modifiers[i].value;
                modifiers.RemoveAt(i);
            }
        }

        return sum;
    }

    public void ClearTimedStatModifiersByDuration(EffectDuration duration)
    {
        ClearModifierListByDuration(powerModifiers, duration);
        int clearedHp = ClearModifierListByDurationReturningSum(hpModifiers, duration);
        if (clearedHp != 0)
        {
            CurrentHp = Mathf.Max(0, CurrentHp - clearedHp);
        }

        ClearModifierListByDuration(costModifiers, duration);
        ClearModifierListByDuration(levelModifiers, duration);
        ClearModifierListByDuration(effectDamageModifiers, duration);
        ClearModifierListByDuration(effectDamageImmunityModifiers, duration);
        ClearModifierListByDuration(incomingDamageReductionModifiers, duration);
        RefreshBattleStatOverlay(force: true);
    }

    public void ClearPowerModifiersByDuration(EffectDuration duration)
    {
        ClearTimedStatModifiersByDuration(duration);
    }

    private static int SumModifierValues(List<StatModifier> modifiers)
    {
        int sum = 0;
        for (int i = 0; i < modifiers.Count; i++)
        {
            sum += modifiers[i].value;
        }

        return sum;
    }

    /// <summary>指定 Duration の AP（パワー）補正合計。</summary>
    public int GetPowerModifierSumByDuration(EffectDuration duration)
    {
        int sum = 0;
        for (int i = 0; i < powerModifiers.Count; i++)
        {
            if (powerModifiers[i].duration == duration)
            {
                sum += powerModifiers[i].value;
            }
        }

        return sum;
    }

    private static void ClearModifierListByDuration(List<StatModifier> modifiers, EffectDuration duration)
    {
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].duration == duration)
            {
                modifiers.RemoveAt(i);
            }
        }
    }

    private static int ClearModifierListByDurationReturningSum(List<StatModifier> modifiers, EffectDuration duration)
    {
        int sum = 0;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].duration == duration)
            {
                sum += modifiers[i].value;
                modifiers.RemoveAt(i);
            }
        }

        return sum;
    }

    public bool CanMountPilot()
    {
        return Data != null
            && Data.IsUnitLike()
            && !Data.cannotMountPilot
            && MountedPilot == null;
    }

    public bool TryAttachPilot(CardController pilot)
    {
        if (!CanMountPilot() || pilot == null || pilot.Data == null || !pilot.Data.IsPilot())
        {
            return false;
        }

        MountedPilot = pilot;
        pilot.MountedUnit = this;
        pilot.SetAttackFlg(AttackFlg.False);
        pilot.SetEligibleForShieldZoneDeploy(false);
        SetEligibleForShieldZoneDeploy(false);

        RectTransform pilotRt = pilot.transform as RectTransform;
        RectTransform unitRt = transform as RectTransform;
        if (pilotRt != null && unitRt != null)
        {
            pilotRt.SetParent(transform, false);

            // ユニット右下に小さく重ねる（下敷き表示はしない）
            pilotRt.anchorMin = new Vector2(0.58f, 0f);
            pilotRt.anchorMax = new Vector2(1f, 0.42f);
            pilotRt.pivot = new Vector2(1f, 0f);
            pilotRt.offsetMin = new Vector2(2f, 2f);
            pilotRt.offsetMax = new Vector2(-2f, -2f);
            pilotRt.anchoredPosition = Vector2.zero;
            pilotRt.localScale = Vector3.one;
            pilotRt.localRotation = Quaternion.identity;

            LayoutElement le = pilot.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = pilot.gameObject.AddComponent<LayoutElement>();
            }
            le.ignoreLayout = true;

            // 旧・下敷き用フェイスレイヤーは不要
            if (unitFaceTopLayer != null)
            {
                Destroy(unitFaceTopLayer.gameObject);
                unitFaceTopLayer = null;
            }

            if (cardImage != null)
            {
                cardImage.enabled = true;
            }

            pilotRt.SetAsLastSibling();
            pilot.SetBattleStatOverlayVisible(false);
            BringBattleStatOverlayToFront();
        }

        Image pilotImage = pilot.GetComponent<Image>();
        if (pilotImage != null)
        {
            pilotImage.raycastTarget = false;
        }

        pilotPowerBonus += Mathf.Max(0, pilot.Data.power);
        CurrentHp += Mathf.Max(0, pilot.Data.hp);
        return true;
    }

    /// <summary>搭乗パイロットを外す（破棄しない）。ユニット側のボーナスを戻す。</summary>
    public CardController DetachMountedPilotWithoutDestroy()
    {
        CardController pilot = MountedPilot;
        if (pilot == null || pilot.Data == null)
        {
            return null;
        }

        pilotPowerBonus = Mathf.Max(0, pilotPowerBonus - Mathf.Max(0, pilot.Data.power));
        CurrentHp = Mathf.Max(0, CurrentHp - Mathf.Max(0, pilot.Data.hp));
        MountedPilot = null;
        pilot.MountedUnit = null;

        // 【リンク中】自身バフ等（UnitGranted:自身）を搭乗解除で落とす
        if (BattleInstanceId > 0)
        {
            RemoveStatModifiersBySourceDetailed(MakeUnitGrantedSourceKey(BattleInstanceId));
        }

        RectTransform pilotRt = pilot.transform as RectTransform;
        if (pilotRt != null)
        {
            pilotRt.SetParent(null, false);
            LayoutElement le = pilot.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.ignoreLayout = false;
            }
        }

        Image pilotImage = pilot.GetComponent<Image>();
        if (pilotImage != null)
        {
            pilotImage.raycastTarget = true;
        }

        return pilot;
    }

    /// <summary>ユニットの搭乗・戦場用レイヤーを手札表示向けに戻す。</summary>
    public void CleanupUnitBattleMountVisuals()
    {
        SetBattleStatOverlayVisible(false);
        if (unitFaceTopLayer != null)
        {
            Destroy(unitFaceTopLayer.gameObject);
            unitFaceTopLayer = null;
        }

        if (cardImage != null)
        {
            cardImage.enabled = true;
        }
    }

    private void EnsureUnitFaceTopLayer()
    {
        if (unitFaceTopLayer != null || cardImage == null)
        {
            return;
        }

        GameObject layer = new GameObject("UnitFaceTopLayer", typeof(RectTransform), typeof(Image));
        RectTransform layerRt = layer.GetComponent<RectTransform>();
        layerRt.SetParent(transform, false);
        layerRt.anchorMin = Vector2.zero;
        layerRt.anchorMax = Vector2.one;
        layerRt.offsetMin = Vector2.zero;
        layerRt.offsetMax = Vector2.zero;

        unitFaceTopLayer = layer.GetComponent<Image>();
        unitFaceTopLayer.sprite = cardImage.sprite;
        unitFaceTopLayer.preserveAspect = true;
        unitFaceTopLayer.raycastTarget = true;

        // ルートの画像は非表示にして、トップレイヤー画像を正面として扱う。
        cardImage.enabled = false;
    }

    public void RefreshVisualSpriteFromData()
    {
        if (cardImage != null)
        {
            cardImage.sprite = cardSprite;
        }

        if (unitFaceTopLayer != null)
        {
            unitFaceTopLayer.sprite = cardSprite;
        }
    }

    /// <summary>
    /// Addressables（Local）を優先して非同期ロード。未登録・失敗時は CardData の Sprite 参照へフォールバック。
    /// </summary>
    private void BeginResolveCardSprite(CardData carddata)
    {
        ReleaseAddressableSpriteHandle();
        _spriteLoadGeneration++;
        int generation = _spriteLoadGeneration;

        // フォールバックを先に表示（Addressables 未登録カードでも即表示）
        cardSprite = CardSpriteLoader.ResolveEmbeddedSprite(carddata);
        RefreshVisualSpriteFromData();

        CardSpriteLoader.LoadForCardAsync(carddata, handle =>
        {
            if (generation != _spriteLoadGeneration)
            {
                CardSpriteLoader.Release(handle);
                return;
            }

            if (!handle.IsValid())
            {
                return;
            }

            _addressableSpriteHandle = handle;
            ApplyAddressableSpriteResult(handle, generation);
        });
    }

    private void ApplyAddressableSpriteResult(AsyncOperationHandle<Sprite> handle, int generation)
    {
        if (generation != _spriteLoadGeneration)
        {
            // 別カードへ差し替え済み。ハンドルは新しい BeginResolve 側で解放済み。
            return;
        }

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            cardSprite = handle.Result;
            RefreshVisualSpriteFromData();
            return;
        }

        // Addressables に無い／失敗 → 埋め込み参照のまま。ハンドルは解放。
        ReleaseAddressableSpriteHandle();
        if (cardSprite == null)
        {
            cardSprite = CardSpriteLoader.ResolveEmbeddedSprite(Data);
            RefreshVisualSpriteFromData();
        }
    }

    private void ReleaseAddressableSpriteHandle()
    {
        if (_addressableSpriteHandle.IsValid())
        {
            CardSpriteLoader.Release(_addressableSpriteHandle);
        }

        _addressableSpriteHandle = default;
    }
}
