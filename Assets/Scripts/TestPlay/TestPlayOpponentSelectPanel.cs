using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// TestPlay 開始前の自分／相手デッキ確認 UI。
/// 一覧は押せるよう全面暗幕は出さず、Canvas 直下の上端から Y=-200 に置く。
/// </summary>
public sealed class TestPlayOpponentSelectPanel
{
    private GameObject _root;
    private SlotView _playerSlot;
    private SlotView _enemySlot;
    private Button _okButton;
    private Action _onOk;
    private Action _onCancel;

    public TestPlayDeckPick PlayerPick { get; private set; }
    public TestPlayDeckPick EnemyPick { get; private set; }

    public bool IsOpen => _root != null;

    public void Show(
        RectTransform boardRoot,
        TestPlayDeckPick playerPick,
        Action onOk,
        Action onCancel)
    {
        Close();
        if (boardRoot == null)
        {
            return;
        }

        _onOk = onOk;
        _onCancel = onCancel;
        PlayerPick = playerPick;
        EnemyPick = null;

        _root = new GameObject(
            "TestPlayOpponentSelectPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _root.transform.SetParent(boardRoot, false);
        _root.transform.SetAsLastSibling();

        RectTransform rt = _root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(480f, 268f);
        rt.anchoredPosition = new Vector2(0f, -200f);

        Image bg = _root.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.96f);
        bg.raycastTarget = true;
        Texture2D white = Texture2D.whiteTexture;
        bg.sprite = Sprite.Create(
            white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f));

        TextMeshProUGUI title = _root.CreateChildTextCustom(
            "SelectTitle",
            UIAnchor.TopCenter,
            440,
            32);
        title.SetLocalizedText("テストプレイ相手のデッキを選択", "Select TestPlay opponent deck");
        title.fontSize = 16f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -10f);

        _playerSlot = CreateSlot(_root.transform, -110f, true);
        _enemySlot = CreateSlot(_root.transform, 110f, false);

        Button cancelBtn = _root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.sizeDelta = new Vector2(140f, 40f);
        cancelRt.anchoredPosition = new Vector2(-90f, 10f);
        cancelBtn.onClick.AddListener(() => _onCancel?.Invoke());

        _okButton = _root.CreateChildButton("OK");
        RectTransform okRt = _okButton.GetComponent<RectTransform>();
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.sizeDelta = new Vector2(140f, 40f);
        okRt.anchoredPosition = new Vector2(90f, 10f);
        _okButton.onClick.AddListener(() =>
        {
            if (PlayerPick == null || EnemyPick == null)
            {
                return;
            }

            _onOk?.Invoke();
        });

        Refresh();
    }

    /// <summary>一覧のデッキ押下。空の自分枠があれば自分、なければ相手に入れる。</summary>
    public void AssignDeckFromList(TestPlayDeckPick pick)
    {
        if (pick == null || _root == null)
        {
            return;
        }

        if (PlayerPick == null)
        {
            PlayerPick = pick;
        }
        else
        {
            EnemyPick = pick;
        }

        Refresh();
    }

    public void Close()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        _playerSlot = null;
        _enemySlot = null;
        _okButton = null;
        _onOk = null;
        _onCancel = null;
    }

    private SlotView CreateSlot(Transform parent, float x, bool isPlayer)
    {
        GameObject slot = new GameObject(
            isPlayer ? "PlayerDeckSlot" : "EnemyDeckSlot",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        slot.transform.SetParent(parent, false);
        RectTransform slotRt = slot.GetComponent<RectTransform>();
        slotRt.anchorMin = new Vector2(0.5f, 1f);
        slotRt.anchorMax = new Vector2(0.5f, 1f);
        slotRt.pivot = new Vector2(0.5f, 1f);
        slotRt.sizeDelta = new Vector2(200f, 168f);
        slotRt.anchoredPosition = new Vector2(x, -44f);
        slot.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.2f, 1f);

        TextMeshProUGUI header = slot.CreateChildTextCustom("SlotHeader", UIAnchor.TopCenter, 188, 22);
        header.SetLocalizedText(
            isPlayer ? "自分のデッキ" : "相手のデッキ",
            isPlayer ? "Your deck" : "Opponent deck");
        header.fontSize = 13f;
        header.fontStyle = FontStyles.Bold;
        header.color = new Color(0.9f, 0.92f, 1f, 1f);
        header.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -6f);

        GameObject thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        thumbGo.transform.SetParent(slot.transform, false);
        RectTransform thumbRt = thumbGo.GetComponent<RectTransform>();
        thumbRt.anchorMin = new Vector2(0.5f, 1f);
        thumbRt.anchorMax = new Vector2(0.5f, 1f);
        thumbRt.pivot = new Vector2(0.5f, 1f);
        thumbRt.sizeDelta = new Vector2(72f, 72f);
        thumbRt.anchoredPosition = new Vector2(0f, -30f);
        Image thumb = thumbGo.GetComponent<Image>();
        thumb.preserveAspect = true;
        thumb.raycastTarget = false;

        TextMeshProUGUI title = slot.CreateChildTextCustom("SlotTitle", UIAnchor.TopCenter, 188, 28);
        title.fontSize = 13f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.enableWordWrapping = true;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -104f);

        TextMeshProUGUI detail = slot.CreateChildTextCustom("SlotDetail", UIAnchor.TopCenter, 188, 18);
        detail.fontSize = 11f;
        detail.color = new Color(0.82f, 0.85f, 0.9f, 1f);
        detail.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -128f);

        Button clearBtn = slot.CreateChildButton("Clear");
        RectTransform clearRt = clearBtn.GetComponent<RectTransform>();
        clearRt.anchorMin = new Vector2(0.5f, 0f);
        clearRt.anchorMax = new Vector2(0.5f, 0f);
        clearRt.pivot = new Vector2(0.5f, 0f);
        clearRt.sizeDelta = new Vector2(88f, 24f);
        clearRt.anchoredPosition = new Vector2(0f, 6f);
        TextMeshProUGUI clearLabel = clearBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (clearLabel != null)
        {
            clearLabel.fontSize = 14f;
        }

        SlotView view = new SlotView
        {
            Thumb = thumb,
            Title = title,
            Detail = detail,
            ClearButton = clearBtn,
        };

        bool capturedIsPlayer = isPlayer;
        clearBtn.onClick.AddListener(() =>
        {
            if (capturedIsPlayer)
            {
                PlayerPick = null;
            }
            else
            {
                EnemyPick = null;
            }

            Refresh();
        });

        return view;
    }

    private void Refresh()
    {
        ApplySlot(_playerSlot, PlayerPick);
        ApplySlot(_enemySlot, EnemyPick);
        if (_okButton != null)
        {
            _okButton.interactable = PlayerPick != null && EnemyPick != null;
        }
    }

    private static void ApplySlot(SlotView slot, TestPlayDeckPick pick)
    {
        if (slot == null)
        {
            return;
        }

        if (pick == null)
        {
            slot.Thumb.sprite = null;
            slot.Thumb.color = new Color(1f, 1f, 1f, 0.12f);
            slot.Title.SetLocalizedText("未選択", "Not selected");
            slot.Detail.text = string.Empty;
            slot.ClearButton.interactable = false;
            return;
        }

        slot.Thumb.sprite = pick.Thumbnail;
        slot.Thumb.color = Color.white;
        slot.Title.SetLocalizedText(pick.Title ?? string.Empty);
        string countLine = GameLocale.T($"{pick.TotalCount}枚", $"{pick.TotalCount} cards");
        slot.Detail.text = string.IsNullOrEmpty(pick.DateLine)
            ? countLine
            : $"{countLine}  {pick.DateLine}";
        slot.ClearButton.interactable = true;
    }

    private sealed class SlotView
    {
        public Image Thumb;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Detail;
        public Button ClearButton;
    }
}
