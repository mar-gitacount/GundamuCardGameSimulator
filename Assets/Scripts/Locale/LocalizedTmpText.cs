using TMPro;
using UnityEngine;

/// <summary>
/// シーン上の TMP を日英で切り替える。どちらか空ならもう一方を使う。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedTmpText : MonoBehaviour
{
    [TextArea(1, 4)]
    [SerializeField] private string japaneseText;

    [TextArea(1, 4)]
    [SerializeField] private string englishText;

    [Tooltip("LocaleCatalog のキー。設定時は japanese/english より優先。")]
    [SerializeField] private string catalogKey;

    [SerializeField] private bool captureCurrentTextAsEnglishIfEmpty = true;

    private TMP_Text _tmp;
    private static readonly System.Collections.Generic.List<LocalizedTmpText> Active =
        new System.Collections.Generic.List<LocalizedTmpText>(64);

    private void Awake()
    {
        _tmp = GetComponent<TMP_Text>();
        if (captureCurrentTextAsEnglishIfEmpty
            && string.IsNullOrEmpty(englishText)
            && _tmp != null
            && !string.IsNullOrEmpty(_tmp.text))
        {
            englishText = _tmp.text;
        }
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
        {
            Active.Add(this);
        }

        GameLocale.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    private void OnDisable()
    {
        GameLocale.LanguageChanged -= OnLanguageChanged;
        Active.Remove(this);
    }

    private void OnLanguageChanged(GameLanguage _)
    {
        Refresh();
    }

    public void SetTexts(string japanese, string english)
    {
        japaneseText = japanese ?? string.Empty;
        englishText = english ?? string.Empty;
        catalogKey = string.Empty;
        Refresh();
    }

    public void SetCatalogKey(string key)
    {
        catalogKey = key ?? string.Empty;
        Refresh();
    }

    public void Refresh()
    {
        if (_tmp == null)
        {
            _tmp = GetComponent<TMP_Text>();
        }

        if (_tmp == null)
        {
            return;
        }

        string text;
        if (!string.IsNullOrEmpty(catalogKey))
        {
            text = GameLocale.TKey(catalogKey);
        }
        else
        {
            string ja = japaneseText ?? string.Empty;
            string en = englishText ?? string.Empty;
            if (string.IsNullOrEmpty(ja))
            {
                ja = en;
            }

            if (string.IsNullOrEmpty(en))
            {
                en = ja;
            }

            text = GameLocale.T(ja, en);
        }

        GameLocale.ApplyFont(_tmp);
        _tmp.text = text;
        _tmp.ForceMeshUpdate(true);
    }

    public static void RefreshAll()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            LocalizedTmpText item = Active[i];
            if (item == null)
            {
                Active.RemoveAt(i);
                continue;
            }

            item.Refresh();
        }
    }
}
