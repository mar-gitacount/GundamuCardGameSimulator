using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IncludedCards : MonoBehaviour
{
    private const float CheckboxSize = 32f;
    private const float ToggleRowHeight = 40f;
    private const float SearchContentWidth = 480f;
    private const float ProductDropdownInnerWidth = 456f;

    [SerializeField] private Toggle togglePrefab;
    [SerializeField] private ToggleGroup toggleGroup;
    [SerializeField] private Transform toggleParent;
    [SerializeField] private string cardSetResourcePath = "Data/CardSetData";
    [SerializeField] private GameObject includeSectionTextPrefab;
    [SerializeField] private CardSetToggleScope toggleScope = CardSetToggleScope.All;
    [SerializeField] private bool replaceExistingToggles;
    [SerializeField] private bool hideSectionHeader;
    [SerializeField] private int maxVersionSetId;

    private bool _togglesCreated;
    private TMP_Dropdown _boosterDropdown;
    private TMP_Dropdown _starterDropdown;
    private TMP_Dropdown _eternalDropdown;
    private TMP_Text _boosterLabel;
    private TMP_Text _starterLabel;
    private TMP_Text _eternalLabel;

    void Awake()
    {
        CreateCardSetToggles();
    }

    void OnEnable()
    {
        GameLocale.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedLabels();
    }

    void OnDisable()
    {
        GameLocale.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(GameLanguage _)
    {
        RefreshLocalizedLabels();
    }

    private void OnCardSetSelected(CardSetData set)
    {
        Debug.Log($"選択されたカードセット: {set.setName}");
        Debug.Log($"カードのID: {set.setId}");
    }

    public List<Toggle> GetOnToggles()
    {
        var result = new List<Toggle>();
        if (toggleParent == null)
        {
            return result;
        }

        Toggle[] toggles = toggleParent.GetComponentsInChildren<Toggle>(true);
        foreach (var toggle in toggles)
        {
            if (toggle != null && toggle.isOn)
            {
                result.Add(toggle);
            }
        }

        return result;
    }

    /// <summary>検索クリア用。折りたたみ中のトグル／作品プルダウンをすべて解除する。</summary>
    public void ClearAllToggles()
    {
        if (toggleParent != null)
        {
            Toggle[] toggles = toggleParent.GetComponentsInChildren<Toggle>(true);
            for (int i = 0; i < toggles.Length; i++)
            {
                Toggle toggle = toggles[i];
                if (toggle != null)
                {
                    toggle.isOn = false;
                }
            }
        }

        ClearProductSetDropdowns();
    }

    private void ClearProductSetDropdowns()
    {
        if (_boosterDropdown != null)
        {
            _boosterDropdown.SetValueWithoutNotify(0);
        }

        if (_starterDropdown != null)
        {
            _starterDropdown.SetValueWithoutNotify(0);
        }

        if (_eternalDropdown != null)
        {
            _eternalDropdown.SetValueWithoutNotify(0);
        }
    }

    /// <summary>言語切替時にセクション見出し・トグルラベル・作品プルダウンを更新する。</summary>
    public void RefreshLocalizedLabels()
    {
        if (toggleParent != null)
        {
            LocalizedTmpText[] localized = toggleParent.GetComponentsInChildren<LocalizedTmpText>(true);
            for (int i = 0; i < localized.Length; i++)
            {
                if (localized[i] != null)
                {
                    localized[i].Refresh();
                }
            }
        }

        RefreshProductSetDropdownLocale();
    }

    private void RefreshProductSetDropdownLocale()
    {
        bool japanese = GameLocale.IsJapanese;
        ApplyDropdownLabel(_boosterLabel, "ブースター", "Booster");
        ApplyDropdownLabel(_starterLabel, "スターター", "Starter");
        ApplyDropdownLabel(_eternalLabel, "Eternal Booster", "Eternal Booster");

        int boosterValue = _boosterDropdown != null ? _boosterDropdown.value : 0;
        int starterValue = _starterDropdown != null ? _starterDropdown.value : 0;
        int eternalValue = _eternalDropdown != null ? _eternalDropdown.value : 0;

        if (_boosterDropdown != null)
        {
            _boosterDropdown.ClearOptions();
            _boosterDropdown.AddOptions(CardProductSetNames.BuildBoosterDropdownOptions(japanese));
            _boosterDropdown.SetValueWithoutNotify(Mathf.Clamp(boosterValue, 0, _boosterDropdown.options.Count - 1));
        }

        if (_starterDropdown != null)
        {
            _starterDropdown.ClearOptions();
            _starterDropdown.AddOptions(CardProductSetNames.BuildStarterDropdownOptions(japanese));
            _starterDropdown.SetValueWithoutNotify(Mathf.Clamp(starterValue, 0, _starterDropdown.options.Count - 1));
        }

        if (_eternalDropdown != null)
        {
            _eternalDropdown.ClearOptions();
            _eternalDropdown.AddOptions(CardProductSetNames.BuildEternalDropdownOptions(japanese));
            _eternalDropdown.SetValueWithoutNotify(Mathf.Clamp(eternalValue, 0, _eternalDropdown.options.Count - 1));
        }
    }

    private static void ApplyDropdownLabel(TMP_Text label, string japanese, string english)
    {
        if (label == null)
        {
            return;
        }

        LocalizedTmpText loc = label.GetComponent<LocalizedTmpText>();
        if (loc == null)
        {
            loc = label.gameObject.AddComponent<LocalizedTmpText>();
        }

        loc.SetTexts(japanese, english);
    }

    public List<CardData> GetSelectedCards(List<CardData> cards)
    {
        if (cards == null)
        {
            return new List<CardData>();
        }

        List<CardData> filtered = ApplyProductSetDropdownFilters(cards);
        List<Toggle> onToggles = GetOnToggles();
        if (onToggles.Count == 0)
        {
            return filtered;
        }

        Dictionary<FilterType, Predicate<CardData>> groupPredicates
            = new Dictionary<FilterType, Predicate<CardData>>();

        foreach (var toggle in onToggles)
        {
            ToggleDatail detail = toggle.GetComponent<ToggleDatail>();
            if (detail == null)
            {
                continue;
            }

            Predicate<CardData> condition = null;
            Debug.Log($"トグルのフィルタータイプ: {detail.filterType}, ,バージョンID: {detail.id}, sourceType: {detail.sourceType}, color: {detail.color}");

            switch (detail.filterType)
            {
                case FilterType.Version:
                    condition = card => card.version == detail.id;
                    Debug.Log($"フィルタリング: {detail.filterType}, version: {detail.id}");
                    break;
                case FilterType.SourceType:
                    condition = card => card.sourceType == detail.sourceType;
                    break;
                case FilterType.Color:
                    condition = card => card.color == detail.color;
                    Debug.Log($"フィルタリング: {detail.filterType}, color: {detail.color}");
                    break;
                default:
                    Debug.LogWarning($"未対応のフィルタータイプ: {detail.filterType}");
                    break;
            }

            if (condition == null)
            {
                continue;
            }

            if (!groupPredicates.ContainsKey(detail.filterType))
            {
                groupPredicates[detail.filterType] = condition;
            }
            else
            {
                groupPredicates[detail.filterType]
                    = groupPredicates[detail.filterType].Or(condition);
            }
        }

        if (groupPredicates.Count == 0)
        {
            return filtered;
        }

        Predicate<CardData> finalPredicate = card => true;
        foreach (var predicate in groupPredicates.Values)
        {
            finalPredicate = finalPredicate.And(predicate);
        }

        return filtered.FindAll(finalPredicate);
    }

    private List<CardData> ApplyProductSetDropdownFilters(List<CardData> cards)
    {
        BoosterProductSet booster = _boosterDropdown != null
            ? CardProductSetNames.BoosterFromDropdownIndex(_boosterDropdown.value)
            : BoosterProductSet.None;
        StarterProductSet starter = _starterDropdown != null
            ? CardProductSetNames.StarterFromDropdownIndex(_starterDropdown.value)
            : StarterProductSet.None;
        EternalBoosterProductSet eternal = _eternalDropdown != null
            ? CardProductSetNames.EternalFromDropdownIndex(_eternalDropdown.value)
            : EternalBoosterProductSet.None;

        if (booster == BoosterProductSet.None
            && starter == StarterProductSet.None
            && eternal == EternalBoosterProductSet.None)
        {
            return cards;
        }

        // 複数プルダウン指定時は OR（いずれかの作品に一致すれば残す）
        return cards.FindAll(card =>
            (booster != BoosterProductSet.None && CardProductSetNames.MatchesBooster(card, booster))
            || (starter != StarterProductSet.None && CardProductSetNames.MatchesStarter(card, starter))
            || (eternal != EternalBoosterProductSet.None && CardProductSetNames.MatchesEternal(card, eternal)));
    }

    void CreateCardSetToggles()
    {
        if (_togglesCreated || toggleParent == null)
        {
            return;
        }

        if (toggleScope == CardSetToggleScope.VersionOnly)
        {
            if (replaceExistingToggles)
            {
                DestroyExistingToggles();
            }

            CreateProductSetDropdowns();
            _togglesCreated = true;
            return;
        }

        if (togglePrefab == null)
        {
            return;
        }

        CardSetData[] cardSets = Resources.LoadAll<CardSetData>(cardSetResourcePath);
        if (cardSets == null || cardSets.Length == 0)
        {
            return;
        }

        if (replaceExistingToggles)
        {
            DestroyExistingToggles();
        }

        List<CardSetData> ordered = new List<CardSetData>(cardSets.Length);
        for (int i = 0; i < cardSets.Length; i++)
        {
            CardSetData set = cardSets[i];
            if (set == null || ShouldSkipDuplicateColor(set) || !ShouldIncludeSet(set))
            {
                continue;
            }

            ordered.Add(set);
        }

        ordered.Sort(CompareCardSets);

        string filterTypeText = "";
        for (int i = 0; i < ordered.Count; i++)
        {
            CardSetData set = ordered[i];
            string typeKey = set.filterType.ToString();
            if (!hideSectionHeader && includeSectionTextPrefab != null && typeKey != filterTypeText)
            {
                filterTypeText = typeKey;
                GameObject sectionText = Instantiate(includeSectionTextPrefab, toggleParent);
                TMP_Text text = sectionText.GetComponentInChildren<TMP_Text>(true);
                ApplyFilterTypeHeader(text, set.filterType);
            }

            Toggle toggle = Instantiate(togglePrefab, toggleParent);
            if (toggle == null || set == null)
            {
                Debug.LogWarning("[IncludedCards] トグル生成に失敗したためスキップします。");
                continue;
            }

            EnlargeToggle(toggle);

            ToggleDatail detail = toggle.GetComponent<ToggleDatail>();
            if (detail == null)
            {
                detail = toggle.gameObject.AddComponent<ToggleDatail>();
            }

            detail.id = set.setId;
            detail.filterType = set.filterType;
            detail.sourceType = set.sourceType;
            detail.color = set.color;
            Debug.Log("トグルのカードのソースタイプ: " + set.sourceType + " フィルタータイプ: " + set.filterType + " カードセットID: " + set.setId);

            ApplyToggleLabel(toggle, set);

            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    OnCardSetSelected(set);
                }
            });
        }

        _togglesCreated = true;
    }

    /// <summary>作品検索用: ブースター / スターター / Eternal のプルダウンを生成する。</summary>
    private void CreateProductSetDropdowns()
    {
        ConfigureProductSetParentLayout();

        _boosterLabel = CreateDropdownRow(
            "BoosterSetRow",
            "ブースター",
            "Booster",
            CardProductSetNames.BuildBoosterDropdownOptions(GameLocale.IsJapanese),
            out _boosterDropdown);
        _starterLabel = CreateDropdownRow(
            "StarterSetRow",
            "スターター",
            "Starter",
            CardProductSetNames.BuildStarterDropdownOptions(GameLocale.IsJapanese),
            out _starterDropdown);
        _eternalLabel = CreateDropdownRow(
            "EternalSetRow",
            "Eternal Booster",
            "Eternal Booster",
            CardProductSetNames.BuildEternalDropdownOptions(GameLocale.IsJapanese),
            out _eternalDropdown);

        Canvas.ForceUpdateCanvases();
        if (toggleParent is RectTransform parentRt)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        }
    }

    /// <summary>作品検索パネルを検索幅480内に収める。</summary>
    private void ConfigureProductSetParentLayout()
    {
        if (toggleParent == null)
        {
            return;
        }

        RectTransform parentRt = toggleParent as RectTransform;
        if (parentRt != null)
        {
            // 親 Content 内で中央・幅480固定（横にはみ出さない）
            parentRt.anchorMin = new Vector2(0.5f, 1f);
            parentRt.anchorMax = new Vector2(0.5f, 1f);
            parentRt.pivot = new Vector2(0.5f, 1f);
            parentRt.sizeDelta = new Vector2(SearchContentWidth, Mathf.Max(parentRt.sizeDelta.y, 260f));
            parentRt.anchoredPosition = new Vector2(0f, parentRt.anchoredPosition.y);
        }

        VerticalLayoutGroup vlg = toggleParent.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = toggleParent.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        vlg.padding = new RectOffset(12, 12, 8, 12);
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.childScaleWidth = false;
        vlg.childScaleHeight = false;

        ContentSizeFitter fitter = toggleParent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = toggleParent.gameObject.AddComponent<ContentSizeFitter>();
        }

        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement parentLayout = toggleParent.GetComponent<LayoutElement>();
        if (parentLayout == null)
        {
            parentLayout = toggleParent.gameObject.AddComponent<LayoutElement>();
        }

        parentLayout.preferredWidth = SearchContentWidth;
        parentLayout.minWidth = SearchContentWidth;
        parentLayout.flexibleWidth = 0f;
    }

    private TMP_Text CreateDropdownRow(
        string rowName,
        string labelJa,
        string labelEn,
        List<string> options,
        out TMP_Dropdown dropdown)
    {
        GameObject row = new GameObject(rowName, typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        row.transform.SetParent(toggleParent, false);

        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 1f);
        rowRt.anchorMax = new Vector2(0.5f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.sizeDelta = new Vector2(ProductDropdownInnerWidth, 78f);

        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.minHeight = 78f;
        rowLayout.preferredHeight = 78f;
        rowLayout.minWidth = ProductDropdownInnerWidth;
        rowLayout.preferredWidth = ProductDropdownInnerWidth;
        rowLayout.flexibleWidth = 0f;

        VerticalLayoutGroup rowVlg = row.GetComponent<VerticalLayoutGroup>();
        rowVlg.padding = new RectOffset(0, 0, 0, 0);
        rowVlg.spacing = 4f;
        rowVlg.childAlignment = TextAnchor.UpperCenter;
        rowVlg.childControlWidth = true;
        rowVlg.childControlHeight = true;
        rowVlg.childForceExpandWidth = true;
        rowVlg.childForceExpandHeight = false;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        labelGo.transform.SetParent(row.transform, false);
        LayoutElement labelLayout = labelGo.GetComponent<LayoutElement>();
        labelLayout.minHeight = 24f;
        labelLayout.preferredHeight = 24f;
        labelLayout.preferredWidth = ProductDropdownInnerWidth;
        labelLayout.flexibleWidth = 0f;

        TextMeshProUGUI labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
        labelTmp.fontSize = 18f;
        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
        labelTmp.color = Color.white;
        labelTmp.raycastTarget = false;
        labelTmp.enableWordWrapping = false;
        labelTmp.overflowMode = TextOverflowModes.Ellipsis;
        ApplyDropdownLabel(labelTmp, labelJa, labelEn);

        GameObject dropdownGo = new GameObject(
            "Dropdown",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(TMP_Dropdown),
            typeof(LayoutElement));
        dropdownGo.transform.SetParent(row.transform, false);

        LayoutElement dropdownLayout = dropdownGo.GetComponent<LayoutElement>();
        dropdownLayout.minHeight = 44f;
        dropdownLayout.preferredHeight = 44f;
        dropdownLayout.minWidth = ProductDropdownInnerWidth;
        dropdownLayout.preferredWidth = ProductDropdownInnerWidth;
        dropdownLayout.flexibleWidth = 0f;

        RectTransform dropdownRt = dropdownGo.GetComponent<RectTransform>();
        dropdownRt.sizeDelta = new Vector2(ProductDropdownInnerWidth, 44f);

        Image dropdownImage = dropdownGo.GetComponent<Image>();
        dropdownImage.color = new Color(1f, 1f, 1f, 0.95f);

        dropdown = dropdownGo.GetComponent<TMP_Dropdown>();
        BuildDropdownTemplate(dropdownGo, dropdown);

        // 右側の▼表示
        GameObject arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(dropdownGo.transform, false);
        RectTransform arrowRt = arrowGo.GetComponent<RectTransform>();
        arrowRt.anchorMin = new Vector2(1f, 0f);
        arrowRt.anchorMax = new Vector2(1f, 1f);
        arrowRt.pivot = new Vector2(1f, 0.5f);
        arrowRt.sizeDelta = new Vector2(28f, 0f);
        arrowRt.anchoredPosition = Vector2.zero;
        TextMeshProUGUI arrowTmp = arrowGo.GetComponent<TextMeshProUGUI>();
        arrowTmp.text = "▼";
        arrowTmp.fontSize = 14f;
        arrowTmp.alignment = TextAlignmentOptions.Center;
        arrowTmp.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        arrowTmp.raycastTarget = false;

        GameObject captionGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        captionGo.transform.SetParent(dropdownGo.transform, false);
        RectTransform captionRt = captionGo.GetComponent<RectTransform>();
        captionRt.anchorMin = Vector2.zero;
        captionRt.anchorMax = Vector2.one;
        captionRt.offsetMin = new Vector2(12f, 4f);
        captionRt.offsetMax = new Vector2(-32f, -4f);
        TextMeshProUGUI caption = captionGo.GetComponent<TextMeshProUGUI>();
        caption.fontSize = 16f;
        caption.color = Color.black;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.raycastTarget = false;
        caption.enableWordWrapping = false;
        caption.overflowMode = TextOverflowModes.Ellipsis;
        dropdown.captionText = caption;

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        GameLocale.ApplyFont(caption);
        GameLocale.ApplyFont(labelTmp);
        GameLocale.ApplyFont(arrowTmp);
        return labelTmp;
    }

    private static void BuildDropdownTemplate(GameObject dropdownGo, TMP_Dropdown dropdown)
    {
        // 最低限のテンプレート（開いたときのリスト）。幅は親ドロップダウンに追従。
        GameObject template = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        template.transform.SetParent(dropdownGo.transform, false);
        RectTransform templateRt = template.GetComponent<RectTransform>();
        templateRt.anchorMin = new Vector2(0f, 0f);
        templateRt.anchorMax = new Vector2(1f, 0f);
        templateRt.pivot = new Vector2(0.5f, 1f);
        templateRt.anchoredPosition = new Vector2(0f, 2f);
        templateRt.sizeDelta = new Vector2(0f, 180f);
        Image templateImage = template.GetComponent<Image>();
        templateImage.color = Color.white;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(4f, 4f);
        viewportRt.offsetMax = new Vector2(-4f, -4f);
        viewport.GetComponent<Image>().color = Color.white;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
        contentVlg.childControlWidth = true;
        contentVlg.childControlHeight = true;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;
        ContentSizeFitter contentFitter = content.GetComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject item = new GameObject(
            "Item",
            typeof(RectTransform),
            typeof(Toggle),
            typeof(LayoutElement));
        item.transform.SetParent(content.transform, false);
        LayoutElement itemLayout = item.GetComponent<LayoutElement>();
        itemLayout.minHeight = 32f;
        itemLayout.preferredHeight = 32f;
        itemLayout.flexibleWidth = 1f;

        GameObject itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        itemBg.transform.SetParent(item.transform, false);
        RectTransform itemBgRt = itemBg.GetComponent<RectTransform>();
        itemBgRt.anchorMin = Vector2.zero;
        itemBgRt.anchorMax = Vector2.one;
        itemBgRt.offsetMin = Vector2.zero;
        itemBgRt.offsetMax = Vector2.zero;
        itemBg.GetComponent<Image>().color = new Color(0.92f, 0.92f, 0.92f, 1f);

        GameObject itemCheck = new GameObject("Item Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        itemCheck.transform.SetParent(item.transform, false);
        RectTransform itemCheckRt = itemCheck.GetComponent<RectTransform>();
        itemCheckRt.anchorMin = new Vector2(0f, 0.5f);
        itemCheckRt.anchorMax = new Vector2(0f, 0.5f);
        itemCheckRt.sizeDelta = new Vector2(14f, 14f);
        itemCheckRt.anchoredPosition = new Vector2(12f, 0f);
        itemCheck.GetComponent<Image>().color = new Color(0.15f, 0.45f, 0.95f, 1f);

        GameObject itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        itemLabel.transform.SetParent(item.transform, false);
        RectTransform itemLabelRt = itemLabel.GetComponent<RectTransform>();
        itemLabelRt.anchorMin = Vector2.zero;
        itemLabelRt.anchorMax = Vector2.one;
        itemLabelRt.offsetMin = new Vector2(28f, 2f);
        itemLabelRt.offsetMax = new Vector2(-8f, -2f);
        TextMeshProUGUI itemTmp = itemLabel.GetComponent<TextMeshProUGUI>();
        itemTmp.fontSize = 15f;
        itemTmp.color = Color.black;
        itemTmp.alignment = TextAlignmentOptions.MidlineLeft;
        itemTmp.raycastTarget = false;
        itemTmp.enableWordWrapping = false;
        itemTmp.overflowMode = TextOverflowModes.Ellipsis;
        GameLocale.ApplyFont(itemTmp);

        Toggle itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBg.GetComponent<Image>();
        itemToggle.graphic = itemCheck.GetComponent<Image>();
        itemToggle.isOn = true;

        ScrollRect scroll = template.GetComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = viewportRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        template.SetActive(false);
        dropdown.template = templateRt;
        dropdown.itemText = itemTmp;
    }

    /// <summary>
    /// Resources はファイル名順のため Version が Color の後ろに再出現する。
    /// filterType → setId で並べてセクションを1つにまとめる。
    /// </summary>
    private static int CompareCardSets(CardSetData a, CardSetData b)
    {
        int cmp = ((int)a.filterType).CompareTo((int)b.filterType);
        if (cmp != 0)
        {
            return cmp;
        }

        if (a.filterType == FilterType.Color)
        {
            cmp = ((int)a.color).CompareTo((int)b.color);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        cmp = a.setId.CompareTo(b.setId);
        if (cmp != 0)
        {
            return cmp;
        }

        return string.Compare(a.setName, b.setName, StringComparison.Ordinal);
    }

    private bool ShouldIncludeSet(CardSetData set)
    {
        if (toggleScope == CardSetToggleScope.VersionOnly && set.filterType != FilterType.Version)
        {
            return false;
        }

        if (toggleScope == CardSetToggleScope.ColorOnly && set.filterType != FilterType.Color)
        {
            return false;
        }

        if (toggleScope == CardSetToggleScope.VersionOnly
            && maxVersionSetId > 0
            && set.setId > maxVersionSetId)
        {
            return false;
        }

        return true;
    }

    private void DestroyExistingToggles()
    {
        if (toggleParent == null)
        {
            return;
        }

        for (int i = toggleParent.childCount - 1; i >= 0; i--)
        {
            Transform child = toggleParent.GetChild(i);
            if (child == null)
            {
                continue;
            }

            // 作品検索はトグル／旧行を消してプルダウンを載せ直す。色フィルターはトグルのみ削除。
            if (toggleScope == CardSetToggleScope.VersionOnly
                || child.GetComponent<Toggle>() != null)
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    /// <summary>Color フォルダの「青」(color が Red) は正規の Blue と重複するため除外。</summary>
    private static bool ShouldSkipDuplicateColor(CardSetData set)
    {
        if (set.filterType != FilterType.Color)
        {
            return false;
        }

        string name = set.setName != null ? set.setName.Trim() : string.Empty;
        return name == "青" && set.color == CardColor.Red;
    }

    private static void ApplyFilterTypeHeader(TMP_Text text, FilterType filterType)
    {
        if (text == null)
        {
            return;
        }

        GetFilterTypeTexts(filterType, out string ja, out string en);
        LocalizedTmpText loc = text.GetComponent<LocalizedTmpText>();
        if (loc == null)
        {
            loc = text.gameObject.AddComponent<LocalizedTmpText>();
        }

        loc.SetTexts(ja, en);
    }

    private static void GetFilterTypeTexts(FilterType filterType, out string ja, out string en)
    {
        switch (filterType)
        {
            case FilterType.Version:
                ja = "バージョン";
                en = "Version";
                return;
            case FilterType.Color:
                ja = "色";
                en = "Color";
                return;
            case FilterType.SourceType:
                ja = "収録元";
                en = "Source";
                return;
            case FilterType.Cost:
                ja = "コスト";
                en = "Cost";
                return;
            case FilterType.Level:
                ja = "レベル";
                en = "Level";
                return;
            default:
                ja = filterType.ToString();
                en = filterType.ToString();
                return;
        }
    }

    private static void ApplyToggleLabel(Toggle toggle, CardSetData set)
    {
        if (toggle == null || set == null)
        {
            return;
        }

        GetSetNameTexts(set, out string ja, out string en);

        Transform labelTr = toggle.transform.Find("Label");
        if (labelTr == null)
        {
            GameObject createdLabel = new GameObject("Label", typeof(RectTransform));
            createdLabel.transform.SetParent(toggle.transform, false);
            RectTransform createdRt = createdLabel.GetComponent<RectTransform>();
            createdRt.anchorMin = Vector2.zero;
            createdRt.anchorMax = Vector2.one;
            createdRt.offsetMin = new Vector2(40f, 2f);
            createdRt.offsetMax = new Vector2(-4f, -2f);
            labelTr = createdLabel.transform;
        }

        GameObject labelGo = labelTr.gameObject;

        // Legacy Text と TMP を同一 GO に同居させない（AddComponent 失敗・NRE 対策）
        Text legacy = labelGo.GetComponent<Text>();
        TMP_Text tmp = labelGo.GetComponent<TMP_Text>();
        if (tmp == null)
        {
            tmp = labelGo.GetComponentInChildren<TMP_Text>(true);
        }

        if (tmp == null)
        {
            Transform tmpTr = labelTr.Find("TMP");
            GameObject tmpGo;
            if (tmpTr != null)
            {
                tmpGo = tmpTr.gameObject;
            }
            else
            {
                tmpGo = new GameObject("TMP", typeof(RectTransform), typeof(CanvasRenderer));
                tmpGo.transform.SetParent(labelTr, false);
                RectTransform tmpRt = tmpGo.GetComponent<RectTransform>();
                tmpRt.anchorMin = Vector2.zero;
                tmpRt.anchorMax = Vector2.one;
                tmpRt.offsetMin = Vector2.zero;
                tmpRt.offsetMax = Vector2.zero;
            }

            tmp = tmpGo.GetComponent<TMP_Text>();
            if (tmp == null)
            {
                tmp = tmpGo.AddComponent<TextMeshProUGUI>();
            }

            if (tmp == null)
            {
                if (legacy != null)
                {
                    legacy.enabled = true;
                    legacy.text = GameLocale.T(ja, en);
                }

                Debug.LogWarning("[IncludedCards] TMP ラベルを作成できないため Legacy Text にフォールバックしました: " + set.setName);
                return;
            }

            tmp.color = new Color(0.196f, 0.196f, 0.196f, 1f);
            tmp.fontSize = 18f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            if (tmp is TextMeshProUGUI tmpUi)
            {
                tmpUi.enableWordWrapping = false;
            }
        }
        else
        {
            tmp.fontSize = 18f;
        }

        if (legacy != null)
        {
            legacy.enabled = false;
            legacy.raycastTarget = false;
        }

        LocalizedTmpText loc = tmp.GetComponent<LocalizedTmpText>();
        if (loc == null)
        {
            loc = tmp.gameObject.AddComponent<LocalizedTmpText>();
        }

        if (loc != null)
        {
            loc.SetTexts(ja, en);
        }
        else
        {
            tmp.SetLocalizedText(ja, en);
        }
    }

    private static void GetSetNameTexts(CardSetData set, out string ja, out string en)
    {
        if (set.filterType == FilterType.Color)
        {
            GetColorNameTexts(set.setName, set.color, out ja, out en);
            return;
        }

        GetVersionSetNameTexts(set.setName, out ja, out en);
    }

    private static void GetVersionSetNameTexts(string setName, out string ja, out string en)
    {
        string name = setName != null ? setName.Trim() : string.Empty;
        string compact = name.Replace(" ", string.Empty).Replace("-", string.Empty);

        if (compact.Equals("NewTyperising", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("NewTypeRizing", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("NewtypeRising", StringComparison.OrdinalIgnoreCase))
        {
            ja = "ニュータイプライジング";
            en = "Newtype Rising";
            return;
        }

        if (compact.Equals("DualImpact", StringComparison.OrdinalIgnoreCase))
        {
            ja = "デュアルインパクト";
            en = "Dual Impact";
            return;
        }

        if (compact.Equals("StealRequiem", StringComparison.OrdinalIgnoreCase))
        {
            ja = "Steal Requiem";
            en = "Steal Requiem";
            return;
        }

        if (compact.Equals("PhantomAria", StringComparison.OrdinalIgnoreCase))
        {
            ja = "Phantom Aria";
            en = "Phantom Aria";
            return;
        }

        if (compact.Equals("HeroicBegining", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("HeroicBeginnings", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("HeroicBeginning", StringComparison.OrdinalIgnoreCase))
        {
            ja = "Heroic Beginnings";
            en = "Heroic Beginnings";
            return;
        }

        if (compact.Equals("FreedomAscension", StringComparison.OrdinalIgnoreCase))
        {
            ja = "Freedom Ascension";
            en = "Freedom Ascension";
            return;
        }

        ja = name;
        en = name;
    }

    private static void GetColorNameTexts(string setName, CardColor color, out string ja, out string en)
    {
        switch (color)
        {
            case CardColor.Red:
                ja = "赤";
                en = "Red";
                return;
            case CardColor.Green:
                ja = "緑";
                en = "Green";
                return;
            case CardColor.Blue:
                ja = "青";
                en = "Blue";
                return;
            case CardColor.Yellow:
                ja = "黄";
                en = "Yellow";
                return;
            case CardColor.White:
                ja = "白";
                en = "White";
                return;
            case CardColor.Purple:
                ja = "紫";
                en = "Purple";
                return;
            case CardColor.Colorless:
                ja = "無色";
                en = "Colorless";
                return;
        }

        string name = setName != null ? setName.Trim() : string.Empty;
        if (name == "青" || name.Equals("Blue", StringComparison.OrdinalIgnoreCase))
        {
            ja = "青";
            en = "Blue";
            return;
        }

        ja = name;
        en = name;
    }

    private static void EnlargeToggle(Toggle toggle)
    {
        if (toggle == null)
        {
            return;
        }

        RectTransform root = toggle.GetComponent<RectTransform>();
        if (root != null)
        {
            root.sizeDelta = new Vector2(Mathf.Max(root.sizeDelta.x, 480f), ToggleRowHeight);
        }

        LayoutElement layout = toggle.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = toggle.gameObject.AddComponent<LayoutElement>();
        }

        layout.minHeight = ToggleRowHeight;
        layout.preferredHeight = ToggleRowHeight;

        Transform bg = toggle.transform.Find("Background");
        if (bg is RectTransform bgRt)
        {
            bgRt.sizeDelta = new Vector2(CheckboxSize, CheckboxSize);
            bgRt.anchoredPosition = new Vector2(8f + CheckboxSize * 0.5f, -ToggleRowHeight * 0.5f);
            Transform mark = bg.Find("Checkmark");
            if (mark is RectTransform markRt)
            {
                markRt.sizeDelta = new Vector2(CheckboxSize, CheckboxSize);
            }
        }

        Transform labelTr = toggle.transform.Find("Label");
        if (labelTr is RectTransform labelRt)
        {
            labelRt.anchoredPosition = new Vector2(18f, -0.5f);
            labelRt.sizeDelta = new Vector2(-(CheckboxSize + 16f), -4f);
        }
    }
}

/// <summary>検索トグルに出す CardSetData の種類。</summary>
public enum CardSetToggleScope
{
    All = 0,
    VersionOnly = 1,
    ColorOnly = 2,
}
