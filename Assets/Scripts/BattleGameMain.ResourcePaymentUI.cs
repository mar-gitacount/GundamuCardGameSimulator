using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 通常リソース／EXリソースをタップしてコスト分を支払い、一致したら OK できる UI。
/// </summary>
public partial class BattleGameMain
{
    private const float ResourcePayTokenWidth = 36f;
    private const float ResourcePayTokenHeight = 50f;

    private static readonly Color32 ResourcePayNormalFace = new Color32(42, 88, 140, 255);
    private static readonly Color32 ResourcePayNormalBorder = new Color32(180, 210, 255, 255);
    private static readonly Color32 ResourcePayExFace = new Color32(160, 120, 30, 255);
    private static readonly Color32 ResourcePayExBorder = new Color32(255, 220, 120, 255);
    private static readonly Color32 ResourcePaySelectedTint = new Color32(90, 200, 120, 255);

    /// <summary>ローカルプレイヤー操作時のみ支払い UI を出す（AI／リモート適用中は自動）。</summary>
    private bool ShouldPromptResourcePaymentUi(PlayerType side)
    {
        return side == PlayerType.Player && !_applyingRemoteBattleAction;
    }

    /// <summary>OnMain 起動コストを UI（または AI 自動）で確定して消費する。</summary>
    private IEnumerator CoTryFinalizeOnMainPaidActivationWithUi(
        PaidActivationBlockContext context,
        Action<bool> onFinished)
    {
        // 保留中の支払いブロックが無い（既払い／非 OnMain）は成功扱い
        if (context.Timed == null)
        {
            onFinished?.Invoke(true);
            yield break;
        }

        if (context.Source == null)
        {
            onFinished?.Invoke(false);
            yield break;
        }

        int cost = GetOnMainActivationCost(context.Source, context.Timed, context.Side);
        if (cost <= 0 || !ShouldPromptResourcePaymentUi(context.Side))
        {
            onFinished?.Invoke(TryFinalizeOnMainPaidActivation(context));
            yield break;
        }

        int requiredLevel = IsOnMainActivatedFromHand(context.Source, context.Side)
            ? context.Source.CurrentLevel
            : 0;
        bool paymentOk = false;
        int exToUse = 0;
        yield return WaitForResourcePaymentCoroutine(
            context.Side,
            cost,
            requiredLevel,
            (ok, chosenEx) =>
            {
                paymentOk = ok;
                exToUse = chosenEx;
            });

        if (!paymentOk)
        {
            onFinished?.Invoke(false);
            yield break;
        }

        onFinished?.Invoke(TryFinalizeOnMainPaidActivation(context, exToUse));
    }

    /// <summary>コスト0・バースト中は即成功。それ以外はオーバーレイで支払い選択。</summary>
    private IEnumerator WaitForResourcePaymentCoroutine(
        PlayerType side,
        int cost,
        int requiredLevel,
        Action<bool, int> onFinished)
    {
        if (cost <= 0 || IsResolvingBurstEffect)
        {
            onFinished?.Invoke(true, 0);
            yield break;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(side);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        if (!ShouldPromptResourcePaymentUi(side))
        {
            int autoEx = Gundam2024RuleScript.GetExNeededForCost(state, cost);
            bool canPay = gundamRule.CanPlayCard(ruleSide, requiredLevel, cost, autoEx);
            onFinished?.Invoke(canPay, canPay ? autoEx : 0);
            yield break;
        }

        if (!gundamRule.CanPlayCardWithAnyEx(ruleSide, requiredLevel, cost))
        {
            Debug.Log("[ResourcePay] リソース不足のため支払い UI を開けません。");
            onFinished?.Invoke(false, 0);
            yield break;
        }

        bool finished = false;
        bool paid = false;
        int exToUse = 0;
        GameObject overlay = OpenResourcePaymentOverlay(
            side,
            cost,
            requiredLevel,
            chosenEx =>
            {
                paid = true;
                exToUse = chosenEx;
                finished = true;
            },
            () =>
            {
                paid = false;
                finished = true;
            });

        if (overlay == null)
        {
            onFinished?.Invoke(false, 0);
            yield break;
        }

        yield return new WaitUntil(() => finished);
        CloseResourcePaymentOverlay(overlay);

        onFinished?.Invoke(paid, exToUse);
    }

    /// <summary>
    /// 既存パネル（FilterPanel 等）に支払い UI を埋め込む。
    /// OK で onPaid(exToUse)、Cancel で onCancel。
    /// </summary>
    private void EmbedResourcePaymentUI(
        GameObject parent,
        PlayerType side,
        int cost,
        int requiredLevel,
        float anchorY,
        Action<int> onPaid,
        Action onCancel)
    {
        if (parent == null)
        {
            onCancel?.Invoke();
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(side);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        if (cost <= 0)
        {
            onPaid?.Invoke(0);
            return;
        }

        if (!gundamRule.CanPlayCardWithAnyEx(ruleSide, requiredLevel, cost))
        {
            Debug.Log("[ResourcePay] リソース不足です。");
            onCancel?.Invoke();
            return;
        }

        BuildResourcePaymentContent(
            parent,
            state.resource,
            state.exResource,
            cost,
            requiredLevel,
            ruleSide,
            anchorY,
            asOverlayRoot: false,
            onPaid,
            onCancel);
    }

    private GameObject OpenResourcePaymentOverlay(
        PlayerType side,
        int cost,
        int requiredLevel,
        Action<int> onPaid,
        Action onCancel)
    {
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(side);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);

        Canvas canvas = ResolveBattleCanvas();
        Transform canvasRoot = canvas != null
            ? canvas.transform
            : ResolveUiCanvasRoot();
        if (canvasRoot == null)
        {
            onCancel?.Invoke();
            return null;
        }

        CloseResourcePaymentOverlay(_activeResourcePaymentOverlay);

        GameObject overlay = new GameObject(
            "ResourcePaymentOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Canvas),
            typeof(GraphicRaycaster));
        overlay.transform.SetParent(canvasRoot, false);
        overlay.transform.SetAsLastSibling();
        RectTransform overlayRt = overlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        Image dim = overlay.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        Canvas overlayCanvas = overlay.GetComponent<Canvas>();
        overlayCanvas.overrideSorting = true;
        overlayCanvas.sortingOrder = 520;
        overlayCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        GameObject sheet = overlay.CreateChildPanelCustom("PaySheet", UIAnchor.CenterStretch, 520, 360);
        RectTransform sheetRt = sheet.GetComponent<RectTransform>();
        sheetRt.anchorMin = new Vector2(0.5f, 0.5f);
        sheetRt.anchorMax = new Vector2(0.5f, 0.5f);
        sheetRt.pivot = new Vector2(0.5f, 0.5f);
        sheetRt.sizeDelta = new Vector2(520f, 360f);
        sheetRt.anchoredPosition = Vector2.zero;
        Image sheetBg = sheet.GetComponent<Image>();
        if (sheetBg != null)
        {
            sheetBg.color = new Color(0.92f, 0.93f, 0.95f, 0.98f);
        }

        BuildResourcePaymentContent(
            sheet,
            state.resource,
            state.exResource,
            cost,
            requiredLevel,
            ruleSide,
            anchorY: 0f,
            asOverlayRoot: true,
            chosenEx =>
            {
                onPaid?.Invoke(chosenEx);
            },
            () =>
            {
                onCancel?.Invoke();
            });

        _activeResourcePaymentOverlay = overlay;
        isOnActionPopupOpen = true;
        return overlay;
    }

    private void CloseResourcePaymentOverlay(GameObject overlay)
    {
        if (overlay == null)
        {
            if (_activeResourcePaymentOverlay == null)
            {
                return;
            }

            overlay = _activeResourcePaymentOverlay;
        }

        if (_activeResourcePaymentOverlay == overlay)
        {
            _activeResourcePaymentOverlay = null;
        }

        if (overlay != null)
        {
            Destroy(overlay);
        }

        isOnActionPopupOpen = activeOnActionPopupRoot != null
            || _activeLookDeckPopupRoot != null
            || _isActionStepCommandResolving;
    }

    private Transform ResolveUiCanvasRoot()
    {
        // CardGameRule は MonoBehaviour ではないため、BattleGameMain 側から Canvas を探す
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            return canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform;
        }

        Canvas any = FindObjectOfType<Canvas>();
        return any != null ? any.transform : transform;
    }

    private void BuildResourcePaymentContent(
        GameObject parent,
        int availableNormal,
        int availableEx,
        int cost,
        int requiredLevel,
        Gundam2024RuleScript.PlayerSide ruleSide,
        float anchorY,
        bool asOverlayRoot,
        Action<int> onPaid,
        Action onCancel)
    {
        int selectedNormal = 0;
        int selectedEx = 0;

        float topY = asOverlayRoot ? 150f : anchorY;
        TextMeshProUGUI title = parent.CreateChildTextCustom("PayTitle", UIAnchor.TopCenter, 480, 36);
        title.SetLocalizedText("リソースを支払う", "Pay Resources");
        title.fontSize = 22;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.black;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, topY);

        TextMeshProUGUI status = parent.CreateChildTextCustom("PayStatus", UIAnchor.TopCenter, 480, 32);
        status.fontSize = 18;
        status.alignment = TextAlignmentOptions.Center;
        status.color = Color.black;
        status.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, topY - 36f);

        TextMeshProUGUI normalLabel = parent.CreateChildTextCustom("NormalLabel", UIAnchor.TopCenter, 480, 24);
        normalLabel.SetLocalizedText("通常リソース（タップで選択）", "Normal resources (tap to select)");
        normalLabel.fontSize = 15;
        normalLabel.alignment = TextAlignmentOptions.Center;
        normalLabel.color = new Color(0.15f, 0.2f, 0.35f);
        normalLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, topY - 70f);

        GameObject normalRow = CreatePaymentTokenRow(parent, "NormalRow", topY - 120f);
        List<Button> normalButtons = new List<Button>();
        for (int i = 0; i < availableNormal; i++)
        {
            normalButtons.Add(CreatePaymentTokenButton(
                normalRow.transform,
                ResourcePayNormalFace,
                ResourcePayNormalBorder,
                false));
        }

        TextMeshProUGUI exLabel = parent.CreateChildTextCustom("ExLabel", UIAnchor.TopCenter, 480, 24);
        exLabel.SetLocalizedText("EXリソース（タップで選択）", "EX resources (tap to select)");
        exLabel.fontSize = 15;
        exLabel.alignment = TextAlignmentOptions.Center;
        exLabel.color = new Color(0.4f, 0.3f, 0.05f);
        exLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, topY - 155f);

        GameObject exRow = CreatePaymentTokenRow(parent, "ExRow", topY - 205f);
        List<Button> exButtons = new List<Button>();
        for (int i = 0; i < availableEx; i++)
        {
            exButtons.Add(CreatePaymentTokenButton(
                exRow.transform,
                ResourcePayExFace,
                ResourcePayExBorder,
                true));
        }

        Button okBtn = parent.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(160f, 48f);
        okRt.anchoredPosition = new Vector2(-90f, topY - 270f);

        Button cancelBtn = parent.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 48f);
        cancelRt.anchoredPosition = new Vector2(90f, topY - 270f);

        Action refresh = null;
        refresh = () =>
        {
            int paid = selectedNormal + selectedEx;
            bool match = paid == cost
                && gundamRule.CanPlayCard(ruleSide, requiredLevel, cost, selectedEx);
            status.SetLocalizedText(
                $"コスト {cost} ／ 選択 {paid}（通常 {selectedNormal} + EX {selectedEx}）",
                $"Cost {cost} / Selected {paid} (Normal {selectedNormal} + EX {selectedEx})");
            status.color = match ? new Color(0.05f, 0.45f, 0.15f) : Color.black;
            okBtn.interactable = match;

            for (int i = 0; i < normalButtons.Count; i++)
            {
                SetPaymentTokenSelected(normalButtons[i], i < selectedNormal, isEx: false);
            }

            for (int i = 0; i < exButtons.Count; i++)
            {
                SetPaymentTokenSelected(exButtons[i], i < selectedEx, isEx: true);
            }
        };

        for (int i = 0; i < normalButtons.Count; i++)
        {
            int index = i;
            normalButtons[i].onClick.AddListener(() =>
            {
                // 先頭から index+1 個まで選択／同じ位置なら解除
                selectedNormal = selectedNormal == index + 1 ? index : index + 1;
                refresh();
            });
        }

        for (int i = 0; i < exButtons.Count; i++)
        {
            int index = i;
            exButtons[i].onClick.AddListener(() =>
            {
                selectedEx = selectedEx == index + 1 ? index : index + 1;
                refresh();
            });
        }

        okBtn.onClick.AddListener(() =>
        {
            if (!gundamRule.CanPlayCard(ruleSide, requiredLevel, cost, selectedEx)
                || selectedNormal + selectedEx != cost)
            {
                return;
            }

            onPaid?.Invoke(selectedEx);
        });
        cancelBtn.onClick.AddListener(() => onCancel?.Invoke());

        refresh();
    }

    private static void ClearEmbeddedResourcePaymentUi(GameObject parent)
    {
        if (parent == null)
        {
            return;
        }

        string[] names =
        {
            "PayTitle", "PayStatus", "NormalLabel", "NormalRow", "ExLabel", "ExRow", "OK", "Cancel"
        };
        for (int i = 0; i < names.Length; i++)
        {
            Transform child = parent.transform.Find(names[i]);
            if (child != null)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // CreateChildButton は label 名がボタン名になるため OK/Cancel を再走査
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            TextMeshProUGUI label = child.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null)
            {
                continue;
            }

            if (string.Equals(label.text, "OK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label.text, "Cancel", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static GameObject CreatePaymentTokenRow(GameObject parent, string name, float anchoredY)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent.transform, false);
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(480f, ResourcePayTokenHeight + 8f);
        rt.anchoredPosition = new Vector2(0f, anchoredY);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 6f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static Button CreatePaymentTokenButton(
        Transform parent,
        Color32 faceColor,
        Color32 borderColor,
        bool isEx)
    {
        GameObject go = new GameObject(
            isEx ? "ExPayToken" : "NormalPayToken",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(ResourcePayTokenWidth, ResourcePayTokenHeight);

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = ResourcePayTokenWidth;
        le.preferredHeight = ResourcePayTokenHeight;
        le.minWidth = ResourcePayTokenWidth;
        le.minHeight = ResourcePayTokenHeight;

        Image img = go.GetComponent<Image>();
        img.color = faceColor;
        img.raycastTarget = true;

        GameObject border = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        border.transform.SetParent(go.transform, false);
        RectTransform borderRt = border.GetComponent<RectTransform>();
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = new Vector2(2f, 2f);
        borderRt.offsetMax = new Vector2(-2f, -2f);
        Image borderImg = border.GetComponent<Image>();
        borderImg.color = borderColor;
        borderImg.raycastTarget = false;

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        inner.transform.SetParent(border.transform, false);
        RectTransform innerRt = inner.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(2f, 2f);
        innerRt.offsetMax = new Vector2(-2f, -2f);
        Image innerImg = inner.GetComponent<Image>();
        innerImg.color = faceColor;
        innerImg.raycastTarget = false;

        return go.GetComponent<Button>();
    }

    private static void SetPaymentTokenSelected(Button button, bool selected, bool isEx)
    {
        if (button == null)
        {
            return;
        }

        Image face = button.GetComponent<Image>();
        Transform inner = button.transform.Find("Border/Inner");
        Image innerImg = inner != null ? inner.GetComponent<Image>() : null;
        Color32 baseFace = isEx ? ResourcePayExFace : ResourcePayNormalFace;
        Color32 color = selected ? ResourcePaySelectedTint : baseFace;
        if (face != null)
        {
            face.color = color;
        }

        if (innerImg != null)
        {
            innerImg.color = color;
        }
    }
}
