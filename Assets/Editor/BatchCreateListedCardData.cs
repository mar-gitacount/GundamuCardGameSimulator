#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 指定リストの CardData を一括作成／更新（色・名前・画像）。
/// メニュー: Tools/Cards/Create Listed CardData (Color + Image)
/// </summary>
public static class BatchCreateListedCardData
{
    private const string CardsFolder = "Assets/Resources/Data/Cards";
    private const string ImagesFolder = "Assets/Resources/Data/Images";

    [MenuItem("Tools/Cards/Create Listed CardData (Color + Image)")]
    public static void CreateOrUpdate()
    {
        var entries = BuildEntries();
        int nextId = FindMaxCardId() + 1;
        int created = 0;
        int updated = 0;
        var missingImages = new List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var entry in entries)
            {
                CardData existing = FindCardByName(entry.cardName);
                Sprite sprite = FindSprite(entry.cardName, entry.imageFallbacks);
                if (sprite == null)
                {
                    missingImages.Add(entry.cardName);
                }

                if (existing != null)
                {
                    existing.color = entry.color;
                    existing.cardName = entry.cardName;
                    if (sprite != null)
                    {
                        existing.imageName = sprite;
                    }

                    ApplyFreedomAscensionOffline(existing);
                    EditorUtility.SetDirty(existing);
                    updated++;
                    continue;
                }

                CardData card = ScriptableObject.CreateInstance<CardData>();
                card.id = nextId++;
                card.cardName = entry.cardName;
                card.color = entry.color;
                card.imageName = sprite;
                card.timedEffects = new List<TimedEffectData>();
                card.features = new List<CardFeatureData>();
                card.pilotIds = new List<CardPilotIdData>();
                card.link = new List<UnitLinkPilotSlot>();
                ApplyFreedomAscensionOffline(card);

                string safeName = SanitizeFileName(entry.cardName);
                string assetPath = $"{CardsFolder}/{card.id}{safeName}.asset";
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                AssetDatabase.CreateAsset(card, assetPath);
                created++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"作成: {created} / 更新: {updated} / 合計対象: {entries.Count}");
        sb.AppendLine("収録: Freedom Ascension / オンライン: 不可");
        if (missingImages.Count > 0)
        {
            sb.AppendLine("画像なし:");
            foreach (string name in missingImages)
            {
                sb.AppendLine(" - " + name);
            }
        }

        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("CardData 一括作成", sb.ToString(), "OK");
    }

    /// <summary>ブースター Freedom Ascension 収録＋オンライン使用不可。</summary>
    private static void ApplyFreedomAscensionOffline(CardData card)
    {
        if (card == null)
        {
            return;
        }

        card.productLine = CardProductLine.Booster;
        card.boosterSet = BoosterProductSet.FreedomAscension;
        card.starterSet = StarterProductSet.None;
        card.eternalBoosterSet = EternalBoosterProductSet.None;
        card.SyncProductFieldsFromLine();
        card.notUsedOnline = true;
    }

    private struct Entry
    {
        public string cardName;
        public CardColor color;
        public string[] imageFallbacks;
    }

    private static List<Entry> BuildEntries()
    {
        var list = new List<Entry>();

        void Add(CardColor color, string name, params string[] fallbacks)
        {
            list.Add(new Entry
            {
                cardName = name,
                color = color,
                imageFallbacks = fallbacks
            });
        }

        // Blue
        Add(CardColor.Blue, "Calamity Gundam & Raider Gundam");
        Add(CardColor.Blue, "Forbidden Gundam");
        Add(CardColor.Blue, "Andrew Waldfeld");
        Add(CardColor.Blue, "Cagalli Yula Athha");
        Add(CardColor.Blue, "Odelo Henrik");
        Add(CardColor.Blue, "Wings of Light");
        Add(CardColor.Blue, "Not with Scattershot!");
        Add(CardColor.Blue, "At the Risk of One's Life");
        Add(CardColor.Blue, "Exclusively Defense-Oriented Policy");
        Add(CardColor.Blue, "Desultor");
        Add(CardColor.Blue, "Lauda Neill");
        Add(CardColor.Blue, "Archangel");
        Add(CardColor.Blue, "White Ark");

        // Green (gleen)
        Add(CardColor.Green, "Gundam AGE-2 Double Bullet");
        Add(CardColor.Green, "Gundam Schwarzette");
        Add(CardColor.Green, "Gundam AGE-2 Normal (SP Ver.)");
        Add(CardColor.Green, "Demi Barding");
        Add(CardColor.Green, "Michaelis");
        Add(CardColor.Green, "Guel's Dilanza");
        Add(CardColor.Green, "Mutual Attraction");
        Add(CardColor.Green, "Interwoven Blessings");
        Add(CardColor.Green, "Overcoming Hardships");
        Add(CardColor.Green, "Felsi's Plea");
        Add(CardColor.Green, "Quiet Zero");

        // Red
        Add(CardColor.Red, "Gaia Gundam");
        Add(CardColor.Red, "Destroy Gundam");
        Add(CardColor.Red, "Gundam Throne Eins (GN High Mega Launcher)");
        Add(CardColor.Red, "Chaos Gundam");
        Add(CardColor.Red, "Abyss Gundam");
        Add(CardColor.Red, "Gaia Gundam (MA Mode)");
        Add(CardColor.Red, "Chaos Gundam (MA Mode)");
        Add(CardColor.Red, "Abyss Gundam (MA Mode)");
        Add(CardColor.Red, "Gundam Kyrios (Flight Mode)");
        Add(CardColor.Red, "Stellar Loussier");
        Add(CardColor.Red, "Auel Neider");

        // Purple (puple)
        Add(CardColor.Purple, "Gundam Exia Repair");
        Add(CardColor.Purple, "Gundam Barbatos Lupus Rex");
        Add(CardColor.Purple, "Destiny Gundam");
        Add(CardColor.Purple, "Shiden Custom (Ryusei-Go)");
        Add(CardColor.Purple, "Gundam Barbatos Lupus");
        Add(CardColor.Purple, "Gundam Flauros (Ryusei-Go)");
        Add(CardColor.Purple, "Force Impulse Gundam");
        Add(CardColor.Purple, "Landman Rodi");
        Add(CardColor.Purple, "Chad Chadan");
        Add(CardColor.Purple, "Become a Shield");

        // White
        Add(CardColor.White, "Wing Gundam Zero (EW)");
        Add(CardColor.White, "Tallgeese", "Tallgeese3", "63_Tallgeese");
        Add(CardColor.White, "Gundam Sandrock Custom (EW)");
        Add(CardColor.White, "Altron Gundam (EW)");
        Add(CardColor.White, "Noin's Taurus");
        Add(CardColor.White, "Leo");
        Add(CardColor.White, "Gundam Deathscythe Hell (EW)");
        Add(CardColor.White, "Gundam Heavyarms Custom (EW)");
        Add(CardColor.White, "Gavane's Borjarnon");
        Add(CardColor.White, "Heero Yuy");
        Add(CardColor.White, "Trowa Barton");
        Add(CardColor.White, "Quatre Raberba Winner");
        Add(CardColor.White, "Presidential Office");

        return list;
    }

    private static Sprite FindSprite(string cardName, string[] fallbacks)
    {
        Sprite sprite = LoadSpriteByName(cardName);
        if (sprite != null)
        {
            return sprite;
        }

        if (fallbacks != null)
        {
            for (int i = 0; i < fallbacks.Length; i++)
            {
                sprite = LoadSpriteByName(fallbacks[i]);
                if (sprite != null)
                {
                    return sprite;
                }
            }
        }

        return null;
    }

    private static Sprite LoadSpriteByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string path = $"{ImagesFolder}/{name}.png";
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        if (assets == null || assets.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Sprite s)
            {
                return s;
            }
        }

        return null;
    }

    private static CardData FindCardByName(string cardName)
    {
        string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { CardsFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card == null || string.IsNullOrEmpty(card.cardName))
            {
                continue;
            }

            if (card.cardName.Trim() == cardName)
            {
                return card;
            }
        }

        return null;
    }

    private static int FindMaxCardId()
    {
        int max = 0;
        string[] guids = AssetDatabase.FindAssets("t:CardData", new[] { CardsFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card != null && card.id > max && card.id < 10000)
            {
                max = card.id;
            }
        }

        return max;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Card";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool bad = false;
            for (int j = 0; j < invalid.Length; j++)
            {
                if (c == invalid[j])
                {
                    bad = true;
                    break;
                }
            }

            sb.Append(bad ? '_' : c);
        }

        return sb.ToString();
    }
}
#endif
