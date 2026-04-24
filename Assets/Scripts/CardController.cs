using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
public class CardController : MonoBehaviour,IPointerClickHandler
{
    [SerializeField] private Image cardImage;
    

    // !バトルパネルの参照

    public CardData Data { get; private set; }
    private Action<CardController> onClickCallback;
    
    public Sprite cardSprite{ get; private set; }

    /// <summary>ユニットの現在 HP（配備・ドロー時に Data.hp で初期化）。</summary>
    public int CurrentHp { get; private set; }
    public int CurrentPower => Mathf.Max(0, (Data != null ? Data.power : 0) + pilotPowerBonus + effectPowerBonus);
    public CardController MountedPilot { get; private set; }
    public CardController MountedUnit { get; private set; }

    /// <summary>シールド用：表が隠れている間は true（カバーを破棄すると false）。</summary>
    public bool IsShieldFaceHidden => shieldFaceCoverRoot != null;

    private GameObject shieldFaceCoverRoot;
    private int pilotPowerBonus;
    private int effectPowerBonus;
    private static readonly Vector2 PilotOffset = new Vector2(0f, -18f);
    private Image unitFaceTopLayer;

    /// <summary>ランタイムの攻撃フラグ（カードデータのアセットは変更しない）。</summary>
    private AttackFlg _attackFlg = AttackFlg.False;
    public AttackFlg AttackFlgState => _attackFlg;
    public bool IsRestState { get; private set; }

    public void SetUp(CardData carddata,Action<CardController> callback)
    {
        this.Data = carddata;
        
        this.onClickCallback = callback;
        cardSprite = Resources.Load<Sprite>($"Data/Images/{carddata.imageName.name}");
        cardImage.sprite = cardSprite;

        // 手札・新規生成時は常に False（ユニット以外は攻撃フラグを使わない）
        _attackFlg = AttackFlg.False;
        ResetRuntimeStatsFromData();
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
        effectPowerBonus = 0;
        MountedPilot = null;
        MountedUnit = null;
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
        if (Data == null || Data.type != Type.Unit)
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
        Debug.Log($"カードがクリックされました。カードコスト: {Data.cost}");
        Debug.Log("クリックされました");
        onClickCallback?.Invoke(this);
    }

    public int GetCardcost()
    {
        return Data.cost;
    }

    public void AddEffectStatBonus(int powerDelta, int hpDelta)
    {
        effectPowerBonus += powerDelta;
        if (hpDelta != 0)
        {
            CurrentHp = Mathf.Max(0, CurrentHp + hpDelta);
        }
    }

    public bool CanMountPilot()
    {
        return Data != null && Data.type == Type.Unit && MountedPilot == null;
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
}
