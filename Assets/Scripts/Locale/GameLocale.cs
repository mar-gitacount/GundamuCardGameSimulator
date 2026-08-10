using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 日英ロケール。PlayerPrefs に保存し、フォントと文言切替の中心になる。
/// 日本語=SourceHanSansJP / 英語=LiberationSans（通常の TMP フォント）。
/// </summary>
public static class GameLocale
{
    public const string PrefsKey = "GameLocale.Language";

    private const string JapaneseFontResourcePath = "SourceHanSansJP-Regular SDF";
    private const string EnglishFontResourcePath = "Fonts & Materials/LiberationSans SDF";

    private static TMP_FontAsset _japaneseFont;
    private static TMP_FontAsset _englishFont;
    private static bool _booted;

    public static GameLanguage Current { get; private set; } = GameLanguage.Japanese;

    public static event Action<GameLanguage> LanguageChanged;

    public static bool IsJapanese => Current == GameLanguage.Japanese;
    public static bool IsEnglish => Current == GameLanguage.English;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (_booted)
        {
            return;
        }

        _booted = true;
        int saved = PlayerPrefs.GetInt(PrefsKey, (int)GameLanguage.Japanese);
        Current = saved == (int)GameLanguage.English
            ? GameLanguage.English
            : GameLanguage.Japanese;
    }

    public static void SetLanguage(GameLanguage language)
    {
        Boot();
        if (Current == language)
        {
            ApplyFontsToAllTmp();
            LocalizedTmpText.RefreshAll();
            return;
        }

        Current = language;
        PlayerPrefs.SetInt(PrefsKey, (int)language);
        PlayerPrefs.Save();
        ApplyFontsToAllTmp();
        LocalizedTmpText.RefreshAll();
        LanguageChanged?.Invoke(Current);
        Debug.Log($"[GameLocale] Language -> {Current}");
    }

    public static void ToggleLanguage()
    {
        SetLanguage(IsJapanese ? GameLanguage.English : GameLanguage.Japanese);
    }

    /// <summary>言語に応じて ja / en のどちらかを返す。</summary>
    public static string T(string japanese, string english)
    {
        Boot();
        return IsJapanese ? (japanese ?? string.Empty) : (english ?? string.Empty);
    }

    /// <summary>カタログキーから文言を返す。</summary>
    public static string TKey(string key)
    {
        Boot();
        return LocaleCatalog.Get(key, Current);
    }

    public static TMP_FontAsset GetUiFont()
    {
        Boot();
        return IsJapanese ? GetJapaneseFont() : GetEnglishFont();
    }

    public static void ApplyFont(TMP_Text tmp)
    {
        if (tmp == null)
        {
            return;
        }

        TMP_FontAsset font = GetUiFont();
        if (font == null)
        {
            return;
        }

        tmp.font = font;
        if (font.material != null)
        {
            tmp.fontSharedMaterial = font.material;
        }

        // 動的アトラスへ現行テキストの字形を取り込む
        if (!string.IsNullOrEmpty(tmp.text))
        {
            tmp.ForceMeshUpdate(true);
        }
    }

    public static void ApplyFontsToAllTmp()
    {
        TMP_FontAsset font = GetUiFont();
        if (font == null)
        {
            return;
        }

        TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text tmp = texts[i];
            if (tmp == null)
            {
                continue;
            }

            tmp.font = font;
            if (font.material != null)
            {
                tmp.fontSharedMaterial = font.material;
            }

            if (!string.IsNullOrEmpty(tmp.text))
            {
                tmp.ForceMeshUpdate(true);
            }
        }
    }

    public static TMP_FontAsset GetJapaneseFont()
    {
        if (_japaneseFont != null)
        {
            EnsureJapaneseFallback(_japaneseFont);
            return _japaneseFont;
        }

        _japaneseFont = Resources.Load<TMP_FontAsset>(JapaneseFontResourcePath);
        if (_japaneseFont == null)
        {
            _japaneseFont = Resources.Load<TMP_FontAsset>("Fonts/" + JapaneseFontResourcePath);
        }

        if (_japaneseFont == null)
        {
            Debug.LogWarning($"[GameLocale] JP font missing: Resources/{JapaneseFontResourcePath}");
            _japaneseFont = CreateOsJapaneseFontAsset();
        }

        EnsureJapaneseFallback(_japaneseFont);
        return _japaneseFont;
    }

    /// <summary>
    /// SourceHan のアトラスに無い漢字・かなを OS フォントで埋める（□化け対策）。
    /// </summary>
    private static void EnsureJapaneseFallback(TMP_FontAsset primary)
    {
        if (primary == null)
        {
            return;
        }

        // Dynamic + Multi Atlas を強制（欠けた字形を実行時追加できるようにする）
        if (primary.atlasPopulationMode != AtlasPopulationMode.Dynamic)
        {
            primary.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        }

        primary.isMultiAtlasTexturesEnabled = true;

        TMP_FontAsset fallback = _japaneseOsFallbackFont;
        if (fallback == null)
        {
            fallback = CreateOsJapaneseFontAsset();
            _japaneseOsFallbackFont = fallback;
        }

        if (fallback == null || fallback == primary)
        {
            return;
        }

        if (primary.fallbackFontAssetTable == null)
        {
            primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
        }

        if (!primary.fallbackFontAssetTable.Contains(fallback))
        {
            primary.fallbackFontAssetTable.Add(fallback);
        }
    }

    private static TMP_FontAsset _japaneseOsFallbackFont;

    private static TMP_FontAsset CreateOsJapaneseFontAsset()
    {
        string[] candidates =
        {
            "Yu Gothic UI",
            "Yu Gothic",
            "Meiryo UI",
            "Meiryo",
            "MS Gothic",
            "Hiragino Sans",
            "Noto Sans CJK JP"
        };

        try
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(candidates, 48);
            if (osFont == null)
            {
                return null;
            }

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(osFont);
            if (asset != null)
            {
                asset.name = "RuntimeOsJapaneseFallback";
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                asset.isMultiAtlasTexturesEnabled = true;
                Debug.Log("[GameLocale] OS JP fallback font: " + osFont.name);
            }

            return asset;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GameLocale] OS JP fallback failed: " + ex.Message);
            return null;
        }
    }

    public static TMP_FontAsset GetEnglishFont()
    {
        if (_englishFont != null)
        {
            return _englishFont;
        }

        _englishFont = Resources.Load<TMP_FontAsset>(EnglishFontResourcePath);
        if (_englishFont == null)
        {
            _englishFont = Resources.Load<TMP_FontAsset>("LiberationSans SDF");
        }

        if (_englishFont == null
            && TMP_Settings.defaultFontAsset != null
            && (TMP_Settings.defaultFontAsset.name == null
                || TMP_Settings.defaultFontAsset.name.IndexOf("SourceHan", StringComparison.OrdinalIgnoreCase) < 0))
        {
            _englishFont = TMP_Settings.defaultFontAsset;
        }

        if (_englishFont == null)
        {
            Debug.LogWarning($"[GameLocale] EN font missing: Resources/{EnglishFontResourcePath}");
        }

        return _englishFont;
    }
}

/// <summary>よく使う UI 文言の簡易カタログ。</summary>
public static class LocaleCatalog
{
    private static readonly Dictionary<string, (string ja, string en)> Map =
        new Dictionary<string, (string ja, string en)>(StringComparer.OrdinalIgnoreCase)
        {
            { "lang.toggle", ("English", "日本語") },
            { "lang.current.jp", ("日本語", "Japanese") },
            { "lang.current.en", ("英語", "English") },
            { "common.ok", ("OK", "OK") },
            { "common.cancel", ("キャンセル", "Cancel") },
            { "common.close", ("閉じる", "Close") },
            { "common.deck", ("デッキ", "Deck") },
            { "common.search", ("検索", "Search") },
            { "auth.sign_in", ("サインイン", "Sign In") },
            { "auth.sign_up", ("新規登録", "Sign Up") },
            { "auth.sign_out", ("サインアウト", "Sign Out") },
            { "auth.guest", ("ゲストで続ける", "Continue as Guest") },
            { "auth.guest_local", ("ゲスト（ローカル保存）", "Guest (local save)") },
            { "auth.username", ("ユーザー名", "Username") },
            { "auth.password", ("パスワード", "Password") },
            { "auth.enter_user_pass", ("ユーザー名とパスワードを入力してください。", "Please enter username and password.") },
            { "auth.signing_in", ("サインイン中...", "Signing in...") },
            { "auth.creating", ("アカウント作成中...", "Creating account...") },
            { "effect.choose_title", ("効果を選んで OK", "Choose an effect — OK") },
            { "effect.choice_n", ("効果 {0}", "Effect {0}") },
            { "mulligan.prompt", ("手札を山札に戻して5枚引き直しますか？（マリガン）", "Do you want to shuffle your hand and draw 5 cards again? (Mulligan)") },
            { "mulligan.yes", ("はい", "Yes") },
            { "mulligan.no", ("いいえ", "No") },
            { "mulligan.waiting_title", ("マリガン待機中", "Waiting for mulligan") },
            { "mulligan.waiting_sub", ("相手のマリガンを待っています…", "Waiting for opponent's mulligan...") },
            { "zone.battle", ("バトルエリア", "Battle Area") },
            { "zone.resource", ("リソース", "Resource") },
            { "zone.ex", ("EX", "EX") },
            { "zone.deck", ("デッキ", "Deck") },
            { "zone.exile", ("除外", "Exile") },
            { "zone.trash", ("トラッシュ", "Trash") },
            { "zone.hand", ("手札", "Hand") },
            { "zone.shield", ("シールド", "Shield") },
            { "zone.base", ("ベース", "Base") },
        };

    public static string Get(string key, GameLanguage language)
    {
        if (string.IsNullOrEmpty(key) || !Map.TryGetValue(key, out (string ja, string en) pair))
        {
            return key ?? string.Empty;
        }

        return language == GameLanguage.Japanese ? pair.ja : pair.en;
    }
}
