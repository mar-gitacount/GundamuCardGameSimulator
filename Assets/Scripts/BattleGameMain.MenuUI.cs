using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>バトル中のメニュー・ホーム確認 UI。</summary>
public partial class BattleGameMain
{
    private const string HomeSceneName = "Home";

    private Button battleMenuButton;
    private GameObject activeBattleMenuRoot;
    private GameObject activeGoHomeConfirmRoot;

    private void ConfigureBattleMenuButtonInHandPanel()
    {
        if (EndTurnButton == null || cardGameRule?.PlayerHandPanel == null)
        {
            return;
        }

        RectTransform handPanel = cardGameRule.PlayerHandPanel;
        RectTransform endRect = EndTurnButton.GetComponent<RectTransform>();
        if (endRect == null)
        {
            return;
        }

        if (battleMenuButton == null)
        {
            battleMenuButton = handPanel.gameObject.CreateChildButton("Menu");
            battleMenuButton.onClick.AddListener(OpenBattleMenuPanel);
        }

        battleMenuButton.transform.SetParent(handPanel, false);
        battleMenuButton.transform.SetAsLastSibling();
        EndTurnButton.transform.SetAsLastSibling();

        RectTransform menuRect = battleMenuButton.GetComponent<RectTransform>();
        menuRect.anchorMin = new Vector2(1f, 0f);
        menuRect.anchorMax = new Vector2(1f, 0f);
        menuRect.pivot = new Vector2(1f, 0f);
        float endHeight = endRect.sizeDelta.y > 0f ? endRect.sizeDelta.y : 44f;
        float endWidth = endRect.sizeDelta.x > 0f ? endRect.sizeDelta.x : 68f;
        menuRect.sizeDelta = new Vector2(endWidth, endHeight);
        menuRect.anchoredPosition = new Vector2(endRect.anchoredPosition.x, endRect.anchoredPosition.y + endHeight + 6f);

        battleMenuButton.gameObject.SetActive(true);
        battleMenuButton.interactable = true;
    }

    private void OpenBattleMenuPanel()
    {
        CloseBattleMenuPanel();
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        activeBattleMenuRoot = new GameObject("BattleMenuPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeBattleMenuRoot.transform.SetParent(canvas.transform, false);
        activeBattleMenuRoot.transform.SetAsLastSibling();
        activeBattleMenuRoot.SetFullSize();

        Image dim = activeBattleMenuRoot.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.35f);
        dim.raycastTarget = true;

        Button backdropClose = activeBattleMenuRoot.GetComponent<Button>();
        if (backdropClose == null)
        {
            backdropClose = activeBattleMenuRoot.AddComponent<Button>();
        }

        backdropClose.transition = Selectable.Transition.None;
        backdropClose.onClick.AddListener(CloseBattleMenuPanel);

        GameObject sheet = new GameObject("MenuSheet", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        sheet.transform.SetParent(activeBattleMenuRoot.transform, false);
        Image sheetBg = sheet.GetComponent<Image>();
        sheetBg.color = new Color32(245, 245, 245, 250);
        sheetBg.raycastTarget = true;
        RectTransform sheetRt = sheet.GetComponent<RectTransform>();
        sheetRt.anchorMin = new Vector2(1f, 0f);
        sheetRt.anchorMax = new Vector2(1f, 0f);
        sheetRt.pivot = new Vector2(1f, 0f);
        sheetRt.sizeDelta = new Vector2(200f, 150f);
        sheetRt.anchoredPosition = new Vector2(-16f, 120f);

        TextMeshProUGUI title = sheet.CreateChildTextCustom("MenuTitle", UIAnchor.TopCenter, 180, 36);
        title.text = "Menu";
        title.fontSize = 22;
        title.alignment = TextAlignmentOptions.Center;
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0f, -8f);

        Button homeBtn = sheet.CreateChildButton("Home");
        RectTransform homeRt = homeBtn.GetComponent<RectTransform>();
        homeRt.sizeDelta = new Vector2(168f, 44f);
        homeRt.anchorMin = new Vector2(0.5f, 0.5f);
        homeRt.anchorMax = new Vector2(0.5f, 0.5f);
        homeRt.pivot = new Vector2(0.5f, 0.5f);
        homeRt.anchoredPosition = new Vector2(0f, -8f);
        homeBtn.onClick.AddListener(() =>
        {
            CloseBattleMenuPanel();
            ShowGoHomeConfirmDialog();
        });

        Button closeBtn = sheet.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(168f, 40f);
        closeRt.anchorMin = new Vector2(0.5f, 0.5f);
        closeRt.anchorMax = new Vector2(0.5f, 0.5f);
        closeRt.pivot = new Vector2(0.5f, 0.5f);
        closeRt.anchoredPosition = new Vector2(0f, -58f);
        closeBtn.onClick.AddListener(CloseBattleMenuPanel);
    }

    private void CloseBattleMenuPanel()
    {
        if (activeBattleMenuRoot != null)
        {
            Destroy(activeBattleMenuRoot);
            activeBattleMenuRoot = null;
        }
    }

    /// <param name="resultSubtitle">勝敗時は "WIN" / "LOSE" などを表示。</param>
    private void ShowGoHomeConfirmDialog(string resultSubtitle = null)
    {
        CloseGoHomeConfirmDialog();
        CloseBattleMenuPanel();

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("[Menu] Canvas not found for GoHome dialog.");
            return;
        }

        activeGoHomeConfirmRoot = new GameObject("GoHomeConfirmOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeGoHomeConfirmRoot.transform.SetParent(canvas.transform, false);
        activeGoHomeConfirmRoot.transform.SetAsLastSibling();
        activeGoHomeConfirmRoot.SetFullSize();

        Image bg = activeGoHomeConfirmRoot.GetComponent<Image>();
        bg.color = new Color(0.35f, 0.35f, 0.35f, 0.72f);
        bg.raycastTarget = true;

        if (!string.IsNullOrEmpty(resultSubtitle))
        {
            TextMeshProUGUI result = activeGoHomeConfirmRoot.CreateChildTextCustom("MatchResultText", UIAnchor.TopCenter, 420, 100);
            result.text = resultSubtitle;
            result.fontSize = 64;
            result.fontStyle = FontStyles.Bold;
            result.alignment = TextAlignmentOptions.Center;
            result.color = resultSubtitle == "WIN"
                ? new Color32(255, 230, 80, 255)
                : new Color32(255, 120, 120, 255);
            RectTransform resultRt = result.GetComponent<RectTransform>();
            resultRt.anchorMin = new Vector2(0.5f, 0.5f);
            resultRt.anchorMax = new Vector2(0.5f, 0.5f);
            resultRt.pivot = new Vector2(0.5f, 0.5f);
            resultRt.sizeDelta = new Vector2(420f, 100f);
            resultRt.anchoredPosition = new Vector2(0f, 72f);
        }

        TextMeshProUGUI prompt = activeGoHomeConfirmRoot.CreateChildTextCustom("GoHomePrompt", UIAnchor.TopCenter, 480, 80);
        prompt.text = "gotohome?";
        prompt.fontSize = 40;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.color = Color.white;
        RectTransform promptRt = prompt.GetComponent<RectTransform>();
        promptRt.anchorMin = new Vector2(0.5f, 0.5f);
        promptRt.anchorMax = new Vector2(0.5f, 0.5f);
        promptRt.pivot = new Vector2(0.5f, 0.5f);
        promptRt.sizeDelta = new Vector2(480f, 80f);
        promptRt.anchoredPosition = new Vector2(0f, string.IsNullOrEmpty(resultSubtitle) ? 24f : -8f);

        Button okBtn = activeGoHomeConfirmRoot.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(160f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0.5f);
        okRt.anchorMax = new Vector2(0.5f, 0.5f);
        okRt.pivot = new Vector2(0.5f, 0.5f);
        okRt.anchoredPosition = new Vector2(-95f, -72f);
        okBtn.onClick.AddListener(() =>
        {
            CloseGoHomeConfirmDialog();
            ReturnToMainScreen();
        });

        Button cancelBtn = activeGoHomeConfirmRoot.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 52f);
        cancelRt.anchorMin = new Vector2(0.5f, 0.5f);
        cancelRt.anchorMax = new Vector2(0.5f, 0.5f);
        cancelRt.pivot = new Vector2(0.5f, 0.5f);
        cancelRt.anchoredPosition = new Vector2(95f, -72f);
        cancelBtn.onClick.AddListener(CloseGoHomeConfirmDialog);
    }

    private void CloseGoHomeConfirmDialog()
    {
        if (activeGoHomeConfirmRoot != null)
        {
            Destroy(activeGoHomeConfirmRoot);
            activeGoHomeConfirmRoot = null;
        }
    }

    private void CloseAllBattleMenuOverlays()
    {
        CloseBattleMenuPanel();
        CloseGoHomeConfirmDialog();
    }

    private void ReturnToMainScreen()
    {
        CloseAllBattleMenuOverlays();

        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.ReturnToMainMenuFromBattle();
            return;
        }

        TeardownBattleSessionForMainMenu();
        SceneManager.LoadScene(HomeSceneName);
    }
}
