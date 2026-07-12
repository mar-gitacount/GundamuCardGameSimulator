using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
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
    

    // !バトルパネルの参照

    public CardData Data { get; private set; }
    private Action<CardController> onClickCallback;
    
    public Sprite cardSprite{ get; private set; }

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
            return Mathf.Max(0, modified);
        }
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

    private GameObject shieldFaceCoverRoot;
    private int pilotPowerBonus;
    private readonly List<StatModifier> powerModifiers = new List<StatModifier>();
    private readonly List<StatModifier> costModifiers = new List<StatModifier>();
    private readonly List<StatModifier> levelModifiers = new List<StatModifier>();
    private readonly List<StatModifier> effectDamageModifiers = new List<StatModifier>();
    private readonly List<StatModifier> effectDamageImmunityModifiers = new List<StatModifier>();
    private readonly List<PilotMountAllyFieldAuraEntry> _pilotMountAllyFieldAuras = new List<PilotMountAllyFieldAuraEntry>();

    /// <summary>効果ダメージ（戦闘交換以外）への実効補正。</summary>
    public int CurrentEffectDamageModifier => SumModifierValues(effectDamageModifiers);

    /// <summary>効果ダメージ無効化レイヤー数（Buff/Debuff の EffectDamageImmunity）。</summary>
    public int CurrentEffectDamageImmunityCount => Mathf.Max(0, SumModifierValues(effectDamageImmunityModifiers));

    /// <summary>効果ダメージ無効化が有効か。</summary>
    public bool HasEffectDamageImmunity => CurrentEffectDamageImmunityCount > 0;
    private static readonly Vector2 PilotOffset = new Vector2(0f, -18f);
    private Image unitFaceTopLayer;

    /// <summary>ランタイムの攻撃フラグ（カードデータのアセットは変更しない）。</summary>
    private AttackFlg _attackFlg = AttackFlg.False;
    public AttackFlg AttackFlgState => _attackFlg;
    public bool IsRestState { get; private set; }

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

    public void AddFirstStrikeUntilEndOfTurnGrant()
    {
        _firstStrikeUntilEndOfTurnDepth++;
    }

    public void ClearFirstStrikeUntilEndOfTurnGrants()
    {
        _firstStrikeUntilEndOfTurnDepth = 0;
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
        this.Data = carddata;
        
        this.onClickCallback = callback;
        cardSprite = ResolveCardSprite(carddata);
        if (cardImage != null)
        {
            cardImage.sprite = cardSprite;
        }

        // 手札・新規生成時は常に False（ユニット以外は攻撃フラグを使わない）
        _attackFlg = AttackFlg.False;
        eligibleForShieldZoneDeploy = false;
        BattleInstanceId = 0;
        ResetRuntimeStatsFromData();
    }

    public void AssignBattleInstanceId(int instanceId)
    {
        BattleInstanceId = Mathf.Max(0, instanceId);
    }

    public void SetCurrentHpForSync(int hp)
    {
        CurrentHp = Mathf.Max(0, hp);
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

        int cap = Data.hp;
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
        costModifiers.Clear();
        levelModifiers.Clear();
        effectDamageModifiers.Clear();
        effectDamageImmunityModifiers.Clear();
        _pilotMountAllyFieldAuras.Clear();
        MountedPilot = null;
        MountedUnit = null;
        _notDirectAttackUntilEndOfTurnDepth = 0;
        _firstStrikeUntilEndOfTurnDepth = 0;
        _turnEndRepairBonus = 0;
    }

    /// <summary>戦闘ダメージ。ユニット以外では呼ばない想定。</summary>
    public void ApplyDamage(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        CurrentHp = Mathf.Max(0, CurrentHp - amount);
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

    /// <summary>
    /// ユニットの表示状態を更新する。
    /// isRest=true: レスト（横向き） / false: アクティブ（起き）
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

        RectTransform rt = transform as RectTransform;
        if (rt == null)
        {
            return;
        }

        IsRestState = isRest;
        float z = isRest ? -90f : 0f;
        rt.localRotation = Quaternion.Euler(0f, 0f, z);
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"カードがクリックされました。カード名前: {Data.cardName}");
        Debug.Log($"カードがクリックされました。カードコスト: {CurrentCost} (base:{Data.cost})");
        Debug.Log("クリックされました");
        onClickCallback?.Invoke(this);
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
        int effectDamageImmunityDelta = 0)
    {
        string key = statModifierSourceKey ?? string.Empty;
        if (powerDelta != 0)
        {
            powerModifiers.Add(new StatModifier { value = powerDelta, duration = duration, sourceKey = key });
        }

        if (hpDelta != 0)
        {
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
            case EffectStatTarget.Cost:
                return costModifiers;
            case EffectStatTarget.Level:
                return levelModifiers;
            case EffectStatTarget.EffectDamage:
                return effectDamageModifiers;
            case EffectStatTarget.EffectDamageImmunity:
                return effectDamageImmunityModifiers;
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
        AppendRemovalIfNonZero(removed, EffectStatTarget.Cost, RemoveKeyedModifiers(costModifiers, sourceKey));
        AppendRemovalIfNonZero(removed, EffectStatTarget.Level, RemoveKeyedModifiers(levelModifiers, sourceKey));
        AppendRemovalIfNonZero(removed, EffectStatTarget.EffectDamage, RemoveKeyedModifiers(effectDamageModifiers, sourceKey));
        AppendRemovalIfNonZero(
            removed,
            EffectStatTarget.EffectDamageImmunity,
            RemoveKeyedModifiers(effectDamageImmunityModifiers, sourceKey));
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
        ClearModifierListByDuration(costModifiers, duration);
        ClearModifierListByDuration(levelModifiers, duration);
        ClearModifierListByDuration(effectDamageModifiers, duration);
        ClearModifierListByDuration(effectDamageImmunityModifiers, duration);
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

    public bool CanMountPilot()
    {
        return Data != null && Data.IsUnitLike() && MountedPilot == null;
    }

    public bool TryAttachPilot(CardController pilot)
    {
        if (!CanMountPilot() || pilot == null || pilot.Data == null || pilot.Data.type != Type.Pilot)
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
            // ユニットと同一サイズで固定（Stretch）し、少しだけ下にずらす。
            pilotRt.anchorMin = Vector2.zero;
            pilotRt.anchorMax = Vector2.one;
            pilotRt.pivot = new Vector2(0.5f, 0.5f);
            pilotRt.offsetMin = Vector2.zero;
            pilotRt.offsetMax = Vector2.zero;
            pilotRt.anchoredPosition = PilotOffset;
            pilotRt.localScale = Vector3.one;
            pilotRt.localRotation = Quaternion.identity;

            // 親ユニットの同一 GameObject 配下で重ねる。レイアウト計算には参加させない。
            LayoutElement le = pilot.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = pilot.gameObject.AddComponent<LayoutElement>();
            }
            le.ignoreLayout = true;

            EnsureUnitFaceTopLayer();
            pilotRt.SetAsFirstSibling();
            if (unitFaceTopLayer != null)
            {
                unitFaceTopLayer.transform.SetAsLastSibling();
            }
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
    /// CardData に設定済みの Sprite を優先。未設定時のみ Resources から名前解決する。
    /// Multiple スプライト（名前が *_0）でも Inspector 参照ならそのまま使える。
    /// </summary>
    private static Sprite ResolveCardSprite(CardData carddata)
    {
        if (carddata == null)
        {
            return null;
        }

        if (carddata.imageName != null)
        {
            return carddata.imageName;
        }

        if (carddata.image != null)
        {
            return carddata.image;
        }

        return null;
    }
}
