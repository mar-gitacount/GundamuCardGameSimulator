
using UnityEngine;   // ← これが必須
using System;        // ← これも必要（[Serializable]属性のため）
using System.Collections.Generic; // ← これも必要（List<T>のため）
[CreateAssetMenu(menuName = "Game/Card")]
public class CardData : ScriptableObject
{
    public int id;

    [Tooltip("公式 GCG カードID（例: ST01-001）。下の gcgId を入力すると自動で入る。手動編集も可。")]
    public string gcgOfficialId;

    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public Sprite imageName;
    public Sprite image;

    [Tooltip("Addressables のアドレス（Local）。例: Data/Images/70_Mikazuki Augus。ランタイムはこれを使い、imageName/image の直参照は使わない。")]
    public string imageAddress;

    public int version;
    public CardSourceType sourceType;

    [Header("収録セット（プルダウン）")]
    [Tooltip("ブースター / スターター / Eternal Booster。セット名は下の各プルダウンで指定。")]
    public CardProductLine productLine;
    [Tooltip("productLine がブースターのとき設定する。")]
    public BoosterProductSet boosterSet;
    [Tooltip("productLine がスターターのとき設定する。")]
    public StarterProductSet starterSet;
    [Tooltip("productLine が Eternal Booster のとき設定する。")]
    public EternalBoosterProductSet eternalBoosterSet;
    [Tooltip("作品（シリーズ）タイトル。")]
    public CardSourceTitle sourceTitle;

    public FilterType filterType;
    public CardColor color;
    [Tooltip("カード種類（ユニット / パイロット / コマンド / ベース / EXリソース / ユニットトークン / コマンドパイロット）。")]
    public Type type;

    [Header("地形（Zone）")]
    [Tooltip("配備可能な地形。Space と Earth を同時指定可。None は未設定（制限なし）。")]
    public CardBattleZone battleZones = CardBattleZone.None;

    /// <summary>ユニット（Type.Unit）向け。アセット上の既定値。実行時は CardController で上書き。</summary>
    [Tooltip("ユニットのみ。配備ターンは False（isDeployTurnAttack 時は True）。Link 条件搭乗で同日 True。次の自分ターン開始で True。")]
    public AttackFlg attackFlg = AttackFlg.False;
    [Tooltip("カード効果定義（タイミング別）。")]
    public List<TimedEffectData> timedEffects = new List<TimedEffectData>();

    [Tooltip("カード特性（複数可）。マスタは Game/Card Feature または Resources/Data/Features。")]
    public List<CardFeatureData> features = new List<CardFeatureData>();

    [Tooltip("パイロット識別子（複数可）。同一人物の別カードは同じ PilotId を共有する。一覧は Resources/Data/Json/pilot_master.json。")]
    public List<CardPilotIdData> pilotIds = new List<CardPilotIdData>();

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

    [Tooltip("true のときオンライン対戦では使用不可（デッキへの追加は可能）。デフォルト false＝オンライン可。")]
    public bool notUsedOnline;

    [Tooltip("true のときカード一覧（カタログ／検索結果）に表示しない。FindById 等の参照・既存デッキ内表示には影響しない。")]
    public bool hideFromCardList;

    [Tooltip("true のときパイロットをセットできない（有線式アーム等）。")]
    public bool cannotMountPilot;

    [Header("公式カード番号（GCG）")]
    [Tooltip("種別・セット番号・カード番号。1 と 1 なら ST01-001。入力した数値がそのまま使われる。")]
    public GcgIdParts gcgId = new GcgIdParts();

#if UNITY_EDITOR
    private void OnValidate()
    {
        SyncProductFieldsFromLine();
        SyncGcgOfficialIdFromParts();
    }
#endif

    /// <summary>公式 detailSearch 用キー（例: ST01-001）。</summary>
    public string GetGcgDetailSearchId()
    {
        if (gcgId != null && gcgId.IsComplete())
        {
            return gcgId.FormatId();
        }

        return string.IsNullOrWhiteSpace(gcgOfficialId) ? string.Empty : gcgOfficialId.Trim();
    }

    public bool HasGcgStNum()
    {
        return gcgId != null && gcgId.IsComplete();
    }

    public bool HasOfficialCardNumberParts()
    {
        return HasGcgStNum();
    }

    public string ResolveGcgPrefix()
    {
        if (gcgId != null && gcgId.setKind != GcgOfficialSetKind.Unset)
        {
            return gcgId.ResolvePrefix();
        }

        switch (productLine)
        {
            case CardProductLine.Starter:
                return "ST";
            case CardProductLine.Booster:
                return "GD";
            case CardProductLine.EternalBooster:
                return "EB";
            default:
                return string.Empty;
        }
    }

    public bool HasGcgSetKind()
    {
        return gcgId != null && gcgId.setKind != GcgOfficialSetKind.Unset;
    }

    public bool HasCompleteGcgOfficialNumber()
    {
        return gcgId != null && gcgId.IsComplete();
    }

    /// <summary>gcgId の入力値から gcgOfficialId を作る（ST01-001 / T-001 形式）。</summary>
    public void SyncGcgOfficialIdFromParts()
    {
        if (gcgId == null)
        {
            return;
        }

        // トークンはセット番号を使わない
        if (gcgId.IsToken() && gcgId.setNumber != 0)
        {
            gcgId.setNumber = 0;
        }

        if (!gcgId.IsComplete())
        {
            return;
        }

        gcgOfficialId = gcgId.FormatId();
    }

    public void SyncGcgOfficialIdFromStNum()
    {
        SyncGcgOfficialIdFromParts();
    }

    /// <summary>productLine に合わせて sourceType / version / 未使用セットを揃える。</summary>
    public void SyncProductFieldsFromLine()
    {
        switch (productLine)
        {
            case CardProductLine.Booster:
                sourceType = CardSourceType.Booster;
                if (boosterSet != BoosterProductSet.None)
                {
                    version = (int)boosterSet;
                }

                starterSet = StarterProductSet.None;
                eternalBoosterSet = EternalBoosterProductSet.None;
                break;
            case CardProductLine.Starter:
                sourceType = CardSourceType.Starter;
                if (starterSet != StarterProductSet.None)
                {
                    version = (int)starterSet;
                }

                boosterSet = BoosterProductSet.None;
                eternalBoosterSet = EternalBoosterProductSet.None;
                break;
            case CardProductLine.EternalBooster:
                sourceType = CardSourceType.EternalBooster;
                if (eternalBoosterSet != EternalBoosterProductSet.None)
                {
                    version = (int)eternalBoosterSet;
                }

                boosterSet = BoosterProductSet.None;
                starterSet = StarterProductSet.None;
                break;
        }
    }

    /// <summary>JSON 用の画像ファイル名（拡張子なし）。imageAddress 優先。</summary>
    public string GetImageLeafNameForJson()
    {
        if (!string.IsNullOrWhiteSpace(imageAddress))
        {
            string addr = imageAddress.Trim();
            const string prefix = "Data/Images/";
            if (addr.StartsWith(prefix))
            {
                return addr.Substring(prefix.Length);
            }

            int slash = addr.LastIndexOf('/');
            return slash >= 0 ? addr.Substring(slash + 1) : addr;
        }

        if (imageName != null)
        {
            return imageName.name;
        }

        if (image != null)
        {
            return image.name;
        }

        return string.Empty;
    }

    /// <summary>画像識別をアドレスのみにする（Sprite 直参照はクリア）。</summary>
    public void SetImageAddressFromLeaf(string leafName)
    {
        imageName = null;
        image = null;
        if (string.IsNullOrWhiteSpace(leafName))
        {
            imageAddress = string.Empty;
            return;
        }

        imageAddress = "Data/Images/" + leafName.Trim();
    }

    /// <summary>
    /// features リストが空のとき、Resources 同名カードまたは card_master.json の featureIds から補完する。
    /// </summary>
    public void EnsureFeaturesResolved()
    {
        if (features != null)
        {
            for (int i = features.Count - 1; i >= 0; i--)
            {
                if (features[i] == null)
                {
                    features.RemoveAt(i);
                }
            }
        }

        if (features != null && features.Count > 0)
        {
            return;
        }

        if (features == null)
        {
            features = new List<CardFeatureData>();
        }

        CardFeatureRegistry.EnsureLoaded();
        CardData[] all = Resources.LoadAll<CardData>("Data/Cards");
        for (int i = 0; i < all.Length; i++)
        {
            CardData source = all[i];
            if (source == null || source.id != id || source.features == null || source.features.Count == 0)
            {
                continue;
            }

            features.AddRange(source.features);
            return;
        }

        if (CardDatabase.Instance != null)
        {
            int[] featureIds = CardDatabase.Instance.GetFeatureIdsFromMasterJson(id);
            if (featureIds != null && featureIds.Length > 0)
            {
                CardFeatureExtensions.SetFeaturesFromIds(this, featureIds);
            }
        }
    }
}


[Serializable]
public class CardJson
{
    public int id;
    public string gcgOfficialId;
    public int gcgSetKind;
    public int gcgSetNumber;
    public int gcgCardNumber;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public string imageName;
    public int version;
    public int sourceType;
    public int productLine;
    public int boosterSet;
    public int starterSet;
    public int eternalBoosterSet;
    public int sourceTitle;
    public int color; // カードの色を追加
    public int type;
    public int battleZones;
    public int[] featureIds;
    public int[] pilotIdIds;
    public bool isBlocker;
    public bool isDeployTurnAttack;
    public bool isNotDirectAttack;
    public bool isShieldToken;
    public bool isRepair;
    public int repairAmount;
    public bool notUsedOnline;
    public bool hideFromCardList;
    public bool cannotMountPilot;
}

[Serializable]
public class CardMasterJson
{
    public List<CardJson> cards = new List<CardJson>();
}

